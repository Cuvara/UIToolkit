namespace Cuvara.UIToolkit.Theme
{
    using System;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The package's theme contract: the default stylesheet and the USS class names its built-in
    /// UI carries.
    /// </summary>
    /// <remarks>
    /// <para><b>The class names are public API.</b> A host stylesheet is written against them, so
    /// renaming one breaks that host's styling silently. Prefer overriding the
    /// <c>--cuvara-*</c> variables declared in <c>CuvaraTheme.uss</c> over restyling these
    /// classes; the variables are the contract, the rules are an implementation.</para>
    ///
    /// <para><b>Why the stylesheet is a <c>Resources</c> asset.</b> The built-in UI
    /// (<c>ConfirmDialog</c>, <c>ToastService</c>, the navigator scrim) is created by static
    /// calls and plain constructors that a host may use with no container and no
    /// <c>RootUIDocument</c>. <c>Resources.Load</c> is the only way runtime code can reach a
    /// package asset without a host handing it over, and "rendering is unchanged when the host
    /// does nothing" is the requirement. The sheet is a few kilobytes.</para>
    ///
    /// <para><b>How an element gets the sheet.</b> If an ancestor was themed by
    /// <see cref="ICuvaraThemeService.Apply"/> (it carries <see cref="ThemeRootUssClassName"/>),
    /// the element inherits from it and nothing is attached. Otherwise the component attaches
    /// the default sheet to its own top element and marks it with
    /// <see cref="LocalThemeUssClassName"/>; a later <c>Apply</c> on an ancestor removes that
    /// local copy so host overrides are not shadowed by the local <c>:root</c> defaults.</para>
    /// </remarks>
    public static class CuvaraTheme
    {
        /// <summary><c>Resources.Load</c> path of the default theme stylesheet.</summary>
        public const string ResourcesPath = "CuvaraTheme";

        /// <summary>Marks a root the theme service applied the theme to.</summary>
        public const string ThemeRootUssClassName = "cuvara-theme-root";

        /// <summary>Marks an element that carries its own copy of the default sheet.</summary>
        public const string LocalThemeUssClassName = "cuvara-theme-local";

        /// <summary>The navigator's tap-outside scrim.</summary>
        public const string ScrimUssClassName = "cuvara-scrim";

        /// <summary>The scrim of a modal opened with <c>ScreenOptions.DimsBelow</c>.</summary>
        public const string ScrimDimUssClassName = "cuvara-scrim--dim";

        /// <summary><c>ConfirmDialog</c>'s full-screen overlay.</summary>
        public const string ConfirmDialogUssClassName = "cuvara-confirm-dialog";

        /// <summary><c>ConfirmDialog</c>'s panel inside the overlay.</summary>
        public const string ConfirmDialogPanelUssClassName = "cuvara-confirm-dialog__panel";

        /// <summary><c>ConfirmDialog</c>'s message label.</summary>
        public const string ConfirmDialogMessageUssClassName = "cuvara-confirm-dialog__message";

        /// <summary><c>ConfirmDialog</c>'s button row.</summary>
        public const string ConfirmDialogButtonsUssClassName = "cuvara-confirm-dialog__buttons";

        /// <summary><c>ConfirmDialog</c>'s confirm button.</summary>
        public const string ConfirmDialogConfirmUssClassName = "cuvara-confirm-dialog__confirm";

        /// <summary><c>ConfirmDialog</c>'s cancel button.</summary>
        public const string ConfirmDialogCancelUssClassName = "cuvara-confirm-dialog__cancel";

        /// <summary><c>ToastService</c>'s stacking container.</summary>
        public const string ToastContainerUssClassName = "cuvara-toast-container";

        /// <summary>One toast.</summary>
        public const string ToastUssClassName = "cuvara-toast";

        /// <summary>A toast's text.</summary>
        public const string ToastLabelUssClassName = "cuvara-toast__label";

        /// <summary>Modifier for an info toast.</summary>
        public const string ToastInfoUssClassName = "cuvara-toast--info";

        /// <summary>Modifier for a success toast.</summary>
        public const string ToastSuccessUssClassName = "cuvara-toast--success";

        /// <summary>Modifier for a warning toast.</summary>
        public const string ToastWarningUssClassName = "cuvara-toast--warning";

        /// <summary>Modifier for an error toast.</summary>
        public const string ToastErrorUssClassName = "cuvara-toast--error";

        private static StyleSheet defaultStyleSheet;
        private static bool loaded;

        private static bool overridden;
        private static StyleSheet overrideSheet;

        /// <summary>
        /// The package's default theme stylesheet, loaded once from <see cref="ResourcesPath"/>.
        /// Null only if the asset was stripped or not imported; the built-in UI then falls back
        /// to the inline styles it used before the theme contract existed.
        /// </summary>
        public static StyleSheet DefaultStyleSheet
        {
            get
            {
                if (overridden) return overrideSheet;

                if (!loaded)
                {
                    defaultStyleSheet = Resources.Load<StyleSheet>(ResourcesPath);
                    loaded            = true;
                }

                return defaultStyleSheet;
            }
        }

        /// <summary>
        /// True when <paramref name="element"/> or an ancestor carries the theme, either applied
        /// by a theme service or attached locally by a component.
        /// </summary>
        public static bool IsThemed(VisualElement element)
        {
            for (var current = element; current != null; current = current.hierarchy.parent)
            {
                if (current.ClassListContains(ThemeRootUssClassName) || current.ClassListContains(LocalThemeUssClassName))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Makes the theme reach <paramref name="element"/>, which is about to be added under
        /// <paramref name="parent"/>.
        /// </summary>
        /// <returns>
        /// False when no stylesheet is available. The caller must then apply its inline fallback
        /// so rendering does not regress.
        /// </returns>
        internal static bool EnsureReachable(VisualElement element, VisualElement parent)
        {
            if (IsThemed(parent)) return true;

            var sheet = DefaultStyleSheet;
            if (sheet == null) return false;

            if (!element.styleSheets.Contains(sheet)) element.styleSheets.Add(sheet);
            element.AddToClassList(LocalThemeUssClassName);

            return true;
        }

        /// <summary>Removes a local copy attached by <see cref="EnsureReachable"/>.</summary>
        internal static void RemoveLocal(VisualElement element)
        {
            var sheet = DefaultStyleSheet;
            if (sheet != null) element.styleSheets.Remove(sheet);
            element.RemoveFromClassList(LocalThemeUssClassName);
        }

        /// <summary>
        /// Replaces <see cref="DefaultStyleSheet"/> until the returned handle is disposed. Tests
        /// use it to exercise the inline fallback (pass null) without stripping the asset.
        /// </summary>
        internal static IDisposable OverrideDefaultStyleSheet(StyleSheet sheet)
        {
            var restore = new Restore(overridden, overrideSheet);
            overridden    = true;
            overrideSheet = sheet;
            return restore;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            defaultStyleSheet = null;
            loaded            = false;
            overridden        = false;
            overrideSheet     = null;
        }

        private sealed class Restore : IDisposable
        {
            private readonly bool wasOverridden;
            private readonly StyleSheet previous;
            private bool disposed;

            public Restore(bool wasOverridden, StyleSheet previous)
            {
                this.wasOverridden = wasOverridden;
                this.previous      = previous;
            }

            public void Dispose()
            {
                if (this.disposed) return;
                this.disposed = true;
                overridden    = this.wasOverridden;
                overrideSheet = this.previous;
            }
        }
    }
}
