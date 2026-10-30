namespace Cuvara.UIToolkit.Editor.Tests.FigmaCore
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Cuvara.UIToolkit.FigmaCore;
    using NUnit.Framework;

    /// <summary>The tolerant JSON reader behind the manifest reader. Pure strings, no assets.</summary>
    public class FigmaJsonTests
    {
        [Test]
        public void Parse_ReadsObjectsArraysAndScalars()
        {
            var root = FigmaJson.Parse("{\"a\": [1, 2.5, -3e2], \"b\": true, \"c\": null, \"d\": \"x\"}");

            var items = FigmaJson.MemberArray(root, "a");
            Assert.That(items, Is.EqualTo(new List<object> { 1d, 2.5d, -300d }));
            Assert.That(FigmaJson.Member(root, "b"), Is.EqualTo(true));
            Assert.That(FigmaJson.Member(root, "c"), Is.Null);
            Assert.That(FigmaJson.MemberString(root, "d"), Is.EqualTo("x"));
        }

        [Test]
        public void Parse_DecodesEscapes()
        {
            var root = FigmaJson.Parse("{\"s\": \"q\\\"b\\\\n\\n\\u00e9\\/\"}");

            Assert.That(FigmaJson.MemberString(root, "s"), Is.EqualTo("q\"b\\n\n\u00e9/"));
        }

        [Test]
        public void Parse_ToleratesBomAndTrailingCommas()
        {
            var root = FigmaJson.Parse("\uFEFF{\"a\": [1, 2,], \"b\": 1,}");

            Assert.That(FigmaJson.MemberArray(root, "a").Count, Is.EqualTo(2));
            Assert.That(FigmaJson.Member(root, "b"), Is.EqualTo(1d));
        }

        [Test]
        public void Parse_DuplicateKey_LastWins()
        {
            Assert.That(FigmaJson.MemberString(FigmaJson.Parse("{\"k\":\"1\",\"k\":\"2\"}"), "k"), Is.EqualTo("2"));
        }

        [TestCase("")]
        [TestCase("not json")]
        [TestCase("{\"a\": 1")]
        [TestCase("{\"a\" 1}")]
        [TestCase("[1, 2")]
        [TestCase("{\"a\": \"unterminated}")]
        [TestCase("{} trailing")]
        [TestCase("{\"a\": tru}")]
        public void Parse_NonJson_ThrowsFormatException(string text)
        {
            Assert.Throws<FormatException>(() => FigmaJson.Parse(text));
        }

        [Test]
        public void Parse_Error_NamesTheLine()
        {
            var exception = Assert.Throws<FormatException>(() => FigmaJson.Parse("{\n\"a\": 1,\n\"b\": ?\n}"));

            Assert.That(exception.Message, Does.Contain("line 3"));
        }

        [Test]
        public void Member_OnNonObject_IsNullAndArrayIsEmpty()
        {
            Assert.That(FigmaJson.Member("text", "a"), Is.Null);
            Assert.That(FigmaJson.MemberArray(null, "a"), Is.Empty);
            Assert.That(FigmaJson.MemberString(FigmaJson.Parse("{\"a\": 1}"), "a"), Is.Null);
        }

        [Test]
        public void WriteString_EscapesAndRoundTrips()
        {
            var builder = new StringBuilder();
            FigmaJson.WriteString(builder, "a\"b\\c\nd\u0001");

            Assert.That(builder.ToString(), Is.EqualTo("\"a\\\"b\\\\c\\nd\\u0001\""));
            Assert.That(FigmaJson.Parse(builder.ToString()), Is.EqualTo("a\"b\\c\nd\u0001"));
        }
    }
}
