namespace Cuvara.UIToolkit.Editor.Tests.FigmaCore
{
    using System;
    using Cuvara.UIToolkit.FigmaCore;
    using NUnit.Framework;

    /// <summary>
    /// <see cref="FigmaManifestReader"/> against the shape connector 0.3.0-exp.1 writes
    /// (<c>JsonUtility.ToJson(manifest, true)</c> of <c>FigmaSyncManifest</c>), and its
    /// tolerance for schema drift.
    /// </summary>
    public class FigmaManifestReaderTests
    {
        private const string ConnectorManifest = @"{
    ""fileName"": ""Shop Demo"",
    ""tokens"": [
        { ""name"": ""--surface-primary"", ""id"": ""VariableID:1:2"", ""type"": ""COLOR"", ""value"": ""rgba(18, 20, 28, 1)"", ""unit"": """" },
        { ""name"": ""--spacing-md"", ""id"": ""VariableID:2:1"", ""type"": ""FLOAT"", ""value"": ""16px"", ""unit"": """" }
    ],
    ""screens"": [
        {
            ""assetName"": ""ShopScreen"",
            ""rootNodeId"": ""12:34"",
            ""contentHash"": ""9f3c"",
            ""generated"": [ ""tokens.uss"", ""ShopScreen.uss"", ""ShopScreen.uxml"", ""images/3f2a9c.png"" ],
            ""tokens"": [],
            ""nodes"": [ { ""figmaNodeId"": ""12:40"", ""doc"": ""ShopScreen"", ""path"": ""1/3"", ""tag"": ""ui:Button"", ""classes"": ""rpg-primary-button"", ""props"": [ { ""k"": ""color"", ""v"": ""var(--x)"" } ] } ],
            ""components"": [],
            ""states"": [],
            ""templateNames"": [],
            ""classNames"": []
        }
    ],
    ""matchedMappings"": [ ""Button"" ],
    ""foreign"": []
}";

        [Test]
        public void Read_ConnectorManifest_ReadsScreensGeneratedAndTokens()
        {
            var manifest = FigmaManifestReader.Read(ConnectorManifest);

            Assert.That(manifest.FileName, Is.EqualTo("Shop Demo"));
            Assert.That(manifest.TokenNames, Is.EqualTo(new[] { "--surface-primary", "--spacing-md" }));
            Assert.That(manifest.Screens.Count, Is.EqualTo(1));
            Assert.That(manifest.Screens[0].AssetName, Is.EqualTo("ShopScreen"));
            Assert.That(manifest.Screens[0].RootNodeId, Is.EqualTo("12:34"));
            Assert.That(manifest.Screens[0].Generated,
                Is.EqualTo(new[] { "tokens.uss", "ShopScreen.uss", "ShopScreen.uxml", "images/3f2a9c.png" }));
        }

        [Test]
        public void Read_UnknownFields_AreIgnored()
        {
            var manifest = FigmaManifestReader.Read(
                "{\"fileName\":\"F\",\"futureField\":{\"x\":[1,2]},\"screens\":[{\"assetName\":\"A\",\"newThing\":true}]}");

            Assert.That(manifest.FileName, Is.EqualTo("F"));
            Assert.That(manifest.Screens[0].AssetName, Is.EqualTo("A"));
        }

        [Test]
        public void Read_MissingFields_AreEmpty()
        {
            var manifest = FigmaManifestReader.Read("{}");

            Assert.That(manifest.FileName, Is.Empty);
            Assert.That(manifest.Screens, Is.Empty);
            Assert.That(manifest.TokenNames, Is.Empty);

            var screen = FigmaManifestReader.Read("{\"screens\":[{}]}").Screens[0];
            Assert.That(screen.AssetName, Is.Empty);
            Assert.That(screen.RootNodeId, Is.Empty);
            Assert.That(screen.Generated, Is.Empty);
        }

        [Test]
        public void Read_WrongTypes_ReadAsMissing()
        {
            var manifest = FigmaManifestReader.Read(
                "{\"fileName\":7,\"tokens\":\"x\",\"screens\":[\"not an object\",{\"assetName\":[],\"generated\":[1,\"\",\"a.uss\",null]}]}");

            Assert.That(manifest.FileName, Is.Empty);
            Assert.That(manifest.TokenNames, Is.Empty);
            Assert.That(manifest.Screens.Count, Is.EqualTo(1));
            Assert.That(manifest.Screens[0].AssetName, Is.Empty);
            Assert.That(manifest.Screens[0].Generated, Is.EqualTo(new[] { "a.uss" }));
        }

        [Test]
        public void Read_OlderManifest_PerScreenTokensFillTheList_WithoutDuplicates()
        {
            var manifest = FigmaManifestReader.Read(
                "{\"tokens\":[{\"name\":\"--a\"}],\"screens\":[{\"tokens\":[{\"name\":\"--a\"},{\"name\":\"--b\"}]}]}");

            Assert.That(manifest.TokenNames, Is.EqualTo(new[] { "--a", "--b" }));
        }

        [TestCase("not json")]
        [TestCase("[1, 2]")]
        [TestCase("\"text\"")]
        [TestCase("")]
        public void Read_NonJsonOrNonObject_ThrowsFormatException(string text)
        {
            Assert.Throws<FormatException>(() => FigmaManifestReader.Read(text));
        }
    }
}
