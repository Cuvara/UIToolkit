namespace Cuvara.UIToolkit.Editor.Figma
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Cuvara.UIToolkit.FigmaCore;
    using UnityEditor;
    using UnityEngine;

    /// <summary>Everything the diagnostics window shows for one profile.</summary>
    public sealed class FigmaProfileReport
    {
        public FigmaProfile Profile;
        public string ProfilePath;
        public bool ConnectorFolderFound;
        public bool ManifestFound;
        public FigmaManifestInfo Manifest;
        public FigmaTokenBridgeResult Bridge;
        public FigmaBridgeFreshness BridgeFreshness;
        public List<FigmaDiagnostic> Diagnostics = new List<FigmaDiagnostic>();

        public int ScreenCount => this.Manifest?.Screens?.Count ?? 0;

        public int Count(FigmaDiagnosticSeverity severity)
        {
            var count = 0;
            foreach (var diagnostic in this.Diagnostics)
            {
                if (diagnostic != null && diagnostic.Severity == severity) count++;
            }

            return count;
        }

        /// <summary>The one-line Console summary.</summary>
        public string Summary()
        {
            var name = this.Profile != null ? this.Profile.name : "(missing)";
            var folder = this.Profile != null ? this.Profile.ConnectorFolder : string.Empty;
            var bridge = this.Bridge;
            return $"Figma profile '{name}' ({folder}): manifest {(this.ManifestFound ? "found" : "missing")}, "
                + $"{this.ScreenCount} screen(s), {this.Count(FigmaDiagnosticSeverity.Error)} error(s), "
                + $"{this.Count(FigmaDiagnosticSeverity.Warning)} warning(s), "
                + $"tokens {bridge?.VerifiedRoles ?? 0}/{bridge?.MappedRoles ?? 0} verified of {bridge?.TotalRoles ?? 0} role(s), "
                + $"bridge {this.BridgeFreshness.ToString().ToLowerInvariant()}.";
        }
    }

    /// <summary>
    /// Validates a <see cref="FigmaProfile"/> against its connector import folder. Reads only;
    /// the connector folder is never written.
    /// </summary>
    public static class FigmaProfileValidator
    {
        public const string CodeConnectorFolderMissing = "cuvara.profile.connector-folder-missing";
        public const string CodeManifestMissing        = "cuvara.profile.manifest-missing";
        public const string CodeManifestUnreadable     = "cuvara.profile.manifest-unreadable";
        public const string CodeValidatorFailed        = "cuvara.profile.validator-failed";
        public const string CodeModeSheetMissing       = "cuvara.profile.mode-sheet-missing";
        public const string CodeScreensFolderInvalid   = "cuvara.profile.screens-folder-invalid";

        /// <summary>Validates one profile. <paramref name="allProfiles"/> supplies every connector folder the bridge must stay out of.</summary>
        public static FigmaProfileReport Validate(FigmaProfile profile, IReadOnlyList<FigmaProfile> allProfiles)
        {
            var report = new FigmaProfileReport { Profile = profile };
            if (profile == null) return report;

            var profilePath = AssetDatabase.GetAssetPath(profile);
            report.ProfilePath = profilePath;

            var folder = profile.ConnectorFolder;
            report.Diagnostics.AddRange(FigmaPaths.ValidateConnectorFolder(folder, profilePath));

            report.ConnectorFolderFound = folder.Length > 0 && Directory.Exists(folder);
            if (!report.ConnectorFolderFound)
            {
                report.Diagnostics.Add(FigmaPaths.Warning(CodeConnectorFolderMissing, profilePath,
                    $"Connector folder '{folder}' does not exist yet. Import the Figma file first, or fix the path."));
            }
            else
            {
                ReadManifest(report, folder);
                RunImportValidator(report, profile, folder);
                CheckModeSheets(report, profile, folder, profilePath);
            }

            BuildBridge(report, profile, allProfiles, profilePath);
            CheckScreensFolder(report, profile, allProfiles, profilePath);
            return report;
        }

        /// <summary>Validates every profile and logs one Console line per profile.</summary>
        public static List<FigmaProfileReport> ValidateAll(bool log)
        {
            var profiles = FigmaProfile.FindAll();
            var reports = new List<FigmaProfileReport>(profiles.Count);
            foreach (var profile in profiles)
            {
                var report = Validate(profile, profiles);
                reports.Add(report);
                if (log) LogSummary(report);
            }

            return reports;
        }

        /// <summary>One Console line. A warning when anything needs attention, never an error log.</summary>
        public static void LogSummary(FigmaProfileReport report)
        {
            if (report == null) return;
            var clean = report.Count(FigmaDiagnosticSeverity.Error) == 0 && report.Count(FigmaDiagnosticSeverity.Warning) == 0;
            if (clean) Debug.Log(report.Summary(), report.Profile);
            else Debug.LogWarning(report.Summary() + " Details: Window > Cuvara > Figma Diagnostics.", report.Profile);
        }

        /// <summary>
        /// Every token the import declares: the manifest's token list plus declarations in
        /// <c>tokens.uss</c> and the profile's mode sheets. Null when none of them could be read.
        /// </summary>
        public static HashSet<string> CollectKnownTokens(FigmaProfile profile, FigmaManifestInfo manifest)
        {
            HashSet<string> known = null;

            if (manifest?.TokenNames != null)
            {
                known = new HashSet<string>(StringComparer.Ordinal);
                foreach (var token in manifest.TokenNames)
                {
                    var normalized = FigmaTokenBridge.NormalizeToken(token);
                    if (normalized.Length > 0) known.Add(normalized);
                }
            }

            if (profile == null) return known;

            var sheets = new List<string> { FigmaPaths.TokensFileName };
            sheets.AddRange(profile.ModeSheets);
            foreach (var sheet in sheets)
            {
                if (string.IsNullOrWhiteSpace(sheet)) continue;
                var path = profile.ConnectorFolder + "/" + FigmaPaths.Normalize(sheet);
                if (!File.Exists(path)) continue;

                try
                {
                    known ??= new HashSet<string>(StringComparer.Ordinal);
                    known.UnionWith(FigmaTokenBridge.ParseDeclaredTokens(File.ReadAllText(path)));
                }
                catch (IOException)
                {
                    // Unreadable right now (the connector may be mid-write); the next pass reads it.
                }
            }

            return known;
        }

        /// <summary>
        /// Writes the profile's bridge file when its bytes changed. Refuses, and writes nothing,
        /// when the bridge folder breaks a path rule.
        /// </summary>
        /// <returns>The diagnostics; the bridge was written only if none is an error from the path rules.</returns>
        public static List<FigmaDiagnostic> RegenerateBridge(FigmaProfile profile, IReadOnlyList<FigmaProfile> allProfiles, out bool written)
        {
            written = false;
            var report = Validate(profile, allProfiles);
            var diagnostics = new List<FigmaDiagnostic>();
            if (profile == null) return diagnostics;

            foreach (var diagnostic in report.Diagnostics)
            {
                if (diagnostic.Code != null && diagnostic.Code.StartsWith("cuvara.bridge.", StringComparison.Ordinal)) diagnostics.Add(diagnostic);
            }

            if (HasBridgePathError(diagnostics) || report.Bridge == null) return diagnostics;

            written = FigmaTokenBridge.WriteIfChanged(profile.BridgePath, report.Bridge.Content);
            if (written) AssetDatabase.ImportAsset(profile.BridgePath);
            return diagnostics;
        }

        private static bool HasBridgePathError(List<FigmaDiagnostic> diagnostics)
        {
            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic.Severity != FigmaDiagnosticSeverity.Error) continue;
                if (diagnostic.Code == FigmaPaths.CodeBridgeFolderMissing
                    || diagnostic.Code == FigmaPaths.CodeBridgeNotGameOwned
                    || diagnostic.Code == FigmaPaths.CodeBridgeInsideConnector)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ReadManifest(FigmaProfileReport report, string folder)
        {
            var manifestPath = folder + "/" + FigmaPaths.ManifestFileName;
            if (!File.Exists(manifestPath))
            {
                report.Diagnostics.Add(FigmaPaths.Warning(CodeManifestMissing, manifestPath,
                    "No .figma-manifest.json in the connector folder: nothing has been imported here yet, or the folder was unlinked."));
                return;
            }

            try
            {
                report.Manifest = FigmaManifestReader.Read(File.ReadAllText(manifestPath));
                report.ManifestFound = true;
            }
            catch (Exception exception) when (exception is FormatException || exception is IOException)
            {
                report.Diagnostics.Add(FigmaPaths.Error(CodeManifestUnreadable, manifestPath,
                    $"The connector manifest could not be read: {exception.Message}"));
            }
        }

        private static void RunImportValidator(FigmaProfileReport report, FigmaProfile profile, string folder)
        {
            try
            {
                var options = new FigmaValidationOptions { ExpectedClassPrefix = profile.ClassPrefix };
                var results = FigmaImportValidator.ValidateFolder(folder, options);
                if (results != null)
                {
                    foreach (var diagnostic in results)
                    {
                        if (diagnostic != null) report.Diagnostics.Add(diagnostic);
                    }
                }
            }
            catch (Exception exception)
            {
                // A validator failure is a diagnostic, never an exception out of an import callback.
                report.Diagnostics.Add(FigmaPaths.Error(CodeValidatorFailed, folder,
                    $"The import validator failed: {exception.GetType().Name}: {exception.Message}"));
            }
        }

        private static void CheckModeSheets(FigmaProfileReport report, FigmaProfile profile, string folder, string profilePath)
        {
            foreach (var sheet in profile.ModeSheets)
            {
                if (string.IsNullOrWhiteSpace(sheet)) continue;
                var path = folder + "/" + FigmaPaths.Normalize(sheet);
                if (File.Exists(path)) continue;
                report.Diagnostics.Add(FigmaPaths.Warning(CodeModeSheetMissing, profilePath,
                    $"Mode sheet '{sheet}' is not in '{folder}'."));
            }
        }

        private static void BuildBridge(FigmaProfileReport report, FigmaProfile profile, IReadOnlyList<FigmaProfile> allProfiles, string profilePath)
        {
            var bridgeFolder = profile.BridgeOutputFolder;
            var pathDiagnostics = FigmaPaths.ValidateBridgeFolder(bridgeFolder, ConnectorFolders(profile, allProfiles), profilePath);
            report.Diagnostics.AddRange(pathDiagnostics);

            var alreadyInside = false;
            foreach (var diagnostic in pathDiagnostics) alreadyInside |= diagnostic.Code == FigmaPaths.CodeBridgeInsideConnector;

            // A connector folder no profile names is still the connector's: its manifest marks it.
            if (bridgeFolder.Length > 0
                && !alreadyInside
                && ScreenCreatorPlan.IsInsideConnectorImportFolder(bridgeFolder, Directory.GetCurrentDirectory()))
            {
                report.Diagnostics.Add(FigmaPaths.Error(FigmaPaths.CodeBridgeInsideConnector, profilePath,
                    $"Bridge folder '{bridgeFolder}' is inside a Figma import folder (a parent holds .figma-manifest.json). Choose a folder your game owns."));
            }

            var known = CollectKnownTokens(profile, report.Manifest);
            report.Bridge = FigmaTokenBridge.Build(profile.name, profile.TokenRoles, known, profile.BridgePath);
            report.Diagnostics.AddRange(report.Bridge.Diagnostics);
            report.BridgeFreshness = bridgeFolder.Length == 0
                ? FigmaBridgeFreshness.Missing
                : FigmaTokenBridge.CompareWithDisk(profile.BridgePath, report.Bridge.Content);
        }

        private static void CheckScreensFolder(FigmaProfileReport report, FigmaProfile profile, IReadOnlyList<FigmaProfile> allProfiles, string profilePath)
        {
            var screens = profile.ScreensFolder;
            if (screens.Length == 0) return;

            var inside = false;
            foreach (var connector in ConnectorFolders(profile, allProfiles)) inside |= FigmaPaths.IsSameOrInside(screens, connector);

            if (!FigmaPaths.IsUnderAssets(screens) || inside)
            {
                report.Diagnostics.Add(FigmaPaths.Warning(CodeScreensFolderInvalid, profilePath,
                    $"Screens folder '{screens}' must be under Assets/ and outside every Figma import folder."));
            }
        }

        private static List<string> ConnectorFolders(FigmaProfile profile, IReadOnlyList<FigmaProfile> allProfiles)
        {
            var folders = new List<string> { profile.ConnectorFolder };
            if (allProfiles == null) return folders;
            foreach (var other in allProfiles)
            {
                if (other != null && other != profile) folders.Add(other.ConnectorFolder);
            }

            return folders;
        }
    }
}
