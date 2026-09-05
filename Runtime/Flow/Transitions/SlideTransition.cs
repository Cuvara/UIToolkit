using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace Cuvara.UIToolkit.Flow.Transitions
{
    /// <summary>
    /// Slides the screen in from the right (enter) or out to the left (exit).
    /// </summary>
    public sealed class SlideTransition : IScreenTransition
    {
        private readonly float _durationSeconds;

        public SlideTransition(float durationSeconds = 0.3f)
        {
            _durationSeconds = Math.Max(0.01f, durationSeconds);
        }

        public async UniTask PlayEnterAsync(VisualElement element, CancellationToken ct = default)
        {
            element.style.translate = new StyleTranslate(new Translate(Length.Percent(100), 0));
            element.style.transitionDuration = new StyleList<TimeValue>(
                new System.Collections.Generic.List<TimeValue> { new TimeValue(_durationSeconds * 1000f, TimeUnit.Millisecond) });
            element.style.transitionProperty = new StyleList<StylePropertyName>(
                new System.Collections.Generic.List<StylePropertyName> { new StylePropertyName("translate") });
            element.style.transitionTimingFunction = new StyleList<EasingFunction>(
                new System.Collections.Generic.List<EasingFunction> { new EasingFunction(EasingMode.EaseOut) });

            await UniTask.Yield(ct);

            element.style.translate = new StyleTranslate(new Translate(0, 0));
            await UniTask.Delay(TimeSpan.FromSeconds(_durationSeconds), ignoreTimeScale: true, cancellationToken: ct);
        }

        public async UniTask PlayExitAsync(VisualElement element, CancellationToken ct = default)
        {
            element.style.translate = new StyleTranslate(new Translate(0, 0));
            element.style.transitionDuration = new StyleList<TimeValue>(
                new System.Collections.Generic.List<TimeValue> { new TimeValue(_durationSeconds * 1000f, TimeUnit.Millisecond) });
            element.style.transitionProperty = new StyleList<StylePropertyName>(
                new System.Collections.Generic.List<StylePropertyName> { new StylePropertyName("translate") });
            element.style.transitionTimingFunction = new StyleList<EasingFunction>(
                new System.Collections.Generic.List<EasingFunction> { new EasingFunction(EasingMode.EaseIn) });

            await UniTask.Yield(ct);

            element.style.translate = new StyleTranslate(new Translate(Length.Percent(-100), 0));
            await UniTask.Delay(TimeSpan.FromSeconds(_durationSeconds), ignoreTimeScale: true, cancellationToken: ct);
        }
    }
}
