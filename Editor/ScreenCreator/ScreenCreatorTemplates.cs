namespace Cuvara.UIToolkit.Editor
{
    using System;

    /// <summary>The type of screen the wizard generates.</summary>
    public enum ScreenType
    {
        Screen,
        Popup,
        Item,
    }

    /// <summary>Where the screen's UXML comes from.</summary>
    public enum ScreenSource
    {
        /// <summary>The wizard writes a new <c>.uxml</c> and <c>.uss</c> from its own templates.</summary>
        NewUxml,

        /// <summary>
        /// The screen uses a UXML that already exists, for example one imported by the UI
        /// Toolkit Connector for Figma. The wizard writes C# and a test only, never UXML or USS.
        /// </summary>
        ExistingUxml,
    }

    /// <summary>
    /// Every string the Screen Creator writes to disk.
    /// </summary>
    /// <remarks>
    /// <para>Pure string constants, deliberately Unity-free — testable with NUnit and nothing
    /// else. The wizard calls <see cref="SelectScriptTemplate"/>,
    /// <see cref="SelectUxmlTemplate"/>, <see cref="SelectUssTemplate"/> and
    /// <see cref="SelectTestTemplate"/>, substitutes placeholders, and writes the result.</para>
    ///
    /// <para><b>What changed from the frozen GameFoundation wizard:</b></para>
    /// <list type="bullet">
    /// <item>No <c>SignalBus</c>, <c>ILoggerManager</c> — parameterless presenter ctor.</item>
    /// <item>No <c>[Preserve]</c> — generic registration, no reflective construction.</item>
    /// <item>No <c>[ScreenInfo]</c> / <c>[PopupInfo]</c> — <c>RegisterScreen&lt;,&gt;</c>.</item>
    /// <item>No <c>ISurfaceScreenView</c> — direct <c>IUIToolkitView</c>.</item>
    /// <item>No <c>Dispose</c> override — <c>ScreenSubscriptions</c> handles cleanup.</item>
    /// <item><c>Require&lt;T&gt;</c> instead of <c>Q&lt;T&gt;</c>.</item>
    /// <item>Generates a <c>.uss</c> and a test skeleton.</item>
    /// <item>UI Toolkit only — no uGUI backend.</item>
    /// </list>
    /// </remarks>
    public static class ScreenCreatorTemplates
    {
        #region Placeholders

        // Every placeholder starts with X_ so a test can assert none survive substitution.
        public const string PH_NAMESPACE = "X_NAMESPACE";
        public const string PH_NAME      = "X_NAME";
        public const string PH_ROOT_NAME = "X_ROOT_NAME"; // kebab-case for UXML
        public const string PH_ASSET_KEY = "X_ASSET_KEY"; // the key the view's UXML loads from
        public const string PH_UXML_PATH = "X_UXML_PATH"; // project path of an existing UXML

        #endregion

        #region C# templates

        private const string SCREEN_MODEL_TEMPLATE =
            @"namespace X_NAMESPACE
{
    using System.Threading;
    using Cuvara.UIToolkit.Core;
    using Cuvara.UIToolkit.Flow;
    using Cuvara.UIToolkit.Utilities;
    using Cuvara.UIToolkit.View;
    using Cysharp.Threading.Tasks;
    using UnityEngine.UIElements;

    public sealed class X_NAMEModel
    {
    }

    public interface IX_NAMEView : IUIToolkitView
    {
        Label Title { get; }
    }

    public sealed class X_NAMEView : BaseUIToolkitView, IX_NAMEView
    {
        public Label Title { get; }

        public X_NAMEView(VisualTreeAsset asset) : base(asset)
        {
            this.StretchToParent();
            this.Title = this.Root.Require<Label>(""title"");
        }
    }

    public sealed class X_NAMEPresenter : BaseUIToolkitScreenPresenter<IX_NAMEView, X_NAMEModel>
    {
        protected override UniTask OnBindAsync(X_NAMEModel model, ScreenSubscriptions subs, CancellationToken ct)
        {
            return UniTask.CompletedTask;
        }
    }
}
";

        private const string SCREEN_NO_MODEL_TEMPLATE =
            @"namespace X_NAMESPACE
{
    using System.Threading;
    using Cuvara.UIToolkit.Core;
    using Cuvara.UIToolkit.Flow;
    using Cuvara.UIToolkit.Utilities;
    using Cuvara.UIToolkit.View;
    using Cysharp.Threading.Tasks;
    using UnityEngine.UIElements;

    public interface IX_NAMEView : IUIToolkitView
    {
        Label Title { get; }
    }

    public sealed class X_NAMEView : BaseUIToolkitView, IX_NAMEView
    {
        public Label Title { get; }

        public X_NAMEView(VisualTreeAsset asset) : base(asset)
        {
            this.StretchToParent();
            this.Title = this.Root.Require<Label>(""title"");
        }
    }

    public sealed class X_NAMEPresenter : BaseUIToolkitScreenPresenter<IX_NAMEView>
    {
        protected override UniTask OnBindAsync(ScreenSubscriptions subs, CancellationToken ct)
        {
            return UniTask.CompletedTask;
        }
    }
}
";

        private const string POPUP_MODEL_TEMPLATE =
            @"namespace X_NAMESPACE
{
    using System.Threading;
    using Cuvara.UIToolkit.Core;
    using Cuvara.UIToolkit.Flow;
    using Cuvara.UIToolkit.Utilities;
    using Cuvara.UIToolkit.View;
    using Cysharp.Threading.Tasks;
    using UnityEngine.UIElements;

    public sealed class X_NAMEModel
    {
    }

    public interface IX_NAMEView : IUIToolkitView
    {
        Label  Title { get; }
        Button Close { get; }
    }

    public sealed class X_NAMEView : BaseUIToolkitView, IX_NAMEView
    {
        public Label  Title { get; }
        public Button Close { get; }

        public X_NAMEView(VisualTreeAsset asset) : base(asset)
        {
            this.StretchToParent();
            this.Title = this.Root.Require<Label>(""title"");
            this.Close = this.Root.Require<Button>(""btn-close"");
        }
    }

    public sealed class X_NAMEPresenter : BaseUIToolkitPopupPresenter<IX_NAMEView, X_NAMEModel>
    {
        protected override UniTask OnBindAsync(X_NAMEModel model, ScreenSubscriptions subs, CancellationToken ct)
        {
            subs.Clicked(this.View.Close, this.Close);

            return UniTask.CompletedTask;
        }
    }
}
";

        private const string POPUP_NO_MODEL_TEMPLATE =
            @"namespace X_NAMESPACE
{
    using System.Threading;
    using Cuvara.UIToolkit.Core;
    using Cuvara.UIToolkit.Flow;
    using Cuvara.UIToolkit.Utilities;
    using Cuvara.UIToolkit.View;
    using Cysharp.Threading.Tasks;
    using UnityEngine.UIElements;

    public interface IX_NAMEView : IUIToolkitView
    {
        Label  Title { get; }
        Button Close { get; }
    }

    public sealed class X_NAMEView : BaseUIToolkitView, IX_NAMEView
    {
        public Label  Title { get; }
        public Button Close { get; }

        public X_NAMEView(VisualTreeAsset asset) : base(asset)
        {
            this.StretchToParent();
            this.Title = this.Root.Require<Label>(""title"");
            this.Close = this.Root.Require<Button>(""btn-close"");
        }
    }

    public sealed class X_NAMEPresenter : BaseUIToolkitPopupPresenter<IX_NAMEView>
    {
        protected override UniTask OnBindAsync(ScreenSubscriptions subs, CancellationToken ct)
        {
            subs.Clicked(this.View.Close, this.Close);

            return UniTask.CompletedTask;
        }
    }
}
";

        private const string ITEM_TEMPLATE =
            @"namespace X_NAMESPACE
{
    using Cuvara.UIToolkit.Collections;
    using Cuvara.UIToolkit.Utilities;
    using UnityEngine.UIElements;

    public sealed class X_NAMEModel
    {
    }

    public sealed class X_NAMEView : BaseUIToolkitItemView
    {
        public Label Title { get; }

        public X_NAMEView(VisualElement root) : base(root)
        {
            this.Title = this.Root.Require<Label>(""title"");
        }
    }

    public sealed class X_NAMEPresenter : BaseUIToolkitItemPresenter<X_NAMEView, X_NAMEModel>
    {
        public override void BindData(X_NAMEModel param) { }
    }
}
";

        #endregion

        #region C# templates for an existing UXML

        // An existing UXML (a Figma import in particular) promises no element names: the
        // connector never emits layer names as name attributes. So these views query nothing
        // and say how to add queries instead. Assembled from parts, because the screen and the
        // popup share the same view and differ only in the presenter.

        private const string EXISTING_USINGS =
            @"namespace X_NAMESPACE
{
    using System.Threading;
    using Cuvara.UIToolkit.Core;
    using Cuvara.UIToolkit.Flow;
    using Cuvara.UIToolkit.Utilities;
    using Cuvara.UIToolkit.View;
    using Cysharp.Threading.Tasks;
    using UnityEngine.UIElements;

";

        private const string EXISTING_MODEL =
            @"    public sealed class X_NAMEModel
    {
    }

";

        private const string EXISTING_VIEW =
            @"    public interface IX_NAMEView : IUIToolkitView
    {
    }

    // Built from the existing UXML X_UXML_PATH.
    public sealed class X_NAMEView : BaseUIToolkitView, IX_NAMEView
    {
        /// <summary>The key the host's IVisualTreeAssetLoader resolves to this view's UXML.</summary>
        public const string AssetKey = ""X_ASSET_KEY"";

        public X_NAMEView(VisualTreeAsset asset) : base(asset)
        {
            this.StretchToParent();

            // This UXML was not written by the Screen Creator, so it names no elements for
            // you. Query what the screen needs with this.Root.Require<T>(""name"") once the
            // elements carry names, or enrol the UXML for typed bindings.
        }
    }
";

        private const string EXISTING_SCREEN_MODEL_PRESENTER =
            @"
    // Register with builder.RegisterScreen<X_NAMEPresenter, X_NAMEView>(X_NAMEView.AssetKey).
    public sealed class X_NAMEPresenter : BaseUIToolkitScreenPresenter<IX_NAMEView, X_NAMEModel>
    {
        protected override UniTask OnBindAsync(X_NAMEModel model, ScreenSubscriptions subs, CancellationToken ct)
        {
            return UniTask.CompletedTask;
        }
    }
}
";

        private const string EXISTING_SCREEN_NO_MODEL_PRESENTER =
            @"
    // Register with builder.RegisterScreen<X_NAMEPresenter, X_NAMEView>(X_NAMEView.AssetKey).
    public sealed class X_NAMEPresenter : BaseUIToolkitScreenPresenter<IX_NAMEView>
    {
        protected override UniTask OnBindAsync(ScreenSubscriptions subs, CancellationToken ct)
        {
            return UniTask.CompletedTask;
        }
    }
}
";

        private const string EXISTING_POPUP_MODEL_PRESENTER =
            @"
    // Register with builder.RegisterPopup<X_NAMEPresenter, X_NAMEView>(X_NAMEView.AssetKey).
    // Wire the close control once it has a name: subs.Clicked(button, this.Close).
    public sealed class X_NAMEPresenter : BaseUIToolkitPopupPresenter<IX_NAMEView, X_NAMEModel>
    {
        protected override UniTask OnBindAsync(X_NAMEModel model, ScreenSubscriptions subs, CancellationToken ct)
        {
            return UniTask.CompletedTask;
        }
    }
}
";

        private const string EXISTING_POPUP_NO_MODEL_PRESENTER =
            @"
    // Register with builder.RegisterPopup<X_NAMEPresenter, X_NAMEView>(X_NAMEView.AssetKey).
    // Wire the close control once it has a name: subs.Clicked(button, this.Close).
    public sealed class X_NAMEPresenter : BaseUIToolkitPopupPresenter<IX_NAMEView>
    {
        protected override UniTask OnBindAsync(ScreenSubscriptions subs, CancellationToken ct)
        {
            return UniTask.CompletedTask;
        }
    }
}
";

        private const string EXISTING_ITEM_TEMPLATE =
            @"namespace X_NAMESPACE
{
    using Cuvara.UIToolkit.Collections;
    using UnityEngine.UIElements;

    public sealed class X_NAMEModel
    {
    }

    // Built from the existing UXML X_UXML_PATH; hand that asset to the collection adapter.
    public sealed class X_NAMEView : BaseUIToolkitItemView
    {
        /// <summary>The key the host's IVisualTreeAssetLoader resolves to this item's UXML.</summary>
        public const string AssetKey = ""X_ASSET_KEY"";

        public X_NAMEView(VisualElement root) : base(root)
        {
            // This UXML was not written by the Screen Creator, so it names no elements for
            // you. Query what the row needs with this.Root.Require<T>(""name"").
        }
    }

    public sealed class X_NAMEPresenter : BaseUIToolkitItemPresenter<X_NAMEView, X_NAMEModel>
    {
        public override void BindData(X_NAMEModel param) { }
    }
}
";

        #endregion

        #region UXML templates

        private const string SCREEN_UXML_TEMPLATE =
            @"<?xml version=""1.0"" encoding=""utf-8""?>
