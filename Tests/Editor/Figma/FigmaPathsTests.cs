namespace Cuvara.UIToolkit.Editor.Figma.Tests
{
    using System.Linq;
    using Cuvara.UIToolkit.FigmaCore;
    using NUnit.Framework;

    /// <summary>The bridge and the game's files stay out of the connector's import folder.</summary>
    public class FigmaPathsTests
    {
        [Test]
        public void IsSameOrInside_ComparesWholeSegments()
        {
            Assert.That(FigmaPaths.IsSameOrInside("Assets/Figma/Game", "Assets/Figma/Game"), Is.True);
            Assert.That(FigmaPaths.IsSameOrInside("Assets/Figma/Game/Theme", "Assets/Figma/Game"), Is.True);
            Assert.That(FigmaPaths.IsSameOrInside("Assets/Figma/GameHud", "Assets/Figma/Game"), Is.False);
            Assert.That(FigmaPaths.IsSameOrInside("Assets/Figma", "Assets/Figma/Game"), Is.False);
            Assert.That(FigmaPaths.IsSameOrInside("", "Assets"), Is.False);
        }

        [Test]
        public void IsSameOrInside_IgnoresSlashStyleAndCase()
        {
            Assert.That(FigmaPaths.IsSameOrInside("assets\\figma\\game\\x\\", "Assets/Figma/Game/"), Is.True);
        }

        [Test]
        public void Normalize_ResolvesDotSegments_SoAssetsCannotBeEscaped()
        {
            Assert.That(FigmaPaths.Normalize("Assets/UI/../Theme/./Bridge/"), Is.EqualTo("Assets/Theme/Bridge"));
            Assert.That(FigmaPaths.IsUnderAssets("Assets/../Packages"), Is.False);
            Assert.That(FigmaPaths.IsUnderAssets("Assets/x/../../ProjectSettings"), Is.False);
            Assert.That(FigmaPaths.IsSameOrInside("Assets/Game/../Figma/Shop/Bridge", "Assets/Figma/Shop"), Is.True);
            Assert.That(FigmaPaths.ValidateBridgeFolder("Assets/../Packages", null, "p").Count, Is.GreaterThan(0));
        }

        [Test]
        public void Normalize_Works()
        {
            Assert.That(FigmaPaths.Normalize(" ./Assets//UI\\Theme/ "), Is.EqualTo("Assets/UI/Theme"));
            Assert.That(FigmaPaths.Normalize(null), Is.Empty);
        }

        [Test]
        public void BridgePath_IsTheFixedFileNameInTheFolder()
        {
            Assert.That(FigmaPaths.BridgePath("Assets/UI/Theme/"), Is.EqualTo("Assets/UI/Theme/cuvara-theme-bridge.uss"));
        }

        [Test]
        public void ValidateBridgeFolder_AcceptsAGameOwnedFolder()
        {
            var diagnostics = FigmaPaths.ValidateBridgeFolder("Assets/UI/Theme", new[] { "Assets/Figma/Game" }, "p");
            Assert.That(diagnostics, Is.Empty);
        }

        [Test]
        public void ValidateBridgeFolder_RejectsTheConnectorFolderAndAnythingInsideIt()
        {
            foreach (var folder in new[] { "Assets/Figma/Game", "Assets/Figma/Game/Bridge" })
            {
                var diagnostics = FigmaPaths.ValidateBridgeFolder(folder, new[] { "Assets/Figma/Other", "Assets/Figma/Game" }, "p");
                Assert.That(diagnostics.Select(d => d.Code), Is.EqualTo(new[] { FigmaPaths.CodeBridgeInsideConnector }), folder);
                Assert.That(diagnostics[0].Severity, Is.EqualTo(FigmaDiagnosticSeverity.Error));
            }
        }

        [Test]
        public void ValidateBridgeFolder_RejectsAnEmptyFolder_AndOneOutsideAssets()
        {
            Assert.That(FigmaPaths.ValidateBridgeFolder("  ", null, "p").Select(d => d.Code),
                Is.EqualTo(new[] { FigmaPaths.CodeBridgeFolderMissing }));
            Assert.That(FigmaPaths.ValidateBridgeFolder("Packages/com.game/Theme", null, "p").Select(d => d.Code),
                Is.EqualTo(new[] { FigmaPaths.CodeBridgeNotGameOwned }));
        }

        [Test]
        public void ValidateConnectorFolder_RequiresAFolderUnderAssets()
        {
            Assert.That(FigmaPaths.ValidateConnectorFolder("Assets/Figma/Game", "p"), Is.Empty);
            Assert.That(FigmaPaths.ValidateConnectorFolder("Assets", "p"), Has.Count.EqualTo(1));
            Assert.That(FigmaPaths.ValidateConnectorFolder("", "p"), Has.Count.EqualTo(1));
            Assert.That(FigmaPaths.ValidateConnectorFolder("Library/Figma", "p"), Has.Count.EqualTo(1));
        }
    }
}
