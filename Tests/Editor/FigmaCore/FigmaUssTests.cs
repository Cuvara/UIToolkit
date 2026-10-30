namespace Cuvara.UIToolkit.Editor.Tests.FigmaCore
{
    using System.Linq;
    using Cuvara.UIToolkit.FigmaCore;
    using NUnit.Framework;

    /// <summary>The minimal USS reader the validator audits connector sheets with.</summary>
    public class FigmaUssTests
    {
        [Test]
        public void Parse_CommentOpenerInsideQuotes_IsNotAComment()
        {
            var sheet = FigmaUss.Parse(".a { background-image: url(\"x/*y.png\"); color: red; }\n.b { width: 1px; }\n");

            Assert.That(sheet.Problem, Is.Null);
            Assert.That(sheet.Rules.Select(r => r.Selector), Is.EqualTo(new[] { ".a", ".b" }));
            Assert.That(sheet.Rules[0].Declarations.Select(d => d.Property), Is.EqualTo(new[] { "background-image", "color" }));
        }

        [Test]
        public void Parse_RulesDeclarationsAndLines()
        {
            var sheet = FigmaUss.Parse(".a {\n    color: red;\n    width: 10px;\n}\n\n.b:hover > .c {\n    height: 2px;\n}\n");

            Assert.That(sheet.Problem, Is.Null);
            Assert.That(sheet.Rules.Select(r => r.Selector), Is.EqualTo(new[] { ".a", ".b:hover > .c" }));
            Assert.That(sheet.Rules[0].Line, Is.EqualTo(1));
            Assert.That(sheet.Rules[1].Line, Is.EqualTo(6));
            Assert.That(sheet.Rules[0].Declarations.Select(d => d.Property), Is.EqualTo(new[] { "color", "width" }));
            Assert.That(sheet.Rules[0].Declarations[1].Value, Is.EqualTo("10px"));
            Assert.That(sheet.Rules[0].Declarations[1].Line, Is.EqualTo(3));
            Assert.That(sheet.Rules[1].Declarations[0].Line, Is.EqualTo(7));
        }

        [Test]
        public void Parse_DropsComments_KeepsLines()
        {
            var sheet = FigmaUss.Parse("/* header\n   two lines */\n.a { /* x: y; */ color: var(--c); }\n");

            Assert.That(sheet.Rules.Count, Is.EqualTo(1));
            Assert.That(sheet.Rules[0].Line, Is.EqualTo(3));
            Assert.That(sheet.Rules[0].Declarations.Single().Property, Is.EqualTo("color"));
        }

        [Test]
        public void Parse_KeepsSemicolonsInsideQuotesAndParentheses()
        {
            var sheet = FigmaUss.Parse(".a { background-image: url(\"a;b.png\"); color: red }");

            Assert.That(sheet.Rules[0].Declarations.Select(d => d.Property), Is.EqualTo(new[] { "background-image", "color" }));
            Assert.That(FigmaUss.FindUrls(sheet.Rules[0].Declarations[0].Value), Is.EqualTo(new[] { "a;b.png" }));
        }

        [TestCase(".a { color: red;", "unterminated rule")]
        [TestCase(".a { color: red; } /* open", "unterminated comment")]
        [TestCase(".a { color: red; } stray", "text outside any rule")]
        public void Parse_MalformedSheet_ReportsProblem(string text, string problem)
        {
            Assert.That(FigmaUss.Parse(text).Problem, Does.StartWith(problem));
        }

        [Test]
        public void ParseInline_ReadsStyleAttribute()
        {
            var declarations = FigmaUss.ParseInline("flex-grow: 1; position: absolute; left: 4px;", 12);

            Assert.That(declarations.Count, Is.EqualTo(3));
            Assert.That(declarations.All(d => d.Line == 12), Is.True);
            Assert.That(declarations.Count(FigmaUss.IsAbsolutePosition), Is.EqualTo(1));
        }

        [TestCase("color", "#fff", "#fff")]
        [TestCase("background-color", "#12141C", "#12141C")]
        [TestCase("border-left-color", "#12141CFF", "#12141CFF")]
        [TestCase("color", "rgba(1, 2, 3, 0.5)", "rgba(1,2,3,0.5)")]
        [TestCase("color", "RGB(1,2,3)", "RGB(1,2,3)")]
        [TestCase("color", "white", "white")]
        [TestCase("-unity-background-image-tint-color", "Tomato", "tomato")]
        [TestCase("text-shadow", "2px 2px 0 black", "black")]
        public void FindLiteralColors_FindsLiterals(string property, string value, string expected)
        {
            Assert.That(FigmaUss.FindLiteralColors(property, value), Is.EqualTo(new[] { expected }));
        }

        [TestCase("color", "var(--text)")]
        [TestCase("color", "var(--text, #fff)")]
        [TestCase("color", "var(--text, rgba(0,0,0,1))")]
        [TestCase("color", "transparent")]
        [TestCase("background-image", "url(\"images/white.png#ff0000\")")]
        [TestCase("-unity-font-style", "bold")]
        [TestCase("-unity-text-align", "middle-center")]
        [TestCase("width", "100px")]
        [TestCase("font-size", "12px")]
        [TestCase("-unity-font", "resource(\"Fonts/Black\")")]
        public void FindLiteralColors_IgnoresTokensKeywordsAndUrls(string property, string value)
        {
            Assert.That(FigmaUss.FindLiteralColors(property, value), Is.Empty);
        }

        [Test]
        public void FindLiteralColors_NamedColorOutsideColorProperty_IsIgnored()
        {
            // 'tan' or 'linen' could be anything outside a colour property.
            Assert.That(FigmaUss.FindLiteralColors("transition-property", "tan"), Is.Empty);
        }

        [Test]
        public void FindVarReferences_ReportsNamesAndFallbacks()
        {
            var references = FigmaUss.FindVarReferences("var(--a) var( --b , 4px ) var(--c,var(--d))");

            Assert.That(references.Select(r => r.Key), Is.EqualTo(new[] { "--a", "--b", "--c", "--d" }));
            Assert.That(references.Select(r => r.Value), Is.EqualTo(new[] { false, true, true, false }));
        }

        [Test]
        public void FindUrls_UnquotesAllForms()
        {
            Assert.That(FigmaUss.FindUrls("url(\"a.png\") url('b.png') url( c.png )"), Is.EqualTo(new[] { "a.png", "b.png", "c.png" }));
        }

        [Test]
        public void SelectorClasses_ListsEveryClass()
        {
            Assert.That(FigmaUss.SelectorClasses(".rpg-card:hover > .unity-label, #id .x_y"),
                Is.EqualTo(new[] { "rpg-card", "unity-label", "x_y" }));
        }
    }
}
