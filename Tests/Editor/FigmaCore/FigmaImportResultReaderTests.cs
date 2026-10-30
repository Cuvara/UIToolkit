namespace Cuvara.UIToolkit.Editor.Tests.FigmaCore
{
    using System;
    using System.Linq;
    using Cuvara.UIToolkit.FigmaCore;
    using NUnit.Framework;

    /// <summary>
    /// <see cref="FigmaImportResultReader"/> against the shapes connector 0.3.0-exp.1 returns:
    /// <c>FigmaImportResult</c> (Editor/Import/FigmaImportResult.cs), the stored reply
    /// <c>figma_import_status</c> describes (FigmaRequestStore.Describe and Trim: no notes,
    /// facts kept), a refusal before queueing, and the helper's saved file.
    /// </summary>
    public class FigmaImportResultReaderTests
    {
        // figma_import_status after a clean import, as Describe builds it around Trim(result).
        private const string DoneReply = @"{
    ""ok"": true, ""requestId"": ""r-1a2b3c4d"", ""state"": ""done"", ""fileName"": ""Shop Demo"", ""nodeId"": ""12:34"", ""elapsedSeconds"": 7,
    ""result"": {
        ""ok"": true, ""code"": """", ""error"": """", ""schemaMismatch"": false, ""converted"": true, ""ussErrors"": false,
        ""nodes"": 42, ""templates"": 3, ""uxml"": ""Assets/Figma/Shop-Demo/ShopScreen.uxml"", ""folder"": ""Assets/Figma/Shop-Demo"",
        ""files"": [ ""Assets/Figma/Shop-Demo/ShopScreen.uxml"", ""Assets/Figma/Shop-Demo/ShopScreen.uss"", ""Assets/Figma/Shop-Demo/tokens.uss"" ],
        ""removed"": [ ""Assets/Figma/Shop-Demo/Old.uxml"" ],
        ""invalidFiles"": [],
        ""facts"": [
            { ""kind"": ""MissingFonts"", ""severity"": ""warning"", ""count"": 2, ""items"": [ ""Inter"", ""Roboto Mono"" ] },
            { ""kind"": ""MissingImages"", ""severity"": ""warning"", ""count"": 1, ""items"": [ ""Hero"" ] },
            { ""kind"": ""DroppedGap"", ""severity"": ""warning"", ""count"": 3, ""items"": [ ""Row"", ""Card"", ""List"" ] },
            { ""kind"": ""MultiMode"", ""severity"": ""message"", ""count"": 2, ""items"": [ ""Light"", ""Dark"" ], ""extra"": [ ""light.uss"", ""dark.uss"" ] }
        ],
        ""inputWarnings"": [ ""node 12:99 has an unknown type"" ],
        ""elapsedMs"": 1834
    }
}";

        // A push Unity could not import cleanly: state failed, code from the result.
        private const string FilesNotImportedReply = @"{
    ""ok"": false, ""requestId"": ""r-00000001"", ""state"": ""failed"", ""fileName"": ""Shop Demo"", ""nodeId"": ""12:34"", ""elapsedSeconds"": 9,
    ""code"": ""files_not_imported"", ""error"": ""Unity did not import every file."",
    ""result"": {
        ""ok"": false, ""code"": ""files_not_imported"", ""error"": ""2 files did not import"", ""converted"": true, ""ussErrors"": true,
        ""folder"": ""Assets/Figma/Shop-Demo"", ""files"": [ ""Assets/Figma/Shop-Demo/ShopScreen.uss"" ],
        ""invalidFiles"": [ ""Assets/Figma/Shop-Demo/ShopScreen.uss"", ""Assets/Figma/Shop-Demo/Card.uss"" ],
        ""facts"": [], ""inputWarnings"": [], ""elapsedMs"": 900
    }
}";

        // figma_import_url refused before queueing (FigmaRequestRefusal.ToReply), as the helper saves it.
        private const string RefusalSaved = @"{
    ""ok"": false, ""code"": ""plugin_not_connected"", ""error"": ""No UI Toolkit Connector plugin is connected to this Editor."", ""project"": ""Lab"",
    ""request"": { ""url"": ""https://www.figma.com/design/AbC123/Shop-Demo?node-id=12-34"", ""fileName"": ""Shop Demo"", ""savedAt"": ""2026-10-30T15:00:00Z"", ""state"": """" }
}";

        [Test]
        public void Read_DoneReply_TakesTheRequestFieldsAndTheResult()
        {
            var info = FigmaImportResultReader.Read(DoneReply);

            Assert.That(info.HasOk, Is.True);
            Assert.That(info.Ok, Is.True);
            Assert.That(info.State, Is.EqualTo("done"));
            Assert.That(info.RequestId, Is.EqualTo("r-1a2b3c4d"));
            Assert.That(info.FileName, Is.EqualTo("Shop Demo"));
            Assert.That(info.Folder, Is.EqualTo("Assets/Figma/Shop-Demo"));
            Assert.That(info.Uxml, Is.EqualTo("Assets/Figma/Shop-Demo/ShopScreen.uxml"));
            Assert.That(info.Converted, Is.True);
            Assert.That(info.ElapsedMs, Is.EqualTo(1834));
            Assert.That(info.Files.Count, Is.EqualTo(3));
            Assert.That(info.Removed.Count, Is.EqualTo(1));
            Assert.That(info.InputWarnings.Count, Is.EqualTo(1));
            Assert.That(info.Facts.Select(f => f.Kind), Is.EqualTo(new[] { "MissingFonts", "MissingImages", "DroppedGap", "MultiMode" }));
            Assert.That(info.Facts[0].Count, Is.EqualTo(2));
            Assert.That(info.Facts[0].Items, Is.EqualTo(new[] { "Inter", "Roboto Mono" }));
        }

        [Test]
        public void ToDiagnostics_DoneReply_MapsFactsWithTheConnectorSeverity()
        {
            var diagnostics = FigmaImportResultReader.ToDiagnostics(FigmaImportResultReader.Read(DoneReply), "Library/CuvaraFigma/last-import-Shop-Demo.json");

            Assert.That(Severity(diagnostics, "cuvara.import.fact.MissingFonts"), Is.EqualTo(FigmaDiagnosticSeverity.Warning));
            Assert.That(Severity(diagnostics, "cuvara.import.fact.MissingImages"), Is.EqualTo(FigmaDiagnosticSeverity.Warning));
            Assert.That(Severity(diagnostics, "cuvara.import.fact.DroppedGap"), Is.EqualTo(FigmaDiagnosticSeverity.Warning));
            Assert.That(Severity(diagnostics, "cuvara.import.fact.MultiMode"), Is.EqualTo(FigmaDiagnosticSeverity.Info));
            Assert.That(Severity(diagnostics, FigmaImportResultReader.CodeInputWarning), Is.EqualTo(FigmaDiagnosticSeverity.Warning));
            Assert.That(Severity(diagnostics, FigmaImportResultReader.CodeSummary), Is.EqualTo(FigmaDiagnosticSeverity.Info));
            Assert.That(FigmaDiagnostics.Count(diagnostics, FigmaDiagnosticSeverity.Error), Is.EqualTo(0));

            var fonts = diagnostics.Single(d => d.Code == "cuvara.import.fact.MissingFonts");
            Assert.That(fonts.Message, Does.Contain("'Inter'").And.Contain("'Roboto Mono'").And.Contain("Font map"));
            Assert.That(fonts.File, Is.EqualTo("Library/CuvaraFigma/last-import-Shop-Demo.json"));
        }

        [Test]
        public void ToDiagnostics_FilesNotImported_IsAnErrorPerFile_PlusUssErrors()
        {
            var info = FigmaImportResultReader.Read(FilesNotImportedReply);
            var diagnostics = FigmaImportResultReader.ToDiagnostics(info, "saved.json");

            Assert.That(info.Ok, Is.False);
            Assert.That(info.State, Is.EqualTo("failed"));
            Assert.That(info.Code, Is.EqualTo("files_not_imported"));
            Assert.That(info.Error, Is.EqualTo("2 files did not import"), "the result's own error wins over the reply's");

            var failed = diagnostics.Single(d => d.Code == FigmaImportResultReader.CodeFailed);
            Assert.That(failed.Severity, Is.EqualTo(FigmaDiagnosticSeverity.Error));
            Assert.That(failed.Message, Does.Contain("files_not_imported"));
            Assert.That(Severity(diagnostics, FigmaImportResultReader.CodeUssErrors), Is.EqualTo(FigmaDiagnosticSeverity.Error));
            Assert.That(diagnostics.Where(d => d.Code == FigmaImportResultReader.CodeInvalidFile).Select(d => d.File),
                Is.EquivalentTo(new[] { "Assets/Figma/Shop-Demo/ShopScreen.uss", "Assets/Figma/Shop-Demo/Card.uss" }));
            Assert.That(FigmaDiagnostics.Count(diagnostics, FigmaDiagnosticSeverity.Error), Is.EqualTo(4));
        }

        [Test]
        public void ToDiagnostics_Refusal_IsOneErrorWithTheCode_AndTheHelperRequestFields()
        {
            var info = FigmaImportResultReader.Read(RefusalSaved);
            var diagnostics = FigmaImportResultReader.ToDiagnostics(info, "saved.json");

            Assert.That(info.Ok, Is.False);
            Assert.That(info.Folder, Is.Empty);
            Assert.That(info.FileName, Is.EqualTo("Shop Demo"));
            Assert.That(info.SavedAt, Is.EqualTo("2026-10-30T15:00:00Z"));
            Assert.That(info.Url, Does.StartWith("https://www.figma.com/design/"));
            Assert.That(FigmaDiagnostics.Count(diagnostics, FigmaDiagnosticSeverity.Error), Is.EqualTo(1));
            Assert.That(diagnostics.Single(d => d.Code == FigmaImportResultReader.CodeFailed).Message, Does.Contain("plugin_not_connected"));
        }

        // What figma-import.py saves when --max-wait runs out while the request is still open.
        private const string TimedOutSaved = @"{
    ""ok"": false,
    ""request"": { ""requestId"": ""r-1a2b3c4d"", ""state"": ""exporting"", ""code"": """", ""error"": """", ""reason"": """",
                   ""fileName"": ""Shop Demo"", ""nodeId"": ""12:34"", ""url"": ""https://www.figma.com/design/AbC123/Shop-Demo?node-id=12-34"",
                   ""savedAt"": ""2026-10-30T15:00:00Z"", ""helper"": ""figma-import.py"", ""finished"": false, ""waitedSeconds"": 600.0 }
}";

        [TestCase("queued")]
        [TestCase("exporting")]
        [TestCase("importing")]
        public void ToDiagnostics_AnUnfinishedRequest_IsOneIncompleteWarning_NotAFailure(string state)
        {
            var info = FigmaImportResultReader.Read(TimedOutSaved.Replace("\"exporting\"", "\"" + state + "\""));
            var diagnostics = FigmaImportResultReader.ToDiagnostics(info, "saved.json");

            Assert.That(info.Finished, Is.False);
            Assert.That(info.State, Is.EqualTo(state));
            Assert.That(FigmaDiagnostics.Count(diagnostics, FigmaDiagnosticSeverity.Error), Is.EqualTo(0));
            var incomplete = diagnostics.Single(d => d.Code == FigmaImportResultReader.CodeIncomplete);
            Assert.That(incomplete.Severity, Is.EqualTo(FigmaDiagnosticSeverity.Warning));
            Assert.That(incomplete.Message, Does.Contain("r-1a2b3c4d").And.Contain(state));
            Assert.That(diagnostics.Single(d => d.Code == FigmaImportResultReader.CodeSummary).Message, Does.Contain("incomplete"));
        }

        [Test]
        public void Read_AnOpenStatusReply_IsUnfinished_EvenWithoutTheHelperFlag()
        {
            var info = FigmaImportResultReader.Read(@"{ ""ok"": true, ""requestId"": ""r-1"", ""state"": ""queued"", ""retryAfterSeconds"": 5 }");
            Assert.That(info.Finished, Is.False);
            Assert.That(FigmaImportResultReader.ToDiagnostics(info, "f").Any(d => d.Code == FigmaImportResultReader.CodeIncomplete), Is.True);
        }

        [Test]
        public void Read_FinishedResults_AreFinished()
        {
            Assert.That(FigmaImportResultReader.Read(DoneReply).Finished, Is.True);
            Assert.That(FigmaImportResultReader.Read(FilesNotImportedReply).Finished, Is.True);
            Assert.That(FigmaImportResultReader.Read(RefusalSaved).Finished, Is.True);
        }

        [Test]
        public void Read_UnwrapsAPipelineEnvelope()
        {
            var info = FigmaImportResultReader.Read(@"{ ""success"": true, ""result"": " + DoneReply + " }");
            Assert.That(info.Ok, Is.True);
            Assert.That(info.State, Is.EqualTo("done"));
            Assert.That(info.Folder, Is.EqualTo("Assets/Figma/Shop-Demo"));
        }

        [Test]
        public void ToDiagnostics_UsesNotesOnlyWhenThereAreNoFacts()
        {
            const string direct = @"{ ""ok"": true, ""folder"": ""Assets/Figma/X"", ""notes"": [ ""Missing fonts: Inter."" ], ""facts"": [] }";
            var diagnostics = FigmaImportResultReader.ToDiagnostics(FigmaImportResultReader.Read(direct), "f");
            Assert.That(diagnostics.Count(d => d.Code == FigmaImportResultReader.CodeNote), Is.EqualTo(1));

            const string both = @"{ ""ok"": true, ""notes"": [ ""Missing fonts: Inter."" ],
                ""facts"": [ { ""kind"": ""MissingFonts"", ""severity"": ""warning"", ""count"": 1, ""items"": [ ""Inter"" ] } ] }";
            diagnostics = FigmaImportResultReader.ToDiagnostics(FigmaImportResultReader.Read(both), "f");
            Assert.That(diagnostics.Count(d => d.Code == FigmaImportResultReader.CodeNote), Is.EqualTo(0));
            Assert.That(diagnostics.Count(d => d.Code == "cuvara.import.fact.MissingFonts"), Is.EqualTo(1));
        }

        [Test]
        public void ToDiagnostics_UnknownKindAndSeverity_AreWarnings_AndExtraItemsAreCounted()
        {
            const string json = @"{ ""ok"": true, ""facts"": [ { ""kind"": ""SomethingNew"", ""severity"": ""odd"", ""count"": 14,
                ""items"": [ ""a"", ""b"", ""c"", ""d"", ""e"", ""f"", ""g"", ""h"", ""i"", ""j"" ] } ] }";
            var diagnostic = FigmaImportResultReader.ToDiagnostics(FigmaImportResultReader.Read(json), "f")
                .Single(d => d.Code == "cuvara.import.fact.SomethingNew");

            Assert.That(diagnostic.Severity, Is.EqualTo(FigmaDiagnosticSeverity.Warning));
            Assert.That(diagnostic.Message, Does.Contain("+4 more"));
        }

        [Test]
        public void Read_IsTolerant_UnknownFieldsIgnored_WrongTypesReadAsMissing()
        {
            const string json = @"{ ""ok"": true, ""future"": { ""x"": 1 }, ""files"": ""not-an-array"", ""folder"": 5,
                ""facts"": [ 3, { ""kind"": ""MissingFonts"", ""count"": ""two"", ""items"": [ 1, ""Inter"" ] } ] }";
            var info = FigmaImportResultReader.Read(json);

            Assert.That(info.Ok, Is.True);
            Assert.That(info.Files.Count, Is.EqualTo(0));
            Assert.That(info.Folder, Is.Empty);
            Assert.That(info.Facts.Count, Is.EqualTo(1));
            Assert.That(info.Facts[0].Count, Is.EqualTo(0));
            Assert.That(info.Facts[0].Items, Is.EqualTo(new[] { "Inter" }));
        }

        [Test]
        public void ToDiagnostics_WithoutAnOkField_IsUnreadable()
        {
            var diagnostics = FigmaImportResultReader.ToDiagnostics(FigmaImportResultReader.Read(@"{ ""fileName"": ""x"" }"), "f");
            Assert.That(diagnostics.Select(d => d.Code), Is.EqualTo(new[] { FigmaImportResultReader.CodeUnreadable }));
            Assert.That(diagnostics[0].Severity, Is.EqualTo(FigmaDiagnosticSeverity.Error));
        }

        [Test]
        public void Diagnose_InvalidJson_IsOneErrorNotAnException()
        {
            var diagnostics = FigmaImportResultReader.Diagnose("{ not json", "f");
            Assert.That(diagnostics.Select(d => d.Code), Is.EqualTo(new[] { FigmaImportResultReader.CodeUnreadable }));
            Assert.Throws<FormatException>(() => FigmaImportResultReader.Read("[1, 2]"));
        }

        [Test]
        public void IsForFolder_IgnoresSlashStyleCaseAndTrailingSlash()
        {
            var info = FigmaImportResultReader.Read(DoneReply);
            Assert.That(FigmaImportResultReader.IsForFolder(info, "assets\\figma\\shop-demo\\"), Is.True);
            Assert.That(FigmaImportResultReader.IsForFolder(info, "Assets/Figma/Shop-Demo2"), Is.False);
            Assert.That(FigmaImportResultReader.IsForFolder(info, ""), Is.False);
            Assert.That(FigmaImportResultReader.IsForFolder(FigmaImportResultReader.Read(RefusalSaved), "Assets/Figma/Shop-Demo"), Is.False);
        }

        [Test]
        public void SanitizeName_FollowsTheConnectorRule()
        {
            Assert.That(FigmaImportResultReader.SanitizeName("Shop Demo (v2)"), Is.EqualTo("Shop-Demo--v2"));
            Assert.That(FigmaImportResultReader.SanitizeName("ui_kit-1"), Is.EqualTo("ui_kit-1"));
            Assert.That(FigmaImportResultReader.SanitizeName("***"), Is.EqualTo("import"));
            Assert.That(FigmaImportResultReader.SanitizeName(null), Is.EqualTo("import"));
        }

        private static FigmaDiagnosticSeverity Severity(System.Collections.Generic.IEnumerable<FigmaDiagnostic> diagnostics, string code)
        {
            return diagnostics.Single(d => d.Code == code).Severity;
        }
    }
}
