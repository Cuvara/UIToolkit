namespace Cuvara.UIToolkit.Editor.Tests.FigmaCore
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Cuvara.UIToolkit.FigmaCore;
    using NUnit.Framework;

    /// <summary>
    /// Release QA for <see cref="FigmaJson"/> and <see cref="FigmaManifestReader"/> on hostile or
    /// unusual manifests: depth, escapes, numbers, and the tolerated shapes.
    /// </summary>
    public class FigmaJsonQaTests
    {
        [Test]
        public void Parse_DeepNesting_IsAFormatException_NotAStackOverflow()
        {
            var text = new string('[', 100000) + new string(']', 100000);

            var exception = Assert.Throws<FormatException>(() => FigmaJson.Parse(text));

            Assert.That(exception.Message, Does.Contain("nesting too deep"));
        }

        [Test]
        public void Parse_NestingAtTheCap_IsAccepted()
        {
            var text = new string('[', 200) + new string(']', 200);

            Assert.That(FigmaJson.Parse(text), Is.InstanceOf<List<object>>());
        }

        [Test]
        public void Parse_SurrogatePairEscape_RoundTrips()
        {
            var value = (string)FigmaJson.Parse("\"\\ud83d\\ude00\"");

            Assert.That(value, Is.EqualTo(char.ConvertFromUtf32(0x1F600)));
        }

        [TestCase("\"\\u12\"", TestName = "TruncatedUnicodeEscape")]
        [TestCase("\"\\uZZZZ\"", TestName = "NonHexUnicodeEscape")]
        [TestCase("\"\\x41\"", TestName = "UnknownEscape")]
        [TestCase("-", TestName = "LoneMinus")]
        [TestCase("1e", TestName = "IncompleteExponent")]
        [TestCase("{\"a\":1,,}", TestName = "DoubleComma")]
        [TestCase("{1:2}", TestName = "NonStringKey")]
        [TestCase("nul", TestName = "TruncatedLiteral")]
        [TestCase("\uFEFF", TestName = "BomOnly")]
        public void Parse_Malformed_ThrowsFormatException(string text)
        {
            Assert.Throws<FormatException>(() => FigmaJson.Parse(text));
        }

        [TestCase("0", 0d)]
        [TestCase("-12.5", -12.5d)]
        [TestCase("1e3", 1000d)]
        [TestCase("2.5E-1", 0.25d)]
        public void Parse_Numbers_AreInvariantDoubles(string text, double expected)
        {
            Assert.That(FigmaJson.Parse(text), Is.EqualTo(expected));
        }

        [Test]
        public void Parse_CrlfAndTabs_AreWhitespace_AndErrorsCountLines()
        {
            Assert.That(FigmaJson.Parse("{\r\n\t\"a\": [1,\r\n 2]\r\n}"), Is.InstanceOf<Dictionary<string, object>>());

            var exception = Assert.Throws<FormatException>(() => FigmaJson.Parse("{\r\n\"a\": 1,\r\n\"b\" 2\r\n}"));
            Assert.That(exception.Message, Does.Contain("line 3"));
        }

        [Test]
        public void ManifestReader_TruncatedJson_IsAFormatException()
        {
            Assert.Throws<FormatException>(() => FigmaManifestReader.Read("{\"screens\":[{\"assetName\":\"A\""));
        }

        [Test]
        public void ManifestReader_EmptyAndNullScreens_ReadAsNoScreens()
        {
            Assert.That(FigmaManifestReader.Read("{\"screens\":[]}").Screens.Count, Is.EqualTo(0));
            Assert.That(FigmaManifestReader.Read("{\"screens\":null}").Screens.Count, Is.EqualTo(0));
            Assert.That(FigmaManifestReader.Read("{}").Screens.Count, Is.EqualTo(0));
        }

        [Test]
        public void ManifestReader_WrongTypesInsideAScreen_ReadAsMissing()
        {
            var info = FigmaManifestReader.Read("{\"fileName\":[],\"tokens\":[{\"name\":7},\"--x\",{\"name\":\"--ok\"}],"
                + "\"screens\":[{\"assetName\":{},\"rootNodeId\":false,\"generated\":\"a.uss\"},{\"assetName\":\"B\",\"generated\":[\"b.uxml\",\"\",1]}]}");

            Assert.That(info.FileName, Is.Empty);
            Assert.That(info.TokenNames, Is.EqualTo(new[] { "--ok" }));
            Assert.That(info.Screens.Count, Is.EqualTo(2));
            Assert.That(info.Screens[0].AssetName, Is.Empty);
            Assert.That(info.Screens[0].RootNodeId, Is.Empty);
            Assert.That(info.Screens[0].Generated.Count, Is.EqualTo(0));
            Assert.That(info.Screens[1].Generated, Is.EqualTo(new[] { "b.uxml" }));
        }

        [Test]
        public void ManifestReader_IsDeterministic()
        {
            const string json = "{\"tokens\":[{\"name\":\"--b\"},{\"name\":\"--a\"},{\"name\":\"--b\"}],\"screens\":[{\"assetName\":\"S\",\"tokens\":[{\"name\":\"--c\"},{\"name\":\"--a\"}]}]}";

            var first = FigmaManifestReader.Read(json).TokenNames.ToList();
            var second = FigmaManifestReader.Read(json).TokenNames.ToList();

            Assert.That(first, Is.EqualTo(new[] { "--b", "--a", "--c" }), "manifest order, first occurrence wins");
            Assert.That(second, Is.EqualTo(first));
        }
    }
}
