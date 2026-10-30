namespace Cuvara.UIToolkit.Editor.Figma
{
    using System.Collections.Generic;
    using Cuvara.UIToolkit.FigmaCore;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// One place to see whether the Figma integration is healthy: installed versions against the
    /// tested range, the connector's active mapping asset, and per profile the import, validator
    /// results, token coverage and bridge freshness. Actions: apply the mapping preset, regenerate
    /// the token bridges, re-validate.
    /// </summary>
    public sealed class FigmaDiagnosticsWindow : EditorWindow
    {
        /// <summary>The connector version this integration was written and tested against.</summary>
        public const string TestedConnectorVersion = "0.3.0-exp.1";

        /// <summary>The Pipeline version that connector depends on.</summary>
        public const string TestedPipelineVersion = "0.9.1-exp.1";

        private const int MaxDiagnosticsPerProfile = 50;

        private ScrollView content;

        [MenuItem("Window/Cuvara/Figma Diagnostics")]
        public static void Open()
        {
            var window = GetWindow<FigmaDiagnosticsWindow>();
            window.titleContent = new GUIContent("Figma Diagnostics");
            window.minSize = new Vector2(420, 300);
        }

        [MenuItem("Tools/Cuvara/Figma/Regenerate Token Bridges")]
        public static void RegenerateAllBridges()
        {
            var profiles = FigmaProfile.FindAll();
            var written = 0;
            foreach (var profile in profiles)
            {
                var diagnostics = FigmaProfileValidator.RegenerateBridge(profile, profiles, out var wrote);
                FigmaMappingPresetCommand.LogDiagnostics($"Token bridge '{profile.name}'", Problems(diagnostics));
                if (wrote) written++;
            }

            Debug.Log($"Cuvara token bridge: {written} of {profiles.Count} profile bridge file(s) written; the others were already up to date or refused.");
            RefreshOpenWindows();
        }

        [MenuItem("Tools/Cuvara/Figma/Validate Profiles")]
        public static void ValidateAllProfiles()
        {
            FigmaProfileValidator.ValidateAll(log: true);
            RefreshOpenWindows();
        }

        /// <summary>Rebuilds every open diagnostics window. Cheap when none is open.</summary>
        public static void RefreshOpenWindows()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<FigmaDiagnosticsWindow>()) window.Rebuild();
        }

        public void CreateGUI()
        {
            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.flexWrap = Wrap.Wrap;
            toolbar.Add(new Button(() => { FigmaMappingPresetCommand.ApplyFromMenu(); }) { text = "Apply mapping preset" });
            toolbar.Add(new Button(RegenerateAllBridges) { text = "Regenerate token bridges" });
            toolbar.Add(new Button(ValidateAllProfiles) { text = "Re-validate" });
            this.rootVisualElement.Add(toolbar);

            this.content = new ScrollView();
            this.content.style.flexGrow = 1;
            this.rootVisualElement.Add(this.content);

            this.Rebuild();
        }

        private void Rebuild()
        {
            if (this.content == null) return;
            this.content.Clear();

            this.AddVersions();
            this.AddMapping();

            var reports = FigmaProfileValidator.ValidateAll(log: false);
            this.AddHeader($"Profiles ({reports.Count})");
            if (reports.Count == 0)
            {
                this.AddLine("No FigmaProfile asset. Create one with Assets > Create > Cuvara > UI Toolkit > Figma Profile.");
            }

            foreach (var report in reports) this.AddProfile(report);
        }

        private void AddVersions()
        {
            this.AddHeader("Versions");
            this.AddLine($"Unity {Application.unityVersion}");
            this.AddLine(VersionLine("com.unity.ui.figma", TestedConnectorVersion));
            this.AddLine(VersionLine("com.unity.pipeline", TestedPipelineVersion));
        }

        private static string VersionLine(string packageName, string tested)
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForPackageName(packageName);
            if (info == null) return $"{packageName}: not installed (tested: {tested})";
            var status = info.version == tested ? "tested" : "NOT the tested version; behaviour may differ";
            return $"{packageName} {info.version} ({status}; tested: {tested})";
        }

        private void AddMapping()
        {
            this.AddHeader("Connector mapping");
            var diagnostics = new List<FigmaDiagnostic>();
            var info = FigmaMappingPresetCommand.LocateActive(diagnostics);
            if (info != null)
            {
                this.AddLine($"Active mapping asset: {info.Path} ({info.Selection})");
                this.AddLine($"Class prefix: {(string.IsNullOrEmpty(info.ClassPrefix) ? "(empty)" : info.ClassPrefix)}; control rows: {info.RowCount}");
                diagnostics.AddRange(FigmaMappingPresetCommand.ValidateControlTypes(info));
            }

            foreach (var diagnostic in diagnostics) this.AddDiagnostic(diagnostic);
        }

        private void AddProfile(FigmaProfileReport report)
        {
            var profile = report.Profile;
            if (profile == null) return;

            this.AddHeader($"{profile.name}  ({report.ProfilePath})");
            this.AddLine($"Connector folder: {profile.ConnectorFolder} ({(report.ConnectorFolderFound ? "found" : "missing")})");
            this.AddLine($"Manifest: {(report.ManifestFound ? "found" : "missing")}; screens: {report.ScreenCount}");
            if (report.Manifest?.Screens != null)
            {
                foreach (var screen in report.Manifest.Screens)
                {
                    if (screen != null) this.AddLine($"    {screen.AssetName}  ({screen.RootNodeId})");
                }
            }

            this.AddLine($"Expected class prefix: {(profile.ClassPrefix.Length == 0 ? "(none)" : profile.ClassPrefix)}");
            var bridge = report.Bridge;
            this.AddLine($"Token coverage: {bridge?.VerifiedRoles ?? 0} verified / {bridge?.MappedRoles ?? 0} mapped / {bridge?.TotalRoles ?? 0} roles");
            this.AddLine($"Bridge: {profile.BridgePath} ({report.BridgeFreshness.ToString().ToLowerInvariant()})");
            this.AddLine($"Errors: {report.Count(FigmaDiagnosticSeverity.Error)}; warnings: {report.Count(FigmaDiagnosticSeverity.Warning)}");

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.Add(new Button(() => Selection.activeObject = profile) { text = "Select profile" });
            row.Add(new Button(() =>
            {
                FigmaMappingPresetCommand.LogDiagnostics("Cuvara mapping preset", FigmaMappingPresetCommand.Apply(profile, out _));
                RefreshOpenWindows();
            }) { text = "Apply preset with this prefix" });
            row.Add(new Button(() =>
            {
                var diagnostics = FigmaProfileValidator.RegenerateBridge(profile, FigmaProfile.FindAll(), out _);
                FigmaMappingPresetCommand.LogDiagnostics($"Token bridge '{profile.name}'", Problems(diagnostics));
                RefreshOpenWindows();
            }) { text = "Regenerate bridge" });
            this.content.Add(row);

            var shown = 0;
            foreach (var diagnostic in report.Diagnostics)
            {
                if (diagnostic == null || diagnostic.Severity == FigmaDiagnosticSeverity.Info) continue;
                if (shown++ == MaxDiagnosticsPerProfile)
                {
                    this.AddLine("    ... more in the Console after Re-validate.");
                    break;
                }

                this.AddDiagnostic(diagnostic);
            }
        }

        private static IEnumerable<FigmaDiagnostic> Problems(IEnumerable<FigmaDiagnostic> diagnostics)
        {
            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic != null && diagnostic.Severity != FigmaDiagnosticSeverity.Info) yield return diagnostic;
            }
        }

        private void AddHeader(string text)
        {
            var label = new Label(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = 8;
            this.content.Add(label);
        }

        private void AddLine(string text)
        {
            var label = new Label(text);
            label.style.whiteSpace = WhiteSpace.Normal;
            this.content.Add(label);
        }

        private void AddDiagnostic(FigmaDiagnostic diagnostic)
        {
            var where = string.IsNullOrEmpty(diagnostic.File) ? string.Empty : $"  {diagnostic.File}{(diagnostic.Line > 0 ? ":" + diagnostic.Line : string.Empty)}";
            this.AddLine($"    {diagnostic.Severity} [{diagnostic.Code}] {diagnostic.Message}{where}");
        }
    }
}
