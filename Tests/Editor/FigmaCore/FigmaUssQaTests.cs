namespace Cuvara.UIToolkit.Editor.Tests.FigmaCore
{
    using System.Linq;
    using Cuvara.UIToolkit.FigmaCore;
    using NUnit.Framework;

    /// <summary>
    /// Release QA for the <see cref="FigmaUss"/> reader: escapes, quotes, stray braces,
    /// <c>@import</c>, and the <c>url()</c> forms, at the parser level.
    /// </summary>
    public class FigmaUssQaTests
    {
        [Test]
        public void Parse_EscapedQuoteInsideString_DoesNotEndTheString()
        {
            var sheet = FigmaUss.Parse(".a { --label: \"x\\\"}\"; color: red; }\n.b { width: 1px; }\n");

            Assert.That(sheet.Problem, Is.Null);
            Assert.That(sheet.Rules.Select(r => r.Selector), Is.EqualTo(new[] { ".a", ".b" }));
            Assert.That(sheet.Rules[0].Declarations.Select(d => d.Property), Is.EqualTo(new[] { "--label", "color" }));
            Assert.That(sheet.Rules[0].Declarations[0].Value, Is.EqualTo("\"x\\\"}\""));
        }

        [Test]
        public void Parse_UnterminatedStringEndsAtTheNewline()
        {
            var sheet = FigmaUss.Parse(".a { --label: \"open;\n}\n.b { width: 1px; }\n");

            Assert.That(sheet.Rules.Select(r => r.Selector), Is.EqualTo(new[] { ".a", ".b" }));
        }

        [Test]
        public void Parse_StrayCloseBrace_IsAProblem_AndTheNextSelectorIsClean()
        {
            var sheet = FigmaUss.Parse(".a { width: 1px; } }\n.b { width: 2px; }\n");

            Assert.That(sheet.Problem, Is.EqualTo("unexpected '}'"));
            Assert.That(sheet.ProblemLine, Is.EqualTo(1));
            Assert.That(sheet.Rules.Select(r => r.Selector), Is.EqualTo(new[] { ".a", ".b" }));
            Assert.That(sheet.Rules[1].Line, Is.EqualTo(2));
        }

        [Test]
        public void Parse_NestedOpenBrace_IsAProblem()
        {
            var sheet = FigmaUss.Parse("@media (min-width: 1px) {\n  .a { width: 1px; }\n}\n");

            Assert.That(sheet.Problem, Does.StartWith("unexpected '{' inside rule"));
            Assert.That(sheet.ProblemLine, Is.EqualTo(2));
        }

        [Test]
        public void Parse_Imports_AreReadWithTheirLines_AndAreNotRules()
        {
            var sheet = FigmaUss.Parse("@charset \"utf-8\";\n@import url(\"a.uss\");\n@IMPORT 'b.uss';\n@import url(c\\(1\\).uss);\n.x { width: 1px; }\n");

            Assert.That(sheet.Problem, Is.Null);
            Assert.That(sheet.Imports.Select(i => i.Target), Is.EqualTo(new[] { "a.uss", "b.uss", "c(1).uss" }));
            Assert.That(sheet.Imports.Select(i => i.Line), Is.EqualTo(new[] { 2, 3, 4 }));
            Assert.That(sheet.Rules.Select(r => r.Selector), Is.EqualTo(new[] { ".x" }));
            Assert.That(sheet.Rules[0].Line, Is.EqualTo(5));
        }

        [Test]
        public void Parse_ImportOnly_IsWellFormed()
        {
            var sheet = FigmaUss.Parse("@import \"a.uss\";\n");

            Assert.That(sheet.Problem, Is.Null);
            Assert.That(sheet.Rules, Is.Empty);
            Assert.That(sheet.Imports.Single().Target, Is.EqualTo("a.uss"));
        }

        [TestCase("url(a.png)", "a.png")]
        [TestCase("url(  a.png  )", "a.png")]
        [TestCase("url(\"a b.png\")", "a b.png")]
        [TestCase("url('a\"b.png')", "a\"b.png")]
        [TestCase("url(\"a\\\"b.png\")", "a\"b.png")]
        [TestCase("url(\"a (1).png\")", "a (1).png")]
        [TestCase("url(a\\(1\\).png)", "a(1).png")]
        [TestCase("url(a\\ b.png)", "a b.png")]
        [TestCase("url(\"\\26 .png\")", "&.png")]
        [TestCase("url(\"project://database/Assets/a.png?fileID=1&guid=2&type=3#a\")", "project://database/Assets/a.png?fileID=1&guid=2&type=3#a")]
        public void FindUrls_UnquotesAndUnescapes(string value, string expected)
        {
            Assert.That(FigmaUss.FindUrls(value), Is.EqualTo(new[] { expected }));
        }

        [TestCase("var(--x)", true)]
        [TestCase("1px var(--x, 2px)", true)]
        [TestCase("Var (--x)", true)]
        [TestCase("calc(var(--x) * 2)", true)]
        [TestCase("\"var(--x)\"", false)]
        [TestCase("url(\"var(--x).png\")", false)]
        [TestCase("covar(--x)", false)]
        [TestCase("variable", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void UsesVariable_FindsVarCallsOutsideStringsAndUrls(string value, bool expected)
        {
            Assert.That(FigmaUss.UsesVariable(value), Is.EqualTo(expected));
        }

        [Test]
        public void FindUrls_SeveralInOneValue()
        {
            Assert.That(FigmaUss.FindUrls("url(a.png), url('b.png')"), Is.EqualTo(new[] { "a.png", "b.png" }));
        }

        [Test]
        public void FindLiteralColors_IgnoresAHexInsideAStringWithAnEscapedQuote()
        {
            Assert.That(FigmaUss.FindLiteralColors("--label", "\"a\\\" #fff\""), Is.Empty);
        }

        [Test]
        public void Parse_KeepsLinesThroughCrlfAndComments()
        {
            var sheet = FigmaUss.Parse("/* a\r\n b */\r\n.a {\r\n  color: red;\r\n}\r\n\r\n.b { width: 1px; }\r\n");

            Assert.That(sheet.Rules.Select(r => r.Line), Is.EqualTo(new[] { 3, 7 }));
            Assert.That(sheet.Rules[0].Declarations.Single().Line, Is.EqualTo(4));
        }

        [Test]
        public void ParseInline_SemicolonInsideQuotesAndUrl_DoesNotSplit()
        {
            var declarations = FigmaUss.ParseInline("background-image: url(\"a;b.png\"); --t: 'x;y'", 7);

            Assert.That(declarations.Select(d => d.Property), Is.EqualTo(new[] { "background-image", "--t" }));
            Assert.That(declarations.All(d => d.Line == 7), Is.True);
        }

        [Test]
        public void ParseInline_TrailingBackslash_KeepsTheLastDeclaration()
        {
            var declarations = FigmaUss.ParseInline("width: 1px; --t: a\\", 1);

            Assert.That(declarations.Select(d => d.Property), Is.EqualTo(new[] { "width", "--t" }));
        }
    }
}
