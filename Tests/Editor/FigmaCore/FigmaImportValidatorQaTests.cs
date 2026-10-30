namespace Cuvara.UIToolkit.Editor.Tests.FigmaCore
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using Cuvara.UIToolkit.FigmaCore;
    using NUnit.Framework;

    /// <summary>
    /// Release QA for <see cref="FigmaImportValidator"/>: malformed and hostile input, encodings,
    /// line endings, every reference form, thresholds at their edges, and determinism. Each test
    /// names the behaviour it pins; the rule table is <c>Documentation~/FIGMA-VALIDATION.md</c>.
    /// </summary>
    /// <remarks>
    /// Unity's bundled NUnit resolves <c>Has.Count</c> by reflection and fails on arrays, so
    /// counts are asserted as <c>x.Count, Is.EqualTo(n)</c>.
    /// </remarks>
    public class FigmaImportValidatorQaTests
    {
        private const string Tokens = ":root {\n    --surface: rgba(18, 20, 28, 1);\n    --text: rgba(240, 240, 245, 1);\n    --gap: 16px;\n}\n";

        private const string CleanScreen = "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">\n"
            + "    <Style src=\"tokens.uss\" />\n"
            + "    <Style src=\"Screen.uss\" />\n"
            + "    <ui:VisualElement style=\"flex-grow: 1;\">\n"
            + "        <ui:Button text=\"Buy\" class=\"rpg-button unity-button\" />\n"
            + "    </ui:VisualElement>\n"
            + "</ui:UXML>\n";

        private const string CleanScreenUss = ".rpg-button {\n    color: var(--text);\n    margin-top: var(--gap);\n}\n";

        private string _project;
        private string _folder;

        [SetUp]
        public void SetUp()
        {
            _project = Path.Combine(Path.GetTempPath(), "cuvara-figma-qa-" + Guid.NewGuid().ToString("N"));
            _folder = Path.Combine(_project, "Assets", "Figma", "Shop");
            Directory.CreateDirectory(_folder);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_project)) Directory.Delete(_project, true);
        }

        #region Helpers

        private void Write(string relative, string content)
        {
            WriteBytes(relative, new UTF8Encoding(false).GetBytes(content));
        }

        private void WriteBytes(string relative, byte[] content)
        {
            var path = Path.Combine(_folder, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, content);
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

        private static string Dump(IEnumerable<FigmaDiagnostic> diagnostics)
        {
            return string.Join("\n", diagnostics.Select(d => d.ToString()));
        }

        #endregion

        #region Malformed UXML

        [TestCase("<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">\n<ui:Label>\n</ui:UXML>\n", 3, TestName = "UnclosedTag")]
        [TestCase("<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">\n<ui:Label text=\"a\" text=\"b\" />\n</ui:UXML>\n", 2, TestName = "DuplicateAttribute")]
        [TestCase("<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">\n\n<ui:Label text=\"&nbsp;\" />\n</ui:UXML>\n", 3, TestName = "UndefinedEntity")]
        [TestCase("<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">\n</ui:UXML>\n<extra />\n", 3, TestName = "TwoRoots")]
        [TestCase("<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">\n<ui:Label text=\"a < b\" />\n</ui:UXML>\n", 2, TestName = "RawLessThanInAttribute")]
        [TestCase("<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"\n", 1, TestName = "TruncatedStartTag")]
        public void MalformedUxml_IsOneFig005Error_WithTheLine(string uxml, int line)
        {
            WriteCleanImport();
            Write("Broken.uxml", uxml);

            var diagnostics = Validate();
            var malformed = WithCode(diagnostics, "FIG005");

            Assert.That(malformed.Count, Is.EqualTo(1), Dump(diagnostics));
            Assert.That(malformed[0].Severity, Is.EqualTo(FigmaDiagnosticSeverity.Error));
            Assert.That(malformed[0].File, Is.EqualTo("Broken.uxml"));
            Assert.That(malformed[0].Line, Is.EqualTo(line));
            Assert.That(WithCode(diagnostics, "FIG031").Count(d => d.File == "Broken.uxml"), Is.EqualTo(0),
                "a document that does not parse gets no layout summary");
        }

        [Test]
        public void EmptyUxml_IsFig005Error()
        {
            WriteCleanImport();
            Write("Empty.uxml", string.Empty);

            Assert.That(WithCode(Validate(), "FIG005").Single().File, Is.EqualTo("Empty.uxml"));
        }

        [Test]
        public void UxmlWithDoctype_IsRejected_NotExpanded()
        {
            WriteCleanImport();
            Write("Doctype.uxml", "<?xml version=\"1.0\"?>\n<!DOCTYPE x [<!ENTITY e \"boom\">]>\n<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:Label text=\"&e;\" /></ui:UXML>\n");

            var diagnostic = WithCode(Validate(), "FIG005").Single();

            Assert.That(diagnostic.File, Is.EqualTo("Doctype.uxml"));
            Assert.That(diagnostic.Severity, Is.EqualTo(FigmaDiagnosticSeverity.Error));
        }

        [Test]
        public void UxmlWithBomAndPredefinedEntities_IsClean()
        {
            WriteCleanImport();
            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            var body = Encoding.UTF8.GetBytes(CleanScreen.Replace("text=\"Buy\"", "text=\"Buy &amp; sell &lt;3 &#x2713;\""));
            WriteBytes("Screen.uxml", bom.Concat(body).ToArray());

            var diagnostics = Validate();

            Assert.That(Problems(diagnostics), Is.Empty, Dump(diagnostics));
        }

        [Test]
        public void CrlfUxml_ReportsTheSameLinesAsLf()
        {
            WriteCleanImport();
            var uxml = CleanScreen.Replace("<Style src=\"Screen.uss\" />", "<Style src=\"Screen.uss\" />\n    <Style src=\"gone.uss\" />");
            Write("Screen.uxml", uxml);
            var lf = WithCode(Validate(), "FIG050").Single().Line;

            Write("Screen.uxml", uxml.Replace("\n", "\r\n"));
            var crlf = WithCode(Validate(), "FIG050").Single().Line;

            Assert.That(lf, Is.EqualTo(4));
            Assert.That(crlf, Is.EqualTo(lf));
        }

        [Test]
        public void StyleAndTemplate_WithoutSrc_AreEmptySourceErrors()
        {
            WriteCleanImport();
            Write("Screen.uxml", CleanScreen.Replace(
                "<Style src=\"Screen.uss\" />",
                "<Style src=\"Screen.uss\" />\n    <Style />\n    <Template name=\"Card\" />\n    <Template name=\"Card2\" src=\"  \" />"));

            var diagnostics = Validate();

            Assert.That(WithCode(diagnostics, "FIG050").Single().Message, Does.Contain("empty"));
            var templates = WithCode(diagnostics, "FIG051");
            Assert.That(templates.Count, Is.EqualTo(2), Dump(diagnostics));
            Assert.That(templates.Select(d => d.Line), Is.EqualTo(new[] { 5, 6 }));
        }

        [Test]
        public void NestedTemplates_EachSourceIsChecked_InItsOwnFolder()
        {
            WriteCleanImport();
            Write("Components/Card.uxml", "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">\n    <Template name=\"Badge\" src=\"Badge.uxml\" />\n    <Template name=\"Gone\" src=\"Gone.uxml\" />\n</ui:UXML>\n");
            Write("Components/Badge.uxml", "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\" />\n");
            Write("Screen.uxml", CleanScreen.Replace(
                "<Style src=\"Screen.uss\" />",
                "<Style src=\"Screen.uss\" />\n    <Template name=\"Card\" src=\"Components/Card.uxml\" />"));

            var diagnostics = Validate();
            var missing = WithCode(diagnostics, "FIG051");

            Assert.That(missing.Count, Is.EqualTo(1), Dump(diagnostics));
            Assert.That(missing[0].File, Is.EqualTo("Components/Card.uxml"));
            Assert.That(missing[0].Line, Is.EqualTo(3));
        }

        [Test]
        public void DuplicateStyle_IsNotAnError()
        {
            WriteCleanImport();
            Write("Screen.uxml", CleanScreen.Replace("<Style src=\"Screen.uss\" />", "<Style src=\"Screen.uss\" />\n    <Style src=\"Screen.uss\" />"));

            var diagnostics = Validate();

            Assert.That(Problems(diagnostics), Is.Empty, Dump(diagnostics));
        }

        [TestCase("project://database/Assets/Figma/Shop/Screen.uss?fileID=7433441132597879392&guid=abc&type=3#Screen", TestName = "ProjectScheme")]
        [TestCase("/Assets/Figma/Shop/Screen.uss", TestName = "RootedAssets")]
        [TestCase("./Screen.uss", TestName = "DotRelative")]
        [TestCase("../Shop/Screen.uss", TestName = "ParentRelative")]
        [TestCase("project://database/Packages/com.example/Theme.uss", TestName = "PackagePath")]
        [TestCase("Screen%20Copy.uss", TestName = "PercentEncoded")]
        public void StyleSrc_EveryReferenceFormThatExists_IsAccepted(string src)
        {
            WriteCleanImport();
            Write("Screen Copy.uss", CleanScreenUss);
            Write("Screen.uxml", CleanScreen.Replace("<Style src=\"Screen.uss\" />", "<Style src=\"" + src.Replace("&", "&amp;") + "\" />"));

            var diagnostics = Validate();

            Assert.That(WithCode(diagnostics, "FIG050"), Is.Empty, Dump(diagnostics));
        }

        [TestCase("project://database/Assets/Figma/Shop/Gone.uss?guid=abc", TestName = "ProjectScheme")]
        [TestCase("/Assets/Gone.uss", TestName = "RootedAssets")]
        [TestCase("../Other/Screen.uss", TestName = "ParentRelative")]
        [TestCase("project://database/Assets/Figma/Shop/screen.USS.bak", TestName = "WrongName")]
        public void StyleSrc_EveryReferenceFormThatIsMissing_IsFig050(string src)
        {
            WriteCleanImport();
            Write("Screen.uxml", CleanScreen.Replace("<Style src=\"Screen.uss\" />", "<Style src=\"" + src + "\" />"));

            Assert.That(WithCode(Validate(), "FIG050").Count, Is.EqualTo(1));
        }

        #endregion

        #region Malformed USS

        [TestCase(".rpg-a { color: var(--text); } /* open\n.rpg-b { color: red; }", "unterminated comment", 1, TestName = "UnterminatedComment")]
        [TestCase(".rpg-a {\n    color: var(--text);\n", "unterminated rule", 1, TestName = "MissingCloseBrace")]
        [TestCase(".rpg-a { color: var(--text); }\n}\n", "'}'", 2, TestName = "StrayCloseBrace")]
        [TestCase(".rpg-a { color: var(--text); \n .rpg-b { width: 1px; }\n}\n", "'{'", 2, TestName = "NestedOpenBrace")]
        [TestCase(".rpg-a { color: var(--text); }\nstray\n", "text outside any rule", 2, TestName = "StrayText")]
        public void MalformedUss_IsOneFig006Warning_WithTheLine(string uss, string problem, int line)
        {
            WriteCleanImport();
            Write("Screen.uss", uss);

            var diagnostics = Validate();
            var malformed = WithCode(diagnostics, "FIG006");

            Assert.That(malformed.Count, Is.EqualTo(1), Dump(diagnostics));
            Assert.That(malformed[0].Severity, Is.EqualTo(FigmaDiagnosticSeverity.Warning));
            Assert.That(malformed[0].Message, Does.Contain(problem));
            Assert.That(malformed[0].Line, Is.EqualTo(line));
        }

        [TestCase(".rpg-a { -unity-font-definition: url(\"fonts/a{b}.asset\"); }\n", TestName = "BracesInQuotedUrl")]
        [TestCase(".rpg-a { --label: \"a; b } c\"; }\n", TestName = "BraceAndSemicolonInString")]
        [TestCase(".rpg-a { --label: \"say \\\"}\\\"\"; }\n", TestName = "EscapedQuoteThenBrace")]
        [TestCase(".rpg-a { --label: 'it\\'s }'; }\n", TestName = "EscapedSingleQuoteThenBrace")]
        [TestCase(".rpg-a { --label: \"/* not a comment */\"; }\n.rpg-b { width: 1px; }\n", TestName = "CommentInsideString")]
        [TestCase(".rpg-a { width: 1px; } /* } { */\n.rpg-b { width: 2px; }\n", TestName = "BracesInsideComment")]
        public void WellFormedUss_WithTrickyStrings_HasNoFig006(string uss)
        {
            WriteCleanImport();
            Write("fonts/a{b}.asset", "x");
            Write("Screen.uss", uss);

            var diagnostics = Validate();

            Assert.That(WithCode(diagnostics, "FIG006"), Is.Empty, Dump(diagnostics));
            Assert.That(WithCode(diagnostics, "FIG010"), Is.Empty, Dump(diagnostics));
        }

        [Test]
        public void EscapedQuote_DoesNotHideTheNextRule()
        {
            WriteCleanImport();
            Write("Screen.uss", ".rpg-a { --label: \"a\\\"}\"; }\n.card { color: red; }\n");

            var diagnostics = Validate();

            Assert.That(WithCode(diagnostics, "FIG010").Single().Line, Is.EqualTo(2), Dump(diagnostics));
            Assert.That(WithCode(diagnostics, "FIG020").Single().Line, Is.EqualTo(2), Dump(diagnostics));
        }

        [Test]
        public void Import_IsNotAClassOrStrayText_AndItsUrlIsChecked()
        {
            WriteCleanImport();
            Write("shared.uss", ":root { --shared: 1px; }\n");
            Write("Screen.uss", "@import url(\"shared.uss\");\n@import \"gone.uss\";\n@import url('also-gone.uss');\n.rpg-button { width: var(--shared); }\n");

            var diagnostics = Validate();

            Assert.That(WithCode(diagnostics, "FIG006"), Is.Empty, Dump(diagnostics));
            Assert.That(WithCode(diagnostics, "FIG010"), Is.Empty, "'@import url(x.uss)' holds no class selector: " + Dump(diagnostics));
            var unresolved = WithCode(diagnostics, "FIG040");
            Assert.That(unresolved.Select(d => d.Line), Is.EqualTo(new[] { 2, 3 }), Dump(diagnostics));
            Assert.That(unresolved.All(d => d.File == "Screen.uss"), Is.True);
        }

        [Test]
        public void CrlfUss_ReportsTheSameLinesAsLf()
        {
            WriteCleanImport();
            const string uss = "/* header\n   comment */\n.rpg-button {\n    color: var(--text);\n    width: var(--missing);\n}\n\n.card { color: red; }\n";
            Write("Screen.uss", uss);
            var lf = Problems(Validate()).Select(d => d.Code + "@" + d.Line).ToList();

            Write("Screen.uss", uss.Replace("\n", "\r\n"));
            var crlf = Problems(Validate()).Select(d => d.Code + "@" + d.Line).ToList();

            Assert.That(lf, Is.EqualTo(new[] { "FIG022@5", "FIG010@8", "FIG020@8" }));
            Assert.That(crlf, Is.EqualTo(lf));
        }

        [Test]
        public void UssWithBom_IsClean()
        {
            WriteCleanImport();
            WriteBytes("Screen.uss", new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(CleanScreenUss)).ToArray());

            Assert.That(Problems(Validate()), Is.Empty);
        }

        [TestCase("url(images/a.png)", TestName = "Unquoted")]
        [TestCase("url( \"images/a.png\" )", TestName = "DoubleQuotedWithSpaces")]
        [TestCase("url('images/a.png')", TestName = "SingleQuoted")]
        [TestCase("url(\"images/a (1).png\")", TestName = "QuotedWithParentheses")]
        [TestCase("url(images/a\\ \\(1\\).png)", TestName = "UnquotedEscapedParentheses")]
        [TestCase("url(\"project://database/Assets/Figma/Shop/images/a.png?fileID=2800000&guid=0&type=3#a\")", TestName = "ProjectScheme")]
        [TestCase("url(\"/Assets/Figma/Shop/images/a.png\")", TestName = "RootedAssets")]
        [TestCase("url(\"project://database/Packages/com.example/x.png\")", TestName = "Package")]
        [TestCase("url(\"https://example.com/x.png\")", TestName = "OtherScheme")]
        [TestCase("url(\"data:image/png;base64,AAAA\")", TestName = "DataUri")]
        [TestCase("resource(\"Images/a\")", TestName = "Resource")]
        public void UrlForms_ThatResolveOrCannotBeChecked_AreNotFig040(string value)
        {
            WriteCleanImport();
            Write("images/a.png", "png");
            Write("images/a (1).png", "png");
            Write("Screen.uss", ".rpg-button {\n    background-image: " + value + ";\n}\n");

            var diagnostics = Validate();

            Assert.That(WithCode(diagnostics, "FIG040"), Is.Empty, Dump(diagnostics));
            Assert.That(WithCode(diagnostics, "FIG006"), Is.Empty, Dump(diagnostics));
        }

        [TestCase("url(images/gone.png)", TestName = "Unquoted")]
        [TestCase("url('images/gone.png')", TestName = "SingleQuoted")]
        [TestCase("url(\"images/gone (1).png\")", TestName = "QuotedWithParentheses")]
        [TestCase("url(\"project://database/Assets/Figma/Shop/images/gone.png?guid=1\")", TestName = "ProjectScheme")]
        [TestCase("url(\"/Assets/gone.png\")", TestName = "RootedAssets")]
        [TestCase("url(\"../../../../outside.png\")", TestName = "EscapesTheProject")]
        public void UrlForms_ThatDoNotResolve_AreOneFig040Each(string value)
        {
            WriteCleanImport();
            Write("Screen.uss", ".rpg-button {\n    background-image: " + value + ";\n}\n");

            var unresolved = WithCode(Validate(), "FIG040");

            Assert.That(unresolved.Count, Is.EqualTo(1));
            Assert.That(unresolved[0].Line, Is.EqualTo(2));
        }

        #endregion

        #region Tokens

        [Test]
        public void TokenAlias_ChainedThroughTheTokenSheet_Resolves()
        {
            WriteCleanImport();
            Write("tokens.uss", Tokens.Replace("}", "    --accent: var(--surface);\n    --accent-pressed: var(--accent);\n}"));
            Write("Screen.uss", ".rpg-button { color: var(--accent-pressed); }\n");

            Assert.That(WithCode(Validate(), "FIG022"), Is.Empty);
        }

        [Test]
        public void TokenAlias_ToAnUndefinedToken_IsFig022InTheTokenSheet()
        {
            WriteCleanImport();
            Write("tokens.uss", Tokens.Replace("}", "    --broken: var(--nowhere);\n}"));

            var diagnostic = WithCode(Validate(), "FIG022").Single();

            Assert.That(diagnostic.File, Is.EqualTo("tokens.uss"));
            Assert.That(diagnostic.Line, Is.EqualTo(5));
        }

        [Test]
        public void Token_ListedOnlyInTheManifest_Resolves()
        {
            WriteCleanImport();
            File.Delete(Path.Combine(_folder, "tokens.uss"));
            Write("Screen.uxml", CleanScreen.Replace("    <Style src=\"tokens.uss\" />\n", string.Empty));

            var diagnostics = Validate();

            Assert.That(WithCode(diagnostics, "FIG022"), Is.Empty, Dump(diagnostics));
        }

        [Test]
        public void Token_DefinedOnlyInAModeSheet_ResolvesWherever_ItIsUsed()
        {
            WriteCleanImport();
            Write("modes/dark.uss", ":root {\n    --dark-only: #000;\n}\n");
            Write("Screen.uss", ".rpg-button { color: var(--dark-only); }\n");
            Write("Screen.uxml", CleanScreen.Replace("flex-grow: 1;", "flex-grow: 1; background-color: var(--dark-only);"));

            Assert.That(WithCode(Validate(), "FIG022"), Is.Empty);
        }

        [TestCase("--unity-metrics-single_line-height", TestName = "UnityPrefix")]
        [TestCase("--cuvara-color-primary", TestName = "CuvaraPrefix")]
        public void ReservedTokenPrefixes_AreNeverUndefined(string token)
        {
            WriteCleanImport();
            Write("Screen.uss", ".rpg-button { color: var(" + token + "); }\n");

            Assert.That(WithCode(Validate(), "FIG022"), Is.Empty);
        }

        [TestCase("--cuvaraish", TestName = "NotQuiteCuvara")]
        [TestCase("--unityx", TestName = "NotQuiteUnity")]
        [TestCase("--Surface", TestName = "CaseDiffers")]
        public void TokenNames_AreExact_NotPrefixOrCaseMatched(string token)
        {
            WriteCleanImport();
            Write("Screen.uss", ".rpg-button { color: var(" + token + "); }\n");

            Assert.That(WithCode(Validate(), "FIG022").Count, Is.EqualTo(1));
        }

        [Test]
        public void UndefinedToken_InInlineStyle_NamesTheElement()
        {
            WriteCleanImport();
            Write("Screen.uxml", CleanScreen.Replace("flex-grow: 1;", "flex-grow: 1; background-color: var(--nope);"));

            var diagnostic = WithCode(Validate(), "FIG022").Single();

            Assert.That(diagnostic.File, Is.EqualTo("Screen.uxml"));
            Assert.That(diagnostic.Line, Is.EqualTo(4));
            Assert.That(diagnostic.Message, Does.Contain("<VisualElement>"));
        }

        [TestCase("background-color: var(--surface)", "background-color", TestName = "DefinedToken")]
        [TestCase("background-color: var(--surface, #000)", "background-color", TestName = "WithFallback")]
        [TestCase("margin-top: var( --gap )", "margin-top", TestName = "Spaces")]
        [TestCase("color: VAR(--text)", "color", TestName = "UpperCase")]
        [TestCase("--local: var(--surface)", "--local", TestName = "CustomProperty")]
        [TestCase("width: calc(var(--gap) * 2)", "width", TestName = "Nested")]
        [TestCase("color: var(--cuvara-color-primary)", "color", TestName = "CuvaraToken")]
        public void InlineStyleVariable_IsFig023Error_NamingTheProperty(string declaration, string property)
        {
            WriteCleanImport();
            Write("Screen.uxml", CleanScreen.Replace("flex-grow: 1;", "flex-grow: 1; " + declaration + ";"));

            var diagnostics = Validate();
            var inline = WithCode(diagnostics, "FIG023");

            Assert.That(inline.Count, Is.EqualTo(1), Dump(diagnostics));
            Assert.That(inline[0].Severity, Is.EqualTo(FigmaDiagnosticSeverity.Error));
            Assert.That(inline[0].File, Is.EqualTo("Screen.uxml"));
            Assert.That(inline[0].Line, Is.EqualTo(4));
            Assert.That(inline[0].Message, Does.Contain("'" + property + ":"));
            Assert.That(inline[0].Message, Does.Contain("<VisualElement>"));
            Assert.That(inline[0].Message, Does.Contain("CloneTree"));
            Assert.That(inline[0].Message, Does.Contain("component"));
        }

        [Test]
        public void InlineStyleVariable_IsReportedPerDeclaration_InScreensAndTemplates()
        {
            WriteCleanImport();
            Write("Components/Card.uxml", "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">\n    <ui:Label text=\"x\" style=\"color: var(--text); margin-top: var(--gap); width: 4px;\" />\n</ui:UXML>\n");

            var diagnostics = Validate();
            var inline = WithCode(diagnostics, "FIG023");

            Assert.That(inline.Count, Is.EqualTo(2), Dump(diagnostics));
            Assert.That(inline.All(d => d.File == "Components/Card.uxml" && d.Line == 2), Is.True);
            Assert.That(inline.Select(d => d.Message.Substring(0, d.Message.IndexOf(':'))),
                Is.EqualTo(new[] { "inline style 'color", "inline style 'margin-top" }));
        }

        [Test]
        public void InlineStyleVariable_UndefinedToken_IsBothFig022AndFig023()
        {
            WriteCleanImport();
            Write("Screen.uxml", CleanScreen.Replace("flex-grow: 1;", "flex-grow: 1; color: var(--nope);"));

            var diagnostics = Validate();

            Assert.That(WithCode(diagnostics, "FIG022").Count, Is.EqualTo(1), Dump(diagnostics));
            Assert.That(WithCode(diagnostics, "FIG023").Count, Is.EqualTo(1), Dump(diagnostics));
        }

        [TestCase("--label: 'var(--x)'", TestName = "InsideQuotes")]
        [TestCase("background-image: url(\"images/var(1).png\")", TestName = "InsideUrl")]
        [TestCase("-unity-font-definition: resource(\"Fonts/variable\")", TestName = "WordVariable")]
        [TestCase("width: covar(1px)", TestName = "OtherFunctionEndingInVar")]
        public void InlineStyle_WithoutAVarCall_IsNotFig023(string declaration)
        {
            WriteCleanImport();
            Write("images/var(1).png", "png");
            Write("Screen.uxml", CleanScreen.Replace("flex-grow: 1;", "flex-grow: 1; " + declaration + ";"));

            var diagnostics = Validate();

            Assert.That(WithCode(diagnostics, "FIG023"), Is.Empty, Dump(diagnostics));
        }

        [Test]
        public void VarInClassRules_IsNotFig023()
        {
            WriteCleanImport();

            var diagnostics = Validate();

            Assert.That(WithCode(diagnostics, "FIG023"), Is.Empty, "Screen.uss and tokens.uss use var(); only inline styles are affected");
        }

        [Test]
        public void TokenOnlyInAComment_IsNotDefined()
        {
            WriteCleanImport();
            Write("tokens.uss", Tokens + "/* :root { --ghost: 1px; } */\n");
            Write("Screen.uss", ".rpg-button { width: var(--ghost); }\n");

            Assert.That(WithCode(Validate(), "FIG022").Count, Is.EqualTo(1));
        }

        #endregion

        #region Manifest

        [TestCase("{\"fileName\":\"Shop\",\"screens\":[{\"assetName\":\"Scr", TestName = "Truncated")]
        [TestCase("", TestName = "Empty")]
        [TestCase("[]", TestName = "TopLevelArray")]
        [TestCase("{\"a\":1}}", TestName = "TrailingBrace")]
        [TestCase("\u0000\u0001", TestName = "Binary")]
        public void UnreadableManifest_IsOneFig002_AndTheRestStillRuns(string manifest)
        {
            WriteCleanImport();
            Write(".figma-manifest.json", manifest);
            Write("Screen.uss", ".rpg-button { color: var(--nope); }\n");

            var diagnostics = Validate();

            Assert.That(WithCode(diagnostics, "FIG002").Count, Is.EqualTo(1), Dump(diagnostics));
            Assert.That(WithCode(diagnostics, "FIG022").Count, Is.EqualTo(1), "sheets are still checked when the manifest is unreadable");
        }

        [Test]
        public void Manifest_WithBomCrlfUnknownFieldsAndWrongTypes_IsTolerated()
        {
            WriteCleanImport();
            var json = "{\r\n  \"fileName\": 42,\r\n  \"future\": {\"x\": [1, 2]},\r\n  \"tokens\": \"not-an-array\",\r\n"
                + "  \"screens\": [7, null, {\"assetName\": \"Screen\", \"rootNodeId\": 3, \"generated\": [\"Screen.uxml\", 5, null, \"\"]}],\r\n}\r\n";
            WriteBytes(".figma-manifest.json", new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(json)).ToArray());

            var diagnostics = Validate();

            Assert.That(Problems(diagnostics), Is.Empty, Dump(diagnostics));
        }

        [Test]
        public void Manifest_WithNoScreens_IsNotAnError()
        {
            WriteCleanImport();
            Write(".figma-manifest.json", "{\"fileName\":\"Shop\",\"screens\":[]}");

            Assert.That(Problems(Validate()), Is.Empty);
        }

        [TestCase("../../outside.uss", TestName = "ParentTraversal")]
        [TestCase("images/../../Shop2/x.png", TestName = "InnerTraversal")]
        [TestCase("/etc/hosts", TestName = "Rooted")]
        public void GeneratedPath_OutsideTheImportFolder_IsFig004_EvenWhenTheFileExists(string generated)
        {
            WriteCleanImport();
            File.WriteAllText(Path.Combine(_project, "Assets", "outside.uss"), ":root { }\n");
            Directory.CreateDirectory(Path.Combine(_project, "Assets", "Figma", "Shop2"));
            File.WriteAllText(Path.Combine(_project, "Assets", "Figma", "Shop2", "x.png"), "png");
            Write(".figma-manifest.json", Manifest("tokens.uss", "Screen.uss", "Screen.uxml", generated));

            var diagnostic = WithCode(Validate(), "FIG004").Single();

            Assert.That(diagnostic.Severity, Is.EqualTo(FigmaDiagnosticSeverity.Warning));
            Assert.That(diagnostic.Message, Does.Contain("outside the import folder"));
        }

        [Test]
        public void ScreenAssetName_WithTraversal_IsNotResolvedOutsideTheFolder()
        {
            WriteCleanImport();
            File.WriteAllText(Path.Combine(_project, "Assets", "Evil.uxml"), "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\" />");
            Write(".figma-manifest.json", "{\"screens\":[{\"assetName\":\"../../Evil\",\"rootNodeId\":\"1:2\",\"generated\":[]}]}");

            var diagnostic = WithCode(Validate(), "FIG003").Single();

            Assert.That(diagnostic.Severity, Is.EqualTo(FigmaDiagnosticSeverity.Error));
        }

        [Test]
        public void GeneratedPath_WithBackslashes_ResolvesLikeForwardSlashes()
        {
            WriteCleanImport();
            Write("images/a.png", "png");
            Write(".figma-manifest.json", Manifest("tokens.uss", "Screen.uss", "Screen.uxml", "images\\\\a.png"));

            Assert.That(WithCode(Validate(), "FIG004"), Is.Empty);
        }

        #endregion

        #region Thresholds

        [TestCase(0, 0, 1, 0, TestName = "ZeroLimit_OneStyled_OverInlineOnly")]
        [TestCase(1, 0, 0, 0, TestName = "AtInlineLimit_NoFinding")]
        [TestCase(-1, -1, 0, 0, TestName = "Unlimited")]
        public void InlineStyleBudget_Edges(int maxInline, int maxAbsolute, int expected030, int expected032)
        {
            WriteCleanImport();

            var diagnostics = Validate(new FigmaValidationOptions { ExpectedClassPrefix = "rpg", MaxInlineStyledElements = maxInline, MaxAbsoluteElements = maxAbsolute });

            Assert.That(WithCode(diagnostics, "FIG030").Count, Is.EqualTo(expected030));
            Assert.That(WithCode(diagnostics, "FIG032").Count, Is.EqualTo(expected032), "the clean screen has no absolute element");
        }

        [Test]
        public void Budgets_ArePerDocument_NotPerFolder()
        {
            WriteCleanImport();
            Write("Other.uxml", CleanScreen);

            var diagnostics = Validate(new FigmaValidationOptions { MaxInlineStyledElements = 1 });

            Assert.That(WithCode(diagnostics, "FIG030"), Is.Empty, "two documents with one styled element each are both at the limit");
            Assert.That(WithCode(diagnostics, "FIG031").Count, Is.EqualTo(2));
        }

        [TestCase("position:absolute", 1, TestName = "NoSpace")]
        [TestCase("POSITION: ABSOLUTE", 1, TestName = "UpperCase")]
        [TestCase("position: relative", 0, TestName = "Relative")]
        [TestCase("position: absolute; position: absolute", 1, TestName = "TwiceOnOneElement")]
        public void AbsoluteCount_CountsElements(string style, int absolute)
        {
            WriteCleanImport();
            Write("Screen.uxml", "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">\n    <ui:VisualElement style=\"" + style + "\" />\n</ui:UXML>\n");

            var summary = WithCode(Validate(), "FIG031").Single(d => d.File == "Screen.uxml");

            Assert.That(summary.Message, Is.EqualTo("1 inline-styled element(s), " + absolute + " absolute"));
        }

        #endregion

        #region Determinism

        [Test]
        public void SameInput_GivesIdenticalOrderedOutput_AndJson()
        {
            WriteCleanImport();
            Write("b/Z.uss", ".z { color: red; }\n.y { color: #fff; }\n");
            Write("a/A.uss", ".a { width: var(--missing); }\n");
            Write("Screen.uxml", CleanScreen.Replace("flex-grow: 1;", "flex-grow: 1; background-color: red;"));

            var first = Validate();
            var second = Validate();

            Assert.That(second.Select(d => d.ToString()), Is.EqualTo(first.Select(d => d.ToString())));
            Assert.That(FigmaDiagnostics.ToJson("x", second), Is.EqualTo(FigmaDiagnostics.ToJson("x", first)));

            var files = first.Where(d => d.File.Length > 0).Select(d => d.File).ToList();
            var sorted = files.ToList();
            sorted.Sort(StringComparer.Ordinal);
            Assert.That(files, Is.EqualTo(sorted), "file order is ordinal, independent of directory enumeration order");
        }

        [Test]
        public void Output_DoesNotDependOnFileCreationOrder()
        {
            WriteCleanImport();
            Write("B.uss", ".b { color: red; }\n");
            Write("A.uss", ".a { color: blue; }\n");
            var first = Validate().Select(d => d.ToString()).ToList();

            File.Delete(Path.Combine(_folder, "A.uss"));
            File.Delete(Path.Combine(_folder, "B.uss"));
            Write("A.uss", ".a { color: blue; }\n");
            Write("B.uss", ".b { color: red; }\n");

            Assert.That(Validate().Select(d => d.ToString()).ToList(), Is.EqualTo(first));
        }

        [Test]
        public void Files_UseForwardSlashes_RelativeToTheFolder()
        {
            WriteCleanImport();
            Write("sub/deeper/X.uss", ".x { width: 1px; }\n");

            var diagnostic = WithCode(Validate(), "FIG010").Single();

            Assert.That(diagnostic.File, Is.EqualTo("sub/deeper/X.uss"));
        }

        [Test]
        public void HiddenFoldersAndNonUssExtensions_AreSkipped()
        {
            WriteCleanImport();
            Write(".cache/X.uss", ".x { color: red; }\n");
            Write("notes.ussx", ".x { color: red; }\n");
            Write("Screen.uxml.bak", "<broken");

            Assert.That(Problems(Validate()), Is.Empty);
        }

        [Test]
        public void ToJson_EscapesMessagesAndKeepsAStableShape()
        {
            var diagnostics = new[]
            {
                new FigmaDiagnostic { Code = "FIG040", Severity = FigmaDiagnosticSeverity.Error, File = "a\\b \"c\".uss", Line = 3, Message = "tab\there\nnew \u0001" },
            };

            var json = FigmaDiagnostics.ToJson("C:\\x", diagnostics);

            Assert.That(json, Is.EqualTo("{\"folder\":\"C:\\\\x\",\"errors\":1,\"warnings\":0,\"infos\":0,\"diagnostics\":[{\"code\":\"FIG040\",\"severity\":\"error\",\"file\":\"a\\\\b \\\"c\\\".uss\",\"line\":3,\"message\":\"tab\\there\\nnew \\u0001\"}]}"));
            var parsed = FigmaJson.Parse(json);
            Assert.That(FigmaJson.MemberString(FigmaJson.MemberArray(parsed, "diagnostics")[0], "message"), Is.EqualTo("tab\there\nnew \u0001"));
        }

        [Test]
        public void ToJson_OfNothing_IsAnEmptyList()
        {
            Assert.That(FigmaDiagnostics.ToJson(null, null), Is.EqualTo("{\"folder\":\"\",\"errors\":0,\"warnings\":0,\"infos\":0,\"diagnostics\":[]}"));
        }

        #endregion
    }
}
