namespace Cuvara.UIToolkit.Editor.Figma.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using Cuvara.UIToolkit.FigmaCore;
    using NUnit.Framework;

    /// <summary>
    /// Release QA for <see cref="FigmaTokenBridge.Build"/>: empty, duplicate and invalid roles,
    /// missing or unknown Figma tokens, and output that stays a valid, injection-free sheet.
    /// </summary>
    public class FigmaTokenBridgeQaTests
    {
        private static FigmaTokenBridgeResult Build(IEnumerable<FigmaTokenRole> roles, ICollection<string> known = null)
        {
            return FigmaTokenBridge.Build("Game", roles, known ?? new HashSet<string> { "--brand", "--surface" }, "Assets/Game.asset");
        }

        private static FigmaTokenRole Role(string cuvara, string figma)
        {
            return new FigmaTokenRole(cuvara, figma);
        }

        private static string[] Codes(FigmaTokenBridgeResult result)
        {
            return result.Diagnostics.Select(d => d.Code).ToArray();
        }

        private static string[] AliasLines(FigmaTokenBridgeResult result)
        {
            return result.Content.Split('\n').Where(l => l.StartsWith("    ")).Select(l => l.Trim()).ToArray();
        }

        [Test]
        public void NullRoles_GiveAnEmptyRoot_AndOneNoRolesWarning()
        {
            var result = Build(null);

            Assert.That(result.TotalRoles, Is.EqualTo(0));
            Assert.That(Codes(result), Is.EqualTo(new[] { FigmaTokenBridge.CodeNoRoles }));
            Assert.That(result.Content, Does.EndWith(":root {\n}\n"));
        }

        [Test]
        public void EmptyRoleList_And_NullEntries_AreLikeNoRoles()
        {
            Assert.That(Codes(Build(new FigmaTokenRole[0])), Is.EqualTo(new[] { FigmaTokenBridge.CodeNoRoles }));
            Assert.That(Codes(Build(new FigmaTokenRole[] { null, null })), Is.EqualTo(new[] { FigmaTokenBridge.CodeNoRoles }));
        }

        [TestCase(null, TestName = "Null")]
        [TestCase("", TestName = "Empty")]
        [TestCase("   ", TestName = "Blank")]
        [TestCase("--color-primary", TestName = "NotCuvara")]
        [TestCase("cuvara-color-primary", TestName = "NoDashes")]
        [TestCase("--cuvara-", TestName = "NoName")]
        [TestCase("--cuvara-Color-Primary", TestName = "UpperCase")]
        [TestCase("--cuvara-color--primary", TestName = "DoubleDash")]
        [TestCase("--cuvara-color primary", TestName = "Space")]
        [TestCase("--cuvara-color-primary;}", TestName = "Injection")]
        [TestCase("--unity-color", TestName = "UnityVariable")]
        public void InvalidRoleName_IsAnErrorAndProducesNoLine(string cuvara)
        {
            var result = Build(new[] { Role(cuvara, "--brand") });

            Assert.That(Codes(result), Does.Contain(FigmaTokenBridge.CodeRoleInvalid));
            Assert.That(result.Diagnostics.Single(d => d.Code == FigmaTokenBridge.CodeRoleInvalid).Severity, Is.EqualTo(FigmaDiagnosticSeverity.Error));
            Assert.That(result.TotalRoles, Is.EqualTo(0));
            Assert.That(AliasLines(result), Is.Empty);
        }

        [Test]
        public void RoleName_IsTrimmed_AndDuplicatesAfterTrimmingAreRejected_FirstRowWins()
        {
            var result = Build(new[]
            {
                Role(" --cuvara-color-primary ", "--brand"),
                Role("--cuvara-color-primary", "--surface"),
            });

            Assert.That(Codes(result), Is.EqualTo(new[] { FigmaTokenBridge.CodeRoleDuplicate }));
            Assert.That(AliasLines(result), Is.EqualTo(new[] { "--cuvara-color-primary: var(--brand);" }));
            Assert.That(result.TotalRoles, Is.EqualTo(1));
        }

        [Test]
        public void DuplicateRole_EvenWhenTheFirstIsUnmapped_IsRejected()
        {
            var result = Build(new[]
            {
                Role("--cuvara-color-primary", ""),
                Role("--cuvara-color-primary", "--brand"),
            });

            Assert.That(Codes(result), Is.EqualTo(new[] { FigmaTokenBridge.CodeRoleUnmapped, FigmaTokenBridge.CodeRoleDuplicate, FigmaTokenBridge.CodeNoRoles }));
            Assert.That(AliasLines(result), Is.Empty);
        }

        [TestCase(null, TestName = "Null")]
        [TestCase("", TestName = "Empty")]
        [TestCase("  \t", TestName = "Blank")]
        public void AbsentFigmaToken_IsUnmappedInfo_NotAnError(string figma)
        {
            var result = Build(new[] { Role("--cuvara-color-primary", figma), Role("--cuvara-color-surface", "--surface") });

            var unmapped = result.Diagnostics.Single(d => d.Code == FigmaTokenBridge.CodeRoleUnmapped);
            Assert.That(unmapped.Severity, Is.EqualTo(FigmaDiagnosticSeverity.Info));
            Assert.That(result.TotalRoles, Is.EqualTo(2));
            Assert.That(result.MappedRoles, Is.EqualTo(1));
            Assert.That(result.VerifiedRoles, Is.EqualTo(1));
        }

        [TestCase("brand primary", TestName = "Space")]
        [TestCase("--brand);color:red", TestName = "Injection")]
        [TestCase("--brand}", TestName = "Brace")]
        [TestCase("--brand/*", TestName = "CommentOpener")]
        [TestCase("--", TestName = "DashesOnly")]
        [TestCase("--brand.primary", TestName = "Dot")]
        public void InvalidFigmaToken_IsAnErrorAndNeverReachesTheOutput(string figma)
        {
            var result = Build(new[] { Role("--cuvara-color-primary", figma) });

            Assert.That(Codes(result), Does.Contain(FigmaTokenBridge.CodeFigmaTokenInvalid));
            Assert.That(AliasLines(result), Is.Empty);
            Assert.That(result.Content, Does.Not.Contain("red"));
            Assert.That(result.Content.Count(c => c == '{'), Is.EqualTo(1));
            Assert.That(result.Content.Count(c => c == '}'), Is.EqualTo(1));
        }

        [Test]
        public void FigmaToken_IsCaseSensitive_AgainstTheImport()
        {
            var result = Build(new[] { Role("--cuvara-color-primary", "--Brand") });

            Assert.That(Codes(result), Is.EqualTo(new[] { FigmaTokenBridge.CodeFigmaTokenUnknown }));
            Assert.That(result.VerifiedRoles, Is.EqualTo(0));
        }

        [Test]
        public void EmptyKnownList_IsNotTheSameAsUnreadable()
        {
            var empty = Build(new[] { Role("--cuvara-color-primary", "--brand") }, new HashSet<string>());
            var unreadable = FigmaTokenBridge.Build("Game", new[] { Role("--cuvara-color-primary", "--brand") }, null, "p");

            Assert.That(Codes(empty), Is.EqualTo(new[] { FigmaTokenBridge.CodeFigmaTokenUnknown }));
            Assert.That(Codes(unreadable), Is.EqualTo(new[] { FigmaTokenBridge.CodeTokensUnverified }));
        }

        [Test]
        public void OneFigmaToken_MayFeedSeveralRoles()
        {
            var result = Build(new[] { Role("--cuvara-color-primary", "brand"), Role("--cuvara-color-on-surface", "--brand") });

            Assert.That(Codes(result), Is.Empty);
            Assert.That(AliasLines(result), Is.EqualTo(new[]
            {
                "--cuvara-color-on-surface: var(--brand);",
                "--cuvara-color-primary: var(--brand);",
            }));
            Assert.That(result.VerifiedRoles, Is.EqualTo(2));
        }

        [Test]
        public void Output_ParsesAsAWellFormedTokenSheet_WithEveryAliasDeclared()
        {
            var roles = FigmaTokenBridge.CreateDefaultRoles();
            foreach (var role in roles) role.figmaToken = "--brand";

            var result = Build(roles);
            var sheet = FigmaUss.Parse(result.Content);

            Assert.That(sheet.Problem, Is.Null);
            Assert.That(sheet.Rules.Select(r => r.Selector), Is.EqualTo(new[] { ":root" }));
            Assert.That(sheet.Rules[0].Declarations.Select(d => d.Property), Is.EqualTo(FigmaTokenBridge.DefaultRoles.OrderBy(r => r, System.StringComparer.Ordinal)));
            Assert.That(sheet.Rules[0].Declarations.All(d => d.Value == "var(--brand)"), Is.True);
            Assert.That(result.Content, Does.Not.Contain("\r"));
        }

        [TestCase("Game */ .x { color: red; } /*", TestName = "CommentCloser")]
        [TestCase("Line1\r\nLine2", TestName = "Newlines")]
        [TestCase(null, TestName = "Null")]
        public void ProfileName_CannotBreakTheSheet(string name)
        {
            var result = FigmaTokenBridge.Build(name, new[] { Role("--cuvara-color-primary", "--brand") }, null, "p");
            var sheet = FigmaUss.Parse(result.Content);

            Assert.That(sheet.Problem, Is.Null);
            Assert.That(sheet.Rules.Select(r => r.Selector), Is.EqualTo(new[] { ":root" }));
            Assert.That(result.Content.Split('\n')[0], Does.StartWith("/* ").And.EndWith(" */"));
        }

        [Test]
        public void ParseDeclaredTokens_HandlesCrlfMultipleSheetsAndSelectorsThatLookLikeTokens()
        {
            var tokens = FigmaTokenBridge.ParseDeclaredTokens(":root {\r\n  --a: 1px;\r\n  --b :2px;\r\n}\r\n.x--y:hover { color: var(--c); }\r\n");

            Assert.That(tokens.OrderBy(t => t, System.StringComparer.Ordinal), Is.EqualTo(new[] { "--a", "--b" }));
        }
    }
}
