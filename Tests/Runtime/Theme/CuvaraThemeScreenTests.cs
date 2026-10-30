namespace Cuvara.UIToolkit.Tests.Theme
{
    using System.Collections.Generic;
    using Cuvara.UIToolkit.Theme;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// <see cref="ICuvaraThemeService.Remove"/>, <see cref="ICuvaraThemeService.RegisterModeSheets"/>
    /// and <see cref="ICuvaraThemeService.AttachScreen"/>: imported screens that link their own
    /// mode sheet follow the service's mode, and roots can be released.
    /// </summary>
    public sealed class CuvaraThemeScreenTests
    {
        private StyleSheet baseSheet, tokens, light, dark, screenSheet;
        private CuvaraThemeService service;

        [SetUp]
        public void SetUp()
        {
            this.baseSheet   = ScriptableObject.CreateInstance<StyleSheet>();
            this.tokens      = ScriptableObject.CreateInstance<StyleSheet>();
            this.light       = ScriptableObject.CreateInstance<StyleSheet>();
            this.dark        = ScriptableObject.CreateInstance<StyleSheet>();
            this.screenSheet = ScriptableObject.CreateInstance<StyleSheet>();
            this.service     = new CuvaraThemeService(this.baseSheet);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(this.baseSheet);
            Object.DestroyImmediate(this.tokens);
            Object.DestroyImmediate(this.light);
            Object.DestroyImmediate(this.dark);
            Object.DestroyImmediate(this.screenSheet);
        }

        /// <summary>What the connector's CloneTree leaves on a screen: tokens, mode, screen sheet.</summary>
        private VisualElement ImportedScreen()
        {
            var screen = new VisualElement();
            screen.styleSheets.Add(this.tokens);
            screen.styleSheets.Add(this.light);
            screen.styleSheets.Add(this.screenSheet);
            return screen;
        }

        private static List<StyleSheet> Sheets(VisualElement element)
        {
            var list = new List<StyleSheet>();
            for (var i = 0; i < element.styleSheets.count; i++) list.Add(element.styleSheets[i]);
            return list;
        }

        [Test]
        public void AttachScreen_SwitchesTheScreensOwnModeSheet_InPlace()
        {
            this.service.RegisterModeSheets(this.light, this.dark);
            var screen = this.ImportedScreen();
            this.service.AttachScreen(screen);

            this.service.SetModeSheet(this.dark);

            Assert.That(Sheets(screen), Is.EqualTo(new[] { this.tokens, this.dark, this.screenSheet }));

            this.service.SetModeSheet(this.light);

            Assert.That(Sheets(screen), Is.EqualTo(new[] { this.tokens, this.light, this.screenSheet }));
        }

        [Test]
        public void AttachScreen_AppliesTheCurrentMode_WhenAttachedAfterTheSwitch()
        {
            this.service.RegisterModeSheets(this.light, this.dark);
            this.service.SetModeSheet(this.dark);

            var screen = this.ImportedScreen();
            this.service.AttachScreen(screen);

            Assert.That(Sheets(screen), Is.EqualTo(new[] { this.tokens, this.dark, this.screenSheet }));
        }

        [Test]
        public void AttachScreen_ReachesNestedElements()
        {
            this.service.RegisterModeSheets(this.light, this.dark);
            var screen = new VisualElement();
            var nested = this.ImportedScreen();
            screen.Add(nested);
            this.service.AttachScreen(screen);

            this.service.SetModeSheet(this.dark);

            Assert.That(Sheets(nested), Is.EqualTo(new[] { this.tokens, this.dark, this.screenSheet }));
        }

        [Test]
        public void AttachScreen_LeavesAScreenWithoutAModeSheetAlone()
        {
            this.service.RegisterModeSheets(this.light, this.dark);
            var screen = new VisualElement();
            screen.styleSheets.Add(this.tokens);
            this.service.AttachScreen(screen);

            this.service.SetModeSheet(this.dark);

            Assert.That(Sheets(screen), Is.EqualTo(new[] { this.tokens }));
        }

        [Test]
        public void AttachScreen_WithoutRegisteredModes_ChangesNothing()
        {
            var screen = this.ImportedScreen();
            this.service.AttachScreen(screen);

            Assert.That(Sheets(screen), Is.EqualTo(new[] { this.tokens, this.light, this.screenSheet }));
        }

        [Test]
        public void SetModeSheet_RegistersTheSheet_SoALaterScreenLinkingItIsSwitched()
        {
            this.service.SetModeSheet(this.light);
            this.service.SetModeSheet(this.dark);

            var screen = this.ImportedScreen();
            this.service.AttachScreen(screen);

            Assert.That(Sheets(screen), Is.EqualTo(new[] { this.tokens, this.dark, this.screenSheet }));
        }

        [Test]
        public void SetModeSheet_Null_LeavesAttachedScreensOnTheirLastMode()
        {
            this.service.RegisterModeSheets(this.light, this.dark);
            var screen = this.ImportedScreen();
            this.service.AttachScreen(screen);
            this.service.SetModeSheet(this.dark);

            this.service.SetModeSheet(null);

            Assert.That(Sheets(screen), Is.EqualTo(new[] { this.tokens, this.dark, this.screenSheet }));
        }

        [Test]
        public void AttachScreen_BeforeAnyModeIsSet_KeepsTheScreensOwnModeSheet()
        {
            this.service.RegisterModeSheets(this.light, this.dark);
            var screen = this.ImportedScreen();
            this.service.AttachScreen(screen);

            Assert.That(Sheets(screen), Is.EqualTo(new[] { this.tokens, this.light, this.screenSheet }));
        }

        [Test]
        public void DisposedAttachment_StopsFollowingTheMode()
        {
            this.service.RegisterModeSheets(this.light, this.dark);
            var screen = this.ImportedScreen();
            var attachment = this.service.AttachScreen(screen);

            attachment.Dispose();
            attachment.Dispose();
            this.service.SetModeSheet(this.dark);

            Assert.That(Sheets(screen), Is.EqualTo(new[] { this.tokens, this.light, this.screenSheet }));
        }

        [Test]
        public void Remove_DetachesTheServicesSheets_AndStopsSyncing()
        {
            var root = new VisualElement();
            var own = ScriptableObject.CreateInstance<StyleSheet>();
            try
            {
                root.styleSheets.Add(own);
                this.service.AddOverride(this.tokens);
                this.service.Apply(root);
                this.service.SetModeSheet(this.light);

                this.service.Remove(root);
                this.service.SetModeSheet(this.dark);

                Assert.That(Sheets(root), Is.EqualTo(new[] { own }));
                Assert.That(root.ClassListContains(CuvaraTheme.ThemeRootUssClassName), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(own);
            }
        }

        [Test]
        public void Remove_UnknownRoot_IsIgnored()
        {
            var root = new VisualElement();

            Assert.DoesNotThrow(() => this.service.Remove(root));
            Assert.That(root.styleSheets.count, Is.EqualTo(0));
        }
    }
}
