#if UNITY_EDITOR
using System.Collections;
using Cuvara.UIToolkit.Prototype;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Cuvara.UIToolkit.Prototype.Tests
{
    /// <summary>
    /// Wheel and drag scrolling of the runner (<see cref="ScrollProbe"/>, the scroll probe of the bridge's gate 7) on a horizontal
    /// [scroll] board (wider than its viewport, no height overflow), a wrapped list, and a view whose content fits. Regression: the
    /// probe only measured height, so the horizontal board was reported as "content fits" and never scrolled.
    /// </summary>
    public class PrototypeScrollTests
    {
        GameObject m_Object;
        PrototypeRunner m_Runner;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            m_Object = new GameObject("ScrollTest");
            m_Object.AddComponent<UIDocument>();
            m_Runner = m_Object.AddComponent<PrototypeRunner>();
            m_Runner.AutoStart = false;
            m_Runner.StartScreen = "Scrolls";
            m_Runner.Theme = TestTheme.Load();
            m_Runner.Screens.Add(new PrototypeScreenEntry
            {
                Name = "Scrolls",
                Uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.cuvara.uitoolkit/Tests/Runtime/Prototype/Fixtures/scroll.uxml"),
                Flow = AssetDatabase.LoadAssetAtPath<TextAsset>("Packages/com.cuvara.uitoolkit/Tests/Runtime/Prototype/Fixtures/scroll.flow.json")
            });
            m_Runner.UseRenderTarget(1920, 1080);
            m_Runner.Begin();
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            if (m_Object != null)
                Object.DestroyImmediate(m_Object);
        }

        IPanel Panel { get { return m_Runner.Document.rootVisualElement.panel; } }

        IEnumerator Probe(string name, System.Action<ScrollProbe.ScrollProbeResult, ScrollView> done)
        {
            yield return ScrollProbe.Frames(4);
            var scroll = m_Runner.ScreenRoot.Q<ScrollView>(name);
            Assert.IsNotNull(scroll, name);
            ScrollProbe.ScrollProbeResult result = null;
            yield return ScrollProbe.ProbeScroll(Panel, scroll, () => 0, r => result = r);
            done(result, scroll);
        }

        [UnityTest]
        public IEnumerator AHorizontalBoardWiderThanItsViewportIsScrolledHorizontally()
        {
            ScrollProbe.ScrollProbeResult result = null;
            ScrollView view = null;
            yield return Probe("HBar", (r, v) => { result = r; view = v; });
            Assert.Greater(view.contentContainer.layout.width - view.contentViewport.layout.width, 100f, "the fixture is wider than its viewport");
            Assert.Less(view.contentContainer.layout.height - view.contentViewport.layout.height, 2f, "and has no height overflow");
            StringAssert.Contains("horizontal", result.observed);
            StringAssert.DoesNotContain("fits", result.observed);
            Assert.IsTrue(result.pass, result.observed);
        }

        [UnityTest]
        public IEnumerator AWrappedListThatOverflowsIsScrolledVertically()
        {
            ScrollProbe.ScrollProbeResult result = null;
            yield return Probe("WrapList", (r, v) => result = r);
            StringAssert.Contains("vertical", result.observed);
            StringAssert.DoesNotContain("fits", result.observed);
            Assert.IsTrue(result.pass, result.observed);
        }

        [UnityTest]
        public IEnumerator AViewWhoseContentFitsOnItsOwnAxisHasNothingToScroll()
        {
            ScrollProbe.ScrollProbeResult result = null;
            yield return Probe("Fits", (r, v) => result = r);
            Assert.IsTrue(result.pass, result.observed);
            StringAssert.Contains("fits", result.observed);
        }
    }
}
#endif
