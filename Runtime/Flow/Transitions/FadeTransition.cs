using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace Cuvara.UIToolkit.Flow.Transitions
{
    /// <summary>
    /// Fades the screen's opacity from 0→1 (enter) or 1→0 (exit).
    /// </summary>
    public sealed class FadeTransition : IScreenTransition
    {
        private readonly float _durationSeconds;

        public FadeTransition(float durationSeconds = 0.3f)
        {
            _durationSeconds = Math.Max(0.01f, durationSeconds);
        }

        public async UniTask PlayEnterAsync(VisualElement element, CancellationToken ct = default)
        {
            element.style.opacity = 0f;
            element.style.transitionDuration = new StyleList<TimeValue>(
                new System.Collections.Generic.List<TimeValue> { new TimeValue(_durationSeconds * 1000f, TimeUnit.Millisecond) });
            element.style.transitionProperty = new StyleList<StylePropertyName>(
                new System.Collections.Generic.List<StylePropertyName> { new StylePropertyName("opacity") });

            // Wait one frame for the initial value to be applied
            await UniTask.Yield(ct);

            element.style.opacity = 1f;
            await UniTask.Delay(TimeSpan.FromSeconds(_durationSeconds), ignoreTimeScale: true, cancellationToken: ct);
        }

        public async UniTask PlayExitAsync(VisualElement element, CancellationToken ct = default)
        {
            element.style.opacity = 1f;
            element.style.transitionDuration = new StyleList<TimeValue>(
                new System.Collections.Generic.List<TimeValue> { new TimeValue(_durationSeconds * 1000f, TimeUnit.Millisecond) });
            element.style.transitionProperty = new StyleList<StylePropertyName>(
                new System.Collections.Generic.List<StylePropertyName> { new StylePropertyName("opacity") });

            await UniTask.Yield(ct);

            element.style.opacity = 0f;
            await UniTask.Delay(TimeSpan.FromSeconds(_durationSeconds), ignoreTimeScale: true, cancellationToken: ct);
        }
    }
}
