namespace Cuvara.UIToolkit.Editor.Figma.Tests
{
    using System;
    using System.Linq;
    using Cuvara.UIToolkit.FigmaCore;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using Object = UnityEngine.Object;

    /// <summary>
    /// The preset against the connector's real <c>FigmaImportSettings</c> type, opened through
    /// <c>SerializedObject</c> exactly as the command does. In-memory instances only: no asset is
    /// created and the project's mapping is never touched.
    /// </summary>
    public class FigmaMappingPresetSerializedTests
    {
        private const string SettingsTypeName = "Unity.UIToolkit.Figma.FigmaImportSettings";

        private Object settings;

        [SetUp]
        public void SetUp()
        {
            var type = TypeCache.GetTypesDerivedFrom<ScriptableObject>().FirstOrDefault(t => t.FullName == SettingsTypeName);
            Assert.That(type, Is.Not.Null, $"{SettingsTypeName} not found: the connector is not installed at the tested version.");
            this.settings = ScriptableObject.CreateInstance(type);
        }

        [TearDown]
        public void TearDown()
        {
            if (this.settings != null) Object.DestroyImmediate(this.settings);
        }

        private static SerializedProperty Rows(SerializedObject serialized)
        {
            return serialized.FindProperty(FigmaMappingPreset.PropertyControlMap);
        }

        private static string RowValue(SerializedObject serialized, int index, string relative)
        {
            return Rows(serialized).GetArrayElementAtIndex(index).FindPropertyRelative(relative).stringValue;
        }