<ui:UXML xmlns:ui=""UnityEngine.UIElements"" xmlns:cuvara=""Cuvara.UIToolkit.Utilities"" editor-extension-mode=""False"">
    <Style src=""X_NAME.uss"" />
    <ui:VisualElement name=""X_ROOT_NAME"" class=""cuvara-screen"">
        <cuvara:SafeAreaElement name=""safe-area"" style=""flex-grow: 1;"">
            <ui:Label name=""title"" text=""X_NAME"" />
        </cuvara:SafeAreaElement>
    </ui:VisualElement>
</ui:UXML>
";

        private const string POPUP_UXML_TEMPLATE =
            @"<?xml version=""1.0"" encoding=""utf-8""?>
<ui:UXML xmlns:ui=""UnityEngine.UIElements"" xmlns:cuvara=""Cuvara.UIToolkit.Utilities"" editor-extension-mode=""False"">
    <Style src=""X_NAME.uss"" />
    <ui:VisualElement name=""X_ROOT_NAME"" class=""cuvara-popup"">
        <cuvara:SafeAreaElement name=""safe-area"" style=""align-items: center; justify-content: center; flex-grow: 1;"">
            <ui:VisualElement name=""panel"" class=""cuvara-popup-panel"">
                <ui:Label name=""title"" text=""X_NAME"" />
                <ui:Button name=""btn-close"" text=""Close"" />
            </ui:VisualElement>
        </cuvara:SafeAreaElement>
    </ui:VisualElement>
