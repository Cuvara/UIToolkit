using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cuvara.UIToolkit.Motion
{
    /// <summary>
    /// The only properties motion can animate. All of them avoid layout and mesh regeneration on mobile
    /// (Documentation~/MOTION.md); width, height, margin, padding, left/top, flex-*, font-size, slices and the like
    /// are deliberately not here.
    /// </summary>
    [Flags]
    public enum MotionChannels
    {
        None = 0,
        Opacity = 1,
        /// <summary>USS <c>translate</c>, pixels.</summary>
        Translate = 2,
        /// <summary>USS <c>scale</c>.</summary>
        Scale = 4,
        /// <summary>USS <c>rotate</c>, degrees.</summary>
        Rotate = 8,
        /// <summary>USS <c>background-color</c>.</summary>
        BackgroundColor = 16,
        /// <summary>USS <c>-unity-background-image-tint-color</c>.</summary>
        Tint = 32,
        /// <summary>USS <c>color</c> (text colour).</summary>
        TextColor = 64
    }

    public enum MotionResult
    {
        Running = 0,
        /// <summary>Reached its end, or <see cref="MotionHandle.Complete"/> jumped to the end state.</summary>
        Completed,
        /// <summary><see cref="MotionHandle.Cancel"/> stopped it.</summary>
        Cancelled,
        /// <summary>Every target left its panel (or was never in one) before the end; the end state was applied.</summary>
        Detached
    }

    /// <summary>What <see cref="MotionHandle.Cancel"/> leaves behind.</summary>
    public enum MotionCancel
    {
        /// <summary>Freeze where the animation is right now (the current, in-between values stay on the elements).</summary>
        Keep = 0,
        /// <summary>Put every animated property back to the value it had when the run first touched it.</summary>
        Restore
    }

    public enum MotionMode
    {
        /// <summary>USS transition where it is possible (tween without a start value, named easing, not in a loop), otherwise frame by frame.</summary>
        Auto = 0,
        /// <summary>Always frame by frame (inline style writes each frame; exact, costs one C# call per frame).</summary>
        Manual
    }

    public static class MotionLoop
    {
        /// <summary>Pass as the loop count to repeat until cancelled. <see cref="MotionHandle.Complete"/> settles an infinite loop as one iteration.</summary>
        public const int Infinite = -1;
    }

    /// <summary>Duration, delay and easing of one tween.</summary>
    public readonly struct MotionTiming
    {
        public readonly float DurationMs;
        public readonly float DelayMs;
        public readonly MotionEasing Easing;

        public MotionTiming(float durationMs, MotionEasing easing, float delayMs = 0f)
        {
            if (float.IsNaN(durationMs) || durationMs < 0f || float.IsInfinity(durationMs))
                throw new ArgumentOutOfRangeException(nameof(durationMs), "duration must be a finite number >= 0");
            if (float.IsNaN(delayMs) || delayMs < 0f || float.IsInfinity(delayMs))
                throw new ArgumentOutOfRangeException(nameof(delayMs), "delay must be a finite number >= 0");
            DurationMs = durationMs;
            DelayMs = delayMs;
            Easing = easing;
        }

        /// <summary>Linear, <paramref name="durationMs"/>, no delay.</summary>
        public static MotionTiming Ms(float durationMs) => new MotionTiming(durationMs, MotionEasing.Linear);

        public static MotionTiming Fast => new MotionTiming(120f, MotionEasing.EaseOut);
        public static MotionTiming Normal => new MotionTiming(200f, MotionEasing.EaseOut);
        public static MotionTiming Slow => new MotionTiming(320f, MotionEasing.EaseOut);

        public MotionTiming WithDelay(float delayMs) => new MotionTiming(DurationMs, Easing, delayMs);
        public MotionTiming WithEasing(MotionEasing easing) => new MotionTiming(DurationMs, easing, DelayMs);
        public MotionTiming WithDuration(float durationMs) => new MotionTiming(durationMs, Easing, DelayMs);
    }

    /// <summary>A set of channel values (for the end or the start of a tween). Build one with the <c>With*</c> methods.</summary>
    public struct MotionValues
    {
        MotionChannels m_Mask;
        float m_Opacity;
        Vector2 m_Translate;
        Vector2 m_Scale;
        float m_Rotate;
        Color m_Background;
        Color m_Tint;
        Color m_Text;

        public MotionChannels Channels => m_Mask;

        public MotionValues WithOpacity(float opacity) { var c = this; c.m_Opacity = opacity; c.m_Mask |= MotionChannels.Opacity; return c; }
        public MotionValues WithTranslate(Vector2 pixels) { var c = this; c.m_Translate = pixels; c.m_Mask |= MotionChannels.Translate; return c; }
        public MotionValues WithTranslate(float x, float y) => WithTranslate(new Vector2(x, y));
        public MotionValues WithScale(Vector2 scale) { var c = this; c.m_Scale = scale; c.m_Mask |= MotionChannels.Scale; return c; }
        public MotionValues WithScale(float uniform) => WithScale(new Vector2(uniform, uniform));
        public MotionValues WithRotate(float degrees) { var c = this; c.m_Rotate = degrees; c.m_Mask |= MotionChannels.Rotate; return c; }
        public MotionValues WithBackgroundColor(Color color) { var c = this; c.m_Background = color; c.m_Mask |= MotionChannels.BackgroundColor; return c; }
        public MotionValues WithTint(Color color) { var c = this; c.m_Tint = color; c.m_Mask |= MotionChannels.Tint; return c; }
        public MotionValues WithTextColor(Color color) { var c = this; c.m_Text = color; c.m_Mask |= MotionChannels.TextColor; return c; }

        public static MotionValues Of(MotionChannels channel, Vector4 value)
        {
            var v = new MotionValues();
            v.Set(channel, value);
            return v;
        }

        /// <summary>One channel as a Vector4 (opacity: x; translate/scale: xy; rotate: x; colours: rgba).</summary>
        public Vector4 Get(MotionChannels channel)
        {
            switch (channel)
            {
                case MotionChannels.Opacity: return new Vector4(m_Opacity, 0f, 0f, 0f);
                case MotionChannels.Translate: return new Vector4(m_Translate.x, m_Translate.y, 0f, 0f);
                case MotionChannels.Scale: return new Vector4(m_Scale.x, m_Scale.y, 0f, 0f);
                case MotionChannels.Rotate: return new Vector4(m_Rotate, 0f, 0f, 0f);
                case MotionChannels.BackgroundColor: return m_Background;
                case MotionChannels.Tint: return m_Tint;
                case MotionChannels.TextColor: return m_Text;
                default: throw new ArgumentException("exactly one channel expected", nameof(channel));
            }
        }

        public void Set(MotionChannels channel, Vector4 v)
        {
            switch (channel)
            {
                case MotionChannels.Opacity: m_Opacity = v.x; break;
                case MotionChannels.Translate: m_Translate = new Vector2(v.x, v.y); break;
                case MotionChannels.Scale: m_Scale = new Vector2(v.x, v.y); break;
                case MotionChannels.Rotate: m_Rotate = v.x; break;
                case MotionChannels.BackgroundColor: m_Background = v; break;
                case MotionChannels.Tint: m_Tint = v; break;
                case MotionChannels.TextColor: m_Text = v; break;
                default: throw new ArgumentException("exactly one channel expected", nameof(channel));
            }
            m_Mask |= channel;
        }
    }

    public struct MotionOptions
    {
        /// <summary>
        /// Put a transparent full-panel element on top of the panel while the run is active, so no pointer event reaches the
        /// UI beneath (owner decision: input is blocked during screen transitions). Released when the run ends. Several runs
        /// share one blocker. The blocker goes to <see cref="BlockRoot"/>'s panel, or the first live target's panel.
        /// </summary>
        public bool BlockInput;

        public VisualElement BlockRoot;

        public MotionMode Mode;
    }

    /// <summary>The animatable surface of one object. <see cref="VisualElementTarget"/> implements it for a VisualElement.</summary>
    public interface IMotionTarget
    {
        /// <summary>Identity used to find runs that animate the same object (a new run takes over overlapping channels).</summary>
        object Key { get; }

        /// <summary>False when the object is not in a live panel (detached, destroyed).</summary>
        bool IsAlive { get; }

        /// <summary>True when <see cref="BeginTransition"/> does something; otherwise every tween runs frame by frame.</summary>
        bool SupportsTransitions { get; }

        /// <summary>
        /// A new frame driver for one run (never shared: stopping one run must not stop another), or null when this object cannot
        /// drive a run.
        /// </summary>
        IMotionDriver CreateDriver();

        /// <summary>Current value (the animated one, not the inline one, while a transition runs).</summary>
        Vector4 Get(MotionChannels channel);

        void Set(MotionChannels channel, Vector4 value);

        /// <summary>
        /// Frame-by-frame writes to these channels must jump, not animate: any transition declared for them (inline or by a style
        /// sheet) is overridden by a zero-duration one for as long as <paramref name="owner"/> holds the claim.
        /// </summary>
        void SuppressTransitions(object owner, MotionChannels channels);

        /// <summary>
        /// Declare a transition for these channels on behalf of <paramref name="owner"/>; the next <see cref="Set"/> animates.
        /// Transitions of other owners and everything declared before the first claim are kept, per property.
        /// </summary>
        void BeginTransition(object owner, MotionChannels channels, float durationMs, MotionEasing easing);

        /// <summary>The owner's transition is over: its channels go back to <see cref="SuppressTransitions"/> (writes jump).</summary>
        void EndTransition(object owner);

        /// <summary>
        /// The owner is done: remove its claims and, when no owner is left, put back the transition declarations that were there
        /// before the first claim. Unless <paramref name="immediate"/>, a target may defer this by a frame so the restored declaration cannot animate the last write (immediate: the object is gone, nothing can animate).
        /// </summary>
        void ReleaseTransitions(object owner, bool immediate);

        /// <summary>Remove the inline values of these channels (back to what the USS says).</summary>
        void Reset(MotionChannels channels);

        /// <summary>Call <paramref name="onDetached"/> when the object leaves its panel.</summary>
        void WatchDetach(Action onDetached);

        void UnwatchDetach(Action onDetached);
    }

    /// <summary>Source of time and per-frame calls for a run. Wall-clock based, like USS transitions.</summary>
    public interface IMotionDriver
    {
        double NowMs { get; }
        bool IsAlive { get; }

        /// <summary>Call <paramref name="tick"/> once per frame until <see cref="Stop"/>.</summary>
        void Start(Action tick);

        void Stop();
    }
}
