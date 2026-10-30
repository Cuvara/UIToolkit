namespace Cuvara.UIToolkit.Editor.Figma.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Cuvara.UIToolkit.FigmaCore;
    using NUnit.Framework;
    using UnityEngine;
    using Object = UnityEngine.Object;

    /// <summary>
    /// A profile's mode sheets name files inside its connector folder. One that escapes it
    /// (<c>..</c>, rooted, drive) is reported and never read, so a foreign stylesheet cannot
    /// pass its custom properties off as Figma tokens.
    /// </summary>
    public class FigmaProfileValidatorTests
    {
        private string root;
        private string connector;
        private FigmaProfile profile;

        [SetUp]
        public void SetUp()
        {
            // Relative to the project (the working directory), like every profile path. Temp/ is never imported.
            this.root = "Temp/cuvara-modesheet-" + Guid.NewGuid().ToString("N");
            this.connector = this.root + "/Figma/Game";
            Directory.CreateDirectory(this.connector);
            File.WriteAllText(this.connector + "/tokens.uss", ":root { --from-import: 1px; }");
            File.WriteAllText(this.connector + "/dark.uss", ":root { --from-mode: 2px; }");
            File.WriteAllText(this.root + "/Other.uss", ":root { --foreign: 3px; }");

            this.profile = ScriptableObject.CreateInstance<FigmaProfile>();
            this.profile.ConnectorFolder = this.connector;
        }

        [TearDown]
        public void TearDown()
        {
            if (this.profile != null) Object.DestroyImmediate(this.profile);
            if (Directory.Exists(this.root)) Directory.Delete(this.root, recursive: true);
        }

        [Test]
        public void CollectKnownTokens_NeverReadsAModeSheetOutsideTheConnectorFolder()
        {
            this.profile.ModeSheets = new List<string> { "dark.uss", "../../Other.uss", "/" + this.root + "/Other.uss" };

            var known = FigmaProfileValidator.CollectKnownTokens(this.profile, null);

            Assert.That(known, Is.EquivalentTo(new[] { "--from-import", "--from-mode" }));
        }

        [Test]
        public void Validate_ReportsEscapingModeSheets_AndMissingOnesSeparately()
        {
            this.profile.ModeSheets = new List<string> { "dark.uss", "light.uss", "../../Other.uss", "C:/Other.uss" };

            var report = FigmaProfileValidator.Validate(this.profile, new[] { this.profile });

            var invalid = report.Diagnostics.Where(d => d.Code == FigmaProfileValidator.CodeModeSheetInvalid).ToList();
            Assert.That(invalid.Count, Is.EqualTo(2));
            Assert.That(invalid.All(d => d.Severity == FigmaDiagnosticSeverity.Error), Is.True);
            Assert.That(invalid.Select(d => d.Message).Any(m => m.Contains("../../Other.uss")), Is.True);

            var missing = report.Diagnostics.Where(d => d.Code == FigmaProfileValidator.CodeModeSheetMissing).ToList();
            Assert.That(missing.Count, Is.EqualTo(1));
            Assert.That(missing[0].Message, Does.Contain("light.uss"));
        }
    }
}
