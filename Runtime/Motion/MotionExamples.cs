using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cuvara.UIToolkit.Motion
{
    /// <summary>
    /// Ready-made clips built from <see cref="UiMotion"/>, generic UI feedback with no game rules. Copy and adapt; each returns a
    /// new clip, call <c>.Play()</c> on it.
    /// </summary>
    public static class MotionExamples
    {
        /// <summary>Scale 0.8 -> 1.05 -> 1 while fading in (an item appearing, a reward icon).</summary>
        public static MotionClip Pop(VisualElement element, float durationMs = 260f)
        {
            var up = new MotionTiming(durationMs * 0.6f, MotionEasing.EaseOut);
            var settle = new MotionTiming(durationMs * 0.4f, MotionEasing.EaseInOut);
            return UiMotion.Sequence(
                UiMotion.Tween(element, new MotionValues().WithScale(1.05f).WithOpacity(1f), up,
                    new MotionValues().WithScale(0.8f).WithOpacity(0f)),
                UiMotion.Scale(element, 1f, settle));
        }

        /// <summary>
        /// Horizontal shake around the element's translate at the time of the call: <paramref name="swings"/> swings that fade out,
        /// then back to rest (an invalid input, a refused action).
        /// </summary>
        public static MotionClip Shake(VisualElement element, float amplitudePx = 8f, int swings = 6, float durationMs = 320f)
        {
            if (swings < 1)
                throw new System.ArgumentOutOfRangeException(nameof(swings));
            var rest = new VisualElementTarget(element).Get(MotionChannels.Translate);
            var step = new MotionTiming(durationMs / (swings + 1), MotionEasing.EaseInOut);
            var clips = new MotionClip[swings + 1];
            for (var i = 0; i < swings; i++)
            {
                var sign = (i & 1) == 0 ? 1f : -1f;
                var decay = 1f - (float)i / swings;
                clips[i] = UiMotion.Move(element, new Vector2(rest.x + sign * amplitudePx * decay, rest.y), step);
            }
            clips[swings] = UiMotion.Move(element, new Vector2(rest.x, rest.y), step);
            return UiMotion.Sequence(clips);
        }

        /// <summary>Scale breathes 1 -> <paramref name="peak"/> and back until cancelled (a call to action).</summary>
        public static MotionClip PulseLoop(VisualElement element, float peak = 1.08f, float periodMs = 900f)
        {
            return UiMotion.Loop(
                UiMotion.Scale(element, 1f, peak, new MotionTiming(periodMs * 0.5f, MotionEasing.EaseInOut)),
                MotionLoop.Infinite,
                pingPong: true);
        }

        /// <summary>Items fade in and rise 16 px one after the other (list entries, a grid of cards).</summary>
        public static MotionClip StaggerIn(IEnumerable<VisualElement> items, float stepMs = 40f, float durationMs = 220f, float risePx = 16f)
        {
            var timing = new MotionTiming(durationMs, MotionEasing.EaseOut);
            return UiMotion.Stagger(items, (item, i) => UiMotion.Tween(item,
                new MotionValues().WithOpacity(1f).WithTranslate(0f, 0f), timing,
                new MotionValues().WithOpacity(0f).WithTranslate(0f, risePx)), stepMs);
        }

        /// <summary>Number in a Label counts from <paramref name="from"/> to <paramref name="to"/> (a score, a currency total).</summary>
        public static MotionClip CountUp(Label label, float from, float to, float durationMs = 600f) =>
            UiMotion.CountUp(label, from, to, new MotionTiming(durationMs, MotionEasing.EaseOut));
    }
}
