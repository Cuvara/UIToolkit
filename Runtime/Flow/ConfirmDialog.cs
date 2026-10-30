using System;
using System.Threading;
using Cuvara.UIToolkit.Theme;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace Cuvara.UIToolkit.Flow
{
    /// <summary>
    /// Reusable modal confirmation dialog. One static call, returns a bool.
    /// </summary>
    /// <example>
    /// <code>
    /// bool confirmed = await ConfirmDialog.ShowAsync(rootElement, "Delete item?", "Delete", "Cancel");
    /// if (confirmed) DeleteItem();
    /// </code>
    /// </example>
    public static class ConfirmDialog
    {
        /// <summary>
        /// Shows a modal confirmation dialog and waits for the user's choice.
        /// </summary>
        /// <param name="parent">Visual element to parent the dialog into.</param>
        /// <param name="message">Question text.</param>
        /// <param name="confirmLabel">Confirm button label (e.g. "Yes", "Delete").</param>
        /// <param name="cancelLabel">Cancel button label (e.g. "No", "Cancel").</param>
        /// <param name="ct">Cancellation token — cancelling returns false.</param>
        /// <returns>True if confirmed, false if cancelled or dismissed.</returns>
        public static async UniTask<bool> ShowAsync(
            VisualElement parent,
            string message,
            string confirmLabel = "OK",
            string cancelLabel = "Cancel",
            CancellationToken ct = default)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));

            var tcs = new UniTaskCompletionSource<bool>();

            // Build UI. Visuals come from the cuvara-confirm-dialog classes in CuvaraTheme.uss;
            // only the full-screen stretch is inline, because it is what makes the dialog modal.
            var overlay = new VisualElement();
            overlay.AddToClassList(CuvaraTheme.ConfirmDialogUssClassName);
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0; overlay.style.right = 0;
            overlay.style.top = 0; overlay.style.bottom = 0;

            var dialog = new VisualElement();
            dialog.AddToClassList(CuvaraTheme.ConfirmDialogPanelUssClassName);

            var label = new Label(message);
            label.AddToClassList(CuvaraTheme.ConfirmDialogMessageUssClassName);
            dialog.Add(label);

            var buttons = new VisualElement();
            buttons.AddToClassList(CuvaraTheme.ConfirmDialogButtonsUssClassName);

            var cancelBtn = new Button(() => { tcs.TrySetResult(false); }) { text = cancelLabel };
            cancelBtn.AddToClassList(CuvaraTheme.ConfirmDialogCancelUssClassName);
            buttons.Add(cancelBtn);

            var confirmBtn = new Button(() => { tcs.TrySetResult(true); }) { text = confirmLabel };
            confirmBtn.AddToClassList(CuvaraTheme.ConfirmDialogConfirmUssClassName);
            buttons.Add(confirmBtn);

            dialog.Add(buttons);
            overlay.Add(dialog);

            if (!CuvaraTheme.EnsureReachable(overlay, parent))
                ApplyFallbackStyle(overlay, dialog, label, buttons, cancelBtn, confirmBtn);

            // Dismiss on overlay click (outside dialog)
            overlay.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.target == overlay) tcs.TrySetResult(false);
            });

            parent.Add(overlay);

            // Handle cancellation
            if (ct.CanBeCanceled)
            {
                ct.Register(() => tcs.TrySetResult(false));
            }

            try
            {
                return await tcs.Task;
            }
            finally
            {
                parent.Remove(overlay);
            }
        }

        /// <summary>
        /// The inline styles this dialog used before the theme contract, applied only when the
        /// theme stylesheet cannot be loaded, so rendering never regresses.
        /// </summary>
        private static void ApplyFallbackStyle(VisualElement overlay, VisualElement dialog, Label label, VisualElement buttons, Button cancelBtn, Button confirmBtn)
        {
            overlay.style.backgroundColor = new StyleColor(new UnityEngine.Color(0, 0, 0, 0.5f));
            overlay.style.alignItems = Align.Center;
            overlay.style.justifyContent = Justify.Center;

            dialog.style.backgroundColor = new StyleColor(new UnityEngine.Color(0.15f, 0.15f, 0.15f, 1f));
            dialog.style.paddingTop = 20; dialog.style.paddingBottom = 20;
            dialog.style.paddingLeft = 30; dialog.style.paddingRight = 30;
            dialog.style.borderTopLeftRadius = 8; dialog.style.borderTopRightRadius = 8;
            dialog.style.borderBottomLeftRadius = 8; dialog.style.borderBottomRightRadius = 8;
            dialog.style.minWidth = 300;
            dialog.style.maxWidth = 500;

            label.style.color = new StyleColor(UnityEngine.Color.white);
            label.style.fontSize = 16;
            label.style.marginBottom = 20;
            label.style.whiteSpace = WhiteSpace.Normal;

            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.justifyContent = Justify.FlexEnd;

            cancelBtn.style.marginRight = 10;

            confirmBtn.style.backgroundColor = new StyleColor(new UnityEngine.Color(0.2f, 0.5f, 0.9f, 1f));
            confirmBtn.style.color = new StyleColor(UnityEngine.Color.white);
        }
    }
}
