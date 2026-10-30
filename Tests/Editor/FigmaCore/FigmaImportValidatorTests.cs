namespace Cuvara.UIToolkit.Editor.Tests.FigmaCore
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Cuvara.UIToolkit.FigmaCore;
    using NUnit.Framework;

    /// <summary>
    /// <see cref="FigmaImportValidator"/>, one rule per test, on folders written to a temp
    /// directory shaped like connector 0.3.0-exp.1 output
    /// (<c>&lt;project&gt;/Assets/Figma/&lt;file&gt;/</c>). No Unity asset is involved.
    /// </summary>
    public class FigmaImportValidatorTests
    {
        private const string Tokens = ":root {\n    --surface: rgba(18, 20, 28, 1);\n    --text: rgba(240, 240, 245, 1);\n    --gap: 16px;\n}\n";

        private const string CleanScreen = @"<ui:UXML xmlns:ui=""UnityEngine.UIElements"">
    <Style src=""tokens.uss"" />
    <Style src=""Screen.uss"" />
    <ui:VisualElement style=""flex-grow: 1;"">
        <ui:Button text=""Buy"" class=""rpg-button unity-button"" />
    </ui:VisualElement>
</ui:UXML>
";

        private const string CleanScreenUss = ".rpg-button {\n    color: var(--text);\n    margin-top: var(--gap);\n}\n";

        private string _project;
        private string _folder;

        [SetUp]
        public void SetUp()
        {
            _project = Path.Combine(Path.GetTempPath(), "cuvara-figma-validator-" + Guid.NewGuid().ToString("N"));
            _folder = Path.Combine(_project, "Assets", "Figma", "Shop");
            Directory.CreateDirectory(_folder);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_project)) Directory.Delete(_project, true);
        }

        private void Write(string relative, string content)
        {
            var path = Path.Combine(_folder, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
        }

        private static string Manifest(params string[] generated)
        {
            var list = string.Join(",", generated.Select(g => "\"" + g + "\""));
            return "{\"fileName\":\"Shop\",\"tokens\":[{\"name\":\"--surface\"},{\"name\":\"--text\"},{\"name\":\"--gap\"}],"
                + "\"screens\":[{\"assetName\":\"Screen\",\"rootNodeId\":\"1:2\",\"generated\":[" + list + "]}]}";
        }

        private void WriteCleanImport()
        {
            Write(".figma-manifest.json", Manifest("tokens.uss", "Screen.uss", "Screen.uxml"));
            Write("tokens.uss", Tokens);
            Write("Screen.uss", CleanScreenUss);
            Write("Screen.uxml", CleanScreen);
        }

        private IReadOnlyList<FigmaDiagnostic> Validate(FigmaValidationOptions options = null)
        {
            return FigmaImportValidator.ValidateFolder(_folder, options ?? new FigmaValidationOptions { ExpectedClassPrefix = "rpg" });
        }

        private static List<FigmaDiagnostic> WithCode(IEnumerable<FigmaDiagnostic> diagnostics, string code)
        {
            return diagnostics.Where(d => d.Code == code).ToList();
        }

        private static List<FigmaDiagnostic> Problems(IEnumerable<FigmaDiagnostic> diagnostics)
        {
            return diagnostics.Where(d => d.Severity != FigmaDiagnosticSeverity.Info).ToList();
        }

        [Test]
        public void CleanImport_HasNoErrorsOrWarnings()
        {
            WriteCleanImport();

            var diagnostics = Validate();

            Assert.That(Problems(diagnostics), Is.Empty, string.Join("\n", diagnostics));
            Assert.That(WithCode(diagnostics, "FIG021").Single().Message, Does.StartWith("0 literal"));
            Assert.That(WithCode(diagnostics, "FIG031").Single().Message, Is.EqualTo("1 inline-styled element(s), 0 absolute"));
        }

        [Test]
        public void MissingFolder_IsFig007()
        {
            var diagnostics = FigmaImportValidator.ValidateFolder(Path.Combine(_project, "nope"), null);

            Assert.That(diagnostics.Single().Code, Is.EqualTo("FIG007"));
            Assert.That(diagnostics.Single().Severity, Is.EqualTo(FigmaDiagnosticSeverity.Error));
        }

        [Test]
        public void MissingManifest_IsFig001Error()
        {
            WriteCleanImport();
            File.Delete(Path.Combine(_folder, ".figma-manifest.json"));

            var diagnostic = WithCode(Validate(), "FIG001").Single();

            Assert.That(diagnostic.Severity, Is.EqualTo(FigmaDiagnosticSeverity.Error));
            Assert.That(diagnostic.File, Is.Empty);
        }

        [Test]
        public void UnreadableManifest_IsFig002Error()
        {
            WriteCleanImport();
            Write(".figma-manifest.json", "{ broken");

            var diagnostic = WithCode(Validate(), "FIG002").Single();

            Assert.That(diagnostic.Severity, Is.EqualTo(FigmaDiagnosticSeverity.Error));
            Assert.That(diagnostic.File, Is.EqualTo(".figma-manifest.json"));
        }

        [Test]
        public void ScreenWithoutUxml_IsFig003Error_AndNotAlsoFig004()
        {
            WriteCleanImport();
            File.Delete(Path.Combine(_folder, "Screen.uxml"));

            var diagnostics = Validate();

            Assert.That(WithCode(diagnostics, "FIG003").Single().Severity, Is.EqualTo(FigmaDiagnosticSeverity.Error));
            Assert.That(WithCode(diagnostics, "FIG004"), Is.Empty);
        }

        [Test]
        public void MissingGeneratedFile_IsFig004Warning()
        {
            WriteCleanImport();
            Write(".figma-manifest.json", Manifest("tokens.uss", "Screen.uss", "Screen.uxml", "images/gone.png"));

            var diagnostic = WithCode(Validate(), "FIG004").Single();

            Assert.That(diagnostic.Severity, Is.EqualTo(FigmaDiagnosticSeverity.Warning));
            Assert.That(diagnostic.Message, Does.Contain("images/gone.png"));
        }

        [Test]
        public void MalformedUxml_IsFig005Error()
        {
            WriteCleanImport();
            Write("Broken.uxml", "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">\n<ui:Label>\n</ui:UXML>\n");

            var diagnostic = WithCode(Validate(), "FIG005").Single();

            Assert.That(diagnostic.File, Is.EqualTo("Broken.uxml"));
            Assert.That(diagnostic.Line, Is.EqualTo(3));
        }

        [Test]
        public void MalformedUss_IsFig006Warning()
        {
            WriteCleanImport();
            Write("Screen.uss", ".rpg-button { color: var(--text);");

            Assert.That(WithCode(Validate(), "FIG006").Single().Severity, Is.EqualTo(FigmaDiagnosticSeverity.Warning));
        }

        [Test]
        public void ClassWithoutPrefix_IsFig010Warning_OncePerClass()
        {
            WriteCleanImport();
            Write("Screen.uss", ".rpg-button { color: var(--text); }\n.card { width: 1px; }\n.card:hover { width: 2px; }\n.unity-button { width: 3px; }\n");

            var diagnostic = WithCode(Validate(), "FIG010").Single();

            Assert.That(diagnostic.Severity, Is.EqualTo(FigmaDiagnosticSeverity.Warning));
            Assert.That(diagnostic.Message, Does.Contain("'.card'"));
            Assert.That(diagnostic.Line, Is.EqualTo(2));
        }

        [Test]
        public void ClassPrefix_NotExpected_IsNotChecked()
        {
            WriteCleanImport();
            Write("Screen.uss", ".card { width: 1px; }\n");

            Assert.That(WithCode(Validate(new FigmaValidationOptions()), "FIG010"), Is.Empty);
        }

        [Test]
        public void LiteralColors_AreFig020PerOccurrence_WithFig021Summary()
        {
            WriteCleanImport();
            Write("Screen.uss", ".rpg-button {\n    color: white;\n    background-color: #ff0000;\n    border-left-color: var(--text, #000);\n}\n");
            Write("Screen.uxml", CleanScreen.Replace("flex-grow: 1;", "flex-grow: 1; background-color: rgba(1, 2, 3, 1);"));

            var diagnostics = Validate();
            var literals = WithCode(diagnostics, "FIG020");

            Assert.That(literals.Count, Is.EqualTo(3), string.Join("\n", diagnostics));
            Assert.That(literals.All(d => d.Severity == FigmaDiagnosticSeverity.Warning), Is.True);
            Assert.That(literals.Select(d => d.File + ":" + d.Line), Is.EqualTo(new[] { "Screen.uss:2", "Screen.uss:3", "Screen.uxml:4" }));
            Assert.That(WithCode(diagnostics, "FIG021").Single().Message, Does.StartWith("3 literal"));
        }

        [Test]
        public void LiteralColors_InTokenAndModeSheets_AreTheTokens_NotReported()
        {
            WriteCleanImport();
            Write("dark.uss", ":root {\n    --surface: rgba(0, 0, 0, 1);\n    --text: #fff;\n}\n");

            Assert.That(WithCode(Validate(), "FIG020"), Is.Empty);
        }

        [Test]
        public void LiteralColors_Disabled_NoFig020OrFig021()
        {
            WriteCleanImport();
            Write("Screen.uss", ".rpg-button { color: white; }\n");

            var diagnostics = Validate(new FigmaValidationOptions { RequireTokenForColors = false });

            Assert.That(WithCode(diagnostics, "FIG020"), Is.Empty);
            Assert.That(WithCode(diagnostics, "FIG021"), Is.Empty);
        }

        [Test]
        public void UndefinedToken_IsFig022Error()
        {
            WriteCleanImport();
            Write("Screen.uss", ".rpg-button {\n    color: var(--missing);\n    width: var(--also-missing, 4px);\n    height: var(--unity-metrics-single_line-height);\n}\n");

            var diagnostic = WithCode(Validate(), "FIG022").Single();

            Assert.That(diagnostic.Severity, Is.EqualTo(FigmaDiagnosticSeverity.Error));
            Assert.That(diagnostic.Message, Does.Contain("--missing"));
            Assert.That(diagnostic.Line, Is.EqualTo(2));
        }

        [Test]
        public void TokenDefinedOnlyInModeSheet_Resolves()
        {
            WriteCleanImport();
            Write("dark.uss", ":root {\n    --dark-only: #000;\n}\n");
            Write("Screen.uss", ".rpg-button { color: var(--dark-only); }\n");

            Assert.That(WithCode(Validate(), "FIG022"), Is.Empty);
        }

        [Test]
        public void InlineAndAbsoluteBudgets_AreFig030AndFig032Warnings()
        {
            WriteCleanImport();
            Write("Screen.uxml", @"<ui:UXML xmlns:ui=""UnityEngine.UIElements"">
    <ui:VisualElement style=""position: absolute; left: 0px;"">
        <ui:VisualElement style=""position: absolute; top: 4px;"" />
        <ui:VisualElement style=""width: 4px;"" />
        <ui:VisualElement style="""" />
    </ui:VisualElement>
</ui:UXML>
");

            var diagnostics = Validate(new FigmaValidationOptions { MaxInlineStyledElements = 2, MaxAbsoluteElements = 1 });

            Assert.That(WithCode(diagnostics, "FIG031").Single().Message, Is.EqualTo("3 inline-styled element(s), 2 absolute"));
            Assert.That(WithCode(diagnostics, "FIG030").Single().Severity, Is.EqualTo(FigmaDiagnosticSeverity.Warning));
            Assert.That(WithCode(diagnostics, "FIG032").Single().Severity, Is.EqualTo(FigmaDiagnosticSeverity.Warning));

            var atLimit = Validate(new FigmaValidationOptions { MaxInlineStyledElements = 3, MaxAbsoluteElements = 2 });
            Assert.That(WithCode(atLimit, "FIG030"), Is.Empty);
            Assert.That(WithCode(atLimit, "FIG032"), Is.Empty);
        }

        [Test]
        public void ProjectUrl_Resolves_FromTheProjectRoot()
        {
            WriteCleanImport();
            Write("images/a1.png", "png");
            Write("Screen.uss", ".rpg-button {\n    background-image: url(\"project://database/Assets/Figma/Shop/images/a1.png?fileID=2800000&guid=0123&type=3#a1\");\n}\n");

            Assert.That(WithCode(Validate(), "FIG040"), Is.Empty);
        }

        [Test]
        public void UnresolvedUrls_AreFig040Errors()
        {
            WriteCleanImport();
            Write("Screen.uss", ".rpg-button {\n    background-image: url(\"project://database/Assets/Figma/Shop/images/gone.png?guid=1\");\n    -unity-font: url('../fonts/missing.ttf');\n}\n");
            Write("Screen.uxml", CleanScreen.Replace("<ui:Button text", "<ui:Button icon-image=\"project://database/Assets/Figma/Shop/images/icon.png?guid=2\" text"));

            var errors = WithCode(Validate(), "FIG040");

            Assert.That(errors.Count, Is.EqualTo(3));
            Assert.That(errors.All(d => d.Severity == FigmaDiagnosticSeverity.Error), Is.True);
        }

        [Test]
        public void ProjectUrl_WithoutAssetsAncestor_IsFig041Warning()
        {
            var bare = Path.Combine(_project, "loose");
            Directory.CreateDirectory(bare);
            File.WriteAllText(Path.Combine(bare, ".figma-manifest.json"), "{}");
            File.WriteAllText(Path.Combine(bare, "Screen.uss"), ".a { background-image: url(\"/Assets/x.png\"); }\n");

            var diagnostics = FigmaImportValidator.ValidateFolder(bare, new FigmaValidationOptions());

            Assert.That(WithCode(diagnostics, "FIG040"), Is.Empty);
            Assert.That(WithCode(diagnostics, "FIG041").Single().Severity, Is.EqualTo(FigmaDiagnosticSeverity.Warning));
        }

        [Test]
        public void PackageUrl_IsNotCheckedOnDisk()
        {
            WriteCleanImport();
            Write("Screen.uss", ".rpg-button { -unity-font: url(\"project://database/Packages/com.example.fonts/Inter.ttf\"); }\n");

            Assert.That(Problems(Validate()), Is.Empty);
        }

        [Test]
        public void MissingStyleAndTemplateSources_AreFig050AndFig051Errors()
        {
            WriteCleanImport();
            Write("Screen.uxml", CleanScreen.Replace(
                "<Style src=\"Screen.uss\" />",
                "<Style src=\"Screen.uss\" />\n    <Style src=\"gone.uss\" />\n    <Template name=\"Card\" src=\"Card.uxml\" />"));

            var diagnostics = Validate();

            Assert.That(WithCode(diagnostics, "FIG050").Single().Line, Is.EqualTo(4));
            Assert.That(WithCode(diagnostics, "FIG051").Single().Line, Is.EqualTo(5));
        }

        [Test]
        public void Diagnostics_AreDeterministicallyOrdered()
        {
            WriteCleanImport();
            Write("Screen.uss", ".b { color: red; }\n.a { color: blue; }\n");
            Write("Other.uss", ".c { color: green; }\n");

            var first = Validate().Select(d => d.ToString()).ToList();
            var second = Validate().Select(d => d.ToString()).ToList();

            Assert.That(second, Is.EqualTo(first));
            var diagnostics = Validate().ToList();
            var resorted = diagnostics.ToList();
            resorted.Sort(FigmaDiagnostics.Compare);
            Assert.That(diagnostics, Is.EqualTo(resorted));
            Assert.That(diagnostics[0].File, Is.Empty); // folder-level findings first
        }

        [Test]
        public void ToJson_IsParseableAndCounts()
        {
            WriteCleanImport();
            Write("Screen.uss", ".rpg-button { color: var(--missing); }\n");
            var diagnostics = Validate();

            var json = FigmaJson.Parse(FigmaDiagnostics.ToJson("Assets/Figma/Shop", diagnostics));

            Assert.That(FigmaJson.MemberString(json, "folder"), Is.EqualTo("Assets/Figma/Shop"));
            Assert.That(FigmaJson.Member(json, "errors"), Is.EqualTo(1d));
            var items = FigmaJson.MemberArray(json, "diagnostics");
            Assert.That(items.Count, Is.EqualTo(diagnostics.Count));
            var error = items.Single(i => FigmaJson.MemberString(i, "code") == "FIG022");
            Assert.That(FigmaJson.MemberString(error, "severity"), Is.EqualTo("error"));
            Assert.That(FigmaJson.MemberString(error, "file"), Is.EqualTo("Screen.uss"));
        }
    }
}
