namespace Cuvara.UIToolkit.Samples.FigmaImport
{
    using System;
    using System.Threading;
    using Cuvara.UIToolkit.Core;
    using Cuvara.UIToolkit.Flow;
    using Cuvara.UIToolkit.Managers;
    using Cuvara.UIToolkit.View;
    using Cysharp.Threading.Tasks;
    using UnityEngine.UIElements;

    // -------------------------------------------------------------------------------------
    // The ShopCard screen over an imported UXML.
    //
    // This file started as the output of Assets > Cuvara > Create Screen with Source =
    // "Existing UXML (e.g. Figma import)", Type = Screen, Model on, name ShopCard, UXML
    // ConnectorOutput/ShopCard.uxml. The wizard writes Model, IView, View and Presenter with
    // a View that queries nothing, because an imported UXML names no elements. Everything
    // below the "added by hand" comments is what a game then writes itself.
    //
    // The UXML is connector output: never edit it to add names. The next push from Figma
    // rewrites it. The view therefore finds its elements without layer names (see
    // ShopCardView). Typed node-id bindings (gap G4) are deferred until a real import confirms
    // the runtime element paths, so this sample does not use or invent them.
    // -------------------------------------------------------------------------------------

    public sealed class ShopCardModel
    {
        // Added by hand: what the presenter writes into the imported texts.
        public string ItemName    = "Ember Blade";
        public string Description = "A sword that never cools. +12 attack.";
        public int    Price       = 250;
    }

    public interface IShopCardView : IUIToolkitView
    {
        // Added by hand.
        Label  ItemName    { get; }
        Label  Description { get; }
        Label  Price       { get; }
        Button Buy         { get; }
    }

    // Built from the existing UXML ConnectorOutput/ShopCard.uxml.
    public sealed class ShopCardView : BaseUIToolkitView, IShopCardView
    {
        /// <summary>The key the host's IVisualTreeAssetLoader resolves to this view's UXML.</summary>
        public const string AssetKey = "ShopCard";

        // The classes the connector emits for the Figma components, with the profile's prefix
        // "shop". Component classes are the most stable handle an import offers: they come
        // from the component name, not from the layer tree.
        private const string TitleClass       = "shop-card-title";
        private const string DescriptionClass = "shop-card-description";
        private const string BuyButtonClass   = "shop-primary-button";
        private const string PriceTagClass    = "shop-price-tag";

        public Label  ItemName    { get; }
        public Label  Description { get; }
        public Label  Price       { get; }
        public Button Buy         { get; }

        public ShopCardView(VisualTreeAsset asset) : base(asset)
        {
            this.StretchToParent();

            // Added by hand. Two ways to find an element in connector output:
            //
            // 1. By component class. The title, the description and the Buy button are
            //    instances of Figma components ("Card Title", "Card Description", "Primary
            //    Button"), so each carries its component's class. This is also why they can use
            //    tokens at all: on Unity 6000.3 a token can only reach an element through a
            //    class rule, because UI Toolkit does not support var() in inline styles.
            this.ItemName    = Find(this.Root.Q<Label>(className: TitleClass), "the title (class " + TitleClass + ")");
            this.Description = Find(this.Root.Q<Label>(className: DescriptionClass), "the description (class " + DescriptionClass + ")");
            this.Buy         = Find(this.Root.Q<Button>(className: BuyButtonClass), "the Buy button (class " + BuyButtonClass + ")");

            // 2. By the template text name. Inside a template the connector names each TEXT
            //    layer text-<i>, in order, for AttributeOverrides. Scope the query to the
            //    instance first, because every PriceTag instance has its own text-0.
            var priceTag = Find(this.Root.Q<VisualElement>(className: PriceTagClass), "the PriceTag instance (class " + PriceTagClass + ")");
            this.Price = Find(priceTag.Q<Label>("text-0"), "text-0 inside the PriceTag instance");

            // A plain layer has neither a name nor a class. Find it by position
            // (this.Root.Query<Label>().ToList()[i]) only as a last resort: a designer who
            // reorders layers changes the result. If a screen needs many such lookups, wrap the
            // imported UXML in a game-owned UXML that names its own containers.
        }

        private static T Find<T>(T element, string what) where T : VisualElement
        {
            return element ?? throw new InvalidOperationException(
                $"ShopCard.uxml has no {what}. The Figma file or the connector's class prefix changed; "
                + "check the FigmaProfile class prefix and the Figma Diagnostics window.");
        }
    }

    // Register with builder.RegisterScreen<ShopCardPresenter, ShopCardView>(ShopCardView.AssetKey).
    public sealed class ShopCardPresenter : BaseUIToolkitScreenPresenter<IShopCardView, ShopCardModel>
    {
        // Added by hand: the presenter's own dependencies, injected by VContainer. The base
        // constructor takes nothing.
        private readonly RootUIDocument    document;
        private readonly FigmaSampleToasts toasts;
        private readonly FigmaModeSwitcher modes;

        public ShopCardPresenter(RootUIDocument document, FigmaSampleToasts toasts, FigmaModeSwitcher modes)
        {
            this.document = document;
            this.toasts   = toasts;
            this.modes    = modes;
        }

        protected override UniTask OnBindAsync(ShopCardModel model, ScreenSubscriptions subs, CancellationToken ct)
        {
            // Added by hand. The imported UXML links light.uss itself; let the mode switcher
            // re-link the active mode on this screen for as long as it is open.
            subs.Add(this.modes.Track(this.View.Root));

            this.View.ItemName.text    = model.ItemName;
            this.View.Description.text = model.Description;
            this.View.Price.text       = model.Price.ToString();

            // ct is cancelled when the screen closes, which also dismisses an open dialog.
            subs.Clicked(this.View.Buy, () => this.BuyAsync(model, ct).Forget());

            return UniTask.CompletedTask;
        }

        private async UniTaskVoid BuyAsync(ShopCardModel model, CancellationToken ct)
        {
            // The dialog and the toasts are the package's built-in UI. They live in the overlay
            // layer, outside the imported screen, and still follow the Figma tokens through the
            // bridge sheet the theme service attached at the document root.
            var confirmed = await ConfirmDialog.ShowAsync(
                this.document.RootUIOverlayElement,
                $"Buy {model.ItemName} for {model.Price} gold?",
                "Buy",
                "Cancel",
                ct);

            if (ct.IsCancellationRequested) return;

            if (confirmed) this.toasts.Show($"{model.ItemName} added to your inventory.", 2.5f, ToastType.Success);
            else this.toasts.Show("Purchase cancelled.", 2f, ToastType.Info);
        }
    }
}
