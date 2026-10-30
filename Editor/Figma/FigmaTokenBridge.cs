namespace Cuvara.UIToolkit.Editor.Figma
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;
    using Cuvara.UIToolkit.FigmaCore;

    /// <summary>One theme role: a Cuvara variable and the Figma token that supplies its value.</summary>
    [Serializable]
    public sealed class FigmaTokenRole
    {
        /// <summary>The Cuvara theme variable, for example <c>--cuvara-color-primary</c>.</summary>
        public string cuvaraToken;

        /// <summary>The connector's token, for example <c>--brand-primary</c>. Empty leaves the role on its theme default.</summary>
        public string figmaToken;

        public FigmaTokenRole()
        {
        }

        public FigmaTokenRole(string cuvaraToken, string figmaToken)
        {
            this.cuvaraToken = cuvaraToken;
            this.figmaToken  = figmaToken;
        }
    }

    /// <summary>Whether the bridge file on disk matches what the profile would generate now.</summary>
    public enum FigmaBridgeFreshness
    {
        Missing,
        Stale,
        Fresh,
    }

    /// <summary>The generated bridge text and what was found while generating it.</summary>
    public sealed class FigmaTokenBridgeResult
    {
        public string Content;
        public List<FigmaDiagnostic> Diagnostics = new List<FigmaDiagnostic>();

        /// <summary>Roles that produced an alias line.</summary>
        public int MappedRoles;

        /// <summary>Mapped roles whose Figma token exists in the import (manifest or token sheets).</summary>
        public int VerifiedRoles;

        /// <summary>Every valid, distinct role in the profile, mapped or not.</summary>
        public int TotalRoles;
    }

    /// <summary>
    /// Generates the game-owned <c>cuvara-theme-bridge.uss</c>: one alias per theme role,
    /// <c>--cuvara-x: var(--figma-y);</c>, inside <c>:root</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Aliases only.</b> The bridge never copies a value, so the connector's
    /// <c>tokens.uss</c> and mode sheets stay the single source and a re-push needs no
    /// regeneration unless a token is renamed. The bridge is written outside the connector folder
    /// (<see cref="FigmaPaths.ValidateBridgeFolder"/>); connector files are only read.</para>
    ///
    /// <para><b>Deterministic.</b> Lines sorted ordinally by Cuvara variable, UTF-8 without BOM,
    /// <c>\n</c> newlines, and no timestamp, so the same profile always yields the same bytes.
    /// <see cref="WriteIfChanged"/> skips the write when the bytes already match, which keeps the
    /// asset import from re-triggering itself, as <c>UxmlBindingPipeline.RegenerateIfChanged</c>
    /// does for bindings.</para>
    ///
    /// <para>Unity-free: System.IO only.</para>
    /// </remarks>
    public static class FigmaTokenBridge
    {
        public const string CodeRoleInvalid       = "cuvara.bridge.role-invalid";
        public const string CodeRoleDuplicate     = "cuvara.bridge.role-duplicate";
        public const string CodeRoleUnmapped      = "cuvara.bridge.role-unmapped";
        public const string CodeFigmaTokenInvalid = "cuvara.bridge.figma-token-invalid";
        public const string CodeFigmaTokenUnknown = "cuvara.bridge.figma-token-unknown";
        public const string CodeTokensUnverified  = "cuvara.bridge.tokens-unverified";
        public const string CodeNoRoles           = "cuvara.bridge.no-roles";

        /// <summary>
        /// The theme roles a new profile starts with. The authoritative list is the variable set
        /// in <c>Runtime/Theme/CuvaraTheme.uss</c>; this constant mirrors it for profiles.
        /// </summary>
        public static readonly IReadOnlyList<string> DefaultRoles = new[]
        {
            "--cuvara-color-surface",
            "--cuvara-color-surface-raised",
            "--cuvara-color-on-surface",
            "--cuvara-color-primary",
            "--cuvara-color-on-primary",
            "--cuvara-color-scrim",
            "--cuvara-color-toast-info",
            "--cuvara-color-toast-success",
            "--cuvara-color-toast-warning",
            "--cuvara-color-toast-error",
            "--cuvara-color-on-toast",
            "--cuvara-radius-sm",
            "--cuvara-radius-md",
            "--cuvara-space-xs",
            "--cuvara-space-sm",
            "--cuvara-space-md",
            "--cuvara-space-lg",
            "--cuvara-space-xl",
            "--cuvara-font-size-sm",
            "--cuvara-font-size-md",
        };

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private static readonly Regex CuvaraTokenPattern = new Regex("^--cuvara-[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant);
        private static readonly Regex TokenPattern       = new Regex("^--[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant);

        /// <summary>The default role list with no Figma token mapped yet.</summary>
        public static List<FigmaTokenRole> CreateDefaultRoles()
        {
            var roles = new List<FigmaTokenRole>(DefaultRoles.Count);
            foreach (var role in DefaultRoles) roles.Add(new FigmaTokenRole(role, string.Empty));
            return roles;
        }

        /// <summary>Trims and adds the leading <c>--</c> when it is missing. Empty stays empty.</summary>
        public static string NormalizeToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return string.Empty;
            var trimmed = token.Trim();
            return trimmed.StartsWith("--", StringComparison.Ordinal) ? trimmed : "--" + trimmed;
        }

        /// <summary>Every custom property declared in a USS text, for example a connector <c>tokens.uss</c>.</summary>
        public static HashSet<string> ParseDeclaredTokens(string ussText)
        {
            var tokens = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(ussText)) return tokens;

            // Only real declarations count: the USS parser drops comments and keeps quoted
            // strings inside values, so "--x:" in a comment or in content: "--x:" declares nothing.
            foreach (var rule in FigmaUss.Parse(ussText).Rules)
            {
                foreach (var declaration in rule.Declarations)
                {
                    if (TokenPattern.IsMatch(declaration.Property)) tokens.Add(declaration.Property);
                }
            }

            return tokens;
        }

        /// <summary>
        /// Builds the bridge text for a profile's roles.
        /// </summary>
        /// <param name="profileName">Written into the header comment only.</param>
        /// <param name="roles">The profile's role map. Empty Figma tokens are skipped.</param>
        /// <param name="knownFigmaTokens">Tokens the import declares (manifest token list plus
        /// token sheets), or null when nothing could be read; then no token can be verified.</param>
        /// <param name="file">The path diagnostics point at.</param>
        public static FigmaTokenBridgeResult Build(string profileName, IEnumerable<FigmaTokenRole> roles, ICollection<string> knownFigmaTokens, string file)
        {
            var result = new FigmaTokenBridgeResult();
            var lines  = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var seen   = new HashSet<string>(StringComparer.Ordinal);

            if (roles != null)
            {
                foreach (var role in roles)
                {
                    if (role == null) continue;

                    var cuvara = (role.cuvaraToken ?? string.Empty).Trim();
                    if (!CuvaraTokenPattern.IsMatch(cuvara))
                    {
                        result.Diagnostics.Add(FigmaPaths.Error(CodeRoleInvalid, file,
                            $"Role '{cuvara}' is not a Cuvara theme variable (expected --cuvara-<name>, lowercase)."));
                        continue;
                    }

                    if (!seen.Add(cuvara))
                    {
                        result.Diagnostics.Add(FigmaPaths.Error(CodeRoleDuplicate, file,
                            $"Role '{cuvara}' is mapped more than once; the first row is used."));
                        continue;
                    }

                    result.TotalRoles++;

                    var figma = NormalizeToken(role.figmaToken);
                    if (figma.Length == 0)
                    {
                        result.Diagnostics.Add(FigmaPaths.Info(CodeRoleUnmapped, file,
                            $"Role '{cuvara}' has no Figma token; it keeps its theme default."));
                        continue;
                    }

                    if (!TokenPattern.IsMatch(figma))
                    {
                        result.Diagnostics.Add(FigmaPaths.Error(CodeFigmaTokenInvalid, file,
                            $"Figma token '{figma}' for role '{cuvara}' is not a USS custom property name."));
                        continue;
                    }

                    lines.Add(cuvara, figma);
                    result.MappedRoles++;

                    if (knownFigmaTokens == null) continue;
                    if (knownFigmaTokens.Contains(figma))
                    {
                        result.VerifiedRoles++;
                    }
                    else
                    {
                        result.Diagnostics.Add(FigmaPaths.Warning(CodeFigmaTokenUnknown, file,
                            $"Figma token '{figma}' (role '{cuvara}') is not in the import's token list or tokens.uss. The alias resolves to nothing until the token exists."));
                    }
                }
            }

            if (knownFigmaTokens == null && result.MappedRoles > 0)
            {
                result.Diagnostics.Add(FigmaPaths.Warning(CodeTokensUnverified, file,
                    "No .figma-manifest.json or tokens.uss could be read, so no Figma token was verified."));
            }

            if (result.MappedRoles == 0)
            {
                result.Diagnostics.Add(FigmaPaths.Warning(CodeNoRoles, file,
                    "No role maps to a Figma token; the bridge declares nothing."));
            }

            var builder = new StringBuilder();
            builder.Append("/* Generated by Cuvara UI Toolkit from the Figma profile \"")
                   .Append(SafeComment(profileName))
                   .Append("\". Do not edit: change the profile and regenerate. */\n");
            builder.Append(":root {\n");
            foreach (var line in lines)
            {
                builder.Append("    ").Append(line.Key).Append(": var(").Append(line.Value).Append(");\n");
            }

            builder.Append("}\n");
            result.Content = builder.ToString();
            return result;
        }

        /// <summary>The exact bytes <see cref="WriteIfChanged"/> writes.</summary>
        public static byte[] GetBytes(string content)
        {
            return Utf8NoBom.GetBytes(content ?? string.Empty);
        }

        /// <summary>Compares the file on disk with freshly generated content.</summary>
        public static FigmaBridgeFreshness CompareWithDisk(string absolutePath, string content)
        {
            if (!File.Exists(absolutePath)) return FigmaBridgeFreshness.Missing;
            return BytesEqual(File.ReadAllBytes(absolutePath), GetBytes(content)) ? FigmaBridgeFreshness.Fresh : FigmaBridgeFreshness.Stale;
        }

        /// <summary>
        /// Writes the bridge, creating its folder if needed, unless the file already holds the
        /// same bytes.
        /// </summary>
        /// <returns>True when the file was created or changed.</returns>
        public static bool WriteIfChanged(string absolutePath, string content)
        {
            var bytes = GetBytes(content);
            if (File.Exists(absolutePath) && BytesEqual(File.ReadAllBytes(absolutePath), bytes)) return false;

            var directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllBytes(absolutePath, bytes);
            return true;
        }

        private static string SafeComment(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace("*/", "* /").Replace("\r", " ").Replace("\n", " ");
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            for (var i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i]) return false;
            }

            return true;
        }
    }
}
