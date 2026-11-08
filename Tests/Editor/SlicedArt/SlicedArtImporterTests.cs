using Cuvara.UIToolkit.Editor.SlicedArt;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Cuvara.UIToolkit.Editor.Tests
{
    public class SlicedArtImporterTests
    {
        SlicedArtTempFolder m_Folder;

        [SetUp]
        public void SetUp()
        {
            m_Folder = new SlicedArtTempFolder("CuvaraUIToolkitTests_SlicedArt");
            SlicedArtSettings.SetEnabled(true);
            SlicedArtSettings.SetRoots(new[] { m_Folder.AssetPath });
        }

        [TearDown]
        public void TearDown()
        {
            m_Folder.Delete();
            SlicedArtSettings.Reload();
        }

        static string Sidecar(string border, int maxSize = 512)
        {
            return "{\"version\":1,\"border\":" + border + ",\"pixelsPerUnit\":100,\"maxSize\":" + maxSize + ",\"group\":\"test\"}";
        }

        [Test]
        public void SidecarBorderIsMappedToUnityOrder()
        {
            // Sidecar order is [left, top, right, bottom]; spriteBorder is (left, bottom, right, top).
            var png = m_Folder.WritePng("panel.png", 32);
            m_Folder.Write("panel.slice.json", Sidecar("[4,3,2,1]"));
            AssetDatabase.Refresh();

            var importer = (TextureImporter)AssetImporter.GetAtPath(png);
            Assert.AreEqual(TextureImporterType.Sprite, importer.textureType);
            Assert.AreEqual(SpriteImportMode.Single, importer.spriteImportMode);
            Assert.AreEqual(new Vector4(4, 1, 2, 3), importer.spriteBorder);
            Assert.IsTrue(importer.alphaIsTransparency);
            Assert.IsFalse(importer.mipmapEnabled);
            Assert.AreEqual(FilterMode.Bilinear, importer.filterMode);
            Assert.AreEqual(TextureWrapMode.Clamp, importer.wrapMode);
            Assert.AreEqual(TextureImporterNPOTScale.None, importer.npotScale);
            Assert.AreEqual(TextureImporterCompression.Uncompressed, importer.textureCompression);
            Assert.AreEqual(512, importer.maxTextureSize);
            Assert.AreEqual(100f, importer.spritePixelsPerUnit);
        }

        [Test]
        public void PostprocessorVersionWasBumpedSoExistingCompressedArtIsReimported()
        {
            // 1 = block-compressed art; 2 = uncompressed. An unchanged PNG + sidecar is reprocessed only when this changes.
            Assert.GreaterOrEqual(new SlicedArtImporter().GetVersion(), 2u);
        }

        [Test]
        public void ReimportingACompressedTextureAppliesUncompressedAndKeepsTheSidecarMaxSize()
        {
            var png = m_Folder.WritePng("panel.png", 32);
            m_Folder.Write("panel.slice.json", Sidecar("[4,3,2,1]", 128));
            AssetDatabase.Refresh();

            // the state an art file imported by the previous package version is in
            var importer = (TextureImporter)AssetImporter.GetAtPath(png);
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();

            importer = (TextureImporter)AssetImporter.GetAtPath(png);
            Assert.AreEqual(TextureImporterCompression.Uncompressed, importer.textureCompression);
            Assert.AreEqual(128, importer.maxTextureSize);
            Assert.AreEqual(new Vector4(4, 1, 2, 3), importer.spriteBorder);
        }

        [Test]
        public void ChangingSidecarReimportsPng()
        {
            var png = m_Folder.WritePng("panel.png", 32);
            var sidecar = m_Folder.Write("panel.slice.json", Sidecar("[4,3,2,1]"));
            AssetDatabase.Refresh();

            m_Folder.Write("panel.slice.json", Sidecar("[8,7,6,5]"));
            AssetDatabase.ImportAsset(sidecar, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(png);
            Assert.AreEqual(new Vector4(8, 5, 6, 7), importer.spriteBorder);
        }

        [Test]
        public void PngWithoutSidecarIsUntouched()
        {
            var png = m_Folder.WritePng("plain.png", 32);
            AssetDatabase.Refresh();

            var importer = (TextureImporter)AssetImporter.GetAtPath(png);
            Assert.AreEqual(TextureImporterType.Default, importer.textureType);
            Assert.AreEqual(Vector4.zero, importer.spriteBorder);
        }

        [Test]
        public void PngOutsideRootsIsUntouched()
        {
            SlicedArtSettings.SetRoots(new[] { "Assets/SomewhereElse" });
            var png = m_Folder.WritePng("panel.png", 32);
            m_Folder.Write("panel.slice.json", Sidecar("[4,3,2,1]"));
            AssetDatabase.Refresh();

            var importer = (TextureImporter)AssetImporter.GetAtPath(png);
            Assert.AreEqual(TextureImporterType.Default, importer.textureType);
        }

        [Test]
        public void ImporterIsOffUnlessEnabled()
        {
            // Opt-in: a project that still runs another importer for the same .slice.json files keeps this one off.
            SlicedArtSettings.SetEnabled(false);
            var png = m_Folder.WritePng("panel.png", 32);
            m_Folder.Write("panel.slice.json", Sidecar("[4,3,2,1]"));
            AssetDatabase.Refresh();

            var importer = (TextureImporter)AssetImporter.GetAtPath(png);
            Assert.AreEqual(TextureImporterType.Default, importer.textureType);
            Assert.AreEqual(Vector4.zero, importer.spriteBorder);
            Assert.IsFalse(SlicedArtSettings.IsInScope(png));
        }
    }
}
