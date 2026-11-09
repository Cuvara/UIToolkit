#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Cuvara.UIToolkit.Prototype.Tests
{
    // TEMPORARY diagnostics: frame time of different panel setups in CI. Removed before merge.
    public class ZzFrameTimeDiagnostics
    {
        protected static string s_Phase = "late";
        const string Fixtures = "Packages/com.cuvara.uitoolkit/Tests/Runtime/Prototype/Fixtures/";

        static IEnumerator Measure(string label)
        {
            yield return null;
            yield return null;
            var t0 = Time.realtimeSinceStartup;
            for (var i = 0; i < 10; i++)
                yield return null;
            Debug.Log("[W9DIAG] " + s_Phase + " " + label + ": " + ((Time.realtimeSinceStartup - t0) * 100f).ToString("0.0") + " ms/frame");
        }

        static PanelSettings Settings(PanelScaleMode mode, int w, int h, bool theme)
        {
            var s = ScriptableObject.CreateInstance<PanelSettings>();
            s.scaleMode = mode;
            if (mode == PanelScaleMode.ScaleWithScreenSize)
            {
                s.referenceResolution = new Vector2Int(1920, 1080);
                s.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                s.match = 0.5f;
            }
            var rt = new RenderTexture(w, h, 24);
            rt.Create();
            s.targetTexture = rt;
            if (theme)
                s.themeStyleSheet = TestTheme.Load();
            return s;
        }

        static IEnumerator Panel(string label, PanelSettings s, string uxml)
        {
            var go = new GameObject("diag");
            go.SetActive(false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = s;
            go.SetActive(true);
            yield return null;
            if (uxml != null)
            {
                var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Fixtures + uxml).Instantiate();
                tree.style.flexGrow = 1f;
                doc.rootVisualElement.Add(tree);
            }
            else
            {
                var box = new VisualElement();
                box.style.width = 100; box.style.height = 60; box.style.backgroundColor = Color.white;
                doc.rootVisualElement.Add(box);
            }
            yield return Measure(label);
            Object.DestroyImmediate(go);
        }

        [UnityTest]
        public IEnumerator FrameTimes()
        {
            s_Phase = GetType().Name.StartsWith("Aa") ? "early" : "late";
            yield return Measure("no panel");
            yield return Panel("box const 640 theme", Settings(PanelScaleMode.ConstantPixelSize, 640, 360, true), null);
            yield return Panel("menu const 640 theme", Settings(PanelScaleMode.ConstantPixelSize, 640, 360, true), "menu.uxml");
            yield return Panel("menu scale 640 theme", Settings(PanelScaleMode.ScaleWithScreenSize, 640, 360, true), "menu.uxml");
            yield return Panel("menu scale 1920 theme", Settings(PanelScaleMode.ScaleWithScreenSize, 1920, 1080, true), "menu.uxml");
            yield return Panel("menu scale 640 notheme", Settings(PanelScaleMode.ScaleWithScreenSize, 640, 360, false), "menu.uxml");
            yield return Panel("detail scale 640 theme", Settings(PanelScaleMode.ScaleWithScreenSize, 640, 360, true), "detail.uxml");
            var go = new GameObject("runner");
            go.AddComponent<UIDocument>();
            var runner = go.AddComponent<PrototypeRunner>();
            runner.AutoStart = false;
            runner.Theme = TestTheme.Load();
            runner.Screens.Add(new PrototypeScreenEntry { Name = "Menu", Uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Fixtures + "menu.uxml") });
            runner.UseRenderTarget(640, 360);
            runner.Begin();
            yield return Measure("runner 640 menu");
            Object.DestroyImmediate(go);
            yield return Measure("no panel after");
            Assert.Pass();
        }
    }

    public class AaFrameTimeDiagnostics : ZzFrameTimeDiagnostics
    {
    }
}
#endif
