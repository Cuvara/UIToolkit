using Cuvara.UIToolkit.Editor.ImportCheck;
using Cuvara.UIToolkit.Editor.SlicedArt;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;

namespace Cuvara.UIToolkit.Editor.Tests
{
    public class UiImportCheckerTests
    {
        const string Uxml =
            "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><Style src=\"{0}\"/><ui:VisualElement class=\"box\"/></ui:UXML>";

        SlicedArtTempFolder m_Folder;

        [SetUp]
        public void SetUp()
        {
            m_Folder = new SlicedArtTempFolder("CuvaraUIToolkitTests_ImportCheck");
            SlicedArtSettings.SetEnabled(true);
            SlicedArtSettings.SetRoots(new[] { m_Folder.AssetPath });
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            m_Folder.Delete();
            SlicedArtSettings.Reload();
        }

        [Test]
        public void ValidPairPasses()
        {
            m_Folder.WritePng("panel.png", 32);
            m_Folder.Write("panel.slice.json", "{\"version\":1,\"border\":[4,4,4,4],\"pixelsPerUnit\":100,\"maxSize\":512}");
            m_Folder.Write("ok.uss",
                ".box { width: 100px; height: 40px; background-color: rgb(10, 20, 30); " +
                "background-image: url(\"panel.png\"); -unity-slice-left: 4; -unity-slice-top: 4; " +
                "-unity-slice-right: 4; -unity-slice-bottom: 4; }");
            m_Folder.Write("ok.uxml", string.Format(Uxml, "ok.uss"));
            AssetDatabase.Refresh();

            var result = UiImportChecker.Check(new[] { m_Folder.AssetPath + "/ok.uxml", m_Folder.AssetPath + "/ok.uss" });

            Assert.IsTrue(result.Pass, result.ToJson());
            Assert.AreEqual(3, result.Checked); // uxml, uss and the png reachable from the uss
        }

        [Test]
        public void UnslicedSidecarWithZeroBorderPasses()
        {
            m_Folder.WritePng("icon.png", 32);
            m_Folder.Write("icon.slice.json", "{\"version\":1,\"border\":[0,0,0,0],\"pixelsPerUnit\":100,\"maxSize\":512}");
            m_Folder.Write("icon.uss", ".box { background-image: url(\"icon.png\"); }");
            AssetDatabase.Refresh();

            var result = UiImportChecker.Check(new[] { m_Folder.AssetPath + "/icon.uss" });

            Assert.IsTrue(result.Pass, result.ToJson());
        }

        [Test]
        public void UnknownUssPropertyFails()
        {
            LogAssert.ignoreFailingMessages = true; // the import itself logs; the checker must see it
            m_Folder.Write("bad.uss", ".box { not-a-real-property: 12px; }");
            AssetDatabase.Refresh();

            var result = UiImportChecker.Check(new[] { m_Folder.AssetPath + "/bad.uss" });

            Assert.IsFalse(result.Pass, result.ToJson());
        }

        [Test]
        public void MissingUrlFails()
        {
            LogAssert.ignoreFailingMessages = true;
            m_Folder.Write("missing.uss", ".box { background-image: url(\"nope.png\"); }");
            AssetDatabase.Refresh();

            var result = UiImportChecker.Check(new[] { m_Folder.AssetPath + "/missing.uss" });

            Assert.IsFalse(result.Pass, result.ToJson());
            Assert.IsTrue(result.Errors.Exists(e => e.Contains("nope.png")), result.ToJson());
        }

        [Test]
        public void SpriteBorderMismatchFails()
        {
            var png = m_Folder.WritePng("panel.png", 32);
            m_Folder.Write("panel.slice.json", "{\"version\":1,\"border\":[4,4,4,4],\"pixelsPerUnit\":100,\"maxSize\":512}");
            m_Folder.Write("ref.uss", ".box { background-image: url(\"panel.png\"); }");
            AssetDatabase.Refresh();

            // Move the roots away first, otherwise OnPreprocessTexture restores the sidecar border on reimport.
            SlicedArtSettings.SetRoots(new[] { "Assets/SomewhereElse" });
            var importer = (TextureImporter)AssetImporter.GetAtPath(png);
            importer.spriteBorder = new UnityEngine.Vector4(1, 1, 1, 1);
            importer.SaveAndReimport();
            Assert.AreEqual(new UnityEngine.Vector4(1, 1, 1, 1), ((TextureImporter)AssetImporter.GetAtPath(png)).spriteBorder);

            var result = UiImportChecker.Check(new[] { m_Folder.AssetPath + "/ref.uss" });

            Assert.IsFalse(result.Pass, result.ToJson());
            Assert.IsTrue(result.Errors.Exists(e => e.Contains("sprite border")), result.ToJson());
        }

        [Test]
        public void UxmlOnlyCheckValidatesReferencedStylesheet()
        {
            LogAssert.ignoreFailingMessages = true;
            m_Folder.Write("dep.uss", ".box { not-a-real-property: 12px; }");
            m_Folder.Write("screen.uxml", string.Format(Uxml, "dep.uss"));
            AssetDatabase.Refresh();

            var result = UiImportChecker.Check(new[] { m_Folder.AssetPath + "/screen.uxml" });

            Assert.IsFalse(result.Pass, result.ToJson());
            Assert.GreaterOrEqual(result.Checked, 2, result.ToJson());
        }

        [Test]
        public void UxmlOnlyCheckCatchesMissingUrlInReferencedStylesheet()
        {
            LogAssert.ignoreFailingMessages = true;
            m_Folder.Write("dep.uss", ".box { background-image: url(\"gone.png\"); }");
            m_Folder.Write("screen.uxml", string.Format(Uxml, "dep.uss"));
            AssetDatabase.Refresh();

            var result = UiImportChecker.Check(new[] { m_Folder.AssetPath + "/screen.uxml" });

            Assert.IsFalse(result.Pass, result.ToJson());
            Assert.IsTrue(result.Errors.Exists(e => e.Contains("gone.png")), result.ToJson());
        }

        [Test]
        public void NestedImportIsValidated()
        {
            LogAssert.ignoreFailingMessages = true;
            m_Folder.Write("leaf.uss", ".box { background-image: url(\"gone.png\"); }");
            m_Folder.Write("mid.uss", "@import url(\"leaf.uss\");\n.box { width: 10px; }");
            m_Folder.Write("top.uss", "@import \"mid.uss\";\n.box { height: 10px; }");
            AssetDatabase.Refresh();

            var result = UiImportChecker.Check(new[] { m_Folder.AssetPath + "/top.uss" });

            Assert.AreEqual(3, result.Checked, result.ToJson());
            Assert.IsFalse(result.Pass, result.ToJson());
            Assert.IsTrue(result.Errors.Exists(e => e.Contains("leaf.uss") && e.Contains("gone.png")), result.ToJson());
        }

        [Test]
        public void CheckToJsonReturnsExpectedKeys()
        {
            m_Folder.Write("ok.uss", ".box { width: 10px; }");
            AssetDatabase.Refresh();

            var json = UiImportChecker.CheckToJson(m_Folder.AssetPath + "/ok.uss");

            StringAssert.Contains("\"pass\":true", json);
            StringAssert.Contains("\"errors\":[]", json);
            StringAssert.Contains("\"checked\":1", json);
        }
    }
}
