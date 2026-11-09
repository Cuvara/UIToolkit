#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using Cuvara.UIToolkit.Prototype;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Cuvara.UIToolkit.Prototype.Tests
{
    /// <summary>
    /// Drives the prototype runner with real UI Toolkit pointer events (UiInputSimulator) on three fixture screens
    /// (Menu, Detail, Dialog overlay). Every assertion is made on the element tree and its pseudo-states, and the render is
    /// checked to be non-black (RenderCheck) so a missing PanelSettings can never pass.
    /// </summary>
    public class PrototypeRunnerTests
    {
        const string Fixtures = "Packages/com.cuvara.uitoolkit/Tests/Runtime/Prototype/Fixtures/";

        GameObject m_Object;
        PrototypeRunner m_Runner;

        static PrototypeScreenEntry Entry(string name)
        {
            return new PrototypeScreenEntry
            {
                Name = name,
                Uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Fixtures + name.ToLowerInvariant() + ".uxml"),
                Flow = AssetDatabase.LoadAssetAtPath<TextAsset>(Fixtures + name.ToLowerInvariant() + ".flow.json")
            };
        }

        static ThemeStyleSheet Theme()
        {
            return TestTheme.Load();
        }

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true; // the runner warns once about the created PanelSettings
            m_Object = new GameObject("RunnerTest");
            m_Object.AddComponent<UIDocument>();
            m_Runner = m_Object.AddComponent<PrototypeRunner>();
            m_Runner.AutoStart = false;
            m_Runner.StartScreen = "Menu";
            m_Runner.Theme = Theme();
            m_Runner.Screens.Add(Entry("Menu"));
            m_Runner.Screens.Add(Entry("Detail"));
            m_Runner.Screens.Add(Entry("Dialog"));
            m_Runner.Screens.Add(Entry("Timer"));
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            if (m_Object != null)
                Object.DestroyImmediate(m_Object);
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

        IEnumerator Click(string name)
        {
            var element = Q(name);
            Assert.IsNotNull(element, name);
            UiInputSimulator.Click(Panel, UiInputSimulator.Center(element));
            yield return Frames(2);
        }

        static int PseudoStates(VisualElement e)
        {
            var p = typeof(VisualElement).GetProperty("pseudoStates", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return (int)p.GetValue(e);
        }

        const int Active = 1, Hover = 2, Checked = 8, Disabled = 32;

        [UnityTest]
        public IEnumerator ScreenIsLaidOutAtThe1920x1080ReferenceAndRendersNonBlack()
        {
            yield return Boot();
            var root = m_Runner.Document.rootVisualElement;
            Assert.AreEqual("Menu", m_Runner.CurrentScreen);
            Assert.AreEqual(1920f, root.layout.width, 1f, "UI width: PanelSettings reference resolution");
            Assert.AreEqual(1080f, root.layout.height, 1f);
            var menu = Q("Menu");
            Assert.AreEqual(1920f, menu.worldBound.width, 1f);
            Assert.AreEqual(1080f, menu.worldBound.height, 1f);
            var go = Q("GoButton");
            Assert.IsTrue(go.worldBound.width > 100f);
            Assert.IsTrue(m_Runner.Document.panelSettings != null, "a PanelSettings is assigned");
        }

        [UnityTest]
        public IEnumerator ClickNavigatesAndBackReturns()
        {
            yield return Boot();
            yield return Click("GoButton");
            Assert.AreEqual("Detail", m_Runner.CurrentScreen);
            Assert.IsNotNull(Q("BackButton"));
            Assert.IsNull(Q("GoButton"));
            yield return Click("BackButton");
            Assert.AreEqual("Menu", m_Runner.CurrentScreen);
            Assert.AreEqual(0, m_Runner.HistoryCount);
        }

        [UnityTest]
        public IEnumerator DisabledControlDoesNotFire()
        {
            yield return Boot();
            var fired = 0;
            m_Runner.InteractionFired += i => fired++;
            yield return Click("LockedButton");
            Assert.AreEqual("Menu", m_Runner.CurrentScreen);
            Assert.AreEqual(0, fired);
            Assert.AreNotEqual(0, PseudoStates(Q("LockedButton")) & Disabled);
        }

        [UnityTest]
        public IEnumerator HoverAndActivePseudoStatesFollowThePointer()
        {
            yield return Boot();
            var go = Q("GoButton");
            Assert.AreEqual(0, PseudoStates(go) & (Hover | Active));
            UiInputSimulator.Move(Panel, UiInputSimulator.Center(go));
            yield return Frames(2);
            Assert.AreNotEqual(0, PseudoStates(go) & Hover, "hover");
            UiInputSimulator.Down(Panel, UiInputSimulator.Center(go));
            yield return Frames(1);
            Assert.AreNotEqual(0, PseudoStates(go) & Active, "active while pressed");
            UiInputSimulator.Up(Panel, UiInputSimulator.Center(go));
            yield return Frames(2);
            Assert.AreEqual("Detail", m_Runner.CurrentScreen);
            // moving away from a hovered element clears hover
            yield return Click("BackButton");
            UiInputSimulator.Move(Panel, UiInputSimulator.Center(Q("GoButton")));
            yield return Frames(1);
            UiInputSimulator.Move(Panel, new Vector2(1500, 900));
            yield return Frames(2);
            Assert.AreEqual(0, PseudoStates(Q("GoButton")) & Hover, "hover cleared");
        }

        [UnityTest]
        public IEnumerator OverlayOpensWithScrimAndClosesOnNoOrOutsideClick()
        {
            yield return Boot();
            yield return Click("GoButton");
            yield return Click("DialogButton");
            Assert.AreEqual(1, m_Runner.OverlayCount);
            Assert.AreEqual("Dialog", string.Join(",", m_Runner.OverlayNames));
            var content = m_Runner.OverlayRoot("Dialog");
            var dialog = content.Q<VisualElement>("Dialog");
            Assert.AreEqual(400f, dialog.worldBound.width, 1f);
            Assert.AreEqual(960f, dialog.worldBound.center.x, 2f, "centered");
            Assert.AreEqual(540f, dialog.worldBound.center.y, 2f, "centered");
            // the screen below does not receive clicks while the scrim is up
            UiInputSimulator.Click(Panel, UiInputSimulator.Center(Q("BackButton")));
            yield return Frames(2);
            Assert.AreEqual("Detail", m_Runner.CurrentScreen);
            Assert.AreEqual(0, m_Runner.OverlayCount, "click outside closed it (closeOnOutside)");

            yield return Click("DialogButton");
            Assert.AreEqual(1, m_Runner.OverlayCount);
            yield return Click("NoButton");
            Assert.AreEqual(0, m_Runner.OverlayCount, "No -> close-overlay");
            Assert.AreEqual("Detail", m_Runner.CurrentScreen);
        }

        [UnityTest]
        public IEnumerator OverlaysStackAndToggleClosesTheSameOne()
        {
            yield return Boot();
            yield return Click("GoButton");
            yield return Click("ToggleDialogButton"); // no scrim, not closeOnOutside, top-left
            Assert.AreEqual(1, m_Runner.OverlayCount);
            var dialog = m_Runner.OverlayRoot("Dialog").Q<VisualElement>("Dialog");
            Assert.AreEqual(0f, dialog.worldBound.x, 2f, "top-left");
            Assert.AreEqual(0f, dialog.worldBound.y, 2f);
            // without a scrim and outside-close the screen stays clickable
            yield return Click("ToggleDialogButton");
            Assert.AreEqual(0, m_Runner.OverlayCount, "toggle closes the open overlay");
        }

        [UnityTest]
        public IEnumerator MouseEnterTriggerFires()
        {
            yield return Boot();
            yield return Click("GoButton");
            UiInputSimulator.Move(Panel, UiInputSimulator.Center(Q("HoverTarget")));
            yield return Frames(2);
            Assert.AreEqual(1, m_Runner.OverlayCount, "mouse-enter -> open-overlay");
        }

        [UnityTest]
        public IEnumerator ListSelectionIsSingleAndShowsTheSelectedElement()
        {
            yield return Boot();
            var seen = -1;
            m_Runner.ListSelectionChanged += (list, i) => seen = i;
            var cell1 = Q("Cell1");
            var cell2 = Q("Cell2");
            UiInputSimulator.Click(Panel, UiInputSimulator.Center(cell1));
            yield return Frames(2);
            Assert.AreEqual(1, seen);
            Assert.IsTrue(cell1.ClassListContains("selected"));
            Assert.AreEqual(DisplayStyle.Flex, cell1.Q("Selected").style.display.value);
            Assert.AreEqual(DisplayStyle.None, cell2.Q("Selected").style.display.value);
            UiInputSimulator.Click(Panel, UiInputSimulator.Center(cell2));
            yield return Frames(2);
            Assert.AreEqual(2, seen);
            Assert.IsFalse(cell1.ClassListContains("selected"), "single selection");
            Assert.IsTrue(cell2.ClassListContains("selected"));
            Assert.AreEqual(DisplayStyle.None, cell1.Q("Selected").style.display.value);
        }

        [UnityTest]
        public IEnumerator ScrollViewScrollsWithWheelAndByDraggingWithoutClicking()
        {
            yield return Boot();
            var scroll = Q("Scroll") as ScrollView;
            Assert.AreEqual(0f, scroll.scrollOffset.y);
            // wheel
            var center = UiInputSimulator.Center(scroll);
            UiInputSimulator.Wheel(Panel, center, 3f);
            yield return Frames(2);
            Assert.Greater(scroll.scrollOffset.y, 0f, "wheel scrolls");
            scroll.scrollOffset = Vector2.zero;
            yield return Frames(1);
            // drag up by 80 px: content moves up, offset grows
            UiInputSimulator.Down(Panel, center);
            for (var i = 1; i <= 8; i++)
            {
                UiInputSimulator.Move(Panel, center + new Vector2(0, -10f * i));
                yield return null;
            }
            var beforeUp = scroll.scrollOffset.y;
            UiInputSimulator.Up(Panel, center + new Vector2(0, -80f));
            yield return Frames(2);
            Assert.Greater(beforeUp, 40f, "drag scrolls (" + beforeUp + ")");
            Assert.AreEqual("Menu", m_Runner.CurrentScreen);
        }

        [UnityTest]
        public IEnumerator ToggleChecksAndTextFieldIsEditable()
        {
            yield return Boot();
            var toggle = Q("Check") as Toggle;
            Assert.IsFalse(toggle.value);
            UiInputSimulator.Click(Panel, UiInputSimulator.Center(toggle));
            yield return Frames(2);
            Assert.IsTrue(toggle.value);
            Assert.AreNotEqual(0, PseudoStates(toggle) & Checked, ":checked");

            var field = Q("Name") as TextField;
            UiInputSimulator.Click(Panel, UiInputSimulator.Center(field));
            yield return Frames(2);
            UiInputSimulator.Type(Panel, field, "hi");
            yield return Frames(2);
            Assert.AreEqual("hi", field.value);
        }


        [UnityTest]
        public IEnumerator AfterDelayTriggerFiresOnItsOwn()
        {
            // recorded rather than read after Boot: on a slow renderer (CI) Boot's three frames can outlast the 1.2 s delay
            var shown = new System.Collections.Generic.List<string>();
            var menuAfter = -1f;
            var begun = Time.realtimeSinceStartup;
            m_Runner.ScreenShown += (name, root) =>
            {
                shown.Add(name);
                if (name == "Menu" && menuAfter < 0f)
                    menuAfter = Time.realtimeSinceStartup - begun;
            };
            m_Runner.StartScreen = "Timer";
            yield return Boot();
            Assert.AreEqual("Timer", shown[0], "the Timer screen is shown first");
            yield return new WaitForSecondsRealtime(1.7f);
            Assert.AreEqual("Menu", m_Runner.CurrentScreen, "after-delay -> navigate-to");
            CollectionAssert.AreEqual(new[] { "Timer", "Menu" }, shown);
            Assert.GreaterOrEqual(menuAfter, 1.15f, "not before its 1200 ms delay");
        }

        [UnityTest]
        public IEnumerator MouseLeaveTriggerOpensAnOverlayAtAManualPosition()
        {
            // the Timer fixture without its after-delay navigation: on a slow renderer (CI) Boot's frames alone outlast the 1.2 s delay
            // and the screen this test hovers was gone before the pointer reached it
            var timer = m_Runner.Screens.Find(e => e.Name == "Timer");
            timer.Flow = new TextAsset("{\"version\": 1, \"screen\": \"Timer\", \"interactions\": [" +
                                       "{\"id\": \"Timer.Zone.mouse-leave#0\", \"element\": \"Zone\", \"trigger\": \"mouse-leave\", \"delayMs\": 0," +
                                       " \"action\": \"open-overlay\", \"target\": \"Dialog\", \"position\": \"manual\", \"manual\": [50, 60]," +
                                       " \"closeOnOutside\": true, \"scrim\": false}]}");
            m_Runner.StartScreen = "Timer";
            yield return Boot();
            var zone = Q("Zone");
            UiInputSimulator.Move(Panel, UiInputSimulator.Center(zone));
            yield return Frames(2);
            Assert.AreEqual(0, m_Runner.OverlayCount, "entering does not trigger mouse-leave");
            UiInputSimulator.Move(Panel, new Vector2(1500, 900));
            yield return Frames(2);
            Assert.AreEqual(1, m_Runner.OverlayCount, "mouse-leave -> open-overlay");
            var dialog = m_Runner.OverlayRoot("Dialog").Q<VisualElement>("Dialog");
            Assert.AreEqual(50f, dialog.worldBound.x, 2f, "manual position x");
            Assert.AreEqual(60f, dialog.worldBound.y, 2f, "manual position y");
        }

        // W7 perf: a generic helper constrained to PointerEventBase<T> spent 1.3-1.4 s in Mono's JIT on the first simulated
        // event (measured in the Editor); gate 8 reported it as the lag of the first click.
        [Test]
        public void InputSimulatorBuildsEventsWithoutGenericMethods()
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var m in typeof(UiInputSimulator).GetMethods(all))
                Assert.IsFalse(m.IsGenericMethodDefinition, "UiInputSimulator." + m.Name + " is generic: its first call can take seconds of JIT");
        }

        // W7 perf: the first change of the element under the pointer spent 1.3-2.7 s in Mono's JIT (UI Toolkit's generic
        // enter/leave helpers); Begin compiles them on a worker thread. The warm-up must finish and must send no event.
        [UnityTest]
        public IEnumerator BeginWarmsThePointerDispatchWithoutSendingEvents()
        {
            var events = 0;
            var root = m_Runner.Document.rootVisualElement;
            EventCallback<PointerEnterEvent> enter = e => events++;
            EventCallback<PointerLeaveEvent> leave = e => events++;
            EventCallback<MouseEnterEvent> mouseEnter = e => events++;
            EventCallback<MouseLeaveEvent> mouseLeave = e => events++;
            root.RegisterCallback(enter);
            root.RegisterCallback(leave);
            root.RegisterCallback(mouseEnter);
            root.RegisterCallback(mouseLeave);
            yield return Boot();
            var button = Q("GoButton");
            button.RegisterCallback(enter);
            button.RegisterCallback(mouseEnter);
            var until = Time.realtimeSinceStartup + 10f;
            while (!PointerWarmUp.Done && Time.realtimeSinceStartup < until)
                yield return null;
            Assert.IsTrue(PointerWarmUp.Done, "the pointer dispatch warm-up did not finish within 10 s");
            Assert.AreEqual(0, events, "the warm-up sent pointer or mouse enter/leave events");
            UiInputSimulator.Move(Panel, new Vector2(1500, 900));
            UiInputSimulator.Move(Panel, UiInputSimulator.Center(button));
            yield return Frames(1);
            Assert.Greater(events, 0, "a real pointer move onto the button still sends enter events");
        }

        [UnityTest]
        public IEnumerator BackWalksTheHistoryOneScreenAtATime()
        {
            yield return Boot();
            yield return Click("GoButton");
            Assert.AreEqual("Detail", m_Runner.CurrentScreen);
            Assert.AreEqual(1, m_Runner.HistoryCount);
            Assert.IsTrue(m_Runner.Back());
            Assert.IsFalse(m_Runner.Back(), "the second Back has nothing to go to");
            Assert.AreEqual("Menu", m_Runner.CurrentScreen);
        }

        [UnityTest]
        public IEnumerator RenderedFrameIsNotBlackAndShowsTheScreenColour()
        {
            yield return Boot();
            // WaitForEndOfFrame is never evoked in batchmode (CI); there the panel has rendered into its target by the next frame
            if (Application.isBatchMode)
                yield return null;
            else
                yield return new WaitForEndOfFrame();
            var rt = m_Runner.RenderTarget;
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply(false);
            RenderTexture.active = previous;
            // menu.uxml fills the screen with rgb(30,30,30); the GoButton is blue
            var background = tex.GetPixel(1800, tex.height - 1 - 900);
            var button = tex.GetPixel(200, tex.height - 1 - 130);
            Object.DestroyImmediate(tex);
            Assert.AreEqual(30, Mathf.RoundToInt(background.r * 255f), 3, "screen background is drawn");
            Assert.Greater(button.b, 0.5f, "the blue button is drawn");
        }

        [UnityTest]
        public IEnumerator RunnerWithoutPanelSettingsCreatesOne()
        {
            yield return Boot();
            Assert.IsNotNull(m_Runner.Document.panelSettings);
            Assert.AreEqual(PanelScaleMode.ScaleWithScreenSize, m_Runner.Document.panelSettings.scaleMode);
            Assert.AreEqual(new Vector2Int(1920, 1080), m_Runner.Document.panelSettings.referenceResolution);
        }
    }
}
#endif
