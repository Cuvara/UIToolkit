namespace Cuvara.UIToolkit.Editor.Figma.Tests
{
    using System.Linq;
    using Cuvara.UIToolkit.FigmaCore;
    using NUnit.Framework;

    /// <summary>
    /// Release QA for profile path validation (<see cref="FigmaPaths"/>): the bridge folder must be
    /// game-owned and outside every connector folder, whatever the spelling of the path.
    /// </summary>
    public class FigmaPathsQaTests
    {
        private static string[] Codes(string bridgeFolder, params string[] connectorFolders)
        {
            return FigmaPaths.ValidateBridgeFolder(bridgeFolder, connectorFolders, "Assets/Profile.asset").Select(d => d.Code).ToArray();
        }

        [TestCase("Assets/UI/Theme", TestName = "Plain")]
        [TestCase("Assets/UI/Theme/", TestName = "TrailingSlash")]
        [TestCase("Assets\\UI\\Theme\\", TestName = "Backslashes")]
        [TestCase("  Assets/UI//Theme  ", TestName = "BlanksAndDoubleSlash")]
        [TestCase("./Assets/UI/./Theme", TestName = "DotSegments")]
        [TestCase("Assets/Figma/Game/../GameTheme", TestName = "DotDotToASibling")]
        [TestCase("Assets/Figma/GameHud", TestName = "PrefixOfConnectorNameOnly")]
        public void BridgeFolder_GameOwnedAndOutsideTheConnector_IsAccepted(string folder)
        {
            Assert.That(Codes(folder, "Assets/Figma/Game"), Is.Empty);
        }

        [TestCase("Assets/Figma/Game", TestName = "SameFolder")]
        [TestCase("Assets/Figma/Game/", TestName = "SameFolderTrailingSlash")]
        [TestCase("Assets/Figma/Game/Bridge", TestName = "Inside")]
        [TestCase("Assets\\Figma\\Game\\Bridge", TestName = "InsideWithBackslashes")]
        [TestCase("assets/figma/game/bridge", TestName = "InsideWithOtherCase")]
        [TestCase("Assets/UI/../Figma/Game", TestName = "DotDotIntoTheConnector")]
        [TestCase("Assets/Figma/Game/Sub/..", TestName = "DotDotBackToTheConnector")]
        public void BridgeFolder_InsideTheConnector_IsRejected(string folder)
        {
            Assert.That(Codes(folder, "Assets/Figma/Game"), Is.EqualTo(new[] { FigmaPaths.CodeBridgeInsideConnector }));
        }

        [TestCase("Assets/Figma/Game/Sub/..", TestName = "ConnectorWithDotDot")]
        [TestCase("Assets\\Figma\\Game\\", TestName = "ConnectorWithBackslashes")]
        [TestCase("ASSETS/FIGMA/GAME", TestName = "ConnectorWithOtherCase")]
        public void ConnectorFolder_SpellingDoesNotOpenAHole(string connector)
        {
            Assert.That(Codes("Assets/Figma/Game/Bridge", connector), Is.EqualTo(new[] { FigmaPaths.CodeBridgeInsideConnector }));
        }

        [TestCase("Packages/com.game/Theme", TestName = "Package")]
        [TestCase("Assets/../Packages/Theme", TestName = "DotDotOutOfAssets")]
        [TestCase("Assets/../../Elsewhere", TestName = "DotDotAboveTheProject")]
        [TestCase("../Assets/UI", TestName = "LeadingDotDot")]
        [TestCase("C:/Projects/Game/Assets/UI", TestName = "WindowsAbsolute")]
        [TestCase("C:\\Projects\\Game\\Assets\\UI", TestName = "WindowsAbsoluteBackslashes")]
        [TestCase("/home/dev/Game/Assets/UI", TestName = "UnixAbsolute")]
        [TestCase("Assetsx/UI", TestName = "AssetsPrefixOnly")]
        [TestCase("ProjectSettings", TestName = "ProjectSettings")]
        public void BridgeFolder_NotUnderAssets_IsNotGameOwned(string folder)
        {
            Assert.That(Codes(folder), Is.EqualTo(new[] { FigmaPaths.CodeBridgeNotGameOwned }));
        }

        [TestCase(null, TestName = "Null")]
        [TestCase("", TestName = "Empty")]
        [TestCase("   ", TestName = "Blank")]
        [TestCase("Assets/..", TestName = "ResolvesToNothing")]
        public void BridgeFolder_Missing_IsOneError(string folder)
        {
            var diagnostics = FigmaPaths.ValidateBridgeFolder(folder, new[] { "Assets/Figma/Game" }, "p");

            Assert.That(diagnostics.Select(d => d.Code), Is.EqualTo(new[] { FigmaPaths.CodeBridgeFolderMissing }));
            Assert.That(diagnostics[0].Severity, Is.EqualTo(FigmaDiagnosticSeverity.Error));
            Assert.That(diagnostics[0].File, Is.EqualTo("p"));
        }

        [Test]
        public void BridgeFolder_OutsideAssetsAndInsideAConnector_ReportsBoth()
        {
            Assert.That(Codes("Packages/x/Bridge", "Packages/x"),
                Is.EqualTo(new[] { FigmaPaths.CodeBridgeNotGameOwned, FigmaPaths.CodeBridgeInsideConnector }));
        }

        [Test]
        public void BridgeFolder_InsideNestedConnectors_ReportsEachConnector()
        {
            Assert.That(Codes("Assets/Figma/Game/Bridge", "Assets/Figma", "Assets/Figma/Game", "Assets/Other"),
                Is.EqualTo(new[] { FigmaPaths.CodeBridgeInsideConnector, FigmaPaths.CodeBridgeInsideConnector }));
        }

        [Test]
        public void ConnectorList_NullAndBlankEntries_AreIgnored()
        {
            Assert.That(Codes("Assets/UI", null, "", "  "), Is.Empty);
            Assert.That(FigmaPaths.ValidateBridgeFolder("Assets/UI", null, "p").Count, Is.EqualTo(0));
        }

        [TestCase("Assets/Figma/Game", 0, TestName = "Valid")]
        [TestCase("Assets\\Figma\\Game\\", 0, TestName = "ValidWithBackslashes")]
        [TestCase("./Assets/Figma/Game", 0, TestName = "ValidWithDot")]
        [TestCase("Assets", 1, TestName = "AssetsItself")]
        [TestCase("Assets/", 1, TestName = "AssetsWithSlash")]
        [TestCase("Assets/Figma/..", 1, TestName = "DotDotBackToAssets")]
        [TestCase("Assets/../Packages/Figma", 1, TestName = "DotDotOutOfAssets")]
        [TestCase("C:/Game/Assets/Figma", 1, TestName = "Absolute")]
        [TestCase(null, 1, TestName = "Null")]
        public void ConnectorFolder_MustBeAFolderUnderAssets(string folder, int errors)
        {
            var diagnostics = FigmaPaths.ValidateConnectorFolder(folder, "p");

            Assert.That(diagnostics.Count, Is.EqualTo(errors));
            Assert.That(diagnostics.All(d => d.Code == FigmaPaths.CodeConnectorFolderInvalid && d.Severity == FigmaDiagnosticSeverity.Error), Is.True);
        }

        [TestCase("Assets/UI/Theme/", "Assets/UI/Theme/cuvara-theme-bridge.uss")]
        [TestCase("Assets\\UI\\Theme", "Assets/UI/Theme/cuvara-theme-bridge.uss")]
        [TestCase("Assets/UI/x/../Theme", "Assets/UI/Theme/cuvara-theme-bridge.uss")]
        public void BridgePath_IsNormalized(string folder, string expected)
        {
            Assert.That(FigmaPaths.BridgePath(folder), Is.EqualTo(expected));
        }

        [TestCase("Assets/a/../../..", "..")]
        [TestCase("../../Assets", "../../Assets")]
        [TestCase("a/./b/../c", "a/c")]
        [TestCase("\\\\server\\share", "server/share")]
        public void Normalize_KeepsDotDotThatClimbsAboveTheStart(string path, string expected)
        {
            Assert.That(FigmaPaths.Normalize(path), Is.EqualTo(expected));
        }
    }
}
