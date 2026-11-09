#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Cuvara.UIToolkit.Prototype.Tests
{
    // TEMPORARY diagnostics: frame time of a themed text panel at points of the suite in CI. Removed before merge.
    public class ZzFrameTimeDiagnostics
    {
        const string Fixtures = "Packages/com.cuvara.uitoolkit/Tests/Runtime/Prototype/Fixtures/";

        static string FontStats(VisualElement root)
        {
            var label = root.Q<Label>();
            if (label == null)
                return "no label";
            var fa = label.resolvedStyle.unityFontDefinition.fontAsset;
            var f = label.resolvedStyle.unityFont;
            var s = "font=" + (f != null ? f.name : "null") + " fontAsset=" + (fa != null ? fa.name : "null");
            if (fa != null)
            {
                s += " mode=" + fa.atlasPopulationMode + " atlases=" + (fa.atlasTextures != null ? fa.atlasTextures.Length : -1) +
                     " size=" + fa.atlasWidth + "x" + fa.atlasHeight + " glyphs=" + fa.glyphTable.Count + " chars=" + fa.characterTable.Count +
                     " multi=" + fa.isMultiAtlasTexturesEnabled + " fallbacks=" + (fa.fallbackFontAssetTable != null ? fa.fallbackFontAssetTable.Count : -1);
            }
            return s;
        }

        [UnityTest]
        public IEnumerator FrameTimes()
        {
            yield return Probe(GetType().Name);
            if (GetType() == typeof(ZzFrameTimeDiagnostics))
            {
                foreach (var kind in new[] { "Label", "Button", "Toggle", "TextField", "ScrollView", "ScrollViewLabels" })
                    yield return ProbeElement(kind);
            }
            Assert.Pass();
        }

        static IEnumerator ProbeElement(string kind)
        {
            var s = ScriptableObject.CreateInstance<PanelSettings>();
            s.scaleMode = PanelScaleMode.ConstantPixelSize;
            var rt = new RenderTexture(640, 360, 24);
            rt.Create();
            s.targetTexture = rt;
            s.themeStyleSheet = TestTheme.Load();
            var go = new GameObject("diag");
            go.SetActive(false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = s;
            go.SetActive(true);
            yield return null;
            VisualElement e;
            switch (kind)
            {
                case "Label": e = new Label("Row 0"); break;
                case "Button": e = new Button { text = "Go" }; break;
                case "Toggle": e = new Toggle("Check"); break;
                case "TextField": e = new TextField(); break;
                case "ScrollView": e = new ScrollView(); e.style.height = 200; break;
                default:
                    var sv = new ScrollView(); sv.style.height = 200;
                    for (var i = 0; i < 8; i++) { var l = new Label("Row " + i); l.style.height = 50; sv.Add(l); }
                    e = sv; break;
            }
            doc.rootVisualElement.Add(e);
            yield return null;
            yield return null;
            var t0 = Time.realtimeSinceStartup;
            for (var i = 0; i < 10; i++)
                yield return null;
            Debug.Log("[W9DIAG] element " + kind + ": " + ((Time.realtimeSinceStartup - t0) * 100f).ToString("0.0") + " ms/frame");
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(s);
            rt.Release();
            Object.DestroyImmediate(rt);
        }

        public static IEnumerator Probe(string name)
        {
            var s = ScriptableObject.CreateInstance<PanelSettings>();
            s.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2Int(1920, 1080);
            s.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            s.match = 0.5f;
            var rt = new RenderTexture(640, 360, 24);
            rt.Create();
            s.targetTexture = rt;
            s.themeStyleSheet = TestTheme.Load();
            var go = new GameObject("diag");
            go.SetActive(false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = s;
            go.SetActive(true);
            yield return null;
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Fixtures + "menu.uxml").Instantiate();
            tree.style.flexGrow = 1f;
            doc.rootVisualElement.Add(tree);
            yield return null;
            yield return null;
            var t0 = Time.realtimeSinceStartup;
            for (var i = 0; i < 10; i++)
                yield return null;
            Debug.Log("[W9DIAG] " + name + ": " + ((Time.realtimeSinceStartup - t0) * 100f).ToString("0.0") + " ms/frame; " + FontStats(doc.rootVisualElement));
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(s);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }

    public class AaFrameTimeDiagnostics : ZzFrameTimeDiagnostics { }
    public class PrototypeMotionTestsZDiag : ZzFrameTimeDiagnostics { }
    public class PrototypeOwnershipTestsZDiag : ZzFrameTimeDiagnostics { }
    public class PrototypeRunnerTestsZDiag : ZzFrameTimeDiagnostics { }
    public class PrototypeScrollTestsZDiag : ZzFrameTimeDiagnostics { }
}
#endif
