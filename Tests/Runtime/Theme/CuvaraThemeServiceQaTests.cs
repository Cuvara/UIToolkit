namespace Cuvara.UIToolkit.Tests.Theme
{
    using Cuvara.UIToolkit.Theme;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// Release QA for <see cref="CuvaraThemeService"/>: sheet order across several roots and late
    /// roots, mode switches that repeat or alias another sheet, a service without a base sheet,
    /// and the default-sheet override used by the fallback tests.
    /// </summary>
    /// <remarks>Detached elements, as in <see cref="CuvaraThemeTests"/>: only sheet lists and
    /// class lists are asserted.</remarks>
    public sealed class CuvaraThemeServiceQaTests
    {
        private VisualElement rootA, rootB;
        private StyleSheet baseSheet, overrideA, overrideB, modeLight, modeDark, host;

        [SetUp]
        public void SetUp()
        {
            this.rootA     = new VisualElement();
            this.rootB     = new VisualElement();
            this.baseSheet = ScriptableObject.CreateInstance<StyleSheet>();
            this.overrideA = ScriptableObject.CreateInstance<StyleSheet>();
            this.overrideB = ScriptableObject.CreateInstance<StyleSheet>();
            this.modeLight = ScriptableObject.CreateInstance<StyleSheet>();
            this.modeDark  = ScriptableObject.CreateInstance<StyleSheet>();
            this.host      = ScriptableObject.CreateInstance<StyleSheet>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(this.baseSheet);
            Object.DestroyImmediate(this.overrideA);
            Object.DestroyImmediate(this.overrideB);
            Object.DestroyImmediate(this.modeLight);
            Object.DestroyImmediate(this.modeDark);
            Object.DestroyImmediate(this.host);
        }

        #region Order across roots

        [Test]
        public void EveryAppliedRoot_GetsTheSameStack_InTheSameOrder()
        {
            var service = new CuvaraThemeService(this.baseSheet);
            service.Apply(this.rootA);
            service.AddOverride(this.overrideA);
            service.SetModeSheet(this.modeLight);
            service.Apply(this.rootB);
            service.AddOverride(this.overrideB);

            AssertSheets(this.rootA, this.baseSheet, this.overrideA, this.overrideB, this.modeLight);
            AssertSheets(this.rootB, this.baseSheet, this.overrideA, this.overrideB, this.modeLight);
        }

        [Test]
        public void OverrideAddedAfterTheMode_GoesBeforeIt_SoTheModeStillWins()
        {
            var service = new CuvaraThemeService(this.baseSheet);
            service.Apply(this.rootA);
            service.SetModeSheet(this.modeDark);
            service.AddOverride(this.overrideA);
            service.AddOverride(this.overrideB);

            AssertSheets(this.rootA, this.baseSheet, this.overrideA, this.overrideB, this.modeDark);
        }

        [Test]
        public void ReApply_RestoresAThemeSheetTheHostRemoved_AndPutsItBackInOrder()
        {
            var service = new CuvaraThemeService(this.baseSheet);
            service.AddOverride(this.overrideA);
            service.SetModeSheet(this.modeLight);
            service.Apply(this.rootA);

            this.rootA.styleSheets.Remove(this.baseSheet);
            this.rootA.styleSheets.Add(this.host);
            service.Apply(this.rootA);

            AssertSheets(this.rootA, this.host, this.baseSheet, this.overrideA, this.modeLight);
        }

        [Test]
        public void ModeSwitch_KeepsHostSheets_AndTheirPosition()
        {
            this.rootA.styleSheets.Add(this.host);
            var service = new CuvaraThemeService(this.baseSheet);
            service.Apply(this.rootA);

            service.SetModeSheet(this.modeLight);
            service.SetModeSheet(this.modeDark);

            AssertSheets(this.rootA, this.host, this.baseSheet, this.modeDark);
        }

        #endregion

        #region Mode switching

        [Test]
        public void ModeSwitch_ReachesEveryRoot()
        {
            var service = new CuvaraThemeService(this.baseSheet);
            service.Apply(this.rootA);
            service.Apply(this.rootB);

            service.SetModeSheet(this.modeLight);
            service.SetModeSheet(this.modeDark);

            AssertSheets(this.rootA, this.baseSheet, this.modeDark);
            AssertSheets(this.rootB, this.baseSheet, this.modeDark);
        }

        [Test]
        public void ModeToggledRepeatedly_IsIdempotent()
        {
            var service = new CuvaraThemeService(this.baseSheet);
            service.Apply(this.rootA);

            for (var i = 0; i < 5; i++)
            {
                service.SetModeSheet(this.modeLight);
                service.SetModeSheet(this.modeLight);
                service.SetModeSheet(this.modeDark);
                service.Apply(this.rootA);
            }

            service.SetModeSheet(this.modeLight);

            AssertSheets(this.rootA, this.baseSheet, this.modeLight);
            Assert.That(service.ModeSheet, Is.SameAs(this.modeLight));
        }

        [Test]
        public void ClearingAModeThatWasNeverSet_ChangesNothing()
        {
            var service = new CuvaraThemeService(this.baseSheet);
            service.Apply(this.rootA);

            service.SetModeSheet(null);

            AssertSheets(this.rootA, this.baseSheet);
            Assert.That(service.ModeSheet, Is.Null);
        }

        [Test]
        public void ModeSetBeforeAnyRoot_ReachesALaterRoot()
        {
            var service = new CuvaraThemeService(this.baseSheet);
            service.SetModeSheet(this.modeDark);
            service.SetModeSheet(null);
            service.SetModeSheet(this.modeLight);

            service.Apply(this.rootA);

            AssertSheets(this.rootA, this.baseSheet, this.modeLight);
        }

        [Test]
        public void ModeThatIsAlsoAnOverride_IsAttachedOnce_AndSurvivesTheNextSwitch()
        {
            var service = new CuvaraThemeService(this.baseSheet);
            service.AddOverride(this.overrideA);
            service.Apply(this.rootA);

            service.SetModeSheet(this.overrideA);
            AssertSheets(this.rootA, this.baseSheet, this.overrideA);

            service.SetModeSheet(this.modeDark);
            AssertSheets(this.rootA, this.baseSheet, this.overrideA, this.modeDark);

            service.SetModeSheet(null);
            AssertSheets(this.rootA, this.baseSheet, this.overrideA);
        }

        [Test]
        public void ModeThatIsTheBaseSheet_IsAttachedOnce_AndClearingItKeepsTheBase()
        {
            var service = new CuvaraThemeService(this.baseSheet);
            service.Apply(this.rootA);

            service.SetModeSheet(this.baseSheet);
            AssertSheets(this.rootA, this.baseSheet);

            service.SetModeSheet(null);
            AssertSheets(this.rootA, this.baseSheet);
            Assert.That(this.rootA.ClassListContains(CuvaraTheme.ThemeRootUssClassName), Is.True);
        }

        [Test]
        public void ModeLaterAddedAsAnOverride_StaysWhenTheModeIsCleared()
        {
            var service = new CuvaraThemeService(this.baseSheet);
            service.Apply(this.rootA);
            service.SetModeSheet(this.modeLight);
            service.AddOverride(this.modeLight);

            service.SetModeSheet(null);

            AssertSheets(this.rootA, this.baseSheet, this.modeLight);
            Assert.That(service.Overrides, Is.EqualTo(new[] { this.modeLight }));
        }

        #endregion

        #region Base sheet

        [Test]
        public void ExplicitBaseSheet_IsUsedInsteadOfTheDefault_AndMarksTheRoot()
        {
            var service = new CuvaraThemeService(this.baseSheet);
            service.Apply(this.rootA);

            Assert.That(service.BaseSheet, Is.SameAs(this.baseSheet));
            AssertSheets(this.rootA, this.baseSheet);
            Assert.That(this.rootA.ClassListContains(CuvaraTheme.ThemeRootUssClassName), Is.True);
        }

        [Test]
        public void NullBaseSheet_AttachesOverridesAndMode_ButNeverMarksTheRoot()
        {
            var service = new CuvaraThemeService(null);
            service.Apply(this.rootA);
            service.AddOverride(this.overrideA);
            service.SetModeSheet(this.modeDark);

            AssertSheets(this.rootA, this.overrideA, this.modeDark);
            Assert.That(this.rootA.ClassListContains(CuvaraTheme.ThemeRootUssClassName), Is.False,
                "an unmarked root keeps built-in components on their inline fallback");
        }

        [Test]
        public void OverrideThatIsTheBaseSheet_IsNotDuplicated()
        {
            var service = new CuvaraThemeService(this.baseSheet);
            service.Apply(this.rootA);
            service.AddOverride(this.baseSheet);
            service.AddOverride(this.overrideA);

            Assert.That(this.rootA.styleSheets.count, Is.EqualTo(2));
            Assert.That(this.rootA.styleSheets[this.rootA.styleSheets.count - 1], Is.SameAs(this.overrideA));
        }

        #endregion

        #region Default sheet override

        [Test]
        public void OverrideDefaultStyleSheet_Nests_AndRestoresInReverse()
        {
            var original = CuvaraTheme.DefaultStyleSheet;

            using (CuvaraTheme.OverrideDefaultStyleSheet(null))
            {
                Assert.That(CuvaraTheme.DefaultStyleSheet, Is.Null);
                using (CuvaraTheme.OverrideDefaultStyleSheet(this.baseSheet))
                {
                    Assert.That(CuvaraTheme.DefaultStyleSheet, Is.SameAs(this.baseSheet));
                }

                Assert.That(CuvaraTheme.DefaultStyleSheet, Is.Null);
            }

            Assert.That(CuvaraTheme.DefaultStyleSheet, Is.SameAs(original));
        }

        [Test]
        public void OverrideDefaultStyleSheet_DisposedTwice_RestoresOnce()
        {
            var original = CuvaraTheme.DefaultStyleSheet;
            var outer = CuvaraTheme.OverrideDefaultStyleSheet(this.baseSheet);
            var inner = CuvaraTheme.OverrideDefaultStyleSheet(this.overrideA);

            inner.Dispose();
            inner.Dispose();
            Assert.That(CuvaraTheme.DefaultStyleSheet, Is.SameAs(this.baseSheet), "a second Dispose must not restore anything again");

            outer.Dispose();
            Assert.That(CuvaraTheme.DefaultStyleSheet, Is.SameAs(original));
        }

        [Test]
        public void DefaultConstructor_ReadsTheDefaultSheetAtConstruction()
        {
            CuvaraThemeService service;
            using (CuvaraTheme.OverrideDefaultStyleSheet(this.baseSheet))
            {
                service = new CuvaraThemeService();
            }

            service.Apply(this.rootA);

            Assert.That(service.BaseSheet, Is.SameAs(this.baseSheet));
            AssertSheets(this.rootA, this.baseSheet);
        }

        #endregion

        private static void AssertSheets(VisualElement root, params StyleSheet[] expected)
        {
            Assert.That(root.styleSheets.count, Is.EqualTo(expected.Length), "sheet count");
            for (var i = 0; i < expected.Length; ++i)
                Assert.That(root.styleSheets[i], Is.SameAs(expected[i]), $"sheet #{i}");
        }
    }
}
