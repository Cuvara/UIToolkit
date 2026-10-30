namespace Cuvara.UIToolkit.Tests.Theme
{
    using System.Collections;
    using System.Globalization;
    using System.Text.RegularExpressions;
    using System.Threading;
    using Cuvara.UIToolkit.Flow;
    using Cuvara.UIToolkit.Theme;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// The theme contract: built-in UI carries the documented classes, the stylesheet reaches it
    /// without host work, the theme service attaches sheets idempotently and in order, and the
    /// inline fallback reproduces the pre-theme visuals when no sheet is available.
    /// </summary>
    /// <remarks>
    /// Elements are detached on purpose: these assertions are about class lists, attached sheets
    /// and inline values, none of which needs a panel or a style pass.
    /// </remarks>
    public sealed class CuvaraThemeTests
    {
        private const float Tolerance = 1e-4f;

        private VisualElement root;
        private StyleSheet overrideA, overrideB, modeLight, modeDark;

        [SetUp]
        public void SetUp()
        {
            this.root      = new VisualElement();
            this.overrideA = ScriptableObject.CreateInstance<StyleSheet>();
            this.overrideB = ScriptableObject.CreateInstance<StyleSheet>();
            this.modeLight = ScriptableObject.CreateInstance<StyleSheet>();
            this.modeDark  = ScriptableObject.CreateInstance<StyleSheet>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(this.overrideA);
            Object.DestroyImmediate(this.overrideB);
            Object.DestroyImmediate(this.modeLight);
            Object.DestroyImmediate(this.modeDark);
        }

        #region Default sheet

        [Test]
        public void DefaultStyleSheet_LoadsFromPackageResources()
        {
            Assert.That(CuvaraTheme.DefaultStyleSheet, Is.Not.Null,
                $"Resources/{CuvaraTheme.ResourcesPath}.uss must ship with the package");
        }

        #endregion

        #region Toast classes

        [TestCase(ToastType.Info,    CuvaraTheme.ToastInfoUssClassName)]
        [TestCase(ToastType.Success, CuvaraTheme.ToastSuccessUssClassName)]
        [TestCase(ToastType.Warning, CuvaraTheme.ToastWarningUssClassName)]
        [TestCase(ToastType.Error,   CuvaraTheme.ToastErrorUssClassName)]
        public void Toast_CarriesContainerToastModifierAndLabelClasses(ToastType type, string modifier)
        {
            using var service = new ToastService(this.root);
            service.Show("hello", 30f, type);

            var container = this.root.Q("toast-container");
            Assert.That(container.ClassListContains(CuvaraTheme.ToastContainerUssClassName), Is.True);

            var toast = container[0];
            Assert.That(toast.ClassListContains(CuvaraTheme.ToastUssClassName), Is.True);
            Assert.That(toast.ClassListContains(modifier), Is.True);
            Assert.That(toast.Q<Label>().ClassListContains(CuvaraTheme.ToastLabelUssClassName), Is.True);
        }

        [Test]
        public void Toast_UnthemedParent_AttachesDefaultSheetToItsContainer_AndNoInlineColour()
        {
            using var service = new ToastService(this.root);
            service.Show("hello", 30f, ToastType.Error);

            var container = this.root.Q("toast-container");
            Assert.That(container.styleSheets.Contains(CuvaraTheme.DefaultStyleSheet), Is.True);
            Assert.That(container.ClassListContains(CuvaraTheme.LocalThemeUssClassName), Is.True);

            Assert.That(container[0].style.backgroundColor.keyword, Is.EqualTo(StyleKeyword.Null),
                "with a sheet available the colour comes from USS, not inline");
        }

        [Test]
        public void Toast_UnderThemedRoot_InheritsInsteadOfAttachingACopy()
        {
            new CuvaraThemeService().Apply(this.root);
            var child = new VisualElement();
            this.root.Add(child);

            using var service = new ToastService(child);

            var container = child.Q("toast-container");
            Assert.That(container.styleSheets.count, Is.Zero);
            Assert.That(container.ClassListContains(CuvaraTheme.LocalThemeUssClassName), Is.False);
        }

        #endregion

        #region ConfirmDialog classes

        [UnityTest]
        public IEnumerator ConfirmDialog_CarriesDocumentedClasses_AndCleansUpOnCancel() => UniTask.ToCoroutine(async () =>
        {
            using var cts = new CancellationTokenSource();
            var pending = ConfirmDialog.ShowAsync(this.root, "Delete?", "Delete", "Keep", cts.Token);

            Assert.That(this.root.childCount, Is.EqualTo(1));
            var overlay = this.root[0];
            Assert.That(overlay.ClassListContains(CuvaraTheme.ConfirmDialogUssClassName), Is.True);
            Assert.That(overlay.styleSheets.Contains(CuvaraTheme.DefaultStyleSheet), Is.True);

            var panel = overlay[0];
            Assert.That(panel.ClassListContains(CuvaraTheme.ConfirmDialogPanelUssClassName), Is.True);
            Assert.That(panel.Q<Label>().ClassListContains(CuvaraTheme.ConfirmDialogMessageUssClassName), Is.True);

            var buttons = panel.Q(className: CuvaraTheme.ConfirmDialogButtonsUssClassName);
            Assert.That(buttons, Is.Not.Null);
            Assert.That(buttons.Q<Button>(className: CuvaraTheme.ConfirmDialogCancelUssClassName).text, Is.EqualTo("Keep"));
            Assert.That(buttons.Q<Button>(className: CuvaraTheme.ConfirmDialogConfirmUssClassName).text, Is.EqualTo("Delete"));

            Assert.That(panel.style.backgroundColor.keyword, Is.EqualTo(StyleKeyword.Null),
                "with a sheet available the panel colour comes from USS, not inline");

            cts.Cancel();
            Assert.That(await pending, Is.False);
            Assert.That(this.root.childCount, Is.Zero);
        });

        #endregion

        #region Fallback

        [Test]
        public void Fallback_NoSheet_ToastKeepsPreThemeInlineValues()
        {
            using (CuvaraTheme.OverrideDefaultStyleSheet(null))
            {
                using var service = new ToastService(this.root);
                service.Show("a", 30f, ToastType.Info);
                service.Show("b", 30f, ToastType.Success);
                service.Show("c", 30f, ToastType.Warning);
                service.Show("d", 30f, ToastType.Error);

                var container = this.root.Q("toast-container");
                Assert.That(container.styleSheets.count, Is.Zero);
                Assert.That(container.style.top.value.value, Is.EqualTo(20f));
                Assert.That(container.style.right.value.value, Is.EqualTo(20f));
                Assert.That(container.style.width.value.value, Is.EqualTo(300f));
                Assert.That(container.style.flexDirection.value, Is.EqualTo(FlexDirection.Column));

                AssertColor(container[0].style.backgroundColor.value, new Color(0.25f, 0.25f, 0.3f, 0.95f));
                AssertColor(container[1].style.backgroundColor.value, new Color(0.2f, 0.7f, 0.3f, 0.95f));
                AssertColor(container[2].style.backgroundColor.value, new Color(0.9f, 0.7f, 0.1f, 0.95f));
                AssertColor(container[3].style.backgroundColor.value, new Color(0.9f, 0.25f, 0.2f, 0.95f));

                var toast = container[0];
                Assert.That(toast.style.paddingTop.value.value, Is.EqualTo(10f));
                Assert.That(toast.style.paddingLeft.value.value, Is.EqualTo(15f));
                Assert.That(toast.style.marginBottom.value.value, Is.EqualTo(8f));
                Assert.That(toast.style.borderTopLeftRadius.value.value, Is.EqualTo(6f));

                var label = toast.Q<Label>();
                AssertColor(label.style.color.value, Color.white);
                Assert.That(label.style.fontSize.value.value, Is.EqualTo(14f));
                Assert.That(label.style.whiteSpace.value, Is.EqualTo(WhiteSpace.Normal));

                // The classes are there either way, so a host sheet still matches.
                Assert.That(toast.ClassListContains(CuvaraTheme.ToastUssClassName), Is.True);
            }
        }

        [UnityTest]
        public IEnumerator Fallback_NoSheet_ConfirmDialogKeepsPreThemeInlineValues() => UniTask.ToCoroutine(async () =>
        {
            using (CuvaraTheme.OverrideDefaultStyleSheet(null))
            {
                using var cts = new CancellationTokenSource();
                var pending = ConfirmDialog.ShowAsync(this.root, "Sure?", ct: cts.Token);

                var overlay = this.root[0];
                Assert.That(overlay.styleSheets.count, Is.Zero);
                AssertColor(overlay.style.backgroundColor.value, new Color(0f, 0f, 0f, 0.5f));
                Assert.That(overlay.style.alignItems.value, Is.EqualTo(Align.Center));

                var panel = overlay[0];
                AssertColor(panel.style.backgroundColor.value, new Color(0.15f, 0.15f, 0.15f, 1f));
                Assert.That(panel.style.paddingTop.value.value, Is.EqualTo(20f));
                Assert.That(panel.style.paddingLeft.value.value, Is.EqualTo(30f));
                Assert.That(panel.style.borderTopLeftRadius.value.value, Is.EqualTo(8f));
                Assert.That(panel.style.minWidth.value.value, Is.EqualTo(300f));
                Assert.That(panel.style.maxWidth.value.value, Is.EqualTo(500f));

                var label = panel.Q<Label>();
                Assert.That(label.style.fontSize.value.value, Is.EqualTo(16f));
                Assert.That(label.style.marginBottom.value.value, Is.EqualTo(20f));

                var cancel  = panel.Q<Button>(className: CuvaraTheme.ConfirmDialogCancelUssClassName);
                var confirm = panel.Q<Button>(className: CuvaraTheme.ConfirmDialogConfirmUssClassName);
                Assert.That(cancel.style.marginRight.value.value, Is.EqualTo(10f));
                AssertColor(confirm.style.backgroundColor.value, new Color(0.2f, 0.5f, 0.9f, 1f));
                AssertColor(confirm.style.color.value, Color.white);

                cts.Cancel();
                await pending;
            }
        });

        [Test]
        public void Fallback_ServiceWithoutBaseSheet_DoesNotMarkRoot_SoComponentsStillFallBack()
        {
            using (CuvaraTheme.OverrideDefaultStyleSheet(null))
            {
                new CuvaraThemeService().Apply(this.root);
                Assert.That(this.root.ClassListContains(CuvaraTheme.ThemeRootUssClassName), Is.False);

                using var service = new ToastService(this.root);
                service.Show("x", 30f);

                Assert.That(this.root.Q("toast-container")[0].style.backgroundColor.keyword, Is.Not.EqualTo(StyleKeyword.Null),
                    "an unmarked root must leave the component on its inline fallback");
            }
        }

        #endregion

        #region Theme service

        [Test]
        public void Apply_Twice_AttachesBaseOnce_AndMarksRoot()
        {
            var service = new CuvaraThemeService();
            service.Apply(this.root);
            service.Apply(this.root);

            Assert.That(this.root.styleSheets.count, Is.EqualTo(1));
            Assert.That(this.root.styleSheets[0], Is.SameAs(CuvaraTheme.DefaultStyleSheet));
            Assert.That(this.root.ClassListContains(CuvaraTheme.ThemeRootUssClassName), Is.True);
        }

        [Test]
        public void Sheets_AreOrdered_BaseThenOverridesThenMode_AndAddedOnce()
        {
            var service = new CuvaraThemeService();
            service.AddOverride(this.overrideA);
            service.Apply(this.root);
            service.SetModeSheet(this.modeLight);
            service.AddOverride(this.overrideB);  // after Apply: synced into the applied root
            service.AddOverride(this.overrideA);  // duplicate: no-op
            service.Apply(this.root);

            AssertSheets(CuvaraTheme.DefaultStyleSheet, this.overrideA, this.overrideB, this.modeLight);
        }

        [Test]
        public void SetModeSheet_ReplacesPreviousMode_AndNullRemovesIt()
        {
            var service = new CuvaraThemeService();
            service.Apply(this.root);

            service.SetModeSheet(this.modeLight);
            service.SetModeSheet(this.modeDark);
            AssertSheets(CuvaraTheme.DefaultStyleSheet, this.modeDark);

            service.SetModeSheet(this.modeDark);
            AssertSheets(CuvaraTheme.DefaultStyleSheet, this.modeDark);

            service.SetModeSheet(null);
            AssertSheets(CuvaraTheme.DefaultStyleSheet);
        }

        [Test]
        public void Apply_KeepsHostSheets_AndPutsThemeSheetsLast()
        {
            this.root.styleSheets.Add(this.overrideB);
            var service = new CuvaraThemeService();
            service.AddOverride(this.overrideA);
            service.Apply(this.root);

            AssertSheets(this.overrideB, CuvaraTheme.DefaultStyleSheet, this.overrideA);
        }

        [Test]
        public void Apply_RemovesLocalCopiesOfComponentsCreatedEarlier()
        {
            var child = new VisualElement();
            this.root.Add(child);
            using var toasts = new ToastService(child);
            var container = child.Q("toast-container");
            Assert.That(container.styleSheets.Contains(CuvaraTheme.DefaultStyleSheet), Is.True, "precondition");

            new CuvaraThemeService().Apply(this.root);

            Assert.That(container.styleSheets.count, Is.Zero,
                "a local :root copy below the themed root would shadow every override");
            Assert.That(container.ClassListContains(CuvaraTheme.LocalThemeUssClassName), Is.False);
        }

        [Test]
        public void Apply_ToAComponentsOwnElement_KeepsTheBaseSheet()
        {
            using var toasts = new ToastService(this.root);
            var container = this.root.Q("toast-container");

            new CuvaraThemeService().Apply(container);

            Assert.That(container.styleSheets.Contains(CuvaraTheme.DefaultStyleSheet), Is.True);
            Assert.That(container.styleSheets.count, Is.EqualTo(1));
        }

        [Test]
        public void ServiceArguments_AreValidated()
        {
            var service = new CuvaraThemeService();
            Assert.Throws<System.ArgumentNullException>(() => service.Apply(null));
            Assert.Throws<System.ArgumentNullException>(() => service.AddOverride(null));
        }

        #endregion

#if UNITY_EDITOR
        #region Defaults equal the pre-theme inline values

        private const string ThemeUssPath = "Packages/com.cuvara.uitoolkit/Runtime/Theme/Resources/CuvaraTheme.uss";

        [TestCase("--cuvara-color-surface",        0.15f, 0.15f, 0.15f, 1f)]
        [TestCase("--cuvara-color-surface-raised", 0.25f, 0.25f, 0.3f,  0.95f)]
        [TestCase("--cuvara-color-on-surface",     1f,    1f,    1f,    1f)]
        [TestCase("--cuvara-color-primary",        0.2f,  0.5f,  0.9f,  1f)]
        [TestCase("--cuvara-color-on-primary",     1f,    1f,    1f,    1f)]
        [TestCase("--cuvara-color-scrim",          0f,    0f,    0f,    0.5f)]
        [TestCase("--cuvara-color-toast-success",  0.2f,  0.7f,  0.3f,  0.95f)]
        [TestCase("--cuvara-color-toast-warning",  0.9f,  0.7f,  0.1f,  0.95f)]
        [TestCase("--cuvara-color-toast-error",    0.9f,  0.25f, 0.2f,  0.95f)]
        public void UssColourDefault_EqualsPreThemeInlineValue(string property, float r, float g, float b, float a)
        {
            var match = Regex.Match(ReadThemeUss(),
                Regex.Escape(property) + @"\s*:\s*rgba\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*\)");
            Assert.That(match.Success, Is.True, $"{property} must be declared as rgba(...) in CuvaraTheme.uss");

            AssertColor(
                new Color(Parse(match.Groups[1]) / 255f, Parse(match.Groups[2]) / 255f, Parse(match.Groups[3]) / 255f, Parse(match.Groups[4])),
                new Color(r, g, b, a));
        }

        [TestCase("--cuvara-space-xs", 8f)]
        [TestCase("--cuvara-space-sm", 10f)]
        [TestCase("--cuvara-space-md", 15f)]
        [TestCase("--cuvara-space-lg", 20f)]
        [TestCase("--cuvara-space-xl", 30f)]
        [TestCase("--cuvara-radius-sm", 6f)]
        [TestCase("--cuvara-radius-md", 8f)]
        [TestCase("--cuvara-font-size-sm", 14f)]
        [TestCase("--cuvara-font-size-md", 16f)]
        public void UssLengthDefault_EqualsPreThemeInlineValue(string property, float expected)
        {
            var match = Regex.Match(ReadThemeUss(), Regex.Escape(property) + @"\s*:\s*([\d.]+)px\s*;");
            Assert.That(match.Success, Is.True, $"{property} must be declared in px in CuvaraTheme.uss");
            Assert.That(Parse(match.Groups[1]), Is.EqualTo(expected));
        }

        private static string ReadThemeUss() => System.IO.File.ReadAllText(System.IO.Path.GetFullPath(ThemeUssPath));

        private static float Parse(Group group) => float.Parse(group.Value, CultureInfo.InvariantCulture);

        #endregion
#endif

        private void AssertSheets(params StyleSheet[] expected)
        {
            Assert.That(this.root.styleSheets.count, Is.EqualTo(expected.Length), "sheet count");
            for (var i = 0; i < expected.Length; ++i)
                Assert.That(this.root.styleSheets[i], Is.SameAs(expected[i]), $"sheet #{i}");
        }

        private static void AssertColor(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(Tolerance), "r");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(Tolerance), "g");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(Tolerance), "b");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(Tolerance), "a");
        }
    }
}
