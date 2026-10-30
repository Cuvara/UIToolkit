namespace Cuvara.UIToolkit.Editor.Figma
{
    using System;
    using System.Collections.Generic;
    using Cuvara.UIToolkit.FigmaCore;

    /// <summary>
    /// Project-path rules for the Figma integration: which folder the connector owns, and where
    /// the game's own files may go.
    /// </summary>
    /// <remarks>
    /// <para>Unity-free: paths are project-relative strings (<c>Assets/...</c>) compared as
    /// segments, so <c>Assets/Figma/Game</c> does not contain <c>Assets/Figma/GameHud</c>.</para>
    /// </remarks>
    public static class FigmaPaths
    {
        /// <summary>The hidden ledger the connector keeps in every import folder.</summary>
        public const string ManifestFileName = ".figma-manifest.json";

        /// <summary>The connector's design-token sheet in an import folder.</summary>
        public const string TokensFileName = "tokens.uss";

        /// <summary>The file the token bridge writes into the profile's bridge folder.</summary>
        public const string BridgeFileName = "cuvara-theme-bridge.uss";

        public const string CodeBridgeFolderMissing   = "cuvara.bridge.folder-missing";
        public const string CodeBridgeNotGameOwned    = "cuvara.bridge.folder-not-game-owned";
        public const string CodeBridgeInsideConnector = "cuvara.bridge.folder-inside-connector";
        public const string CodeConnectorFolderInvalid = "cuvara.profile.connector-folder-invalid";

        /// <summary>Forward slashes, no surrounding blanks, no trailing slash. Null stays empty.</summary>
        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;

            var normalized = path.Trim().Replace('\\', '/');
            while (normalized.Contains("//")) normalized = normalized.Replace("//", "/");

            // Resolve "." and ".." so containment checks cannot be escaped with
            // "Assets/../Packages". A ".." that climbs above the start is kept, so the
            // result is never "under Assets".
            var segments = new List<string>();
            foreach (var segment in normalized.Split('/'))
            {
                if (segment.Length == 0 || segment == ".") continue;
                if (segment == ".." && segments.Count > 0 && segments[segments.Count - 1] != "..")
                {
                    segments.RemoveAt(segments.Count - 1);
                    continue;
                }

                segments.Add(segment);
            }

            return string.Join("/", segments);
        }

        /// <summary>True when <paramref name="path"/> is <paramref name="folder"/> or lies under it.</summary>
        public static bool IsSameOrInside(string path, string folder)
        {
            var p = Normalize(path);
            var f = Normalize(folder);
            if (p.Length == 0 || f.Length == 0) return false;

            // Unity asset paths are case-insensitive on the platforms the Editor runs on.
            if (string.Equals(p, f, StringComparison.OrdinalIgnoreCase)) return true;
            return p.Length > f.Length
                && p.StartsWith(f, StringComparison.OrdinalIgnoreCase)
                && p[f.Length] == '/';
        }

        /// <summary>True for <c>Assets</c> and anything under it: the only place a game owns files.</summary>
        public static bool IsUnderAssets(string path)
        {
            return IsSameOrInside(path, "Assets");
        }

        /// <summary><c>&lt;bridge folder&gt;/cuvara-theme-bridge.uss</c>.</summary>
        public static string BridgePath(string bridgeFolder)
        {
            return Normalize(bridgeFolder) + "/" + BridgeFileName;
        }

        /// <summary>
        /// Checks a profile's bridge folder: it must be game-owned (under <c>Assets/</c>) and
        /// outside every connector import folder, because the connector rewrites that folder on
        /// each push and write-back treats foreign edits there as design changes.
        /// </summary>
        public static IReadOnlyList<FigmaDiagnostic> ValidateBridgeFolder(string bridgeFolder, IEnumerable<string> connectorFolders, string file)
        {
            var diagnostics = new List<FigmaDiagnostic>();
            var folder = Normalize(bridgeFolder);

            if (folder.Length == 0)
            {
                diagnostics.Add(Error(CodeBridgeFolderMissing, file, "The profile has no bridge output folder. Choose a folder your game owns, outside the Figma import folder."));
                return diagnostics;
            }

            if (!IsUnderAssets(folder))
            {
                diagnostics.Add(Error(CodeBridgeNotGameOwned, file, $"Bridge folder '{folder}' is not under Assets/. The bridge is a game-owned file."));
            }

            if (connectorFolders != null)
            {
                foreach (var connector in connectorFolders)
                {
                    if (Normalize(connector).Length == 0 || !IsSameOrInside(folder, connector)) continue;
                    diagnostics.Add(Error(CodeBridgeInsideConnector, file,
                        $"Bridge folder '{folder}' is inside the Figma import folder '{Normalize(connector)}'. The connector owns that folder; choose one outside it."));
                }
            }

            return diagnostics;
        }

        /// <summary>
        /// Resolves a file named relative to a folder, such as a profile mode sheet relative to
        /// the connector folder. Refuses rooted paths (<c>/x</c>, <c>\\server</c>, <c>C:</c>), any
        /// <c>..</c> segment and anything that would not end up strictly inside
        /// <paramref name="folder"/>, so a profile can never make a tool read a file outside it.
        /// </summary>
        /// <returns>True with the normalized project path, or false with an empty path.</returns>
        public static bool TryResolveInside(string folder, string relativePath, out string path)
        {
            path = string.Empty;
            var root = Normalize(folder);
            if (root.Length == 0 || string.IsNullOrWhiteSpace(relativePath)) return false;

            var relative = relativePath.Trim().Replace('\\', '/');
            if (relative.StartsWith("/", StringComparison.Ordinal) || relative.IndexOf(':') >= 0) return false;
            foreach (var segment in relative.Split('/'))
            {
                if (segment == "..") return false;
            }

            var candidate = Normalize(root + "/" + relative);
            if (candidate.Length <= root.Length || !IsSameOrInside(candidate, root)) return false;

            path = candidate;
            return true;
        }

        /// <summary>A connector folder must be a project folder under <c>Assets/</c>.</summary>
        public static IReadOnlyList<FigmaDiagnostic> ValidateConnectorFolder(string connectorFolder, string file)
        {
            var diagnostics = new List<FigmaDiagnostic>();
            var folder = Normalize(connectorFolder);
            if (folder.Length == 0 || !IsUnderAssets(folder) || string.Equals(folder, "Assets", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(Error(CodeConnectorFolderInvalid, file,
                    $"Connector import folder '{folder}' must be a folder under Assets/, for example Assets/Figma/<file>."));
            }

            return diagnostics;
        }

        internal static FigmaDiagnostic Error(string code, string file, string message)
        {
            return Make(code, FigmaDiagnosticSeverity.Error, file, message);
        }

        internal static FigmaDiagnostic Warning(string code, string file, string message)
        {
            return Make(code, FigmaDiagnosticSeverity.Warning, file, message);
        }

        internal static FigmaDiagnostic Info(string code, string file, string message)
        {
            return Make(code, FigmaDiagnosticSeverity.Info, file, message);
        }

        private static FigmaDiagnostic Make(string code, FigmaDiagnosticSeverity severity, string file, string message)
        {
            return new FigmaDiagnostic { Code = code, Severity = severity, File = file ?? string.Empty, Line = 0, Message = message };
        }
    }
}