        private void AddUserRow(string figmaName, string control, int bindingCount)
        {
            var serialized = new SerializedObject(this.settings);
            var rows = Rows(serialized);
            rows.InsertArrayElementAtIndex(rows.arraySize);
            var row = rows.GetArrayElementAtIndex(rows.arraySize - 1);
            row.FindPropertyRelative(FigmaMappingPreset.PropertyFigmaName).stringValue = figmaName;
            row.FindPropertyRelative(FigmaMappingPreset.PropertyUitkControl).stringValue = control;
            row.FindPropertyRelative(FigmaMappingPreset.PropertyBindings).arraySize = bindingCount;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void TheTestedConnector_HasEveryPropertyThePresetEdits()
        {
            var serialized = new SerializedObject(this.settings);
            Assert.That(serialized.FindProperty(FigmaMappingPreset.PropertyClassPrefix), Is.Not.Null);
            Assert.That(Rows(serialized), Is.Not.Null);
            Assert.That(Rows(serialized).isArray, Is.True);
        }

        [Test]
        public void ApplyTo_AddsTheCuvaraRows_ThenNothingTheSecondTime()
        {
            var first = FigmaMappingPresetCommand.ApplyTo(new SerializedObject(this.settings), "mem", "", "P", out var added);
            Assert.That(added, Is.EqualTo(FigmaMappingPreset.CuvaraRows.Count));
            Assert.That(first.Where(d => d.Severity == FigmaDiagnosticSeverity.Error), Is.Empty);

            var second = FigmaMappingPresetCommand.ApplyTo(new SerializedObject(this.settings), "mem", "", "P", out var addedAgain);
            Assert.That(addedAgain, Is.Zero);
            Assert.That(second, Is.Empty);

            var serialized = new SerializedObject(this.settings);
            Assert.That(Rows(serialized).arraySize, Is.EqualTo(FigmaMappingPreset.CuvaraRows.Count));
            Assert.That(RowValue(serialized, 0, FigmaMappingPreset.PropertyUitkControl), Is.EqualTo("Cuvara.UIToolkit.Utilities.SafeAreaElement"));
        }

        [Test]
        public void ApplyTo_KeepsUserRows_AndClearsWhatTheInsertCopied()
        {
            this.AddUserRow("PrimaryButton", "Button", bindingCount: 2);

            FigmaMappingPresetCommand.ApplyTo(new SerializedObject(this.settings), "mem", "", "P", out var added);
            Assert.That(added, Is.EqualTo(1));

            var serialized = new SerializedObject(this.settings);
            Assert.That(Rows(serialized).arraySize, Is.EqualTo(2));
            Assert.That(RowValue(serialized, 0, FigmaMappingPreset.PropertyFigmaName), Is.EqualTo("PrimaryButton"));
            Assert.That(RowValue(serialized, 0, FigmaMappingPreset.PropertyUitkControl), Is.EqualTo("Button"));
            Assert.That(Rows(serialized).GetArrayElementAtIndex(0).FindPropertyRelative(FigmaMappingPreset.PropertyBindings).arraySize, Is.EqualTo(2));

            // InsertArrayElementAtIndex copies the previous row; its bindings must not leak into ours.
            Assert.That(RowValue(serialized, 1, FigmaMappingPreset.PropertyFigmaName), Is.EqualTo("SafeArea"));
            Assert.That(Rows(serialized).GetArrayElementAtIndex(1).FindPropertyRelative(FigmaMappingPreset.PropertyBindings).arraySize, Is.Zero);
        }

        [Test]
        public void ApplyTo_LeavesAUserMappingOfTheSameNameAlone()
        {
            this.AddUserRow("Safe Area", "VisualElement", bindingCount: 0);

            FigmaMappingPresetCommand.ApplyTo(new SerializedObject(this.settings), "mem", "", "P", out var added);

            var serialized = new SerializedObject(this.settings);
            Assert.That(added, Is.Zero);
            Assert.That(Rows(serialized).arraySize, Is.EqualTo(1));
            Assert.That(RowValue(serialized, 0, FigmaMappingPreset.PropertyUitkControl), Is.EqualTo("VisualElement"));
        }

        [Test]
        public void ApplyTo_SetsTheClassPrefixOnlyWhenEmpty()
        {
            FigmaMappingPresetCommand.ApplyTo(new SerializedObject(this.settings), "mem", "rpg", "P", out _);
            Assert.That(new SerializedObject(this.settings).FindProperty(FigmaMappingPreset.PropertyClassPrefix).stringValue, Is.EqualTo("rpg"));

            var diagnostics = FigmaMappingPresetCommand.ApplyTo(new SerializedObject(this.settings), "mem", "shop", "Other", out _);
            Assert.That(new SerializedObject(this.settings).FindProperty(FigmaMappingPreset.PropertyClassPrefix).stringValue, Is.EqualTo("rpg"));
            Assert.That(diagnostics.Select(d => d.Code), Does.Contain(FigmaMappingPresetCommand.CodePrefixDiffers));
        }

        [Test]
        public void ApplyTo_AnObjectWithoutTheProperties_IsADiagnostic_NotAnException()
        {
            var notAMapping = ScriptableObject.CreateInstance<FigmaProfile>();
            try
            {
                var diagnostics = FigmaMappingPresetCommand.ApplyTo(new SerializedObject(notAMapping), "mem", "rpg", "P", out var added);
                Assert.That(added, Is.Zero);
                Assert.That(diagnostics.Select(d => d.Code), Is.EqualTo(new[] { FigmaMappingPresetCommand.CodePropertyMissing }));
                Assert.That(diagnostics[0].Severity, Is.EqualTo(FigmaDiagnosticSeverity.Error));
            }
            finally
            {
                Object.DestroyImmediate(notAMapping);
            }
        }

        [Test]
        public void ValidateControlTypes_ResolvesCuvaraControls_AndFlagsUnknownTypes()
        {
            this.AddUserRow("Missing", "Game.DoesNotExist.Widget", bindingCount: 0);
            FigmaMappingPresetCommand.ApplyTo(new SerializedObject(this.settings), "mem", "", "P", out _);

            var diagnostics = FigmaMappingPresetCommand.ValidateControlTypes(new FigmaMappingAssetInfo { Asset = this.settings, Path = "mem" });

            var unresolved = diagnostics.Where(d => d.Code == FigmaMappingPresetCommand.CodeControlUnresolved).ToArray();
            Assert.That(unresolved, Has.Length.EqualTo(1));
            Assert.That(unresolved[0].Message, Does.Contain("Game.DoesNotExist.Widget"));
            Assert.That(diagnostics.Any(d => d.Message.Contains("SafeAreaElement")), Is.False,
                "SafeAreaElement is an [UxmlElement] VisualElement and must resolve cleanly");
        }

        [Test]
        public void SettingsScriptGuid_MatchesTheInstalledConnector()
        {
            var path = AssetDatabase.GUIDToAssetPath(FigmaMappingPreset.SettingsScriptGuid);
            Assert.That(path, Does.EndWith("FigmaImportSettings.cs"), "the connector's script GUID moved; update FigmaMappingPreset.SettingsScriptGuid");
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            Assert.That(script.GetClass(), Is.EqualTo(this.settings.GetType()));
        }
    }
}
