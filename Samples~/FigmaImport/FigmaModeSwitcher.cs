namespace Cuvara.UIToolkit.Samples.FigmaImport
{
    using System;
    using Cuvara.UIToolkit.Theme;
    using UnityEngine.UIElements;

    /// <summary>
    /// Switches between the connector's light and dark mode sheets, for the built-in UI and for
    /// imported screens alike.
    /// </summary>
    /// <remarks>
    /// <para>Every imported screen links its own <c>tokens.uss</c> and the mode it resolved to
    /// (<c>&lt;Style src="light.uss"/&gt;</c>). <c>CloneTree</c> attaches those sheets inside the
    /// screen, and their <c>:root</c> block re-declares the light values below the document root,
    /// so switching the root's mode sheet alone would never reach the screen.</para>
    ///
    /// <para>The package's <see cref="ICuvaraThemeService"/> handles that:
    /// <see cref="ICuvaraThemeService.RegisterModeSheets"/> declares both sheets, and
    /// <see cref="ICuvaraThemeService.AttachScreen"/> keeps a screen on the current mode. This
    /// class only remembers which of the two is active. The UXML on disk is never touched.</para>
    ///
    /// <para>A screen attaches itself for exactly its own lifetime:
    /// <c>subs.Add(modes.Track(this.View.Root))</c> in <c>OnBindAsync</c>. Closing the screen
    /// disposes the subscription, so a closed screen is never kept alive.</para>
    /// </remarks>
    public sealed class FigmaModeSwitcher
    {
        private readonly ICuvaraThemeService theme;
        private readonly StyleSheet light;
        private readonly StyleSheet dark;

        public FigmaModeSwitcher(ICuvaraThemeService theme, StyleSheet light, StyleSheet dark)
        {
            this.theme = theme ?? throw new ArgumentNullException(nameof(theme));
            this.light = light;
            this.dark  = dark;
            this.theme.RegisterModeSheets(light, dark);
        }

        /// <summary>True while the dark sheet is active.</summary>
        public bool IsDark { get; private set; }

        /// <summary>Switches every themed root and attached screen to light or dark.</summary>
        public void SetDark(bool dark)
        {
            this.IsDark = dark;
            this.theme.SetModeSheet(dark ? this.dark : this.light);
        }

        /// <summary>Keeps <paramref name="screenRoot"/> on the active mode until disposed.</summary>
        public IDisposable Track(VisualElement screenRoot)
        {
            return this.theme.AttachScreen(screenRoot);
        }
    }
}
