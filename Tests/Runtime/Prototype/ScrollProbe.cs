#if UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cuvara.UIToolkit.Prototype.Tests
{
    /// <summary>
    /// The scroll probe of penpot-uitoolkit-bridge's gate 7 (<c>PenpotGate7Tests.ProbeScroll</c>), copied without the gate: it drives
    /// a ScrollView with real wheel and drag events through <see cref="UiInputSimulator"/>, so the runner's drag scroller is exercised
    /// the way a player would use it.
    /// </summary>
    internal static class ScrollProbe
    {
        public sealed class ScrollProbeResult
        {
            public bool pass;
            public string expected, observed;
        }

        public static IEnumerator Frames(int n)
        {
            for (var i = 0; i < n; i++)
                yield return null;
        }

        /// <summary>
        /// Scrolls a ScrollView the way its <see cref="ScrollView.mode"/> allows: a vertical view overflows in height and is
        /// scrolled with a vertical wheel and a vertical drag, a horizontal one overflows in width and is scrolled
        /// horizontally (a wheel on either axis counts, a horizontal drag). Overflow is measured on that axis only: a wide
        /// horizontal board without height overflow is NOT "content fits". A drag must not fire any interaction.
        /// </summary>
        public static IEnumerator ProbeScroll(IPanel panel, ScrollView scroll, Func<int> fired, Action<ScrollProbeResult> done)
        {
            var mode = scroll.mode;
            var overflowY = scroll.contentContainer.layout.height - scroll.contentViewport.layout.height;
            var overflowX = scroll.contentContainer.layout.width - scroll.contentViewport.layout.width;
            // the axis to exercise: the one the mode scrolls; with both, the one that overflows more
            var horizontal = mode == ScrollViewMode.Horizontal || (mode == ScrollViewMode.VerticalAndHorizontal && overflowX > overflowY);
            var overflow = horizontal ? overflowX : overflowY;
            var axis = horizontal ? "horizontal" : "vertical";
            var result = new ScrollProbeResult { expected = "wheel and drag scroll the " + axis + " axis, a drag fires nothing" };
            if (overflow < 2f)
            {
                result.pass = true;
                result.observed = mode + " view: content fits the viewport on its " + axis + " axis, nothing to scroll";
                done(result);
                yield break;
            }
            var center = UiInputSimulator.Center(scroll);
            var firedBefore = fired();
            Func<float> offset = () => horizontal ? scroll.scrollOffset.x : scroll.scrollOffset.y;
            UiInputSimulator.Wheel(panel, center, new Vector2(0f, 3f));
            yield return Frames(3);
            if (offset() <= 0f)
            {
                UiInputSimulator.Wheel(panel, center, new Vector2(3f, 0f)); // a horizontal wheel
                yield return Frames(3);
            }
            var wheel = offset();
            scroll.scrollOffset = Vector2.zero;
            yield return Frames(2);
            UiInputSimulator.Down(panel, center);
            for (var s = 1; s <= 8; s++)
            {
                var step = horizontal ? new Vector2(-8f * s, 0f) : new Vector2(0f, -8f * s);
                UiInputSimulator.Move(panel, center + step);
                yield return null;
            }
            var drag = offset();
            UiInputSimulator.Up(panel, center + (horizontal ? new Vector2(-64f, 0f) : new Vector2(0f, -64f)));
            yield return Frames(2);
            var nothingFired = fired() == firedBefore;
            result.pass = wheel > 0f && drag > 20f && nothingFired;
            result.observed = axis + ": wheel offset " + wheel.ToString("0.#") + ", drag offset " + drag.ToString("0.#") + ", fired " + (fired() - firedBefore);
            done(result);
        }
    }
}
#endif
