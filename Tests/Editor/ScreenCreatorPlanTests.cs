namespace Cuvara.UIToolkit.Editor.Tests
{
    using System;
    using System.IO;
    using System.Linq;
    using NUnit.Framework;

    /// <summary>
    /// Which files the Screen Creator writes. The new-UXML plan must equal what the wizard always
    /// wrote; the existing-UXML plan must never write a .uxml or a .uss.
    /// </summary>
    public class ScreenCreatorPlanTests
    {
        private static ScreenCreatorRequest Request(ScreenType type, ScreenSource source, bool hasModel = true)
        {
            return new ScreenCreatorRequest
            {
                Type             = type,
                HasModel         = hasModel,
                Source           = source,
                Name             = "Shop",
                Namespace        = "Game.Feature",
                ExistingUxmlPath = source == ScreenSource.ExistingUxml ? "Assets/Figma/Game/Shop-Screen.uxml" : null,
            };
        }

        [Test]
        public void NewUxml_WritesScriptUxmlUssAndTest_InTheWizardsOrder()
        {
            foreach (var type in new[] { ScreenType.Screen, ScreenType.Popup })
            {
                var names = ScreenCreatorPlan.Build(Request(type, ScreenSource.NewUxml)).Select(f => f.FileName);
                Assert.That(names, Is.EqualTo(new[] { "ShopScreen.cs", "Shop.uxml", "Shop.uss", "ShopPresenterTests.cs" }), $"{type}");
            }

            var itemNames = ScreenCreatorPlan.Build(Request(ScreenType.Item, ScreenSource.NewUxml)).Select(f => f.FileName);
            Assert.That(itemNames, Is.EqualTo(new[] { "ShopScreen.cs", "Shop.uxml", "ShopPresenterTests.cs" }));
        }

        [Test]
        public void NewUxml_ContentIsExactlyTheTemplatesSubstituted()
        {
            foreach (var type in new[] { ScreenType.Screen, ScreenType.Popup, ScreenType.Item })
            foreach (var hasModel in new[] { true, false })
            {
                var files = ScreenCreatorPlan.Build(Request(type, ScreenSource.NewUxml, hasModel)).ToDictionary(f => f.FileName, f => f.Content);

                Assert.That(files["ShopScreen.cs"], Is.EqualTo(ScreenCreatorTemplates.Substitute(
                    ScreenCreatorTemplates.SelectScriptTemplate(type, hasModel || type == ScreenType.Item), "Game.Feature", "Shop")));
                Assert.That(files["Shop.uxml"], Is.EqualTo(ScreenCreatorTemplates.Substitute(
                    ScreenCreatorTemplates.SelectUxmlTemplate(type), "Game.Feature", "Shop")));
                Assert.That(files["ShopPresenterTests.cs"], Is.EqualTo(ScreenCreatorTemplates.Substitute(
                    ScreenCreatorTemplates.SelectTestTemplate(), "Game.Feature", "Shop")));
            }
        }

        [Test]
        public void ExistingUxml_WritesNoUxmlAndNoUss()
        {
            foreach (var type in new[] { ScreenType.Screen, ScreenType.Popup, ScreenType.Item })
            foreach (var hasModel in new[] { true, false })
            {
                var names = ScreenCreatorPlan.Build(Request(type, ScreenSource.ExistingUxml, hasModel)).Select(f => f.FileName).ToArray();
                Assert.That(names, Is.EqualTo(new[] { "ShopScreen.cs", "ShopPresenterTests.cs" }), $"{type}/hasModel={hasModel}");
                Assert.That(names.Any(n => n.EndsWith(".uxml", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".uss", StringComparison.OrdinalIgnoreCase)), Is.False);
            }
        }

        [Test]
        public void ExistingUxml_ViewLoadsTheChosenUxmlsKey()
        {
            var script = ScreenCreatorPlan.Build(Request(ScreenType.Screen, ScreenSource.ExistingUxml)).First().Content;
            Assert.That(script, Does.Contain("AssetKey = \"Shop-Screen\";"), "default key is the UXML file name");

            var request = Request(ScreenType.Screen, ScreenSource.ExistingUxml);
            request.AssetKey = "ui/shop";
            Assert.That(ScreenCreatorPlan.Build(request).First().Content, Does.Contain("AssetKey = \"ui/shop\";"));
        }

        [Test]
        public void ExistingUxml_WithoutAUxmlPath_IsRejected()
        {
            var request = Request(ScreenType.Screen, ScreenSource.ExistingUxml);
            request.ExistingUxmlPath = null;
            Assert.Throws<ArgumentException>(() => ScreenCreatorPlan.Build(request));

            request.ExistingUxmlPath = "Assets/Figma/Game/Shop.uss";
            Assert.Throws<ArgumentException>(() => ScreenCreatorPlan.Build(request));
        }

        [Test]
        public void AMissingName_IsRejected()
        {
            var request = Request(ScreenType.Screen, ScreenSource.NewUxml);
            request.Name = "  ";
            Assert.Throws<ArgumentException>(() => ScreenCreatorPlan.Build(request));
            Assert.Throws<ArgumentNullException>(() => ScreenCreatorPlan.Build(null));
        }

        [Test]
        public void IsInsideConnectorImportFolder_FindsAManifestInTheFolderOrAParent()
        {
            var root = Path.Combine(Path.GetTempPath(), "cuvara-plan-tests-" + Guid.NewGuid().ToString("N"));
            var import = Path.Combine(root, "Assets", "Figma", "Game");
            var nested = Path.Combine(import, "images");
            var gameOwned = Path.Combine(root, "Assets", "UI");
            Directory.CreateDirectory(nested);
            Directory.CreateDirectory(gameOwned);

            try
            {
                Assert.That(ScreenCreatorPlan.IsInsideConnectorImportFolder(nested, root), Is.False, "no manifest yet");

                File.WriteAllText(Path.Combine(import, ScreenCreatorPlan.ConnectorManifestFileName), "{}");
                Assert.That(ScreenCreatorPlan.IsInsideConnectorImportFolder(import, root), Is.True);
                Assert.That(ScreenCreatorPlan.IsInsideConnectorImportFolder(nested, root), Is.True);
                Assert.That(ScreenCreatorPlan.IsInsideConnectorImportFolder(gameOwned, root), Is.False);
                Assert.That(ScreenCreatorPlan.IsInsideConnectorImportFolder(Path.Combine(gameOwned, "NotCreatedYet"), root), Is.False);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
