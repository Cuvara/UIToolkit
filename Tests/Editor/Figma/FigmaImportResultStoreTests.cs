namespace Cuvara.UIToolkit.Editor.Figma.Tests
{
    using System;
    using System.IO;
    using System.Linq;
    using Cuvara.UIToolkit.FigmaCore;
    using NUnit.Framework;

    /// <summary>
    /// How the diagnostics window finds the result the headless helper saved for a profile:
    /// newest first, matched by import folder (or, for a refused request, by Figma file name),
    /// unreadable files reported, and a result older than the connector's manifest flagged.
    /// </summary>
    public class FigmaImportResultStoreTests
    {
        private string directory;

        [SetUp]
        public void SetUp()
        {
            this.directory = Path.Combine(Path.GetTempPath(), "cuvara-import-results-" + Guid.NewGuid().ToString("N")).Replace('\\', '/');
            Directory.CreateDirectory(this.directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(this.directory)) Directory.Delete(this.directory, recursive: true);
        }

        private string Save(string name, string json, DateTime writtenUtc)
        {
            var path = this.directory + "/" + name;
            File.WriteAllText(path, json);
            File.SetLastWriteTimeUtc(path, writtenUtc);
            return path;
        }

        private static string Result(bool ok, string folder, string fileName = "Shop Demo", string code = "")
        {
            return "{ \"ok\": " + (ok ? "true" : "false") + ", \"code\": \"" + code + "\", \"folder\": \"" + folder + "\", "
                + "\"facts\": [ { \"kind\": \"MissingFonts\", \"severity\": \"warning\", \"count\": 1, \"items\": [ \"Inter\" ] } ], "
                + "\"request\": { \"fileName\": \"" + fileName + "\" } }";
        }

        [Test]
        public void ReadAll_ReadsOnlySavedResults_NewestFirst_AndReportsUnreadableOnes()
        {
            var old = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            Save("last-import-Old.json", Result(true, "Assets/Figma/Old"), old);
            Save("last-import-New.json", Result(true, "Assets/Figma/New"), old.AddHours(1));
            Save("last-import-Broken.json", "{ not json", old.AddMinutes(30));
            Save("unrelated.json", Result(true, "Assets/Figma/Other"), old.AddHours(2));

            var saved = FigmaImportResultStore.ReadAll(this.directory);

            Assert.That(saved.Select(s => Path.GetFileName(s.Path)),
                Is.EqualTo(new[] { "last-import-New.json", "last-import-Broken.json", "last-import-Old.json" }));
            Assert.That(saved[1].Info, Is.Null);
            Assert.That(saved[1].Diagnostics.Select(d => d.Code), Is.EqualTo(new[] { FigmaImportResultReader.CodeUnreadable }));
            Assert.That(saved[0].Diagnostics.Any(d => d.Code == "cuvara.import.fact.MissingFonts"), Is.True);
        }

        [Test]
        public void ReadAll_OfAMissingDirectory_IsEmpty()
        {
            Assert.That(FigmaImportResultStore.ReadAll(this.directory + "/absent"), Is.Empty);
            Assert.That(FigmaImportResultStore.NewestWriteUtc(this.directory + "/absent"), Is.EqualTo(DateTime.MinValue));
        }

        [Test]
        public void FindLatest_MatchesTheImportFolder_NotAPrefixOfIt()
        {
            var at = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            Save("last-import-A.json", Result(true, "Assets/Figma/Shop-Demo"), at);
            Save("last-import-B.json", Result(false, "Assets/Figma/Shop-Demo2", code: "files_not_imported"), at.AddHours(1));
            var saved = FigmaImportResultStore.ReadAll(this.directory);

            var latest = FigmaImportResultStore.FindLatest(saved, "Assets/Figma/Shop-Demo", "Shop Demo");
            Assert.That(latest, Is.Not.Null);
            Assert.That(Path.GetFileName(latest.Path), Is.EqualTo("last-import-A.json"));
            Assert.That(FigmaImportResultStore.FindLatest(saved, "Assets/Figma/Other", "Other"), Is.Null);
        }

        [Test]
        public void FindLatest_TakesTheNewestOfSeveralForOneFolder()
        {
            var at = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            Save("last-import-First.json", Result(true, "Assets/Figma/Shop-Demo"), at);
            Save("last-import-Second.json", Result(false, "Assets/Figma/Shop-Demo", code: "files_not_imported"), at.AddMinutes(5));

            var latest = FigmaImportResultStore.FindLatest(FigmaImportResultStore.ReadAll(this.directory), "Assets/Figma/Shop-Demo", null);
            Assert.That(Path.GetFileName(latest.Path), Is.EqualTo("last-import-Second.json"));
            Assert.That(latest.Info.Ok, Is.False);
        }

        [Test]
        public void FindLatest_ARefusalWithoutFolder_MatchesByFigmaFileName()
        {
            Save("last-import-Shop-Demo.json",
                "{ \"ok\": false, \"code\": \"plugin_not_connected\", \"error\": \"x\", \"request\": { \"fileName\": \"Shop Demo\" } }",
                DateTime.UtcNow);
            var saved = FigmaImportResultStore.ReadAll(this.directory);

            Assert.That(FigmaImportResultStore.FindLatest(saved, "Assets/Figma/Shop-Demo", "Shop Demo"), Is.Not.Null);
            Assert.That(FigmaImportResultStore.FindLatest(saved, "Assets/Figma/Shop-Demo", "Other File"), Is.Null);
            Assert.That(FigmaImportResultStore.FindLatest(saved, "Assets/Figma/Shop-Demo", null), Is.Null);
        }

        [Test]
        public void IsOlderThanManifest_FlagsAResultAPushFromFigmaReplaced()
        {
            var connector = this.directory + "/Figma/Shop-Demo";
            Directory.CreateDirectory(connector);
            var manifest = connector + "/" + FigmaPaths.ManifestFileName;
            File.WriteAllText(manifest, "{}");

            var at = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
            Save("last-import-Shop-Demo.json", Result(true, "Assets/Figma/Shop-Demo"), at);
            var saved = FigmaImportResultStore.ReadAll(this.directory).Single();

            File.SetLastWriteTimeUtc(manifest, at.AddSeconds(2));
            Assert.That(FigmaImportResultStore.IsOlderThanManifest(saved, connector), Is.False, "the helper's own import");

            File.SetLastWriteTimeUtc(manifest, at.AddMinutes(10));
            Assert.That(FigmaImportResultStore.IsOlderThanManifest(saved, connector), Is.True, "a later push");

            Assert.That(FigmaImportResultStore.IsOlderThanManifest(saved, this.directory + "/NoManifest"), Is.False);
        }

        [Test]
        public void NewestWriteUtc_TracksTheNewestSavedResult()
        {
            var at = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            Save("last-import-A.json", Result(true, "Assets/Figma/A"), at);
            Save("last-import-B.json", Result(true, "Assets/Figma/B"), at.AddMinutes(3));
            Assert.That(FigmaImportResultStore.NewestWriteUtc(this.directory), Is.EqualTo(at.AddMinutes(3)));
        }
    }
}
