#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Cuvara.UIToolkit.Prototype;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Cuvara.UIToolkit.Prototype.Tests
{
    /// <summary>
    /// The runner plays the animation of an interaction (dissolve, slide, push) with native USS transitions: the frames are sampled
    /// from the resolved styles of the two screen roots against the wall clock, input is blocked until the end, back plays the
    /// reverse, and a second request completes the running transition. The flow files of the fixture screens get their
    /// <c>animation</c> injected here so the same fixtures serve the static tests.
    /// </summary>
    public class PrototypeTransitionTests
    {
        const string Fixtures = "Packages/com.cuvara.uitoolkit/Tests/Runtime/Prototype/Fixtures/";

        GameObject m_Object;
        PrototypeRunner m_Runner;
        readonly List<string> m_Finished = new List<string>();

        static string Anim(string type, int ms, string easing = "linear", string way = "", string direction = "", bool offset = false)
        {
            return "{\"type\": \"" + type + "\", \"durationMs\": " + ms + ", \"easing\": \"" + easing + "\", \"way\": \"" + way +
                   "\", \"direction\": \"" + direction + "\", \"offsetEffect\": " + (offset ? "true" : "false") + "}";
        }

        static PrototypeScreenEntry Entry(string name, string interactionId = null, string animation = null, string motion = null)
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(Fixtures + name.ToLowerInvariant() + ".flow.json").text;
            if (interactionId != null)
                json = json.Replace("{\"id\": \"" + interactionId + "\"", "{\"animation\": " + animation + ", \"id\": \"" + interactionId + "\"");
            if (motion != null)
                json = json.Replace("\"version\": 1,", "\"version\": 1, \"motion\": " + motion + ",");
            return new PrototypeScreenEntry
            {
                Name = name,
                Uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Fixtures + name.ToLowerInvariant() + ".uxml"),
                Flow = new TextAsset(json)
            };
        }

        static ThemeStyleSheet Theme()
        {
            return TestTheme.Load();
        }

        void Setup(PrototypeScreenEntry menu, PrototypeScreenEntry detail)
        {
            LogAssert.ignoreFailingMessages = true;
            m_Object = new GameObject("TransitionTest");
            m_Object.AddComponent<UIDocument>();
            m_Runner = m_Object.AddComponent<PrototypeRunner>();
            m_Runner.AutoStart = false;
            m_Runner.StartScreen = "Menu";
            m_Runner.Theme = Theme();
            m_Runner.Screens.Add(menu);
            m_Runner.Screens.Add(detail);
            m_Runner.Screens.Add(Entry("Dialog"));
            m_Runner.TransitionFinished += (kind, name) => m_Finished.Add(kind + ":" + name);
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            if (m_Object != null)
                Object.DestroyImmediate(m_Object);
            m_Finished.Clear();
            m_Inconclusive = null;
        }

        IEnumerator Boot()
        {
            m_Runner.UseRenderTarget(1920, 1080);
            m_Runner.Begin();
            yield return Frames(3);
        }

        static IEnumerator Frames(int n)
        {
            for (var i = 0; i < n; i++)
                yield return null;
        }

        IPanel Panel { get { return m_Runner.Document.rootVisualElement.panel; } }

        VisualElement Q(string name)
        {
            return m_Runner.Host.panel.visualTree.Q<VisualElement>(name);
        }

        void Click(string name)
        {
            var element = Q(name);
            Assert.IsNotNull(element, name);
            UiInputSimulator.Click(Panel, UiInputSimulator.Center(element));
        }

        IEnumerator WaitIdle(float seconds = 4f)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (m_Runner.IsTransitioning && Time.realtimeSinceStartup < until)
                yield return null;
            Assert.IsFalse(m_Runner.IsTransitioning, "the transition ended");
            yield return Frames(2);
        }

        /// <summary>
        /// The longest gap between two sampled frames. A check that samples a transition between its ends can only see what the
        /// frames show: when they are further apart than half the transition, nothing in between is observable. CI renders with
        /// a software rasteriser where a themed screen frame took 0.5-1.5 s, so such a check is reported inconclusive there
        /// (like the motion tests' RequireFrames) instead of failing; every other assertion of the test still runs.
        /// </summary>
        sealed class FrameClock
        {
            float m_Last = -1f;
            public float MaxGapMs;

            public void Tick()
            {
                var now = Time.realtimeSinceStartup;
                if (m_Last >= 0f)
                    MaxGapMs = Mathf.Max(MaxGapMs, (now - m_Last) * 1000f);
                m_Last = now;
            }

            public bool TooCoarseFor(float durationMs)
            {
                return MaxGapMs > durationMs * 0.5f;
            }
        }

        string m_Inconclusive;

        // asserts an observation of the running transition, or records it as unobservable at this frame rate
        void Observed(bool observed, FrameClock clock, float durationMs, string what)
        {
            if (!observed && clock.TooCoarseFor(durationMs))
            {
                m_Inconclusive = (m_Inconclusive == null ? "" : m_Inconclusive + "; ") + what + ": frames were up to " +
                                 clock.MaxGapMs.ToString("0") + " ms apart during a " + durationMs + " ms transition";
                return;
            }
            Assert.IsTrue(observed, what);
        }

        void ReportInconclusive()
        {
            if (m_Inconclusive != null)
                Assert.Inconclusive(m_Inconclusive + " (the Editor renders too slowly to sample it)");
        }

        // frames until `done` holds or the time is up; returns how long it took
        IEnumerator Until(System.Func<bool> done, float seconds = 3f)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < until)
                yield return null;
            Assert.IsTrue(done(), "condition reached within " + seconds + " s");
        }

        [UnityTest]
        public IEnumerator DissolveFadesTheNewScreenInAndTheOldOneOutThenLeavesOneScreen()
        {
            Setup(Entry("Menu", "Menu.GoButton.click#0", Anim("dissolve", 400)), Entry("Detail"));
            yield return Boot();
            var menu = m_Runner.ScreenRoot; // the TemplateContainer of the screen: the element the runner animates
            Click("GoButton");
            yield return Frames(1);
            Assert.IsTrue(m_Runner.IsTransitioning);
            Assert.AreEqual("Detail", m_Runner.CurrentScreen, "the new screen is current as soon as the transition starts");
            Assert.AreEqual(2, m_Runner.Host.childCount, "both screens are on the panel while it plays");
            Assert.IsNotNull(Q("PrototypeInputBlock"));
            var detail = m_Runner.ScreenRoot;
            var sawPartial = false;
            var sawOldFading = false;
            var clock = new FrameClock();
            var until = Time.realtimeSinceStartup + 2f;
            while (m_Runner.IsTransitioning && Time.realtimeSinceStartup < until)
            {
                clock.Tick();
                var o = detail.resolvedStyle.opacity;
                if (o > 0.05f && o < 0.95f)
                    sawPartial = true;
                if (menu.resolvedStyle.opacity < 0.9f && menu.resolvedStyle.opacity > 0.02f)
                    sawOldFading = true;
                yield return null;
            }
            Observed(sawPartial, clock, 400f, "the new screen passed through intermediate opacities");
            Observed(sawOldFading, clock, 400f * 2f / 3f, "the old screen faded out");
            yield return WaitIdle();
            Assert.AreEqual(1, m_Runner.Host.childCount, "the old screen is removed");
            Assert.IsNull(Q("PrototypeInputBlock"));
            Assert.AreEqual(1f, detail.resolvedStyle.opacity, 0.001f);
            Assert.AreEqual(StyleKeyword.Null, detail.style.opacity.keyword, "inline opacity removed");
            CollectionAssert.AreEqual(new[] { "screen:Detail" }, m_Finished);
            ReportInconclusive();
        }

        [UnityTest]
        public IEnumerator SlideInMovesTheNewScreenAcrossThePanelInAboutItsDuration()
        {
            Setup(Entry("Menu", "Menu.GoButton.click#0", Anim("slide", 300, "ease-out", "in", "left")), Entry("Detail"));
            yield return Boot();
            Click("GoButton");
            var start = Time.realtimeSinceStartup;
            yield return Frames(1);
            var detail = m_Runner.ScreenRoot;
            var maxX = 0f;
            var sawMid = false;
            var log = new System.Text.StringBuilder();
            var clock = new FrameClock();
            while (m_Runner.IsTransitioning && Time.realtimeSinceStartup - start < 3f)
            {
                clock.Tick();
                var x = detail.resolvedStyle.translate.x;
                log.Append(((Time.realtimeSinceStartup - start) * 1000f).ToString("0")).Append(':').Append(x.ToString("0")).Append('/').Append(detail.style.translate.value.x.value.ToString("0")).Append(' ');
                maxX = Mathf.Max(maxX, x);
                if (x > 100f && x < 1800f)
                    sawMid = true;
                yield return null;
            }
            var elapsed = Time.realtimeSinceStartup - start;
            Assert.IsFalse(m_Runner.IsTransitioning);
            Observed(sawMid, clock, 300f, "an intermediate frame was sampled: " + log);
            Assert.Greater(maxX, 1000f, "slide in `left` starts at the right edge: translate x = +100% of the panel");
            Assert.AreEqual(0f, detail.resolvedStyle.translate.x, 0.5f, "settles at its place");
            if (!clock.TooCoarseFor(300f))
                Assert.That(elapsed, Is.InRange(0.2f, 0.9f), "took about 300 ms");
            ReportInconclusive();
        }

        [UnityTest]
        public IEnumerator SlowFramesBeforeTheAnimationStartsDoNotCutItShort()
        {
            // the frames between the request and the start of the animation can be slow (instantiating a screen: 3.8 s in a busy
            // Editor); the safety that ends a transition whose end event never comes must count from the real start
            Setup(Entry("Menu", "Menu.GoButton.click#0", Anim("slide", 300, "linear", "in", "left")), Entry("Detail"));
            yield return Boot();
            var cancels = 0;
            m_Runner.Document.rootVisualElement.RegisterCallback<TransitionCancelEvent>(e => cancels++);
            Click("GoButton");
            var info = m_Runner.LastTransition;
            System.Threading.Thread.Sleep(800); // longer than the duration + the 400 ms margin, before the end state is applied
            yield return Until(() => info.StartedAt >= 0f || !m_Runner.IsTransitioning);
            Assert.GreaterOrEqual(info.StartedAt, 0f, "the animation started (it was not finished before its start)");
            var startedAt = info.StartedAt;
            var until = Time.realtimeSinceStartup + 3f;
            while (m_Runner.IsTransitioning && Time.realtimeSinceStartup < until)
                yield return null;
            var took = Time.realtimeSinceStartup - startedAt;
            Assert.AreEqual(0, cancels, "no transition was cancelled");
            Assert.That(took, Is.InRange(0.25f, 1.2f), "the slide ran about its 300 ms after it started");
        }

        [UnityTest]
        public IEnumerator PushMovesBothScreensTogether()
        {
            Setup(Entry("Menu", "Menu.GoButton.click#0", Anim("push", 400, "linear", "", "up")), Entry("Detail"));
            yield return Boot();
            var menu = m_Runner.ScreenRoot;
            Click("GoButton");
            yield return Frames(1);
            var detail = m_Runner.ScreenRoot;
            var both = false;
            var clock = new FrameClock();
            var until = Time.realtimeSinceStartup + 2f;
            while (m_Runner.IsTransitioning && Time.realtimeSinceStartup < until)
            {
                clock.Tick();
                // direction up: the new screen comes from below (+y), the old one leaves upwards (-y)
                if (detail.resolvedStyle.translate.y > 50f && menu.resolvedStyle.translate.y < -50f)
                    both = true;
                yield return null;
            }
            Observed(both, clock, 400f, "both screens were off their place at the same time, moving in the same direction");
            yield return WaitIdle();
            Assert.AreEqual(1, m_Runner.Host.childCount);
            ReportInconclusive();
        }

        [UnityTest]
        public IEnumerator PointerInputIsBlockedUntilTheTransitionEnds()
        {
            Setup(Entry("Menu", "Menu.GoButton.click#0", Anim("dissolve", 500)), Entry("Detail", "Detail.BackButton.click#0", Anim("dissolve", 100)));
            yield return Boot();
            Click("GoButton");
            yield return Frames(2);
            Assert.IsTrue(m_Runner.IsTransitioning);
            var fired = 0;
            m_Runner.InteractionFired += i => fired++;
            Click("BackButton"); // the Detail screen is current but pointer input is swallowed
            yield return Frames(2);
            Assert.AreEqual(0, fired, "no interaction fires during the transition");
            Assert.AreEqual("Detail", m_Runner.CurrentScreen);
            yield return WaitIdle();
            Click("BackButton");
            yield return Frames(2);
            Assert.AreEqual(1, fired, "input works again after the end");
        }

        [UnityTest]
        public IEnumerator BackPlaysTheReverseOfTheNavigation()
        {
            Setup(Entry("Menu", "Menu.GoButton.click#0", Anim("slide", 300, "linear", "in", "left")), Entry("Detail"));
            yield return Boot();
            Click("GoButton");
            yield return WaitIdle();
            var detail = m_Runner.ScreenRoot;
            Click("BackButton");
            yield return Frames(2);
            Assert.IsTrue(m_Runner.IsTransitioning, "back is animated");
            Assert.AreEqual("Menu", m_Runner.CurrentScreen);
            Assert.IsNotNull(Q("Detail"), "the screen being left stays on the panel while it slides out");
            var movedRight = false;
            var clock = new FrameClock();
            var until = Time.realtimeSinceStartup + 2f;
            while (m_Runner.IsTransitioning && Time.realtimeSinceStartup < until)
            {
                clock.Tick();
                // forward: slide in, direction left (new enters from the right moving left); reverse: the leaving screen exits to the right
                if (detail.resolvedStyle.translate.x > 100f)
                    movedRight = true;
                yield return null;
            }
            Observed(movedRight, clock, 300f, "the leaving screen moved right");
            yield return WaitIdle();
            Assert.IsNull(Q("Detail"), "removed at the end");
            Assert.AreEqual(0, m_Runner.HistoryCount);
            ReportInconclusive();
        }

        [UnityTest]
        public IEnumerator BackWithMotionBackNoneIsInstant()
        {
            Setup(Entry("Menu", "Menu.GoButton.click#0", Anim("slide", 300, "linear", "in", "left")),
                  Entry("Detail", null, null, "{\"back\": \"none\", \"enabled\": true}"));
            yield return Boot();
            Click("GoButton");
            yield return WaitIdle();
            Click("BackButton");
            yield return Frames(2);
            Assert.IsFalse(m_Runner.IsTransitioning);
            Assert.AreEqual("Menu", m_Runner.CurrentScreen);
            Assert.AreEqual(1, m_Runner.Host.childCount);
        }

        [UnityTest]
        public IEnumerator MotionDisabledInTheFlowPlaysNothing()
        {
            Setup(Entry("Menu", "Menu.GoButton.click#0", Anim("dissolve", 400), "{\"back\": \"reverse\", \"enabled\": false}"), Entry("Detail"));
            yield return Boot();
            Click("GoButton");
            yield return Frames(2);
            Assert.IsFalse(m_Runner.IsTransitioning);
            Assert.AreEqual(1, m_Runner.Host.childCount);
        }

        [UnityTest]
        public IEnumerator AnimateFalseKeepsNavigationInstant()
        {
            Setup(Entry("Menu", "Menu.GoButton.click#0", Anim("dissolve", 400)), Entry("Detail"));
            m_Runner.Animate = false;
            yield return Boot();
            Click("GoButton");
            yield return Frames(1);
            Assert.IsFalse(m_Runner.IsTransitioning);
            Assert.AreEqual(1, m_Runner.Host.childCount);
            Assert.AreEqual("Detail", m_Runner.CurrentScreen);
        }

        [UnityTest]
        public IEnumerator ASecondRequestCompletesTheRunningTransitionAndLeavesNothingBehind()
        {
            Setup(Entry("Menu", "Menu.GoButton.click#0", Anim("slide", 800, "linear", "in", "left")), Entry("Detail"));
            yield return Boot();
            Click("GoButton");
            yield return Frames(3);
            Assert.IsTrue(m_Runner.IsTransitioning);
            Assert.IsTrue(m_Runner.Navigate("Menu"));
            yield return Frames(2);
            Assert.IsFalse(m_Runner.IsTransitioning);
            Assert.AreEqual("Menu", m_Runner.CurrentScreen);
            Assert.AreEqual(1, m_Runner.Host.childCount, "no leftover screen");
            Assert.IsNull(Q("Detail"));
            Assert.IsNull(Q("PrototypeInputBlock"));
            CollectionAssert.AreEqual(new[] { "screen:Detail" }, m_Finished, "the interrupted transition reported its end");
            var menu = m_Runner.ScreenRoot;
            Assert.AreEqual(0f, menu.resolvedStyle.translate.x, 0.5f);
            Assert.AreEqual(1f, menu.resolvedStyle.opacity, 0.001f);
        }

        [UnityTest]
        public IEnumerator OverlayDissolveOpensAndClosesByClickingOutsideWithTheInvertedAnimation()
        {
            Setup(Entry("Menu", "Menu.GoButton.click#0", Anim("dissolve", 100)),
                  Entry("Detail", "Detail.DialogButton.click#0", Anim("dissolve", 300)));
            yield return Boot();
            Click("GoButton");
            yield return WaitIdle();
            Click("DialogButton");
            yield return Frames(2);
            Assert.AreEqual(1, m_Runner.OverlayCount);
            Assert.IsTrue(m_Runner.IsTransitioning, "overlay open is a transition");
            var container = Q("PrototypeOverlay-Dialog");
            var partial = false;
            var clock = new FrameClock();
            var until = Time.realtimeSinceStartup + 2f;
            while (m_Runner.IsTransitioning && Time.realtimeSinceStartup < until)
            {
                clock.Tick();
                var o = container.resolvedStyle.opacity;
                partial |= o > 0.05f && o < 0.95f;
                yield return null;
            }
            Observed(partial, clock, 300f, "the overlay passed through intermediate opacities");
            yield return WaitIdle();
            UiInputSimulator.Click(Panel, new Vector2(1800f, 1000f)); // on the scrim, outside the dialog
            yield return Frames(2);
            Assert.IsTrue(m_Runner.IsTransitioning, "closing plays the animation too");
            Assert.AreEqual(1, m_Runner.OverlayCount, "still there while it fades");
            yield return WaitIdle();
            Assert.AreEqual(0, m_Runner.OverlayCount);
            CollectionAssert.AreEqual(new[] { "screen:Detail", "overlay-open:Dialog", "overlay-close:Dialog" }, m_Finished);
            ReportInconclusive();
        }

        [UnityTest]
        public IEnumerator OverlaySlideStartsOutsideThePanelAndSettlesInPlace()
        {
            Setup(Entry("Menu", "Menu.GoButton.click#0", Anim("dissolve", 100)),
                  Entry("Detail", "Detail.DialogButton.click#0", Anim("slide", 400, "linear", "", "right")));
            yield return Boot();
            Click("GoButton");
            yield return WaitIdle();
            Click("DialogButton");
            yield return Frames(3);
            var content = m_Runner.OverlayRoot("Dialog");
            Assert.IsNotNull(content);
            var minX = 0f;
            var log = new System.Text.StringBuilder();
            var t0 = Time.realtimeSinceStartup;
            var until = t0 + 2f;
            var clock = new FrameClock();
            while (m_Runner.IsTransitioning && Time.realtimeSinceStartup < until)
            {
                clock.Tick();
                minX = Mathf.Min(minX, content.resolvedStyle.translate.x);
                log.Append(((Time.realtimeSinceStartup - t0) * 1000f).ToString("0")).Append(":").Append(content.resolvedStyle.translate.x.ToString("0")).Append("/").Append(content.style.translate.value.x.value.ToString("0")).Append(content.style.translate.value.x.unit == LengthUnit.Percent ? "%" : "px").Append(" ");
                yield return null;
            }
            Observed(minX < -200f, clock, 400f, "direction right: the overlay enters from the left of the panel: " + log + " layout " + content.layout + " parent " + content.parent.layout);
            yield return WaitIdle();
            Assert.AreEqual(0f, content.resolvedStyle.translate.x, 0.5f, "layout " + content.layout + " parent " + content.parent.layout + " world " + content.worldBound);
            ReportInconclusive();
        }

        static PrototypeAnimation Plain(string type, string way, string direction, float ms)
        {
            return new PrototypeAnimation { type = type, way = way, direction = direction, durationMs = ms, easing = "linear" };
        }

        // a slide that moves only the leaving screen gives the new screen no native transition end: the bindings still hear one signal
        [UnityTest]
        public IEnumerator BindingsHearOneTransitionFinishedPerTransitionWhateverEndedIt()
        {
            Setup(Entry("Menu"), Entry("Detail"));
            m_Runner.StartScreen = "Detail";
            yield return Boot();
            var heard = 0;
            Assert.IsTrue(m_Runner.Navigate("Menu", Plain("slide", "out", "left", 250f)));
            var bindings = Generated.MenuBindings.Bind(m_Runner.ScreenRoot);
            bindings.TransitionFinished += () => heard++;
            Assert.IsTrue(m_Runner.IsTransitioning);
            yield return WaitIdle();
            Assert.AreEqual(1, heard, "slide out: only the old screen has a native end event");

            // completed early by the next request: still exactly one, for the screen that was being shown
            Assert.IsTrue(m_Runner.Navigate("Detail", Plain("dissolve", null, null, 400f)));
            Assert.IsTrue(m_Runner.IsTransitioning);
            Assert.IsTrue(m_Runner.Navigate("Menu"));
            yield return Frames(2);
            Assert.AreEqual(1, heard, "the screen that was replaced mid-transition is not the bound one");
            var second = Generated.MenuBindings.Bind(m_Runner.ScreenRoot);
            var secondHeard = 0;
            second.TransitionFinished += () => secondHeard++;
            Assert.IsTrue(m_Runner.Navigate("Detail", Plain("slide", "in", "left", 400f)));
            yield return Frames(3);
            m_Runner.FinishTransition();
            Assert.AreEqual(0, secondHeard, "Menu was the screen that left");
            Assert.AreEqual(1, heard, "an unbound old root never calls the old bindings");
            bindings.Dispose();
            second.Dispose();
            Assert.AreEqual(1, heard);
        }

        [UnityTest]
        public IEnumerator ANativeEndEventIsNotAFinishedTransitionSignal()
        {
            Setup(Entry("Menu"), Entry("Detail"));
            yield return Boot();
            var bindings = Generated.MenuBindings.Bind(m_Runner.ScreenRoot);
            var heard = 0;
            bindings.TransitionFinished += () => heard++;
            using (var native = TransitionEndEvent.GetPooled(new StylePropertyName("opacity"), 0.1f))
            {
                native.target = m_Runner.ScreenRoot;
                m_Runner.ScreenRoot.SendEvent(native);
            }
            Assert.AreEqual(0, heard, "a native end event of an animated property has a positive elapsed time");
            ScreenTransitionSignal.Send(m_Runner.ScreenRoot);
            Assert.AreEqual(1, heard);
            bindings.Unbind();
            ScreenTransitionSignal.Send(m_Runner.ScreenRoot);
            Assert.AreEqual(1, heard, "Unbind removes the callback");
        }

        static VisualElement ItemWithMark(bool stylesheetDriven)
        {
            var item = new VisualElement();
            var mark = new VisualElement { name = PrototypeRunner.SelectedElementName };
            if (stylesheetDriven)
                mark.AddToClassList(PrototypeRunner.SelectionMarkClass);
            item.Add(mark);
            return item;
        }

        [Test]
        public void SelectLeavesTheDisplayOfAStylesheetDrivenMarkAloneAndSwitchesAPlainOne()
        {
            var list = new VisualElement();
            var plain = ItemWithMark(false);
            var fading = ItemWithMark(true);
            list.Add(plain);
            list.Add(fading);
            PrototypeRunner.Select(list, plain);
            Assert.IsTrue(plain.ClassListContains(PrototypeRunner.SelectedClass));
            Assert.AreEqual(DisplayStyle.Flex, plain[0].style.display.value, "a plain mark is shown");
            Assert.AreEqual(StyleKeyword.Null, fading[0].style.display.keyword, "a selection mark keeps the stylesheet's display");
            PrototypeRunner.Select(list, fading);
            Assert.IsFalse(plain.ClassListContains(PrototypeRunner.SelectedClass));
            Assert.IsTrue(fading.ClassListContains(PrototypeRunner.SelectedClass));
            Assert.AreEqual(DisplayStyle.None, plain[0].style.display.value, "a plain mark of an unselected item is hidden");
            Assert.AreEqual(StyleKeyword.Null, fading[0].style.display.keyword, "still untouched");
        }

        [Test]
        public void StaticGatesDetachTheMotionSheetAndOnlyThat()
        {
            var root = new VisualElement();
            var child = new VisualElement();
            root.Add(child);
            var motion = ScriptableObject.CreateInstance<StyleSheet>();
            motion.name = "Inventory.motion";
            var other = ScriptableObject.CreateInstance<StyleSheet>();
            other.name = "Inventory";
            root.styleSheets.Add(other);
            root.styleSheets.Add(motion);
            child.styleSheets.Add(motion);
            Assert.AreEqual(2, MotionSheets.Detach(root));
            Assert.AreEqual(1, root.styleSheets.count);
            Assert.AreEqual(0, child.styleSheets.count);
            Assert.AreSame(other, root.styleSheets[0]);
            Object.DestroyImmediate(motion);
            Object.DestroyImmediate(other);
        }

        [UnityTest]
        public IEnumerator ClosingAnOverlayFromTheTreeLeavesNoBlocker()
        {
            Setup(Entry("Menu", "Menu.GoButton.click#0", Anim("dissolve", 100)),
                  Entry("Detail", "Detail.DialogButton.click#0", Anim("dissolve", 600)));
            yield return Boot();
            Click("GoButton");
            yield return WaitIdle();
            Click("DialogButton");
            yield return Frames(3);
            Assert.IsTrue(m_Runner.IsTransitioning);
            m_Runner.Begin(); // a restart in the middle of a transition
            yield return Frames(3);
            Assert.IsFalse(m_Runner.IsTransitioning);
            Assert.IsNull(Q("PrototypeInputBlock"));
            Assert.AreEqual(0, m_Runner.OverlayCount);
        }
    }
}
#endif
