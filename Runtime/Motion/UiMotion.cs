using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cuvara.UIToolkit.Motion
{
    /// <summary>Which colour a <c>UiMotion.Tint</c> tween changes.</summary>
    public enum MotionColor
    {
        /// <summary>USS <c>background-color</c>.</summary>
        Background = 0,
        /// <summary>USS <c>-unity-background-image-tint-color</c> (multiplies the sprite).</summary>
        ImageTint,
        /// <summary>USS <c>color</c> (text).</summary>
        Text
    }

    /// <summary>
    /// Builders for game-driven UI animation on UI Toolkit elements (see Documentation~/MOTION.md). Every builder returns a
    /// <see cref="MotionClip"/>; call <c>.Play()</c> to run it and keep the returned <see cref="MotionHandle"/> to cancel, complete or
    /// await it.
    /// <para>
    /// Only mobile-safe properties can be animated (opacity, translate, scale, rotate, three colours): there is no builder for
    /// width, height, margin, padding, left/top, flex or font-size, because those relayout or regenerate geometry every frame.
    /// A tween without a start value starts from the element's current value and, when it can, runs as a USS transition (the
    /// style system animates it; see <see cref="MotionMode"/>).
    /// </para>
    /// <code>
    /// UiMotion.Fade(panel, 1f, MotionTiming.Normal).Play();
    /// var handle = UiMotion.Parallel(
    ///     UiMotion.Move(card, new Vector2(0, 24), new Vector2(0, 0), MotionTiming.Normal),
    ///     UiMotion.Fade(card, 0f, 1f, MotionTiming.Normal)).Play(new MotionOptions { BlockInput = true });
    /// await handle;
    /// </code>
    /// </summary>
    public static class UiMotion
    {
        public static IMotionTarget Target(VisualElement element) => new VisualElementTarget(element);

        #region Tweens

        /// <summary>Several channels in one tween. <paramref name="from"/> may name a subset of the channels of <paramref name="to"/>.</summary>
        public static MotionClip Tween(IMotionTarget target, MotionValues to, MotionTiming timing, MotionValues from = default) =>
            new TweenClip(target, to, from, timing);

        public static MotionClip Tween(VisualElement element, MotionValues to, MotionTiming timing, MotionValues from = default) =>
            new TweenClip(Target(element), to, from, timing);

        /// <summary>Opacity from its current value to <paramref name="to"/>.</summary>
        public static MotionClip Fade(VisualElement element, float to, MotionTiming timing) =>
            Tween(element, new MotionValues().WithOpacity(to), timing);

        public static MotionClip Fade(VisualElement element, float from, float to, MotionTiming timing) =>
            Tween(element, new MotionValues().WithOpacity(to), timing, new MotionValues().WithOpacity(from));

        /// <summary>USS <c>translate</c> in pixels (not percent).</summary>
        public static MotionClip Move(VisualElement element, Vector2 to, MotionTiming timing) =>
            Tween(element, new MotionValues().WithTranslate(to), timing);

        public static MotionClip Move(VisualElement element, Vector2 from, Vector2 to, MotionTiming timing) =>
            Tween(element, new MotionValues().WithTranslate(to), timing, new MotionValues().WithTranslate(from));

        public static MotionClip Scale(VisualElement element, float to, MotionTiming timing) =>
            Tween(element, new MotionValues().WithScale(to), timing);

        public static MotionClip Scale(VisualElement element, float from, float to, MotionTiming timing) =>
            Tween(element, new MotionValues().WithScale(to), timing, new MotionValues().WithScale(from));

        /// <summary>USS <c>rotate</c> in degrees.</summary>
        public static MotionClip Rotate(VisualElement element, float toDegrees, MotionTiming timing) =>
            Tween(element, new MotionValues().WithRotate(toDegrees), timing);

        public static MotionClip Rotate(VisualElement element, float fromDegrees, float toDegrees, MotionTiming timing) =>
            Tween(element, new MotionValues().WithRotate(toDegrees), timing, new MotionValues().WithRotate(fromDegrees));

        public static MotionClip Tint(VisualElement element, Color to, MotionTiming timing, MotionColor which = MotionColor.Background) =>
            Tween(element, WithColor(default, which, to), timing);

        public static MotionClip Tint(VisualElement element, Color from, Color to, MotionTiming timing, MotionColor which = MotionColor.Background) =>
            Tween(element, WithColor(default, which, to), timing, WithColor(default, which, from));

        static MotionValues WithColor(MotionValues v, MotionColor which, Color c)
        {
            switch (which)
            {
                case MotionColor.ImageTint: return v.WithTint(c);
                case MotionColor.Text: return v.WithTextColor(c);
                default: return v.WithBackgroundColor(c);
            }
        }

        /// <summary>
        /// Animates a float and passes it to <paramref name="setter"/> every frame (frame by frame, never a transition).
        /// <paramref name="liveness"/> (may be null) is the element whose panel keeps the run alive.
        /// </summary>
        public static MotionClip Value(VisualElement liveness, float from, float to, MotionTiming timing, Action<float> setter) =>
            new ValueClip(liveness == null ? null : Target(liveness), from, to, timing, setter);

        /// <summary>
        /// Interpolates the number shown in a Label. Writes the text only when it changes. The default format is the rounded integer;
        /// a custom <paramref name="format"/> allocates a string per call (keep it to what is visible).
        /// </summary>
        public static MotionClip CountUp(Label label, float from, float to, MotionTiming timing, Func<float, string> format = null)
        {
            if (label == null)
                throw new ArgumentNullException(nameof(label));
            var last = string.Empty;
            var lastInt = int.MinValue;
            return Value(label, from, to, timing, v =>
            {
                if (format == null)
                {
                    var i = Mathf.RoundToInt(v);
                    if (i == lastInt)
                        return;
                    lastInt = i;
                    label.text = i.ToString(CultureInfo.InvariantCulture);
                    return;
                }
                var s = format(v);
                if (string.Equals(s, last, StringComparison.Ordinal))
                    return;
                last = s;
                label.text = s;
            });
        }

        #endregion

        #region Composition

        /// <summary>Steps one after another. Each step starts when the one before has ended.</summary>
        public static MotionClip Sequence(params MotionClip[] clips)
        {
            Validate(clips);
            var offsets = new float[clips.Length];
            var at = 0f;
            for (var i = 0; i < clips.Length; i++)
            {
                offsets[i] = at;
                at += clips[i].Duration;
            }
            return new GroupClip(clips, offsets);
        }

        /// <summary>All clips start together; the group ends with the longest.</summary>
        public static MotionClip Parallel(params MotionClip[] clips)
        {
            Validate(clips);
            return new GroupClip(clips, new float[clips.Length]);
        }

        /// <summary>Clip i starts <c>i * stepMs</c> after the first. Items are built with <paramref name="build"/>(element, index).</summary>
        public static MotionClip Stagger(IEnumerable<VisualElement> items, Func<VisualElement, int, MotionClip> build, float stepMs)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (build == null) throw new ArgumentNullException(nameof(build));
            var clips = new List<MotionClip>();
            foreach (var item in items)
                clips.Add(build(item, clips.Count));
            return Stagger(clips, stepMs);
        }

        /// <summary>Clip i starts <c>i * stepMs</c> after the first.</summary>
        public static MotionClip Stagger(IReadOnlyList<MotionClip> clips, float stepMs)
        {
            if (clips == null) throw new ArgumentNullException(nameof(clips));
            if (float.IsNaN(stepMs) || stepMs < 0f || float.IsInfinity(stepMs))
                throw new ArgumentOutOfRangeException(nameof(stepMs), "step must be a finite number >= 0");
            var array = new MotionClip[clips.Count];
            var offsets = new float[clips.Count];
            for (var i = 0; i < array.Length; i++)
            {
                array[i] = clips[i];
                offsets[i] = i * stepMs;
            }
            Validate(array);
            return new GroupClip(array, offsets);
        }

        /// <summary>
        /// Repeat <paramref name="clip"/> <paramref name="count"/> times (<see cref="MotionLoop.Infinite"/> for ever). Ping-pong plays
        /// every second iteration backwards; a ping-pong loop cannot contain a <see cref="Call"/> (anywhere inside it, nested loops
        /// included): it is not defined when a call fires on the way back, so building such a loop throws <see cref="ArgumentException"/>.
        /// Use <see cref="MotionHandle.OnFinished"/> for the end of the loop, or put the call outside the ping-pong loop.
        /// </summary>
        public static MotionClip Loop(MotionClip clip, int count = MotionLoop.Infinite, bool pingPong = false)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            return new LoopClip(clip, count, pingPong);
        }

        /// <summary>A pause. Use it inside a <see cref="Sequence"/>; to delay one tween use its <see cref="MotionTiming.DelayMs"/>.</summary>
        public static MotionClip Wait(float ms)
        {
            if (float.IsNaN(ms) || ms < 0f || float.IsInfinity(ms))
                throw new ArgumentOutOfRangeException(nameof(ms), "wait must be a finite number >= 0");
            return new WaitClip(ms);
        }

        /// <summary>Run <paramref name="action"/> when the timeline reaches this point (also when the run is completed early; not when cancelled).</summary>
        public static MotionClip Call(Action action) => new CallClip(action);

        static void Validate(MotionClip[] clips)
        {
            if (clips == null) throw new ArgumentNullException(nameof(clips));
            for (var i = 0; i < clips.Length; i++)
                if (clips[i] == null)
                    throw new ArgumentNullException(nameof(clips), "clip " + i + " is null");
        }

        #endregion

        /// <summary>
        /// Remove the inline values motion may have left on <paramref name="element"/> so the USS decides again, and give back any
        /// transition declaration motion still holds on it. Does not cancel a running clip.
        /// </summary>
        public static void ResetStyles(VisualElement element)
        {
            if (element == null)
                throw new ArgumentNullException(nameof(element));
            TransitionLedger.ReleaseAll(element);
            new VisualElementTarget(element).Reset(MotionChannels.Opacity | MotionChannels.Translate | MotionChannels.Scale
                | MotionChannels.Rotate | MotionChannels.BackgroundColor | MotionChannels.Tint | MotionChannels.TextColor);
        }
    }
}
