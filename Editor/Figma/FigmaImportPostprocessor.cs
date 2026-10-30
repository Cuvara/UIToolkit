namespace Cuvara.UIToolkit.Editor.Figma
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;

    /// <summary>
    /// Re-validates a profile when the connector writes into its import folder, and logs one
    /// Console line per profile.
    /// </summary>
    /// <remarks>
    /// <para><b>What triggers it.</b> <c>.uxml</c> and <c>.uss</c> imports, moves and deletions
    /// under a profile's connector folder; a push rewrites those every time. The hidden
    /// <c>.figma-manifest.json</c> is listed too, but Unity does not import dot-files, so in
    /// practice the generated sheets carry the signal.</para>
    ///
    /// <para><b>No loop.</b> Validation only reads, and nothing is ever written into a connector
    /// folder, so validation cannot cause another import. The token bridge is not regenerated here:
    /// it lives outside the connector folder and changes only on request.</para>
    ///
    /// <para><b>Debounced.</b> A push imports dozens of files in one or more batches. The work is
    /// scheduled once through <c>EditorApplication.delayCall</c>, after the import, and runs once
    /// for all profiles touched in the meantime.</para>
    /// </remarks>
    internal sealed class FigmaImportPostprocessor : AssetPostprocessor
    {
        private static readonly HashSet<string> PendingProfiles = new HashSet<string>(StringComparer.Ordinal);
        private static bool scheduled;

        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (!AnyRelevant(importedAssets) && !AnyRelevant(deletedAssets) && !AnyRelevant(movedAssets) && !AnyRelevant(movedFromAssetPaths))
                return;

            var profiles = FigmaProfile.FindAll();
            foreach (var profile in profiles)
            {
                var folder = profile.ConnectorFolder;
                if (folder.Length == 0) continue;
                if (Touches(importedAssets, folder) || Touches(deletedAssets, folder) || Touches(movedAssets, folder) || Touches(movedFromAssetPaths, folder))
                    PendingProfiles.Add(AssetDatabase.GetAssetPath(profile));
            }

            if (PendingProfiles.Count == 0 || scheduled) return;
            scheduled = true;
            EditorApplication.delayCall += Flush;
        }

        private static void Flush()
        {
            scheduled = false;
            if (PendingProfiles.Count == 0) return;

            var wanted = new HashSet<string>(PendingProfiles, StringComparer.Ordinal);
            PendingProfiles.Clear();

            var profiles = FigmaProfile.FindAll();
            var validated = 0;
            foreach (var profile in profiles)
            {
                if (!wanted.Contains(AssetDatabase.GetAssetPath(profile))) continue;
                var report = FigmaProfileValidator.Validate(profile, profiles);
                FigmaProfileValidator.LogSummary(report);
                validated++;
            }

            if (validated > 0) FigmaDiagnosticsWindow.RefreshOpenWindows();
        }

        private static bool AnyRelevant(string[] paths)
        {
            if (paths == null) return false;
            foreach (var path in paths)
            {
                if (IsRelevant(path)) return true;
            }

            return false;
        }

        private static bool Touches(string[] paths, string folder)
        {
            if (paths == null) return false;
            foreach (var path in paths)
            {
                if (IsRelevant(path) && FigmaPaths.IsSameOrInside(path, folder)) return true;
            }

            return false;
        }

        private static bool IsRelevant(string path)
        {
            return !string.IsNullOrEmpty(path)
                && (path.EndsWith(".uxml", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".uss", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(FigmaPaths.ManifestFileName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
