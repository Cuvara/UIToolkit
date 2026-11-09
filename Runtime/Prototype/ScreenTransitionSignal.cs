using UnityEngine.UIElements;

namespace Cuvara.UIToolkit.Prototype
{
    /// <summary>
    /// The "a screen transition is over" notification. The runner sends it once per completed transition (also when the transition is
    /// completed early by the next request or by the safety timer) to the root of the screen or overlay that came or went. It is a
    /// <see cref="TransitionEndEvent"/> with a negative <c>elapsedTime</c> (native end events never have one), so a generated bindings
    /// class can listen for it without referencing this assembly and it is never confused with the native end events of the animated
    /// properties (a slide that moves only the leaving screen gives the new screen no native event at all). A game navigator that
    /// animates screens itself can raise it with <see cref="Send"/>.
    /// </summary>
    public static class ScreenTransitionSignal
    {
        /// <summary>The (arbitrary) property the signal names: the event needs one; the marker is the negative elapsed time.</summary>
        const string CarrierProperty = "opacity";

        /// <summary>Raises the signal on <paramref name="target"/> (must be on a panel; otherwise nothing is sent).</summary>
        public static void Send(VisualElement target)
        {
            if (target == null || target.panel == null)
                return;
            using (var e = TransitionEndEvent.GetPooled(new StylePropertyName(CarrierProperty), -1f))
            {
                e.target = target;
                target.SendEvent(e);
            }
        }

        public static bool IsSignal(TransitionEndEvent e)
        {
            return e.elapsedTime < 0f;
        }
    }
}
