using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cuvara.UIToolkit.Motion
{
    /// <summary>
    /// One transparent full-panel element per panel that swallows pointer events while at least one run asked for
    /// <see cref="MotionOptions.BlockInput"/>. Reference counted, so overlapping runs share it.
    /// </summary>
    internal static class MotionInputBlock
    {
        internal const string BlockerName = "motion-input-blocker";

        sealed class Entry
        {
            public VisualElement Blocker;
            public int Count;
        }

        static readonly Dictionary<IPanel, Entry> s_Entries = new Dictionary<IPanel, Entry>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            s_Entries.Clear(); // domain reload may be off: statics outlive Play Mode
        }

        internal static IPanel Acquire(VisualElement anyElementInPanel)
        {
            var panel = anyElementInPanel?.panel;
            if (panel == null)
                return null;
            if (!s_Entries.TryGetValue(panel, out var entry))
            {
                var blocker = new VisualElement { name = BlockerName, pickingMode = PickingMode.Position };
                blocker.style.position = Position.Absolute;
                blocker.style.left = 0f;
                blocker.style.top = 0f;
                blocker.style.right = 0f;
                blocker.style.bottom = 0f;
                panel.visualTree.Add(blocker);
                entry = new Entry { Blocker = blocker };
                s_Entries[panel] = entry;
            }
            entry.Count++;
            entry.Blocker.BringToFront();
            return panel;
        }

        internal static void Release(IPanel panel)
        {
            if (panel == null || !s_Entries.TryGetValue(panel, out var entry))
                return;
            if (--entry.Count > 0)
                return;
            entry.Blocker.RemoveFromHierarchy();
            s_Entries.Remove(panel);
        }

        internal static int ActiveBlockers => s_Entries.Count;
    }
}
