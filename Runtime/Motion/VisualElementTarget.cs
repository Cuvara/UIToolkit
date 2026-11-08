using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cuvara.UIToolkit.Motion
{
    /// <summary>
    /// <see cref="IMotionTarget"/> for a VisualElement: writes inline styles (opacity, translate, scale, rotate, three colours) and
    /// declares inline USS transitions through a per-element ledger (<see cref="TransitionLedger"/>), so any number of runs and
    /// target objects can animate one element without clobbering each other's or the designer's transition declarations.
    /// Create it with <see cref="UiMotion.Target"/>; one target object may be used for several runs.
    /// </summary>
    public sealed class VisualElementTarget : IMotionTarget
    {
        readonly VisualElement m_Element;

        public VisualElementTarget(VisualElement element)
        {
            m_Element = element ?? throw new ArgumentNullException(nameof(element));
        }

        public VisualElement Element => m_Element;
        public object Key => m_Element;
        public bool IsAlive => m_Element.panel != null;
        public bool SupportsTransitions => true;
        public IMotionDriver CreateDriver() => new ElementDriver(m_Element);

        /// <summary>
        /// The value the element has now. An inline value written earlier in the same frame is not in <c>resolvedStyle</c> yet, so it
        /// wins; while a transition of ours animates the channel the inline value is the end value and <c>resolvedStyle</c> is the
        /// animated one, so that wins.
        /// </summary>
        public Vector4 Get(MotionChannels channel)
        {
            var inline = !TransitionLedger.IsAnimating(m_Element, channel);
            var st = m_Element.style;
            var r = m_Element.resolvedStyle;
            switch (channel)
            {
                case MotionChannels.Opacity:
                    return new Vector4(inline && st.opacity.keyword == StyleKeyword.Undefined ? st.opacity.value : r.opacity, 0f, 0f, 0f);
                case MotionChannels.Translate:
                {
                    var t = st.translate;
                    if (inline && t.keyword == StyleKeyword.Undefined && t.value.x.unit == LengthUnit.Pixel && t.value.y.unit == LengthUnit.Pixel)
                        return new Vector4(t.value.x.value, t.value.y.value, 0f, 0f);
                    var rt = r.translate;
                    return new Vector4(rt.x, rt.y, 0f, 0f);
                }
                case MotionChannels.Scale:
                {
                    var sc = st.scale;
                    var v = inline && sc.keyword == StyleKeyword.Undefined ? sc.value.value : r.scale.value;
                    return new Vector4(v.x, v.y, 0f, 0f);
                }
                case MotionChannels.Rotate:
                {
                    var ro = st.rotate;
                    var a = inline && ro.keyword == StyleKeyword.Undefined ? ro.value.angle : r.rotate.angle;
                    return new Vector4(a.ToDegrees(), 0f, 0f, 0f);
                }
                case MotionChannels.BackgroundColor:
                    return inline && st.backgroundColor.keyword == StyleKeyword.Undefined ? st.backgroundColor.value : r.backgroundColor;
                case MotionChannels.Tint:
                    return inline && st.unityBackgroundImageTintColor.keyword == StyleKeyword.Undefined
                        ? st.unityBackgroundImageTintColor.value : r.unityBackgroundImageTintColor;
                case MotionChannels.TextColor:
                    return inline && st.color.keyword == StyleKeyword.Undefined ? st.color.value : r.color;
                default: throw new ArgumentException("exactly one channel expected", nameof(channel));
            }
        }

        public void Set(MotionChannels channel, Vector4 v)
        {
            var s = m_Element.style;
            switch (channel)
            {
                case MotionChannels.Opacity: s.opacity = v.x; break;
                case MotionChannels.Translate: s.translate = new Translate(new Length(v.x), new Length(v.y)); break;
                case MotionChannels.Scale: s.scale = new Scale(new Vector3(v.x, v.y, 1f)); break;
                case MotionChannels.Rotate: s.rotate = new Rotate(Angle.Degrees(v.x)); break;
                case MotionChannels.BackgroundColor: s.backgroundColor = (Color)v; break;
                case MotionChannels.Tint: s.unityBackgroundImageTintColor = (Color)v; break;
                case MotionChannels.TextColor: s.color = (Color)v; break;
                default: throw new ArgumentException("exactly one channel expected", nameof(channel));
            }
        }

        public void SuppressTransitions(object owner, MotionChannels channels) =>
            TransitionLedger.Declare(m_Element, owner, channels, 0f, MotionEasing.Linear);

        public void BeginTransition(object owner, MotionChannels channels, float durationMs, MotionEasing easing) =>
            TransitionLedger.Declare(m_Element, owner, channels, durationMs, easing);

        public void EndTransition(object owner) => TransitionLedger.EndAnimation(m_Element, owner);

        public void ReleaseTransitions(object owner, bool immediate)
        {
            var element = m_Element;
            if (immediate || element.panel == null)
            {
                TransitionLedger.Release(element, owner);
                return;
            }
            // Restoring the old declaration in the frame of the last value write would let it animate that write. Wait a frame;
            // when the element leaves its panel first, release at once.
            EventCallback<DetachFromPanelEvent> onDetach = null;
            var item = element.schedule.Execute(() =>
            {
                element.UnregisterCallback(onDetach);
                TransitionLedger.Release(element, owner);
            });
            item.StartingIn(1);
            onDetach = e =>
            {
                item.Pause();
                element.UnregisterCallback(onDetach);
                TransitionLedger.Release(element, owner);
            };
            element.RegisterCallback(onDetach);
        }

        public void Reset(MotionChannels channels)
        {
            var s = m_Element.style;
            if ((channels & MotionChannels.Opacity) != 0) s.opacity = StyleKeyword.Null;
            if ((channels & MotionChannels.Translate) != 0) s.translate = StyleKeyword.Null;
            if ((channels & MotionChannels.Scale) != 0) s.scale = StyleKeyword.Null;
            if ((channels & MotionChannels.Rotate) != 0) s.rotate = StyleKeyword.Null;
            if ((channels & MotionChannels.BackgroundColor) != 0) s.backgroundColor = StyleKeyword.Null;
            if ((channels & MotionChannels.Tint) != 0) s.unityBackgroundImageTintColor = StyleKeyword.Null;
            if ((channels & MotionChannels.TextColor) != 0) s.color = StyleKeyword.Null;
        }

        Action m_OnDetached;
        bool m_Watching;

        public void WatchDetach(Action onDetached)
        {
            m_OnDetached += onDetached;
            if (m_Watching)
                return;
            m_Watching = true;
            m_Element.RegisterCallback<DetachFromPanelEvent>(OnDetach);
        }

        public void UnwatchDetach(Action onDetached)
        {
            m_OnDetached -= onDetached;
            if (m_OnDetached != null || !m_Watching)
                return;
            m_Watching = false;
            m_Element.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
        }

        void OnDetach(DetachFromPanelEvent evt)
        {
            m_OnDetached?.Invoke();
        }

        /// <summary>Drives one run from the element's own scheduler (a scheduler item only ticks while the element is in a panel).</summary>
        sealed class ElementDriver : IMotionDriver
        {
            readonly VisualElement m_Element;
            IVisualElementScheduledItem m_Item;

            public ElementDriver(VisualElement element)
            {
                m_Element = element;
            }

            public double NowMs => Time.realtimeSinceStartupAsDouble * 1000.0;
            public bool IsAlive => m_Element.panel != null;

            public void Start(Action tick)
            {
                Stop();
                m_Item = m_Element.schedule.Execute(tick).Every(1);
            }

            public void Stop()
            {
                m_Item?.Pause();
                m_Item = null;
            }
        }
    }

    /// <summary>
    /// Who has declared which inline USS transitions on an element. <c>transition-property</c> and friends are element-wide lists,
    /// so every claim (owner, channels, duration, easing) is merged into one list per property: unequal durations coexist, one
    /// owner finishing removes only its entries, and what the element declared before the first claim (inline, or from a style
    /// sheet through <c>resolvedStyle</c>) stays in force for properties nobody claims and comes back when the last owner leaves.
    /// A claim with duration 0 means "writes to this channel must jump".
    /// </summary>
    internal static class TransitionLedger
    {
        sealed class Claim
        {
            public object Owner;
            public MotionChannels Channels;
            public float DurationMs;
            public MotionEasing Easing;
        }

        sealed class State
        {
            public readonly List<Claim> Claims = new List<Claim>(2);
            // inline lists before the first claim, put back when the last owner leaves
            public StyleList<StylePropertyName> PriorProperty;
            public StyleList<TimeValue> PriorDuration, PriorDelay;
            public StyleList<EasingFunction> PriorTiming;
            // effective lists before the first claim (style sheet + inline), kept for properties nobody claims
            public readonly List<StylePropertyName> BaseProperty = new List<StylePropertyName>();
            public readonly List<TimeValue> BaseDuration = new List<TimeValue>();
            public readonly List<TimeValue> BaseDelay = new List<TimeValue>();
            public readonly List<EasingFunction> BaseTiming = new List<EasingFunction>();
        }

        static readonly ConditionalWeakTable<VisualElement, State> s_States = new ConditionalWeakTable<VisualElement, State>();

        static readonly MotionChannels[] k_All =
        {
            MotionChannels.Opacity, MotionChannels.Translate, MotionChannels.Scale, MotionChannels.Rotate,
            MotionChannels.BackgroundColor, MotionChannels.Tint, MotionChannels.TextColor
        };

        static readonly StylePropertyName[] k_Names =
        {
            new StylePropertyName("opacity"), new StylePropertyName("translate"), new StylePropertyName("scale"),
            new StylePropertyName("rotate"), new StylePropertyName("background-color"),
            new StylePropertyName("-unity-background-image-tint-color"), new StylePropertyName("color")
        };

        /// <summary>Claims currently held on an element (tests).</summary>
        internal static int ClaimCount(VisualElement element) =>
            s_States.TryGetValue(element, out var state) ? state.Claims.Count : 0;

        /// <summary>True while a claim with a duration above zero covers <paramref name="channel"/>.</summary>
        internal static bool IsAnimating(VisualElement element, MotionChannels channel)
        {
            if (!s_States.TryGetValue(element, out var state))
                return false;
            foreach (var c in state.Claims)
                if ((c.Channels & channel) != 0 && c.DurationMs > 0f)
                    return true;
            return false;
        }

        internal static void Declare(VisualElement element, object owner, MotionChannels channels, float durationMs, MotionEasing easing)
        {
            if (channels == MotionChannels.None)
                return;
            if (!s_States.TryGetValue(element, out var state))
            {
                state = Snapshot(element);
                s_States.Add(element, state);
            }
            // a channel has one effective claim: the newest, whoever held it before
            for (var i = state.Claims.Count - 1; i >= 0; i--)
            {
                var c = state.Claims[i];
                c.Channels &= ~channels;
                if (c.Channels == MotionChannels.None)
                    state.Claims.RemoveAt(i);
            }
            Claim mine = null;
            foreach (var c in state.Claims)
                if (c.Owner == owner && c.DurationMs == durationMs && c.Easing.Equals(easing))
                    mine = c;
            if (mine == null)
            {
                mine = new Claim { Owner = owner, DurationMs = durationMs, Easing = easing };
                state.Claims.Add(mine);
            }
            mine.Channels |= channels;
            Apply(element, state);
        }

        /// <summary>The owner's animation is over: its channels keep a claim with duration 0 so later writes jump.</summary>
        internal static void EndAnimation(VisualElement element, object owner)
        {
            if (!s_States.TryGetValue(element, out var state))
                return;
            var merged = MotionChannels.None;
            for (var i = state.Claims.Count - 1; i >= 0; i--)
            {
                var c = state.Claims[i];
                if (c.Owner != owner || c.DurationMs <= 0f)
                    continue;
                merged |= c.Channels;
                state.Claims.RemoveAt(i);
            }
            if (merged == MotionChannels.None)
                return;
            Declare(element, owner, merged, 0f, MotionEasing.Linear);
        }

        /// <summary>Drop every claim and put the prior declarations back.</summary>
        internal static void ReleaseAll(VisualElement element)
        {
            if (!s_States.TryGetValue(element, out var state))
                return;
            state.Claims.Clear();
            Release(element, null);
        }

        internal static void Release(VisualElement element, object owner)
        {
            if (!s_States.TryGetValue(element, out var state))
                return;
            state.Claims.RemoveAll(c => c.Owner == owner);
            if (state.Claims.Count > 0)
            {
                Apply(element, state);
                return;
            }
            var s = element.style;
            s.transitionProperty = state.PriorProperty;
            s.transitionDuration = state.PriorDuration;
            s.transitionDelay = state.PriorDelay;
            s.transitionTimingFunction = state.PriorTiming;
            s_States.Remove(element);
        }

        static State Snapshot(VisualElement element)
        {
            var state = new State();
            var s = element.style;
            state.PriorProperty = CopyOf(s.transitionProperty);
            state.PriorDuration = CopyOf(s.transitionDuration);
            state.PriorDelay = CopyOf(s.transitionDelay);
            state.PriorTiming = CopyOf(s.transitionTimingFunction);

            var r = element.resolvedStyle;
            foreach (var p in r.transitionProperty) state.BaseProperty.Add(p);
            foreach (var d in r.transitionDuration) state.BaseDuration.Add(d);
            foreach (var d in r.transitionDelay) state.BaseDelay.Add(d);
            foreach (var f in r.transitionTimingFunction) state.BaseTiming.Add(f);
            return state;
        }

        // the style system may reuse the list object it stores, so the prior value must be a copy
        static StyleList<T> CopyOf<T>(StyleList<T> list) =>
            list.keyword == StyleKeyword.Undefined ? new StyleList<T>(new List<T>(list.value)) : new StyleList<T>(list.keyword);

        static void Apply(VisualElement element, State state)
        {
            var owned = MotionChannels.None;
            foreach (var c in state.Claims)
                owned |= c.Channels;

            var properties = new List<StylePropertyName>(state.BaseProperty.Count + 4);
            var durations = new List<TimeValue>(properties.Capacity);
            var delays = new List<TimeValue>(properties.Capacity);
            var timings = new List<EasingFunction>(properties.Capacity);

            // what the element declared, for every property we do not own (USS repeats a shorter list to the length of the property list)
            for (var i = 0; i < state.BaseProperty.Count; i++)
            {
                var name = state.BaseProperty[i];
                var skip = false;
                for (var k = 0; k < k_All.Length && !skip; k++)
                    skip = (owned & k_All[k]) != 0 && k_Names[k].Equals(name);
                if (skip)
                    continue;
                properties.Add(name);
                durations.Add(Cycle(state.BaseDuration, i, new TimeValue(0f, TimeUnit.Millisecond)));
                delays.Add(Cycle(state.BaseDelay, i, new TimeValue(0f, TimeUnit.Millisecond)));
                timings.Add(Cycle(state.BaseTiming, i, new EasingFunction(EasingMode.Ease)));
            }
            foreach (var c in state.Claims)
            {
                for (var k = 0; k < k_All.Length; k++)
                {
                    if ((c.Channels & k_All[k]) == 0)
                        continue;
                    properties.Add(k_Names[k]);
                    durations.Add(new TimeValue(c.DurationMs, TimeUnit.Millisecond));
                    delays.Add(new TimeValue(0f, TimeUnit.Millisecond));
                    timings.Add(new EasingFunction(c.Easing.ToEasingMode()));
                }
            }

            var s = element.style;
            s.transitionProperty = properties;
            s.transitionDuration = durations;
            s.transitionDelay = delays;
            s.transitionTimingFunction = timings;
        }

        static T Cycle<T>(List<T> list, int index, T fallback) => list.Count == 0 ? fallback : list[index % list.Count];
    }
}
