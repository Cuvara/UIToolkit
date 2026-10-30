namespace Cuvara.UIToolkit.Samples.FigmaImport
{
    using System;
    using Cuvara.UIToolkit.Flow;
    using Cuvara.UIToolkit.Managers;
    using Cuvara.UIToolkit.Theme;
    using Cysharp.Threading.Tasks;
    using global::VContainer.Unity;
    using UnityEngine.UIElements;

    /// <summary>
    /// Themes the document from the Figma sheets, adds the mode toggle, and opens ShopCard.
    /// </summary>
    /// <remarks>
    /// <para><b>Theme order.</b> Overrides <c>tokens.uss</c> then <c>cuvara-theme-bridge.uss</c>,
    /// then the mode sheet. <c>tokens.uss</c> must be attached at the document root as well:
    /// each imported screen links it on its own subtree, but the dialog, the toasts and the
    /// scrim live in the overlay layer, outside that subtree, and the bridge's aliases resolve
    /// where those components are.</para>
    ///
    /// <para><b>The toggle is a button, not a key.</b> A Unity 6 project is usually on the
    /// Input System package only, where the legacy <c>UnityEngine.Input</c> API throws. A UI
    /// Toolkit button works under every input setting.</para>
    ///
    /// <para><c>IInitializable</c> plus a one-frame wait, as in the Screen Flow sample:
    /// <c>UIDocument</c> builds its root in its own <c>OnEnable</c>, whose order relative to
    /// this scope is undefined.</para>
    /// </remarks>
    public sealed class FigmaImportSampleBootstrap : IInitializable, IDisposable
    {
        private readonly IScreenNavigator       navigator;
        private readonly RootUIDocument         document;
        private readonly ICuvaraThemeService    theme;
        private readonly FigmaSampleThemeSheets sheets;
        private readonly FigmaModeSwitcher      modes;
        private readonly FigmaSampleToasts      toasts;

        private VisualElement toolbar;
        private Button        modeButton;

        public FigmaImportSampleBootstrap(
            IScreenNavigator       navigator,
            RootUIDocument         document,
            ICuvaraThemeService    theme,
            FigmaSampleThemeSheets sheets,
            FigmaModeSwitcher      modes,
            FigmaSampleToasts      toasts)
        {
            this.navigator = navigator;
            this.document  = document;
            this.theme     = theme;
            this.sheets    = sheets;
            this.modes     = modes;
            this.toasts    = toasts;
        }

        public void Initialize()
        {
            this.StartAsync().Forget();
        }

        private async UniTaskVoid StartAsync()
        {
            await UniTask.Yield();

            if (this.sheets.Tokens != null) this.theme.AddOverride(this.sheets.Tokens);
            if (this.sheets.Bridge != null) this.theme.AddOverride(this.sheets.Bridge);
            this.modes.SetDark(false);

            this.BuildToolbar();

            await this.navigator.PushAsync<ShopCardPresenter, ShopCardModel>(new ShopCardModel());
        }

        private void BuildToolbar()
        {
            // Straight into the document root, not a layer: the toolbar belongs to the sample,
            // not to the screen flow.
            this.toolbar = new VisualElement();
            this.toolbar.style.position      = Position.Absolute;
            this.toolbar.style.top           = 16;
            this.toolbar.style.right         = 16;
            this.toolbar.style.flexDirection = FlexDirection.Row;

            this.modeButton = new Button(this.ToggleMode) { text = "Dark mode" };
            this.toolbar.Add(this.modeButton);
            this.toolbar.Add(new Button(this.ShowThemeToast) { text = "Show toast" });

            this.document.RootVisualElement.Add(this.toolbar);
        }

        private void ToggleMode()
        {
            this.modes.SetDark(!this.modes.IsDark);
            this.modeButton.text = this.modes.IsDark ? "Light mode" : "Dark mode";
        }

        private void ShowThemeToast()
        {
            this.toasts.Show(this.modes.IsDark ? "Dark mode: dark.uss" : "Light mode: light.uss", 2f, ToastType.Info);
        }

        public void Dispose()
        {
            this.toolbar?.RemoveFromHierarchy();
        }
    }
}
