using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cuvara.UIToolkit.Prototype
{
    /// <summary>
    /// Plays a Penpot prototype from the generated screens: loads screens by name, wires clicks / hover / delays from
    /// <c>&lt;Screen&gt;.flow.json</c>, implements navigate, overlays (stacked, with scrim, close on outside click), close
    /// and back history, drag scrolling, and single selection in lists. It knows nothing about the game and depends on
    /// nothing but UI Toolkit. A game's Presenters normally use the generated <c>&lt;Screen&gt;.bindings.g.cs</c> instead.
    ///
    /// The runner owns the UIDocument's tree. Its PanelSettings come from <see cref="PanelSettingsAsset"/> (a serialized
    /// reference) or, when none is assigned and the document has none, from a created instance
    /// (<see cref="CreateDefaultPanelSettings"/>) so the screen is never silently invisible.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class PrototypeRunner : MonoBehaviour
    {
        public const string SelectedClass = "selected";
        public const string SelectedElementName = "Selected";
        /// <summary>
        /// Class of a selection mark whose visibility the stylesheet drives (opacity, so it can fade): <see cref="Select"/> leaves its display
        /// alone. The converter writes it on the Selected layer of a list item that carries a selection fade.
        /// </summary>
        public const string SelectionMarkClass = "selection-mark";
        const float DragThreshold = 6f;

        public List<PrototypeScreenEntry> Screens = new List<PrototypeScreenEntry>();
        public string StartScreen;
        public PanelSettings PanelSettingsAsset;
        /// <summary>Theme for a created PanelSettings (ignored when <see cref="PanelSettingsAsset"/> is assigned).</summary>
        public ThemeStyleSheet Theme;
        public bool AutoStart = true;
        /// <summary>
        /// Plays the Penpot animation of an interaction (dissolve / slide / push) with USS transitions. Off for the static gates
        /// (screen captures, gate 7): navigation is then instant, exactly as before animation existed.
        /// </summary>
        public bool Animate = true;

        sealed class Layer
        {
            public string Name;
            public VisualElement Container; // scrim layer
            public VisualElement Content;
            public PrototypeAnimation OpenAnimation; // played when it opened: click-outside and a second toggle press play it inverted
            public bool Closing;
            public List<IVisualElementScheduledItem> Timers = new List<IVisualElementScheduledItem>();
        }

        readonly Dictionary<string, PrototypeFlowFile> m_Flows = new Dictionary<string, PrototypeFlowFile>();
        sealed class HistoryEntry
        {
            public string Screen;
            public PrototypeAnimation Animation; // the animation of the navigation that left Screen (played in reverse by Back)
        }

        readonly Stack<HistoryEntry> m_History = new Stack<HistoryEntry>();
        readonly List<Layer> m_Overlays = new List<Layer>();
        readonly List<IVisualElementScheduledItem> m_ScreenTimers = new List<IVisualElementScheduledItem>();
        VisualElement m_Host;
        VisualElement m_ScreenRoot;
        VisualElement m_OverlayLayer;
        PrototypeTransition m_Transition;
        VisualElement m_Blocker;

        public string CurrentScreen { get; private set; }
        public int OverlayCount { get { return m_Overlays.Count; } }
        public int HistoryCount { get { return m_History.Count; } }
        public VisualElement ScreenRoot { get { return m_ScreenRoot; } }
        public VisualElement Host { get { return m_Host; } }
        /// <summary>True while a screen or overlay transition runs: pointer input is blocked until it ends.</summary>
        public bool IsTransitioning { get { return m_Transition != null && !m_Transition.Finished; } }
        /// <summary>The last transition that started (kind <c>screen</c>, <c>overlay-open</c> or <c>overlay-close</c>) and the animation it played; null before the first.</summary>
        public PrototypeTransitionInfo LastTransition { get; private set; }
        public UIDocument Document { get { return GetComponent<UIDocument>(); } }

        /// <summary>Names of the open overlays, bottom to top.</summary>
        public IEnumerable<string> OverlayNames
        {
            get
            {
                foreach (var o in m_Overlays)
                    yield return o.Name;
            }
        }

        public VisualElement OverlayRoot(string name)
        {
            foreach (var o in m_Overlays)
            {
                if (o.Name == name)
                    return o.Content;
            }
            return null;
        }

        public event Action<string, VisualElement> ScreenShown;
        public event Action<string, VisualElement> OverlayShown;
        public event Action<string> OverlayClosed;
        public event Action<string, int> ListSelectionChanged;
        public event Action<PrototypeInteraction> InteractionFired;
        /// <summary>A control's trigger reached the runner, before the transition guard: the flag is true when the request is
        /// dropped because a transition runs (<see cref="InteractionFired"/> is then not raised).</summary>
        public event Action<PrototypeInteraction, bool> InteractionRequested;
        /// <summary>A transition ended (or was completed early by the next one): kind is <c>screen</c>, <c>overlay-open</c> or <c>overlay-close</c>.</summary>
        public event Action<string, string> TransitionFinished;

        void Start()
        {
            if (AutoStart)
                Begin();
        }

        void OnDisable()
        {
            CancelTimers();
            FinishTransition();
        }

        /// <summary>Panel settings for a runner without an assigned asset: 1920x1080 reference, match 0.5.</summary>
        public static PanelSettings CreateDefaultPanelSettings()
        {
            return CreateDefaultPanelSettings(null);
        }

        public static PanelSettings CreateDefaultPanelSettings(ThemeStyleSheet theme)
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "PrototypeRunnerPanelSettings (created)";
            ConfigurePanelSettings(settings);
            settings.themeStyleSheet = theme;
            return settings;
        }

        public static void ConfigurePanelSettings(PanelSettings settings)
        {
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
        }

        // PanelSettings the runner created or copied: it is the only one it ever writes to (targetTexture) and destroys.
        // A PanelSettingsAsset (a project asset, maybe shared with other documents) is only ever read.
        PanelSettings m_OwnedSettings;
        PanelSettings m_OwnedFrom;

        void EnsurePanelSettings()
        {
            var document = Document;
            if (m_OwnedSettings != null && (PanelSettingsAsset == null || PanelSettingsAsset == m_OwnedFrom))
            {
                document.panelSettings = m_OwnedSettings;
                return;
            }
            if (PanelSettingsAsset != null)
            {
                document.panelSettings = PanelSettingsAsset;
                return;
            }
            if (document.panelSettings == null)
            {
                Debug.LogWarning("[Cuvara.UIToolkit] PrototypeRunner has no PanelSettings: using a created 1920x1080 ScaleWithScreenSize instance. " +
                                 "Assign PanelSettingsAsset to control the theme.");
                DestroyOwnedSettings();
                m_OwnedSettings = CreateDefaultPanelSettings(Theme);
                m_OwnedSettings.hideFlags = HideFlags.HideAndDontSave;
                m_OwnedFrom = null;
                document.panelSettings = m_OwnedSettings;
            }
        }

        /// <summary>
        /// Renders the panel into a RenderTexture of the given size instead of the Game view (captures and automated
        /// tests: the layout is then exactly 1920x1080 whatever the Game view looks like). Call before <see cref="Begin"/>.
        /// The previous target is released; an assigned or shared PanelSettings is never changed: the runner renders through
        /// a copy it owns and destroys.
        /// </summary>
        public RenderTexture UseRenderTarget(int width = 1920, int height = 1080)
        {
            EnsurePanelSettings();
            var settings = Document.panelSettings;
            if (settings != m_OwnedSettings)
            {
                var copy = Instantiate(settings);
                copy.name = settings.name + " (runner copy)";
                copy.hideFlags = HideFlags.HideAndDontSave;
                DestroyOwnedSettings();
                m_OwnedSettings = copy;
                m_OwnedFrom = PanelSettingsAsset;
                Document.panelSettings = copy;
                settings = copy;
            }
            ReleaseTarget();
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "PrototypeRunnerTarget",
                hideFlags = HideFlags.HideAndDontSave
            };
            target.Create();
            settings.targetTexture = target;
            RenderTarget = target;
            return target;
        }

        public RenderTexture RenderTarget { get; private set; }

        /// <summary>The PanelSettings the runner owns (a created default or the copy used with a render target), or null.</summary>
        public PanelSettings OwnedPanelSettings { get { return m_OwnedSettings; } }

        // resources the runner created are destroyed at once: a repeated UseRenderTarget must not pile up targets until the frame ends
        static void DestroyOwnedObject(UnityEngine.Object o)
        {
            DestroyImmediate(o);
        }

        void ReleaseTarget()
        {
            if (RenderTarget == null)
                return;
            if (m_OwnedSettings != null && m_OwnedSettings.targetTexture == RenderTarget)
                m_OwnedSettings.targetTexture = null;
            RenderTarget.Release();
            DestroyOwnedObject(RenderTarget);
            RenderTarget = null;
        }

        void DestroyOwnedSettings()
        {
            if (m_OwnedSettings == null)
                return;
            m_OwnedSettings.targetTexture = null;
            var document = GetComponent<UIDocument>();
            if (document != null && document.panelSettings == m_OwnedSettings)
                document.panelSettings = null;
            DestroyOwnedObject(m_OwnedSettings);
            m_OwnedSettings = null;
            m_OwnedFrom = null;
        }

        void OnDestroy()
        {
            ReleaseTarget();
            DestroyOwnedSettings();
        }

        /// <summary>Builds the host element tree and shows the start screen.</summary>
        public void Begin()
        {
            PointerWarmUp.Start();
            EnsurePanelSettings();
            FinishTransition();
            LoadFlows();
            var document = Document;
            document.visualTreeAsset = null;
            var root = document.rootVisualElement;
            root.Clear();
            // without a theme (or its .unity-ui-document__root rule) the document root collapses to its content height
            root.style.position = Position.Absolute;
            root.style.left = 0;
            root.style.top = 0;
            root.style.right = 0;
            root.style.bottom = 0;
            m_Host = new VisualElement { name = "PrototypeHost" };
            m_Host.style.position = Position.Absolute;
            m_Host.style.left = 0;
            m_Host.style.top = 0;
            m_Host.style.right = 0;
            m_Host.style.bottom = 0;
            root.Add(m_Host);
            m_OverlayLayer = new VisualElement { name = "PrototypeOverlays", pickingMode = PickingMode.Ignore };
            m_OverlayLayer.style.position = Position.Absolute;
            m_OverlayLayer.style.left = 0;
            m_OverlayLayer.style.top = 0;
            m_OverlayLayer.style.right = 0;
            m_OverlayLayer.style.bottom = 0;
            root.Add(m_OverlayLayer);
            m_History.Clear();
            var start = ResolveStart();
            if (string.IsNullOrEmpty(start))
            {
                Debug.LogError("[Cuvara.UIToolkit] PrototypeRunner has no screens.");
                return;
            }
            ShowScreen(start);
        }

        void LoadFlows()
        {
            m_Flows.Clear();
            foreach (var entry in Screens)
            {
                if (entry == null || entry.Flow == null)
                    continue;
                try
                {
                    m_Flows[entry.Name] = PrototypeFlowFile.Parse(entry.Flow.text);
                }
                catch (Exception e)
                {
                    Debug.LogError("[Cuvara.UIToolkit] flow of " + entry.Name + ": " + e.Message);
                }
            }
        }

        string ResolveStart()
        {
            if (!string.IsNullOrEmpty(StartScreen) && Find(StartScreen) != null)
                return StartScreen;
            foreach (var flow in m_Flows.Values)
            {
                if (flow.flows == null)
                    continue;
                foreach (var f in flow.flows)
                {
                    if (!string.IsNullOrEmpty(f.start) && Find(f.start) != null)
                        return f.start;
                }
            }
            return Screens.Count > 0 && Screens[0] != null ? Screens[0].Name : null;
        }

        PrototypeScreenEntry Find(string name)
        {
            foreach (var s in Screens)
            {
                if (s != null && s.Name == name)
                    return s;
            }
            return null;
        }

        // ------------------------------------------------------------------------------------------ navigation

        /// <summary>Shows a screen without touching the history (used for the first screen). Never animated.</summary>
        public bool ShowScreen(string name)
        {
            return Show(name, null);
        }

        bool Show(string name, PrototypeAnimation animation)
        {
            var entry = Find(name);
            if (entry == null || entry.Uxml == null)
            {
                Debug.LogError("[Cuvara.UIToolkit] unknown screen \"" + name + "\"");
                return false;
            }
            FinishTransition(); // a request while another transition runs completes the running one first
            CloseAllOverlays();
            CancelTimers();
            var oldRoot = m_ScreenRoot != null && m_ScreenRoot.parent == m_Host ? m_ScreenRoot : null;
            var newRoot = entry.Uxml.Instantiate();
            newRoot.style.width = Length.Percent(100);
            newRoot.style.height = Length.Percent(100);
            if (!Animate)
                MotionSheets.Detach(newRoot);
            var animate = oldRoot != null && IsPlayable(animation);
            if (!animate)
                m_Host.Clear();
            else
                PrepareStack(oldRoot, newRoot, animation);
            m_ScreenRoot = newRoot;
            CurrentScreen = name;
            Wire(name, newRoot, m_ScreenTimers);
            if (animate)
                BeginScreenTransition(name, oldRoot, newRoot, animation);
            else
                m_Host.Add(newRoot);
            if (ScreenShown != null)
                ScreenShown(name, newRoot);
            return true;
        }

        /// <summary>navigate-to: replaces the screen, remembers the previous one (and the animation) for <see cref="Back"/>.</summary>
        public bool Navigate(string name, PrototypeAnimation animation = null)
        {
            var previous = CurrentScreen;
            if (!Show(name, animation))
                return false;
            if (!string.IsNullOrEmpty(previous))
                m_History.Push(new HistoryEntry { Screen = previous, Animation = animation != null && animation.IsSet ? animation.Clone() : null });
            return true;
        }

        /// <summary>previous-screen: plays the reverse of the navigation it undoes when the profile says <c>motion.back: reverse</c>.</summary>
        public bool Back()
        {
            if (m_History.Count == 0)
                return false;
            var entry = m_History.Pop();
            PrototypeAnimation animation = null;
            if (entry.Animation != null && entry.Animation.IsSet && FlowMotion().back != "none")
                animation = Reverse(entry.Animation);
            return Show(entry.Screen, animation);
        }

        public bool OpenOverlay(string name, string position = "center", bool closeOnOutside = true, bool scrim = true, float[] manual = null,
            PrototypeAnimation animation = null)
        {
            var entry = Find(name);
            if (entry == null || entry.Uxml == null)
            {
                Debug.LogError("[Cuvara.UIToolkit] unknown overlay \"" + name + "\"");
                return false;
            }
            FinishTransition();
            var layer = new Layer { Name = name, OpenAnimation = animation != null && animation.IsSet ? animation.Clone() : null };
            var container = new VisualElement { name = "PrototypeOverlay-" + name };
            container.style.position = Position.Absolute;
            container.style.left = 0;
            container.style.top = 0;
            container.style.right = 0;
            container.style.bottom = 0;
            container.style.backgroundColor = scrim ? new Color(0f, 0f, 0f, 0.5f) : new Color(0f, 0f, 0f, 0f);
            container.pickingMode = closeOnOutside || scrim ? PickingMode.Position : PickingMode.Ignore;
            ApplyPosition(container, position, manual);
            var content = entry.Uxml.Instantiate();
            if (!Animate)
                MotionSheets.Detach(content);
            if (position == "manual" && manual != null && manual.Length >= 2)
            {
                content.style.position = Position.Absolute;
                content.style.left = manual[0];
                content.style.top = manual[1];
            }
            content.pickingMode = PickingMode.Ignore;
            container.Add(content);
            layer.Container = container;
            layer.Content = content;
            container.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target != container)
                    return;
                if (closeOnOutside)
                    CloseLayerAnimated(layer, Inverted(layer.OpenAnimation));
                e.StopPropagation();
            });
            m_OverlayLayer.Add(container);
            m_Overlays.Add(layer);
            Wire(name, content, layer.Timers);
            if (OverlayShown != null)
                OverlayShown(name, content);
            if (IsPlayable(animation) && animation.type != "push")
                BeginOverlayOpen(layer, animation);
            return true;
        }

        static void ApplyPosition(VisualElement container, string position, float[] manual)
        {
            var justify = Justify.Center;
            var align = Align.Center;
            switch (position)
            {
                case "top-left": justify = Justify.FlexStart; align = Align.FlexStart; break;
                case "top-center": justify = Justify.Center; align = Align.FlexStart; break;
                case "top-right": justify = Justify.FlexEnd; align = Align.FlexStart; break;
                case "bottom-left": justify = Justify.FlexStart; align = Align.FlexEnd; break;
                case "bottom-center": justify = Justify.Center; align = Align.FlexEnd; break;
                case "bottom-right": justify = Justify.FlexEnd; align = Align.FlexEnd; break;
            }
            // the container is a row: justify spreads horizontally, align vertically
            container.style.flexDirection = FlexDirection.Row;
            container.style.justifyContent = justify;
            container.style.alignItems = align;
        }

        /// <summary>close-overlay (top one) or a named overlay; instant unless an animation is given.</summary>
        public bool CloseOverlay(string name = null, PrototypeAnimation animation = null)
        {
            var layer = FindLayer(name);
            if (layer == null)
                return false;
            if (animation != null)
                CloseLayerAnimated(layer, animation);
            else
                CloseLayer(layer);
            return true;
        }

        Layer FindLayer(string name)
        {
            for (var i = m_Overlays.Count - 1; i >= 0; i--)
            {
                if (string.IsNullOrEmpty(name) || m_Overlays[i].Name == name)
                    return m_Overlays[i];
            }
            return null;
        }

        void CloseLayer(Layer layer)
        {
            if (!m_Overlays.Contains(layer))
                return;
            foreach (var t in layer.Timers)
                t.Pause();
            layer.Container.RemoveFromHierarchy();
            m_Overlays.Remove(layer);
            if (OverlayClosed != null)
                OverlayClosed(layer.Name);
        }

        public void CloseAllOverlays()
        {
            while (m_Overlays.Count > 0)
                CloseLayer(m_Overlays[m_Overlays.Count - 1]);
        }

        bool IsOpen(string name)
        {
            foreach (var o in m_Overlays)
            {
                if (o.Name == name)
                    return true;
            }
            return false;
        }

        void CancelTimers()
        {
            foreach (var t in m_ScreenTimers)
                t.Pause();
            m_ScreenTimers.Clear();
        }

        // ---------------------------------------------------------------------------------------------- wiring

        void Wire(string screen, VisualElement root, List<IVisualElementScheduledItem> timers)
        {
            PrototypeFlowFile flow;
            if (!m_Flows.TryGetValue(screen, out flow))
                return;
            if (flow.interactions != null)
            {
                foreach (var interaction in flow.interactions)
                    WireInteraction(root, interaction, timers);
            }
            if (flow.lists != null)
            {
                foreach (var list in flow.lists)
                {
                    var element = root.Q<VisualElement>(list.name);
                    if (element == null)
                        continue;
                    if (list.selectable)
                        AttachSelection(element, list.name);
                    var scroll = element as ScrollView;
                    if (scroll != null)
                        scroll.AddManipulator(new DragScroller(scroll));
                }
            }
            if (flow.scrolls != null)
            {
                foreach (var s in flow.scrolls)
                {
                    var scroll = root.Q<ScrollView>(s.name);
                    if (scroll != null)
                        scroll.AddManipulator(new DragScroller(scroll));
                }
            }
        }

        void WireInteraction(VisualElement root, PrototypeInteraction interaction, List<IVisualElementScheduledItem> timers)
        {
            var element = root.Q<VisualElement>(interaction.element);
            if (element == null)
            {
                Debug.LogWarning("[Cuvara.UIToolkit] interaction " + interaction.id + ": element not found");
                return;
            }
            switch (interaction.trigger)
            {
                case "click":
                    UiClicks.Attach(element, () =>
                    {
                        if (element.enabledInHierarchy)
                            Fire(interaction, element);
                    });
                    break;
                case "mouse-enter":
                    element.RegisterCallback<PointerEnterEvent>(e =>
                    {
                        if (element.enabledInHierarchy)
                            Fire(interaction, element);
                    });
                    break;
                case "mouse-leave":
                    element.RegisterCallback<PointerLeaveEvent>(e =>
                    {
                        if (element.enabledInHierarchy)
                            Fire(interaction, element);
                    });
                    break;
                case "after-delay":
                    timers.Add(element.schedule.Execute(() => Fire(interaction, element)).StartingIn((long)interaction.delayMs));
                    break;
                default:
                    Debug.LogWarning("[Cuvara.UIToolkit] interaction " + interaction.id + ": trigger " + interaction.trigger + " is not supported");
                    break;
            }
        }

        void Fire(PrototypeInteraction i, VisualElement source)
        {
            if (InteractionRequested != null)
                InteractionRequested(i, IsTransitioning);
            if (IsTransitioning)
            {
                // input is blocked while a transition runs; a timer-driven interaction waits for the end instead of being lost
                if (i.trigger == "after-delay" && source.panel != null)
                    m_Host.schedule.Execute(() => Fire(i, source)).StartingIn(50);
                return;
            }
            if (InteractionFired != null)
                InteractionFired(i);
            var animation = i.animation != null && i.animation.IsSet ? i.animation : null;
            switch (i.action)
            {
                case "navigate-to":
                    Navigate(i.target, animation);
                    break;
                case "previous-screen":
                    Back();
                    break;
                case "open-overlay":
                    OpenOverlay(i.target, i.position, i.closeOnOutside, i.scrim, i.manual, animation);
                    break;
                case "toggle-overlay":
                    if (IsOpen(i.target))
                        CloseLayerAnimated(FindLayer(i.target), Inverted(FindLayer(i.target).OpenAnimation));
                    else
                        OpenOverlay(i.target, i.position, i.closeOnOutside, i.scrim, i.manual, animation);
                    break;
                case "close-overlay":
                    // a hover overlay that closes on mouse-leave closes without animation (Penpot's viewer does the same)
                    CloseOverlay(string.IsNullOrEmpty(i.target) ? OverlayOf(source) : i.target, i.trigger == "mouse-leave" ? null : animation);
                    break;
                default:
                    Debug.LogWarning("[Cuvara.UIToolkit] unsupported action " + i.action);
                    break;
            }
        }

        // the overlay that contains the clicked element (close-overlay without a destination closes its own overlay)
        string OverlayOf(VisualElement source)
        {
            foreach (var o in m_Overlays)
            {
                for (var e = source; e != null; e = e.parent)
                {
                    if (e == o.Content)
                        return o.Name;
                }
            }
            return null;
        }

        // ----------------------------------------------------------------------------------------- transitions

        PrototypeMotion FlowMotion()
        {
            PrototypeFlowFile flow;
            if (CurrentScreen != null && m_Flows.TryGetValue(CurrentScreen, out flow) && flow.motion != null)
                return flow.motion;
            return new PrototypeMotion();
        }

        bool IsPlayable(PrototypeAnimation a)
        {
            if (!Animate || a == null || !a.IsSet || m_Host == null || m_Host.panel == null || !FlowMotion().enabled)
                return false;
            return a.type == "dissolve" || a.type == "slide" || a.type == "push";
        }

        /// <summary>The animation that undoes <paramref name="a"/>: the same type and duration moving the other way (a slide also swaps in and out).</summary>
        public static PrototypeAnimation Reverse(PrototypeAnimation a)
        {
            var r = a.Clone();
            if (a.type == "slide")
            {
                r.way = a.way == "out" ? "in" : "out";
                r.direction = PrototypeAnimation.Invert(a.direction);
            }
            else if (a.type == "push")
            {
                r.direction = PrototypeAnimation.Invert(a.direction);
            }
            return r;
        }

        // closing an overlay by click outside or a second toggle press plays the open animation with the direction inverted
        static PrototypeAnimation Inverted(PrototypeAnimation a)
        {
            if (a == null)
                return null;
            var r = a.Clone();
            if (a.type == "slide")
                r.direction = PrototypeAnimation.Invert(a.direction);
            return r;
        }

        static Vector2 Dir(string direction)
        {
            switch (direction)
            {
                case "right": return new Vector2(1f, 0f);
                case "left": return new Vector2(-1f, 0f);
                case "down": return new Vector2(0f, 1f);
                case "up": return new Vector2(0f, -1f);
                default: return Vector2.zero;
            }
        }

        static Translate Percent(Vector2 v)
        {
            return new Translate(new Length(v.x, LengthUnit.Percent), new Length(v.y, LengthUnit.Percent));
        }

        static Translate Pixels(Vector2 v)
        {
            return new Translate(new Length(v.x, LengthUnit.Pixel), new Length(v.y, LengthUnit.Pixel));
        }

        static PrototypeTrack TrackOf(VisualElement element, PrototypeAnimation a, float delay, float duration)
        {
            return new PrototypeTrack { Element = element, DelayMs = delay, DurationMs = duration, Easing = PrototypeTrack.ParseEasing(a.easing) };
        }

        static PrototypeTrack Fade(VisualElement element, PrototypeAnimation a, float from, float to, float delay, float duration)
        {
            var t = TrackOf(element, a, delay, duration);
            t.HasOpacity = true;
            t.OpacityFrom = from;
            t.OpacityTo = to;
            return t;
        }

        static PrototypeTrack Move(VisualElement element, PrototypeAnimation a, Translate from, Translate to)
        {
            var t = TrackOf(element, a, 0f, a.durationMs);
            t.HasMove = true;
            t.MoveFrom = from;
            t.MoveTo = to;
            return t;
        }

        static void Fill(VisualElement e)
        {
            e.style.position = Position.Absolute;
            e.style.left = 0;
            e.style.top = 0;
        }

        void PrepareStack(VisualElement oldRoot, VisualElement newRoot, PrototypeAnimation a)
        {
            Fill(oldRoot);
            Fill(newRoot);
            if (a.type == "slide" && a.way == "out")
                m_Host.Insert(0, newRoot); // the old screen slides away above the new one
            else
                m_Host.Add(newRoot);
        }

        void BeginScreenTransition(string name, VisualElement oldRoot, VisualElement newRoot, PrototypeAnimation a)
        {
            BlockInput();
            PrototypeTransition tr = null;
            tr = new PrototypeTransition(m_Host, "screen", name, () =>
            {
                oldRoot.RemoveFromHierarchy();
                newRoot.style.position = StyleKeyword.Null;
                newRoot.style.left = StyleKeyword.Null;
                newRoot.style.top = StyleKeyword.Null;
                EndTransition(tr, newRoot);
            });
            m_Transition = tr;
            LastTransition = new PrototypeTransitionInfo { Kind = "screen", Name = name, Animation = a.Clone() };
            tr.Info = LastTransition;
            var d = a.durationMs;
            var v = Dir(a.direction);
            var none = Percent(Vector2.zero);
            var tracks = new List<PrototypeTrack>();
            switch (a.type)
            {
                case "dissolve":
                    // Penpot's viewer: the new frame fades in over the whole duration, the old one starts fading after a third of it
                    tracks.Add(Fade(newRoot, a, 0f, 1f, 0f, d));
                    tracks.Add(Fade(oldRoot, a, 1f, 0f, d / 3f, d * 2f / 3f));
                    break;
                case "slide":
                    if (a.way == "out")
                    {
                        tracks.Add(Move(oldRoot, a, none, Percent(v * 100f)));
                        if (a.offsetEffect)
                            tracks.Add(Move(newRoot, a, Percent(-v * 20f), none));
                    }
                    else
                    {
                        tracks.Add(Move(newRoot, a, Percent(-v * 100f), none));
                        if (a.offsetEffect)
                        {
                            var drift = Move(oldRoot, a, none, Percent(v * 20f));
                            drift.HasOpacity = true;
                            drift.OpacityFrom = 1f;
                            drift.OpacityTo = 0f;
                            tracks.Add(drift);
                        }
                    }
                    break;
                default: // push: both frames move by the full panel size
                    tracks.Add(Move(newRoot, a, Percent(-v * 100f), none));
                    tracks.Add(Move(oldRoot, a, none, Percent(v * 100f)));
                    break;
            }
            tr.Start(tracks);
        }

        // translation that moves an overlay's content completely out of the panel towards `direction`. The content is a child of the
        // overlay container, which fills the panel, so the container's layout is the panel and the content's layout is its place in it
        // (the world transform is not yet up to date when the first layout of a new element is reported).
        static Vector2 ExitOffset(VisualElement content, string direction)
        {
            var panel = content.parent != null ? content.parent.layout : new Rect(0f, 0f, 1920f, 1080f);
            if (panel.width < 1f || panel.height < 1f)
                panel = new Rect(0f, 0f, 1920f, 1080f);
            var r = content.layout;
            switch (direction)
            {
                case "right": return new Vector2(panel.width - r.x, 0f);
                case "left": return new Vector2(-r.xMax, 0f);
                case "down": return new Vector2(0f, panel.height - r.y);
                case "up": return new Vector2(0f, -r.yMax);
                default: return Vector2.zero;
            }
        }

        void BeginOverlayOpen(Layer layer, PrototypeAnimation a)
        {
            BlockInput();
            PrototypeTransition tr = null;
            tr = new PrototypeTransition(m_Host, "overlay-open", layer.Name, () => EndTransition(tr, layer.Content));
            m_Transition = tr;
            LastTransition = new PrototypeTransitionInfo { Kind = "overlay-open", Name = layer.Name, Animation = a.Clone() };
            tr.Info = LastTransition;
            if (a.type == "dissolve")
            {
                tr.Start(new List<PrototypeTrack> { Fade(layer.Container, a, 0f, 1f, 0f, a.durationMs) });
                return;
            }
            // slide: the way in starts outside the panel edge opposite to the direction of motion; where that is depends on where
            // the overlay sits, so wait until its layout has settled (way and offsetEffect are ignored for overlays, like the viewer)
            var last = Rect.zero;
            var stable = 0;
            IVisualElementScheduledItem wait = null;
            wait = m_Host.schedule.Execute(() =>
            {
                if (tr.Finished)
                {
                    wait.Pause();
                    return;
                }
                var r = layer.Content.layout;
                var panel = layer.Content.parent != null ? layer.Content.parent.layout : Rect.zero;
                stable = r.width >= 1f && panel.width >= 1f && r == last ? stable + 1 : 0;
                last = r;
                if (stable < 2)
                    return;
                wait.Pause();
                var from = ExitOffset(layer.Content, PrototypeAnimation.Invert(a.direction));
                tr.Start(new List<PrototypeTrack> { Move(layer.Content, a, Pixels(from), Pixels(Vector2.zero)) });
            }).Every(1);
            tr.OnCancelUnstarted = () => wait.Pause();
        }

        void CloseLayerAnimated(Layer layer, PrototypeAnimation a)
        {
            if (layer == null || layer.Closing)
                return;
            if (!IsPlayable(a) || a.type == "push")
            {
                CloseLayer(layer);
                return;
            }
            FinishTransition();
            layer.Closing = true;
            BlockInput();
            PrototypeTransition tr = null;
            tr = new PrototypeTransition(m_Host, "overlay-close", layer.Name, () =>
            {
                EndTransition(tr, layer.Content); // the content is still on the panel: its bindings hear the end before the overlay goes
                CloseLayer(layer);
            });
            m_Transition = tr;
            LastTransition = new PrototypeTransitionInfo { Kind = "overlay-close", Name = layer.Name, Animation = a.Clone() };
            tr.Info = LastTransition;
            if (a.type == "dissolve")
                tr.Start(new List<PrototypeTrack> { Fade(layer.Container, a, 1f, 0f, 0f, a.durationMs) });
            else
                tr.Start(new List<PrototypeTrack> { Move(layer.Content, a, Pixels(Vector2.zero), Pixels(ExitOffset(layer.Content, a.direction))) });
        }

        void EndTransition(PrototypeTransition tr, VisualElement root)
        {
            if (m_Transition == tr)
                m_Transition = null;
            UnblockInput();
            ScreenTransitionSignal.Send(root); // once per completed transition, whatever ended it
            if (TransitionFinished != null)
                TransitionFinished(tr.Kind, tr.Name);
        }

        /// <summary>Completes the running transition at once (end state, old screen removed). No-op when none runs.</summary>
        public void FinishTransition()
        {
            if (m_Transition != null)
                m_Transition.Finish();
        }

        // a transparent element above the whole UI swallows pointer input while a transition runs (owner decision)
        void BlockInput()
        {
            if (m_Blocker != null)
                return;
            m_Blocker = new VisualElement { name = "PrototypeInputBlock", pickingMode = PickingMode.Position };
            m_Blocker.style.position = Position.Absolute;
            m_Blocker.style.left = 0;
            m_Blocker.style.top = 0;
            m_Blocker.style.right = 0;
            m_Blocker.style.bottom = 0;
            Document.rootVisualElement.Add(m_Blocker);
        }

        void UnblockInput()
        {
            if (m_Blocker == null)
                return;
            m_Blocker.RemoveFromHierarchy();
            m_Blocker = null;
        }

        // ------------------------------------------------------------------------------------------- selection

        void AttachSelection(VisualElement list, string name)
        {
            var listView = list as ListView;
            if (listView != null)
            {
                listView.selectionType = SelectionType.Single;
                listView.selectedIndicesChanged += indices =>
                {
                    foreach (var index in indices)
                    {
                        if (ListSelectionChanged != null)
                            ListSelectionChanged(name, index);
                        return;
                    }
                };
                return;
            }
            // Children() and parent are logical: the items of a ScrollView are its children, not its content container's
            var content = list;
            VisualElement pressed = null;
            list.RegisterCallback<PointerDownEvent>(e =>
            {
                pressed = ItemOf(content, e.target as VisualElement);
            });
            list.RegisterCallback<PointerUpEvent>(e =>
            {
                var item = ItemOf(content, e.target as VisualElement);
                var down = pressed;
                pressed = null;
                if (item == null || item != down)
                    return;
                Select(content, item);
                if (ListSelectionChanged != null)
                    ListSelectionChanged(name, content.IndexOf(item));
            });
        }

        static VisualElement ItemOf(VisualElement content, VisualElement target)
        {
            var item = target;
            while (item != null && item.parent != content)
                item = item.parent;
            return item;
        }

        /// <summary>
        /// Single selection: the item gets the class <c>selected</c>; an element named Selected inside it is shown, unless it is a
        /// <see cref="SelectionMarkClass"/> mark (its opacity follows the <c>selected</c> class in the stylesheet and may transition).
        /// </summary>
        public static void Select(VisualElement list, VisualElement item)
        {
            foreach (var other in list.Children())
            {
                var on = other == item;
                other.EnableInClassList(SelectedClass, on);
                var mark = other.Q(SelectedElementName);
                if (mark != null && !mark.ClassListContains(SelectionMarkClass))
                    mark.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // ------------------------------------------------------------------------------------------ drag scroll

        /// <summary>Scrolls a ScrollView by dragging with the primary pointer; a drag does not click what is under it.</summary>
        sealed class DragScroller : Manipulator
        {
            readonly ScrollView m_Scroll;
            Vector2 m_Start;
            Vector2 m_Offset;
            int m_Pointer = -1;
            bool m_Dragging;

            public DragScroller(ScrollView scroll)
            {
                m_Scroll = scroll;
            }

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
            }

            void OnDown(PointerDownEvent e)
            {
                if (e.button != 0)
                    return;
                m_Pointer = e.pointerId;
                m_Start = e.position;
                m_Offset = m_Scroll.scrollOffset;
                m_Dragging = false;
            }

            void OnMove(PointerMoveEvent e)
            {
                if (m_Pointer != e.pointerId || (e.pressedButtons & 1) == 0)
                    return;
                var delta = (Vector2)e.position - m_Start;
                if (!m_Dragging && delta.magnitude < DragThreshold)
                    return;
                if (!m_Dragging)
                {
                    m_Dragging = true;
                    target.CapturePointer(e.pointerId);
                }
                var vertical = m_Scroll.mode != ScrollViewMode.Horizontal;
                var horizontal = m_Scroll.mode != ScrollViewMode.Vertical;
                m_Scroll.scrollOffset = new Vector2(
                    horizontal ? m_Offset.x - delta.x : m_Offset.x,
                    vertical ? m_Offset.y - delta.y : m_Offset.y);
                e.StopPropagation();
            }

            void OnUp(PointerUpEvent e)
            {
                if (m_Pointer != e.pointerId)
                    return;
                m_Pointer = -1;
                if (m_Dragging)
                {
                    m_Dragging = false;
                    if (target.HasPointerCapture(e.pointerId))
                        target.ReleasePointer(e.pointerId);
                    e.StopImmediatePropagation(); // a drag is not a click
                }
            }
        }
    }

    /// <summary>
    /// UI Toolkit sends pointer enter/leave events through generic helpers constrained to their own event types
    /// (<c>PointerEventsHelper.SendEnterLeave&lt;TLeave, TEnter&gt;</c>, the same in <c>MouseEventsHelper</c>). Under Mono the JIT
    /// spent 1.3-2.7 s compiling them the first time the element under the pointer changed (measured in the Editor with deep
    /// profiling: <c>ElementUnderPointer.CommitElementUnderPointers</c>), which stalled the first click or hover of a prototype.
    /// They are compiled once per domain on a worker thread while the start screen shows. Nothing is executed there (UI Toolkit
    /// is not thread-safe): <c>MethodHandle.GetFunctionPointer</c> only makes Mono compile the method.
    /// <c>RuntimeHelpers.PrepareMethod</c> does not compile under Unity's Mono. IL2CPP has no JIT.
    /// </summary>
    internal static class PointerWarmUp
    {
        static int s_State; // 0 not started, 1 running, 2 done

        /// <summary>True once the helpers are compiled (or there was nothing to do).</summary>
        public static bool Done { get { return Volatile.Read(ref s_State) == 2; } }

        public static void Start()
        {
#if ENABLE_MONO
            if (Interlocked.CompareExchange(ref s_State, 1, 0) != 0)
                return;
            var methods = new List<MethodInfo>();
            Add(methods, "UnityEngine.UIElements.PointerEventsHelper", typeof(PointerLeaveEvent), typeof(PointerEnterEvent));
            Add(methods, "UnityEngine.UIElements.MouseEventsHelper", typeof(MouseLeaveEvent), typeof(MouseEnterEvent));
            if (methods.Count == 0 || !ThreadPool.QueueUserWorkItem(_ => Compile(methods)))
                Volatile.Write(ref s_State, 2);
#else
            Volatile.Write(ref s_State, 2);
#endif
        }

        static void Compile(List<MethodInfo> methods)
        {
            foreach (var method in methods)
            {
                try
                {
                    method.MethodHandle.GetFunctionPointer();
                }
                catch (Exception)
                {
                    // a runtime that cannot compile it ahead of the first call: it is compiled then, as before
                }
            }
            Volatile.Write(ref s_State, 2);
        }

        static void Add(List<MethodInfo> methods, string type, Type leave, Type enter)
        {
            var helper = typeof(VisualElement).Assembly.GetType(type);
            var method = helper != null ? helper.GetMethod("SendEnterLeave", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) : null;
            if (method == null || !method.IsGenericMethodDefinition || method.GetGenericArguments().Length != 2)
                return;
            try
            {
                methods.Add(method.MakeGenericMethod(leave, enter));
            }
            catch (ArgumentException)
            {
                // the constraints changed in this Unity version: nothing to warm
            }
        }
    }
}
