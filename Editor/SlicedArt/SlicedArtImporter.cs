using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Cuvara.UIToolkit.Editor.SlicedArt
{
    /// <summary>
    /// Configures PNGs that have a sibling <c>&lt;name&gt;.slice.json</c> as 9-slice Sprites.
    /// Off by default: PNGs are touched only when <see cref="SlicedArtSettings.Enabled"/> is true and the PNG is
    /// under one of <see cref="SlicedArtSettings.Roots"/>. PNGs without a sidecar are never touched.
    /// </summary>
    public sealed class SlicedArtImporter : AssetPostprocessor
    {
        // Bump when the settings applied below change, so Unity reimports existing sprites (2: uncompressed art).
        public override uint GetVersion()
        {
            return 2;
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                return;
            if (!SlicedArtSettings.IsInScope(assetPath))
                return;

            var sidecarPath = SliceSidecar.SidecarPathFor(assetPath);
            if (!File.Exists(Path.Combine(SlicedArtSettings.ProjectRoot, sidecarPath)))
                return;

            if (!SliceSidecar.TryLoad(sidecarPath, out var sidecar, out var error))
            {
                Debug.LogError("[Cuvara.UIToolkit] " + error);
                return;
            }

            Apply((TextureImporter)assetImporter, sidecar);
        }

        public static void Apply(TextureImporter importer, SliceSidecar sidecar)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = sidecar.ToSpriteBorder();
            importer.spritePixelsPerUnit = sidecar.pixelsPerUnit;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            // block compression (DXT/ASTC) adds up to 30 levels of error on 1px ornaments: sliced art stays pixel-exact
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = sidecar.maxSize;
        }

        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            var imported = new HashSet<string>(importedAssets);
            var toReimport = new List<string>();

            foreach (var path in importedAssets)
                Collect(path, imported, toReimport);
            foreach (var path in movedAssets)
                Collect(path, imported, toReimport);

            foreach (var png in toReimport)
                AssetDatabase.ImportAsset(png, ImportAssetOptions.ForceUpdate);
        }

        static void Collect(string path, HashSet<string> imported, List<string> toReimport)
        {
            var png = SliceSidecar.PngPathFor(path);
            if (png == null || !SlicedArtSettings.IsInScope(path))
                return;
            // A PNG imported in the same batch already saw the sidecar in OnPreprocessTexture.
            if (imported.Contains(png) || toReimport.Contains(png))
                return;
            if (AssetDatabase.AssetPathToGUID(png).Length == 0)
                return;
            toReimport.Add(png);
        }
    }
}
