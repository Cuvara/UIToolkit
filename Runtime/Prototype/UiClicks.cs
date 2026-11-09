using System;
using UnityEngine.UIElements;

namespace Cuvara.UIToolkit.Prototype
{
    /// <summary>
    /// A click on any element: a Button reports it through its own ClickEvent (so keyboard/gamepad submit works too); any
    /// other element is clicked when the primary pointer goes down and up inside it (a plain VisualElement has no
    /// Clickable, so no ClickEvent is raised for it by a simulated or a drag-free pointer).
    /// </summary>
    public static class UiClicks
    {
        public static void Attach(VisualElement element, Action clicked)
        {
            if (element is Button)
            {
                element.RegisterCallback<ClickEvent>(e => clicked());
                return;
            }
            var down = false;
            element.RegisterCallback<PointerDownEvent>(e => down = e.button == 0);
            element.RegisterCallback<PointerUpEvent>(e =>
            {
                var was = down;
                down = false;
                if (was && element.worldBound.Contains(e.position))
                    clicked();
            });
        }
    }
}
