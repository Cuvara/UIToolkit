namespace Cuvara.UIToolkit.FigmaCore
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// What this package reads from the connector's <c>.figma-manifest.json</c>: the file
    /// name, the screens and the files each one generated, and the token names in
    /// <c>tokens.uss</c>. Everything else in the manifest (node records, write-back
    /// baselines) is the connector's business and is ignored.
    /// </summary>
    public sealed class FigmaManifestInfo
    {
        /// <summary>The Figma file name (<c>fileName</c>), or empty.</summary>
        public string FileName;

        public IReadOnlyList<FigmaManifestScreen> Screens;

        /// <summary>Token custom-property names (<c>--x</c>) from the folder's
        /// <c>tokens[]</c>, plus older manifests' per-screen copies.</summary>
        public IReadOnlyList<string> TokenNames;
    }

    /// <summary>One imported screen (a Figma frame).</summary>
    public sealed class FigmaManifestScreen
    {
        /// <summary>Base name of the screen's <c>.uxml</c> / <c>.uss</c>.</summary>
        public string AssetName;

        /// <summary>Stable Figma node id of the frame; survives renames.</summary>
        public string RootNodeId;

        /// <summary>Files the import of this screen wrote, relative to the import folder.</summary>
        public IReadOnlyList<string> Generated;
    }

    /// <summary>
    /// Reads <c>.figma-manifest.json</c> (connector 0.3.0-exp.1, written by
    /// <c>JsonUtility</c>). Tolerant by design, because the connector is experimental:
    /// unknown fields are ignored, missing ones read as empty, and a field of the wrong
    /// type reads as missing.
    /// </summary>
    public static class FigmaManifestReader
    {
        /// <summary>The connector's hidden manifest file name in an import folder.</summary>
        public const string ManifestFileName = ".figma-manifest.json";

        /// <exception cref="FormatException">The text is not JSON (or not a JSON object).</exception>
        public static FigmaManifestInfo Read(string jsonText)
        {
            var root = FigmaJson.Parse(jsonText);
            if (!(root is Dictionary<string, object>))
            {
                throw new FormatException("Invalid manifest: the top-level JSON value is not an object.");
            }

            var tokenNames = new List<string>();
            var seenTokens = new HashSet<string>(StringComparer.Ordinal);
            AddTokenNames(FigmaJson.MemberArray(root, "tokens"), tokenNames, seenTokens);

            var screens = new List<FigmaManifestScreen>();
            foreach (var screenNode in FigmaJson.MemberArray(root, "screens"))
            {
                if (!(screenNode is Dictionary<string, object>)) continue;

                var generated = new List<string>();
                foreach (var entry in FigmaJson.MemberArray(screenNode, "generated"))
                {
                    if (entry is string path && path.Length > 0) generated.Add(path);
                }

                // Older manifests kept a token copy per screen; the folder list wins, these fill gaps.
                AddTokenNames(FigmaJson.MemberArray(screenNode, "tokens"), tokenNames, seenTokens);

                screens.Add(new FigmaManifestScreen
                {
                    AssetName = FigmaJson.MemberString(screenNode, "assetName") ?? string.Empty,
                    RootNodeId = FigmaJson.MemberString(screenNode, "rootNodeId") ?? string.Empty,
                    Generated = generated,
                });
            }

            return new FigmaManifestInfo
            {
                FileName = FigmaJson.MemberString(root, "fileName") ?? string.Empty,
                Screens = screens,
                TokenNames = tokenNames,
            };
        }

        private static void AddTokenNames(List<object> records, List<string> names, HashSet<string> seen)
        {
            foreach (var record in records)
            {
                var name = FigmaJson.MemberString(record, "name");
                if (!string.IsNullOrEmpty(name) && seen.Add(name)) names.Add(name);
            }
        }
    }
}
