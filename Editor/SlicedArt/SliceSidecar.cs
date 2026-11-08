using System;
using System.IO;
using UnityEngine;

namespace Cuvara.UIToolkit.Editor.SlicedArt
{
    /// <summary>
    /// Contents of <c>&lt;name&gt;.slice.json</c>:
    /// <c>{"version":1,"border":[left,top,right,bottom],"pixelsPerUnit":100,"maxSize":2048,"group":"..."}</c>.
    /// </summary>
    [Serializable]
    public sealed class SliceSidecar
    {
        public const string Suffix = ".slice.json";

        public int version = 1;
        public float[] border;
        public float pixelsPerUnit = 100f;
        public int maxSize = 2048;
        public string group;

        /// <summary>Sidecar path for a PNG asset path, or null when the path is not a PNG.</summary>
        public static string SidecarPathFor(string pngAssetPath)
        {
            if (string.IsNullOrEmpty(pngAssetPath) || !pngAssetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                return null;
            return pngAssetPath.Substring(0, pngAssetPath.Length - 4) + Suffix;
        }

        /// <summary>PNG asset path for a sidecar path, or null when the path is not a sidecar.</summary>
        public static string PngPathFor(string sidecarAssetPath)
        {
            if (string.IsNullOrEmpty(sidecarAssetPath) || !sidecarAssetPath.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase))
                return null;
            return sidecarAssetPath.Substring(0, sidecarAssetPath.Length - Suffix.Length) + ".png";
        }

        /// <summary>Reads and validates a sidecar from an asset path (relative to the project root).</summary>
        public static bool TryLoad(string sidecarAssetPath, out SliceSidecar sidecar, out string error)
        {
            sidecar = null;
            error = null;
            try
            {
                var full = Path.Combine(SlicedArtSettings.ProjectRoot, sidecarAssetPath);
                if (!File.Exists(full))
                {
                    error = "Sidecar not found: " + sidecarAssetPath;
                    return false;
                }
                sidecar = JsonUtility.FromJson<SliceSidecar>(File.ReadAllText(full));
            }
            catch (Exception e)
            {
                error = "Cannot parse " + sidecarAssetPath + ": " + e.Message;
                return false;
            }

            if (sidecar == null || sidecar.border == null || sidecar.border.Length != 4)
            {
                error = sidecarAssetPath + ": \"border\" must be [left, top, right, bottom].";
                sidecar = null;
                return false;
            }
            for (var i = 0; i < 4; i++)
            {
                if (sidecar.border[i] < 0f)
                {
                    error = sidecarAssetPath + ": \"border\" values must be >= 0.";
                    sidecar = null;
                    return false;
                }
            }
            if (sidecar.pixelsPerUnit <= 0f)
                sidecar.pixelsPerUnit = 100f;
            if (sidecar.maxSize <= 0)
                sidecar.maxSize = 2048;
            return true;
        }

        /// <summary>
        /// The order of TextureImporter.spriteBorder is (left, bottom, right, top);
        /// the sidecar order is [left, top, right, bottom].
        /// </summary>
        public Vector4 ToSpriteBorder()
        {
            return new Vector4(border[0], border[3], border[2], border[1]);
        }
    }
}
