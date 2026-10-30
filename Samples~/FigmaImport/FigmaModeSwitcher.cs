namespace Cuvara.UIToolkit.Samples.FigmaImport
{
    using System;
    using System.Collections.Generic;
    using Cuvara.UIToolkit.Theme;
    using UnityEngine.UIElements;

    /// <summary>
    /// Switches between the connector's light and dark mode sheets, for the built-in UI and for
    /// imported screens alike.
    /// </summary>
    /// <remarks>
    /// <para><b>Why the theme service alone is not enough.</b> <c>SetModeSheet</c> swaps the
    /// mode sheet at the document root, which re-themes the dialog, the toasts and the scrim.
    /// But every imported screen links its own <c>tokens.uss</c> and the mode it resolved to
    /// (<c>&lt;Style src="light.uss"/&gt;</c>). <c>CloneTree</c> attaches those sheets to the
    /// screen's own root, and their <c>:root</c> block matches that element, so the screen
    /// re-declares the light values below the document root and never sees the switch.</para>
    ///
    /// <para>So the switcher also re-links the mode sheet on each imported screen it tracks:
    /// it removes the light and dark sheets from the screen root and appends the active one,
    /// which then wins over the screen's <c>tokens.uss</c>. The UXML on disk is not touched;
    /// only the cloned tree is.</para>
    ///
    /// <para>A screen tracks itself for exactly its own lifetime:
    /// <c>subs.Add(modes.Track(this.View.Root))</c> in <c>OnBindAsync</c>. Closing the screen
    /// disposes the subscription, which stops tracking, so a closed screen is never kept
    /// alive by this list.</para>
    /// </remarks>
    public sealed class FigmaModeSwitcher
    {
        private readonly ICuvaraThemeService theme;
        private readonly StyleSheet light;
        private readonly StyleSheet dark;
        private readonly List<VisualElement> screens = new List<VisualElement>();

        public FigmaModeSwitcher(ICuvaraThemeService theme, StyleSheet light, StyleSheet dark)
        {
            this.theme = theme ?? throw new ArgumentNullException(nameof(theme));
            this.light = light;
            this.dark  = dark;
        }

        /// <summary>True while the dark mode sheet is active.</summary>
        public bool IsDark { get; private set; }

        private StyleSheet Current => this.IsDark ? this.dark : this.light;

        /// <summary>Sets the mode on the document root and on every tracked screen.</summary>
        public void SetDark(bool dark)
        {
            this.IsDark = dark;
            this.theme.SetModeSheet(this.Current);

            foreach (var screen in this.screens) this.Relink(screen);
        }

        /// <summary>Re-links the active mode sheet on an imported screen until the result is disposed.</summary>
        public IDisposable Track(VisualElement screenRoot)
        {
            if (screenRoot == null) throw new ArgumentNullException(nameof(screenRoot));

            if (!this.screens.Contains(screenRoot)) this.screens.Add(screenRoot);
            this.Relink(screenRoot);

            return new Untrack(this, screenRoot);
        }

        private void Relink(VisualElement screenRoot)
        {
            if (this.light != null) screenRoot.styleSheets.Remove(this.light);
            if (this.dark != null) screenRoot.styleSheets.Remove(this.dark);

            var current = this.Current;
            if (current != null) screenRoot.styleSheets.Add(current);
        }

        private sealed class Untrack : IDisposable
        {
            private FigmaModeSwitcher owner;
            private readonly VisualElement screenRoot;

            public Untrack(FigmaModeSwitcher owner, VisualElement screenRoot)
            {
                this.owner      = owner;
                this.screenRoot = screenRoot;
            }

            public void Dispose()
            {
                this.owner?.screens.Remove(this.screenRoot);
                this.owner = null;
            }
        }
    }
}
