namespace Cuvara.UIToolkit.Theme
{
    using UnityEngine.UIElements;

    /// <summary>
    /// Attaches the Cuvara theme, a host's override sheets and one mode sheet to visual roots.
    /// </summary>
    /// <remarks>
    /// <para>Sheet order on every applied root is: base theme, overrides in the order added, mode
    /// sheet. A later sheet wins at equal specificity, so an override that sets
    /// <c>--cuvara-*</c> on <c>:root</c> replaces the default, and a mode sheet replaces both.</para>
    ///
    /// <para>All operations are idempotent: applying the same root twice, or adding the same
    /// override twice, leaves one copy of each sheet.</para>
    /// </remarks>
    public interface ICuvaraThemeService
    {
        /// <summary>Attaches the theme to <paramref name="root"/> and keeps it in sync with later changes.</summary>
        void Apply(VisualElement root);

        /// <summary>Appends a host sheet (Figma <c>tokens.uss</c>, a bridge sheet) after the base theme on every applied root.</summary>
        void AddOverride(StyleSheet sheet);

        /// <summary>Replaces the current mode sheet (light, dark, ...) on every applied root. Null removes it.</summary>
        void SetModeSheet(StyleSheet sheet);
    }
}
