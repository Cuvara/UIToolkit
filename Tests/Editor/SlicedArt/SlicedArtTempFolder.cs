using System.IO;
using Cuvara.UIToolkit.Editor.SlicedArt;
using UnityEditor;
using UnityEngine;

namespace Cuvara.UIToolkit.Editor.Tests
{
    /// <summary>Creates and removes a temporary asset folder under Assets/.</summary>
    internal sealed class SlicedArtTempFolder
    {
        public const string Parent = "Assets";
        public readonly string AssetPath;

        public SlicedArtTempFolder(string name)
        {
            AssetPath = Parent + "/" + name;
            if (AssetDatabase.IsValidFolder(AssetPath))
                AssetDatabase.DeleteAsset(AssetPath);
            AssetDatabase.CreateFolder(Parent, name);
        }

        public string Write(string fileName, string text)
        {
            var path = AssetPath + "/" + fileName;
            File.WriteAllText(Path.Combine(SlicedArtSettings.ProjectRoot, path), text);
            return path;
        }

        public string WritePng(string fileName, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(200, 50, 50, 255);
            tex.SetPixels32(pixels);
            tex.Apply();
            var path = AssetPath + "/" + fileName;
            File.WriteAllBytes(Path.Combine(SlicedArtSettings.ProjectRoot, path), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            return path;
        }

        public void Delete()
        {
            if (AssetDatabase.IsValidFolder(AssetPath))
                AssetDatabase.DeleteAsset(AssetPath);
        }
    }
}
