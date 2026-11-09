using UnityEngine;
using UnityEngine.UIElements;

namespace Cuvara.UIToolkit.Prototype
{
    /// <summary>
    /// Sends real UI Toolkit pointer events (move, down, up, click) to a panel at a position, the way the input system
    /// would, so tests and the interaction gate drive hover, :active, clicks and drags through the normal event pipeline
    /// instead of calling handlers. Positions are panel (UI) coordinates, origin top-left.
    /// </summary>
    public static class UiInputSimulator
    {
        public static Vector2 Center(VisualElement element)
        {
            return element.worldBound.center;
        }

        // Every pointer event is built by a non-generic call on its own type. A generic helper constrained to
        // `PointerEventBase<T>` spent 1.3-1.4 s in Mono's JIT on its first call (measured in the Editor with deep profiling),
        // which gate 8 reported as the lag of the first click.
        static Event Mouse(EventType type, Vector2 position)
        {
            return new Event { type = type, mousePosition = position, button = 0, clickCount = 1 };
        }

        public static void Move(IPanel panel, Vector2 position)
        {
            using (var e = PointerMoveEvent.GetPooled(Mouse(EventType.MouseMove, position)))
                Send(panel, e, position);
        }

        public static void Down(IPanel panel, Vector2 position)
        {
            Move(panel, position);
            using (var e = PointerDownEvent.GetPooled(Mouse(EventType.MouseDown, position)))
                Send(panel, e, position);
        }

        public static void Up(IPanel panel, Vector2 position)
        {
            using (var e = PointerUpEvent.GetPooled(Mouse(EventType.MouseUp, position)))
                Send(panel, e, position);
        }

        /// <summary>Move onto the position, press and release: hover, :active and a ClickEvent for what is under it.</summary>
        public static void Click(IPanel panel, Vector2 position)
        {
            Down(panel, position);
            Up(panel, position);
        }

        static void Send(IPanel panel, EventBase e, Vector2 position)
        {
            // like the input system: the event is aimed at the element under the pointer
            var target = panel.Pick(position) ?? panel.visualTree;
            target.SendEvent(e);
        }

        /// <summary>Mouse wheel over a position (positive delta scrolls down).</summary>
        public static void Wheel(IPanel panel, Vector2 position, float delta)
        {
            Wheel(panel, position, new Vector2(0f, delta));
        }

        /// <summary>Mouse wheel with a delta on both axes (a horizontal wheel or a shifted vertical one).</summary>
        public static void Wheel(IPanel panel, Vector2 position, Vector2 delta)
        {
            var e = new Event { type = EventType.ScrollWheel, mousePosition = position, delta = delta };
            using (var evt = WheelEvent.GetPooled(e))
                Send(panel, evt, position);
        }

        /// <summary>Types characters into the focused element as key events.</summary>
        public static void Type(IPanel panel, VisualElement field, string text)
        {
            var focused = panel.focusController.focusedElement as VisualElement ?? field;
            foreach (var c in text)
            {
                var down = new Event { type = EventType.KeyDown, character = c, keyCode = KeyCode.None };
                using (var evt = KeyDownEvent.GetPooled(down))
                    focused.SendEvent(evt);
            }
        }
    }
}