</ui:UXML>
";

        private const string ITEM_UXML_TEMPLATE =
            @"<?xml version=""1.0"" encoding=""utf-8""?>
<ui:UXML xmlns:ui=""UnityEngine.UIElements"" editor-extension-mode=""False"">
    <ui:VisualElement name=""X_ROOT_NAME"">
        <ui:Label name=""title"" text=""X_NAME"" />
    </ui:VisualElement>
</ui:UXML>
";

        #endregion

        #region USS template

        private const string USS_TEMPLATE =
            @"#X_ROOT_NAME {
    flex-grow: 1;
}
";

        #endregion

        #region Test template

        private const string TEST_TEMPLATE =
            @"namespace X_NAMESPACE.Tests
{
    using Cuvara.UIToolkit.Flow;
    using NUnit.Framework;

    public class X_NAMEPresenterTests
    {
        [Test]
        public void OnBind_LeaksNothing()
        {
            var subs = new ScreenSubscriptions();

            // TODO: construct X_NAMEPresenter with its dependencies, attach a view,
            // call BindForTest, and assert behaviour.

            subs.Dispose();
            Assert.That(subs.LiveCount, Is.Zero, ""OnBindAsync leaked a subscription"");
        }
    }
}
";

        #endregion

        #region Selection

        /// <summary>Picks the C# template for a (type, hasModel) combination.</summary>
        public static string SelectScriptTemplate(ScreenType type, bool hasModel)
        {
            return type switch
            {
                ScreenType.Screen => hasModel ? SCREEN_MODEL_TEMPLATE : SCREEN_NO_MODEL_TEMPLATE,
                ScreenType.Popup  => hasModel ? POPUP_MODEL_TEMPLATE : POPUP_NO_MODEL_TEMPLATE,
                ScreenType.Item   => ITEM_TEMPLATE,
                _                 => throw new ArgumentOutOfRangeException(nameof(type), type, null),
            };
        }

        /// <summary>
        /// Picks the C# template for a (type, hasModel, source) combination.
        /// <see cref="ScreenSource.NewUxml"/> returns exactly what
        /// <see cref="SelectScriptTemplate(ScreenType, bool)"/> returns. Items ignore
        /// <paramref name="hasModel"/>, as they do for a new UXML.
        /// </summary>
        public static string SelectScriptTemplate(ScreenType type, bool hasModel, ScreenSource source)
        {
            if (source == ScreenSource.NewUxml) return SelectScriptTemplate(type, hasModel);
            if (source != ScreenSource.ExistingUxml) throw new ArgumentOutOfRangeException(nameof(source), source, null);

            var model = hasModel ? EXISTING_MODEL : string.Empty;

            return type switch
            {
                ScreenType.Screen => EXISTING_USINGS + model + EXISTING_VIEW
                                     + (hasModel ? EXISTING_SCREEN_MODEL_PRESENTER : EXISTING_SCREEN_NO_MODEL_PRESENTER),
                ScreenType.Popup  => EXISTING_USINGS + model + EXISTING_VIEW
                                     + (hasModel ? EXISTING_POPUP_MODEL_PRESENTER : EXISTING_POPUP_NO_MODEL_PRESENTER),
                ScreenType.Item   => EXISTING_ITEM_TEMPLATE,
                _                 => throw new ArgumentOutOfRangeException(nameof(type), type, null),
            };
        }

        /// <summary>Picks the UXML template for a view type.</summary>
        public static string SelectUxmlTemplate(ScreenType type)
        {
            return type switch
            {
                ScreenType.Screen => SCREEN_UXML_TEMPLATE,
                ScreenType.Popup  => POPUP_UXML_TEMPLATE,
                ScreenType.Item   => ITEM_UXML_TEMPLATE,
                _                 => throw new ArgumentOutOfRangeException(nameof(type), type, null),
            };
        }

        /// <summary>Returns the USS template. Only for Screen and Popup — items have no USS.</summary>
        public static string SelectUssTemplate() => USS_TEMPLATE;

        /// <summary>Returns the test file template.</summary>
        public static string SelectTestTemplate() => TEST_TEMPLATE;

        /// <summary>Applies all placeholder substitutions.</summary>
        public static string Substitute(string template, string namespaceName, string name)
        {
            return template
                .Replace(PH_NAMESPACE, namespaceName)
                .Replace(PH_ROOT_NAME, ToKebabCase(name))
                .Replace(PH_NAME, name);
        }

        /// <summary>
        /// Applies all placeholder substitutions, including the asset key and UXML path that an
        /// <see cref="ScreenSource.ExistingUxml"/> template carries. The key lands in a C# string
        /// literal, so quotes and backslashes are escaped; a line break in either is rejected.
        /// </summary>
        /// <exception cref="ArgumentException">The key or the path contains a line break.</exception>
        public static string Substitute(string template, string namespaceName, string name, string assetKey, string uxmlPath)
        {
            // Name and namespace first, exactly as the 3-argument overload; key and path last,
            // so a value that happens to contain "X_NAME" is not substituted a second time.
            return Substitute(template, namespaceName, name)
                .Replace(PH_ASSET_KEY, EscapeStringLiteral(SingleLine(assetKey)))
                .Replace(PH_UXML_PATH, SingleLine(uxmlPath));
        }

        /// <summary>The default asset key for a UXML path: its file name without extension.</summary>
        public static string DefaultAssetKey(string uxmlPath)
        {
            return string.IsNullOrEmpty(uxmlPath) ? string.Empty : System.IO.Path.GetFileNameWithoutExtension(uxmlPath);
        }

        private static string EscapeStringLiteral(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string SingleLine(string value)
        {
            if (value == null) return string.Empty;
            if (value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0)
                throw new ArgumentException("An asset key or UXML path cannot contain a line break.", nameof(value));
            return value;
        }

        /// <summary><c>ShopPopup</c> -> <c>shop-popup</c>.</summary>
        public static string ToKebabCase(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            var builder = new System.Text.StringBuilder(name.Length + 8);

            for (var i = 0; i < name.Length; ++i)
            {
                var c = name[i];

                if (char.IsUpper(c))
                {
                    if (i > 0) builder.Append('-');
                    builder.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }

        #endregion
    }
}
