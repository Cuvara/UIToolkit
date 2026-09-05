using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace Cuvara.UIToolkit.Flow
{
    /// <summary>
    /// Auto-dismissing toast notifications. Stackable, type-tinted, positional.
    /// </summary>
    /// <example>
    /// <code>
    /// var toasts = new ToastService(rootElement);
    /// toasts.Show("Item acquired!", 3f, ToastType.Success);
    /// toasts.Show("Connection lost", 5f, ToastType.Error);
    /// </code>
    /// </example>
    public sealed class ToastService : IDisposable
    {
        private readonly VisualElement _container;
        private readonly List<VisualElement> _active = new();
        private bool _disposed;

        /// <summary>Number of currently visible toasts.</summary>
        public int ActiveCount => _active.Count;

        /// <summary>
        /// Creates a toast service anchored to a parent element.
        /// </summary>
        /// <param name="parent">Element to parent toast container into.</param>
        public ToastService(VisualElement parent)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));

            _container = new VisualElement();
            _container.name = "toast-container";
            _container.style.position = Position.Absolute;
            _container.style.top = 20;
            _container.style.right = 20;
            _container.style.width = 300;
            _container.style.flexDirection = FlexDirection.Column;
            _container.pickingMode = PickingMode.Ignore;

            parent.Add(_container);
        }

        /// <summary>
        /// Shows a toast notification that auto-dismisses after <paramref name="duration"/> seconds.
        /// </summary>
        /// <param name="message">Text to display.</param>
        /// <param name="duration">Seconds before auto-dismiss. Default 3.</param>
        /// <param name="type">Visual style: Info, Success, Warning, Error.</param>
        public void Show(string message, float duration = 3f, ToastType type = ToastType.Info)
        {
            if (_disposed) return;

            var toast = CreateToastElement(message, type);
            _container.Add(toast);
            _active.Add(toast);

            AutoDismiss(toast, duration).Forget();
        }

        private async UniTaskVoid AutoDismiss(VisualElement toast, float duration)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(duration), ignoreTimeScale: true);

            if (_disposed) return;
            if (_active.Remove(toast))
            {
                _container.Remove(toast);
            }
        }

        /// <summary>Dismisses all visible toasts immediately.</summary>
        public void Clear()
        {
            foreach (var toast in _active)
                _container.Remove(toast);
            _active.Clear();
        }

        public void Dispose()
        {
            _disposed = true;
            Clear();
            _container.RemoveFromHierarchy();
        }

        private static VisualElement CreateToastElement(string message, ToastType type)
        {
            var toast = new VisualElement();
            toast.style.backgroundColor = new StyleColor(GetColor(type));
            toast.style.paddingTop = 10; toast.style.paddingBottom = 10;
            toast.style.paddingLeft = 15; toast.style.paddingRight = 15;
            toast.style.marginBottom = 8;
            toast.style.borderTopLeftRadius = 6; toast.style.borderTopRightRadius = 6;
            toast.style.borderBottomLeftRadius = 6; toast.style.borderBottomRightRadius = 6;

            var label = new Label(message);
            label.style.color = new StyleColor(UnityEngine.Color.white);
            label.style.fontSize = 14;
            label.style.whiteSpace = WhiteSpace.Normal;
            toast.Add(label);

            return toast;
        }

        private static UnityEngine.Color GetColor(ToastType type) => type switch
        {
            ToastType.Success => new UnityEngine.Color(0.2f, 0.7f, 0.3f, 0.95f),
            ToastType.Warning => new UnityEngine.Color(0.9f, 0.7f, 0.1f, 0.95f),
            ToastType.Error   => new UnityEngine.Color(0.9f, 0.25f, 0.2f, 0.95f),
            _                 => new UnityEngine.Color(0.25f, 0.25f, 0.3f, 0.95f),
        };
    }

    /// <summary>Visual style for toast notifications.</summary>
    public enum ToastType
    {
        Info,
        Success,
        Warning,
        Error,
    }
}
