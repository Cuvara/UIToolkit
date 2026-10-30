namespace Cuvara.UIToolkit.Theme
{
    using System;
    using UnityEngine.UIElements;

    /// <summary>
    /// Attaches the Cuvara theme to a visual-tree root: the base sheet (<c>CuvaraTheme.uss</c>),
    /// per-game override sheets in the order they were added (for example a Figma connector
    /// <c>tokens.uss</c>, then a bridge sheet that sets <c>--cuvara-*</c> to Figma tokens), and
    /// one switchable mode sheet (light, dark, ...) last.
    /// </summary>
    /// <remarks>
    /// Every call is idempotent. A root keeps the theme until <see cref="Remove"/>.
    /// <para>Imported screens link their own mode sheet (the connector writes
    /// <c>&lt;Style src="light.uss"/&gt;</c> into each screen), and a sheet on a descendant wins
    /// over the same custom property on the root. <see cref="AttachScreen"/> keeps such a screen
    /// on the current mode: every sheet registered with <see cref="RegisterModeSheets"/> is
    /// replaced by the current mode sheet inside the screen.</para>
    /// </remarks>
    public interface ICuvaraThemeService
    {
        /// <summary>Attaches the theme to <paramref name="root"/> and keeps it in sync.</summary>
        void Apply(VisualElement root);

        /// <summary>
        /// Stops keeping <paramref name="root"/> in sync and detaches the sheets this service
        /// attached to it. A root that was never applied is ignored.
        /// </summary>
        void Remove(VisualElement root);

        /// <summary>Appends a per-game override sheet to every applied root.</summary>
        void AddOverride(StyleSheet sheet);

        /// <summary>
        /// Declares every mode sheet a game uses (for example the connector's <c>light.uss</c>
        /// and <c>dark.uss</c>), so screens attached with <see cref="AttachScreen"/> can be
        /// switched off whichever one they linked. Sheets passed to <see cref="SetModeSheet"/>
        /// are registered automatically.
        /// </summary>
        void RegisterModeSheets(params StyleSheet[] modeSheets);

        /// <summary>
        /// Switches the mode sheet on every applied root and every attached screen. Null clears
        /// it from the roots; attached screens then keep the mode sheet they link last.
        /// </summary>
        void SetModeSheet(StyleSheet sheet);

        /// <summary>
        /// Keeps an imported screen on the current mode: inside <paramref name="screenRoot"/>,
        /// every registered mode sheet is replaced by the current one, now and on every
        /// <see cref="SetModeSheet"/>. Dispose the result when the screen closes.
        /// </summary>
        IDisposable AttachScreen(VisualElement screenRoot);
    }
}
