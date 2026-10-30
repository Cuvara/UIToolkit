namespace Cuvara.UIToolkit.Editor.Figma.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;

    /// <summary>The Unity-free half of the mapping preset: name matching, idempotence, the state file.</summary>
    public class FigmaMappingPresetTests
    {
        [Test]
        public void NormalizeName_IgnoresPunctuationSpacingAndCase()
        {
            Assert.That(FigmaMappingPreset.NormalizeName("Safe Area"), Is.EqualTo("safearea"));
            Assert.That(FigmaMappingPreset.NormalizeName("safe-area"), Is.EqualTo("safearea"));
            Assert.That(FigmaMappingPreset.NormalizeName("SafeArea"), Is.EqualTo("safearea"));
            Assert.That(FigmaMappingPreset.NormalizeName(null), Is.Empty);
        }

        [Test]
        public void CuvaraRows_MapSafeAreaToTheFullyQualifiedElement()
        {
            var row = FigmaMappingPreset.CuvaraRows.Single(r => r.FigmaName == "SafeArea");
            Assert.That(row.UitkControl, Is.EqualTo("Cuvara.UIToolkit.Utilities.SafeAreaElement"));
        }

        [Test]
        public void MissingRows_AddsEveryPresetRow_ToAnEmptyMap()
        {
            var missing = FigmaMappingPreset.MissingRows(new string[0], FigmaMappingPreset.CuvaraRows);
            Assert.That(missing, Has.Count.EqualTo(FigmaMappingPreset.CuvaraRows.Count));
        }

        [Test]
        public void MissingRows_IsIdempotent()
        {
            var names = new List<string> { "Button" };
            names.AddRange(FigmaMappingPreset.MissingRows(names, FigmaMappingPreset.CuvaraRows).Select(r => r.FigmaName));

            Assert.That(FigmaMappingPreset.MissingRows(names, FigmaMappingPreset.CuvaraRows), Is.Empty);
        }

        [Test]
        public void MissingRows_NeverOverridesAUserRowForTheSameName()
        {
            // The user mapped "Safe Area" to something else; the preset must leave it alone.
            Assert.That(FigmaMappingPreset.MissingRows(new[] { "Safe Area" }, FigmaMappingPreset.CuvaraRows), Is.Empty);
        }

        [Test]
        public void MissingRows_DropsDuplicatesWithinThePreset()
        {
            var preset = new[]
            {
                new FigmaControlMapRow("Card", "A.Card"),
                new FigmaControlMapRow("card", "B.Card"),
            };

            Assert.That(FigmaMappingPreset.MissingRows(null, preset).Select(r => r.UitkControl), Is.EqualTo(new[] { "A.Card" }));
        }

        [Test]
        public void ParseMappingState_ReadsTheThreeStates()
        {
            const string selected =
                "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &1\nMonoBehaviour:\n  m_Name: \n  m_ActiveMappingGuid: 0123456789abcdef0123456789abcdef\n";
            Assert.That(FigmaMappingPreset.ParseMappingState(selected, out var guid), Is.EqualTo(FigmaMappingSelection.Selected));
            Assert.That(guid, Is.EqualTo("0123456789abcdef0123456789abcdef"));

            Assert.That(FigmaMappingPreset.ParseMappingState("  m_ActiveMappingGuid: -\n", out guid), Is.EqualTo(FigmaMappingSelection.Cleared));
            Assert.That(guid, Is.Empty);

            Assert.That(FigmaMappingPreset.ParseMappingState("  m_ActiveMappingGuid: \n", out _), Is.EqualTo(FigmaMappingSelection.Unset));
            Assert.That(FigmaMappingPreset.ParseMappingState("  m_Other: 1\n", out _), Is.EqualTo(FigmaMappingSelection.Unset));
            Assert.That(FigmaMappingPreset.ParseMappingState(null, out _), Is.EqualTo(FigmaMappingSelection.Unset));
        }

        [Test]
        public void ParseMappingState_HandlesCrLfAndQuotes()
        {
            Assert.That(FigmaMappingPreset.ParseMappingState("a: 1\r\n  m_ActiveMappingGuid: '-'\r\n", out _), Is.EqualTo(FigmaMappingSelection.Cleared));
        }

        [Test]
        public void UnresolvedControlTypes_ChecksOnlyTypedNames()
        {
            var known = new HashSet<string> { "Cuvara.UIToolkit.Utilities.SafeAreaElement" };
            var unresolved = FigmaMappingPreset.UnresolvedControlTypes(
                new[] { "Button", "", null, "Cuvara.UIToolkit.Utilities.SafeAreaElement", "Game.Missing", "Game.Missing" },
                known);

            Assert.That(unresolved, Is.EqualTo(new[] { "Game.Missing" }));
        }
    }
}
