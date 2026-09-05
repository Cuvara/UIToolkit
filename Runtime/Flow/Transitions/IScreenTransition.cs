using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace Cuvara.UIToolkit.Flow.Transitions
{
    /// <summary>
    /// Animates the visual transition between two screens during push/pop/replace.
    /// </summary>
    public interface IScreenTransition
    {
        /// <summary>
        /// Plays the enter animation for an incoming screen.
        /// </summary>
        /// <param name="element">The screen's root VisualElement.</param>
        /// <param name="ct">Cancellation token.</param>
        UniTask PlayEnterAsync(VisualElement element, CancellationToken ct = default);

        /// <summary>
        /// Plays the exit animation for an outgoing screen.
        /// </summary>
        /// <param name="element">The screen's root VisualElement.</param>
        /// <param name="ct">Cancellation token.</param>
        UniTask PlayExitAsync(VisualElement element, CancellationToken ct = default);
    }
}
