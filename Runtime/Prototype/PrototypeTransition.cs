using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cuvara.UIToolkit.Prototype
{
    /// <summary>
    /// One element animated by native USS transitions: the start state is written inline, the transition and the end state a few frames
    /// later (a transition needs frames between the two), and the inline values are removed when the transition is over.
    /// Only <c>opacity</c> and <c>translate</c> are ever animated here (they do not regenerate the mesh).
    /// </summary>
    internal sealed class PrototypeTrack
    {
        public VisualElement Element;
        public float DelayMs;
        public float DurationMs;
        public EasingMode Easing = EasingMode.EaseOut;
        public bool HasOpacity;
        public float OpacityFrom = 1f;
        public float OpacityTo = 1f;
        public bool HasMove;
        public Translate MoveFrom;
        public Translate MoveTo;

        public float EndMs { get { return DelayMs + DurationMs; } }

        // The start state is written WITHOUT a transition (an element that is already on the panel would otherwise animate towards it);
        // the transition is installed together with the end state, a few frames later.
        public void ApplyStart()
        {
            if (HasOpacity)
                Element.style.opacity = OpacityFrom;
            if (HasMove)
                Element.style.translate = MoveFrom;
        }

        public void ApplyEnd()
        {
            var properties = new List<StylePropertyName>();
            if (HasOpacity)
                properties.Add(new StylePropertyName("opacity"));
            if (HasMove)
                properties.Add(new StylePropertyName("translate"));
            Element.style.transitionProperty = properties;
            Element.style.transitionDuration = new List<TimeValue> { new TimeValue(DurationMs, TimeUnit.Millisecond) };
            Element.style.transitionDelay = new List<TimeValue> { new TimeValue(DelayMs, TimeUnit.Millisecond) };
            Element.style.transitionTimingFunction = new List<EasingFunction> { new EasingFunction(Easing) };
            if (HasOpacity)
                Element.style.opacity = OpacityTo;
            if (HasMove)
                Element.style.translate = MoveTo;
        }

        public void Clear()
        {
            Element.style.transitionProperty = StyleKeyword.Null;
            Element.style.transitionDuration = StyleKeyword.Null;
            Element.style.transitionDelay = StyleKeyword.Null;
            Element.style.transitionTimingFunction = StyleKeyword.Null;
            Element.style.opacity = StyleKeyword.Null;
            Element.style.translate = StyleKeyword.Null;
        }

        public static EasingMode ParseEasing(string name)
        {
            switch (name)
            {
                case "linear": return EasingMode.Linear;
                case "ease": return EasingMode.Ease;
                case "ease-in": return EasingMode.EaseIn;
                case "ease-in-out": return EasingMode.EaseInOut;
                default: return EasingMode.EaseOut;
            }
        }
    }

    /// <summary>
    /// The generated <c>&lt;Screen&gt;.motion.uss</c> sheet holds the transition rules of the screen's controls. The static gates
    /// (screen captures, gate 7) compare with Penpot's pictures, which know nothing of a press scale or a fade, so they detach it:
    /// the controls then jump to their state as before. Gate 8 and the runner with <c>Animate</c> on keep it.
    /// </summary>
    public static class MotionSheets
    {
        public const string Suffix = ".motion";

        /// <summary>Removes every motion sheet from <paramref name="root"/> and its descendants; returns how many were removed.</summary>
        public static int Detach(VisualElement root)
        {
            var removed = 0;
            if (root == null)
                return removed;
            removed += DetachFrom(root);
            root.Query<VisualElement>().Build().ForEach(e => removed += DetachFrom(e));
            return removed;
        }

        static int DetachFrom(VisualElement e)
        {
            var removed = 0;
            for (var i = e.styleSheets.count - 1; i >= 0; i--)
            {
                var sheet = e.styleSheets[i];
                if (sheet != null && sheet.name.EndsWith(Suffix))
                {
                    e.styleSheets.Remove(sheet);
                    removed++;
                }
            }
            return removed;
        }
    }

    /// <summary>
    /// A running set of tracks. It ends at the <c>TransitionEndEvent</c> of its longest track, or at a wall-clock safety timer
    /// (a removed or hidden element can miss the event), whichever comes first; <see cref="Finish"/> may also be called at any
    /// time to jump to the end state. The finish action runs exactly once.
    /// </summary>
    internal sealed class PrototypeTransition
    {
        const long SafetyMarginMs = 400;
        const long UnstartedLimitMs = 1500;
        // frames between the start state and the end state: a style change in the frame the element joins the panel (or the next
        // one) is not a transition, the value just jumps (measured in the animation spike)
        const int StartFrames = 3;

        public readonly string Kind;
        public readonly string Name;
        readonly VisualElement m_Scheduler;
        readonly Action m_OnFinish;
        List<PrototypeTrack> m_Tracks = new List<PrototypeTrack>();
        IVisualElementScheduledItem m_Frames;
        IVisualElementScheduledItem m_Safety;
        VisualElement m_Main;
        EventCallback<TransitionEndEvent> m_EndCallback;

        public bool Finished { get; private set; }
        public bool Started { get; private set; }
        /// <summary>Gets the time the animation really starts (<see cref="PrototypeTransitionInfo.StartedAt"/>).</summary>
        public PrototypeTransitionInfo Info;
        /// <summary>Extra cleanup for a transition that never started (a deferred start that is cancelled).</summary>
        public Action OnCancelUnstarted;

        public PrototypeTransition(VisualElement scheduler, string kind, string name, Action onFinish)
        {
            m_Scheduler = scheduler;
            Kind = kind;
            Name = name;
            m_OnFinish = onFinish;
            m_Safety = scheduler.schedule.Execute(Finish).StartingIn(UnstartedLimitMs);
        }

        public void Start(List<PrototypeTrack> tracks)
        {
            if (Finished || Started)
                return;
            Started = true;
            m_Tracks = tracks;
            float longest = 0f;
            foreach (var t in tracks)
            {
                t.ApplyStart();
                if (t.EndMs >= longest)
                {
                    longest = t.EndMs;
                    m_Main = t.Element;
                }
            }
            // no time limit until the end state is applied (a frame-driven step below): the safety is armed then
            m_Safety.Pause();
            if (m_Main != null)
            {
                var main = m_Main;
                m_EndCallback = e =>
                {
                    if (e.target == main)
                        Finish();
                };
                m_Main.RegisterCallback(m_EndCallback);
            }
            var startFrame = Time.frameCount;
            m_Frames = m_Scheduler.schedule.Execute(() =>
            {
                if (Finished || Time.frameCount - startFrame < StartFrames)
                    return;
                m_Frames.Pause();
                if (Info != null)
                    Info.StartedAt = Time.realtimeSinceStartup;
                // the animation starts now, not at Start(): the first frames of a freshly instantiated screen can be slow, and a
                // safety armed at Start() would cut the running transition (seen in a busy Editor: 3.8 s before the start)
                m_Safety.Pause();
                m_Safety = m_Scheduler.schedule.Execute(Finish).StartingIn((long)longest + SafetyMarginMs);
                foreach (var t in m_Tracks)
                    t.ApplyEnd();
            }).Every(1);
        }

        /// <summary>Jumps to the end state and runs the finish action (once).</summary>
        public void Finish()
        {
            if (Finished)
                return;
            Finished = true;
            if (m_Frames != null)
                m_Frames.Pause();
            if (m_Safety != null)
                m_Safety.Pause();
            if (m_Main != null && m_EndCallback != null)
                m_Main.UnregisterCallback(m_EndCallback);
            if (!Started && OnCancelUnstarted != null)
                OnCancelUnstarted();
            foreach (var t in m_Tracks)
                t.Clear();
            m_OnFinish();
        }
    }
}
