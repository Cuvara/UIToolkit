namespace Cuvara.UIToolkit.Theme
{
    using System;
    using System.Collections.Generic;
    using UnityEngine.UIElements;

    /// <summary>The default <see cref="ICuvaraThemeService"/>.</summary>
    /// <remarks>
    /// <para>It edits <c>VisualElement.styleSheets</c> and nothing else. No asset loading beyond
    /// the default sheet, no Figma knowledge: a Figma-driven game passes the connector's
    /// <c>tokens.uss</c>, its bridge sheet and its mode sheets in as plain
    /// <see cref="StyleSheet"/> assets.</para>
    ///
    /// <para>A root is marked with <see cref="CuvaraTheme.ThemeRootUssClassName"/> only when a
    /// base sheet exists. Without one the built-in components must keep their inline fallback,
    /// and the mark would tell them not to.</para>
    /// </remarks>
    public sealed class CuvaraThemeService : ICuvaraThemeService
    {
        private readonly StyleSheet baseSheet;
        private readonly List<StyleSheet> overrides = new();
        private readonly List<VisualElement> roots = new();
        private StyleSheet modeSheet;

        /// <summary>Uses <see cref="CuvaraTheme.DefaultStyleSheet"/> as the base.</summary>
        public CuvaraThemeService() : this(CuvaraTheme.DefaultStyleSheet) { }

        /// <summary>Uses <paramref name="baseSheet"/> as the base; null attaches overrides and modes only.</summary>
        public CuvaraThemeService(StyleSheet baseSheet) { this.baseSheet = baseSheet; }

        /// <summary>The base sheet, or null.</summary>
        public StyleSheet BaseSheet => this.baseSheet;

        /// <summary>The override sheets, in attach order.</summary>
        public IReadOnlyList<StyleSheet> Overrides => this.overrides;

        /// <summary>The current mode sheet, or null.</summary>
        public StyleSheet ModeSheet => this.modeSheet;

        /// <inheritdoc />
        public void Apply(VisualElement root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            if (!this.roots.Contains(root)) this.roots.Add(root);

            this.Sync(root);
        }

        /// <inheritdoc />
        public void AddOverride(StyleSheet sheet)
        {
            if (sheet == null) throw new ArgumentNullException(nameof(sheet));
            if (this.overrides.Contains(sheet)) return;

            this.overrides.Add(sheet);
            this.SyncAll();
        }

        /// <inheritdoc />
        public void SetModeSheet(StyleSheet sheet)
        {
            if (sheet == this.modeSheet) return;

            var previous = this.modeSheet;
            this.modeSheet = sheet;

            if (previous != null && previous != this.baseSheet && !this.overrides.Contains(previous))
            {
                foreach (var root in this.roots) root.styleSheets.Remove(previous);
            }

            this.SyncAll();
        }

        private void SyncAll()
        {
            foreach (var root in this.roots) this.Sync(root);
        }

        /// <summary>Re-establishes base, overrides, mode as the last sheets on <paramref name="root"/>, once each.</summary>
        private void Sync(VisualElement root)
        {
            var sheets = root.styleSheets;

            if (this.baseSheet != null) sheets.Remove(this.baseSheet);
            foreach (var sheet in this.overrides) sheets.Remove(sheet);
            if (this.modeSheet != null) sheets.Remove(this.modeSheet);

            if (this.baseSheet != null) sheets.Add(this.baseSheet);
            foreach (var sheet in this.overrides) sheets.Add(sheet);
            if (this.modeSheet != null && !sheets.Contains(this.modeSheet)) sheets.Add(this.modeSheet);

            if (this.baseSheet == null) return;

            root.AddToClassList(CuvaraTheme.ThemeRootUssClassName);

            // A component created before this root was themed carries its own copy of the
            // default sheet. Its :root block would re-declare the defaults below this root and
            // shadow every override, so it goes now that the root provides the theme.
            // The root itself only loses the mark: the base sheet just attached may be that copy.
            root.Query(className: CuvaraTheme.LocalThemeUssClassName).ForEach(element =>
            {
                if (element == root) element.RemoveFromClassList(CuvaraTheme.LocalThemeUssClassName);
                else CuvaraTheme.RemoveLocal(element);
            });
        }
    }
}
