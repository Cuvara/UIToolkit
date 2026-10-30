namespace Cuvara.UIToolkit.Editor.Figma
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Cuvara.UIToolkit.FigmaCore;

    /// <summary>One import result the <c>Tools~/FigmaImport</c> helper saved.</summary>
    public sealed class FigmaSavedImportResult
    {
        /// <summary>Path of the saved file, with <c>/</c> separators.</summary>
        public string Path;

        /// <summary>The parsed result, or null when the file could not be read.</summary>
        public FigmaImportResultInfo Info;

        /// <summary>What the result reports (<see cref="FigmaImportResultReader.ToDiagnostics"/>),
        /// or the one error that says why the file could not be read.</summary>
        public List<FigmaDiagnostic> Diagnostics = new List<FigmaDiagnostic>();

        /// <summary>When the file was last written (UTC).</summary>
        public DateTime WrittenUtc;
    }

    /// <summary>
    /// Finds the import results the headless helper saved (<c>Library/CuvaraFigma/last-import-*.json</c>)
    /// and matches them to a profile's connector folder. Reads only. The connector itself keeps no
    /// record of a headless import beyond its manifest, so this is the only place its notes
    /// (missing fonts, images that never arrived, dropped features) survive the call.
    /// </summary>
    public static class FigmaImportResultStore
    {
        // A result written this long before the connector's manifest describes an older import.
        private static readonly TimeSpan StaleMargin = TimeSpan.FromSeconds(5);

        /// <summary>Every saved result in <paramref name="directory"/>, newest first. A missing directory gives none.</summary>
        public static List<FigmaSavedImportResult> ReadAll(string directory)
        {
            var saved = new List<FigmaSavedImportResult>();
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return saved;

            foreach (var file in Directory.GetFiles(directory, FigmaImportResultReader.FilePrefix + "*.json"))
            {
                var path = file.Replace('\\', '/');
                var entry = new FigmaSavedImportResult { Path = path };
                try
                {
                    entry.WrittenUtc = File.GetLastWriteTimeUtc(file);
                    entry.Info = FigmaImportResultReader.Read(File.ReadAllText(file));
                    entry.Diagnostics = FigmaImportResultReader.ToDiagnostics(entry.Info, path);
                }
                catch (Exception exception) when (exception is FormatException || exception is IOException || exception is UnauthorizedAccessException)
                {
                    entry.Info = null;
                    entry.Diagnostics = new List<FigmaDiagnostic>
                    {
                        FigmaPaths.Error(FigmaImportResultReader.CodeUnreadable, path, "The saved import result could not be read: " + exception.Message),
                    };
                }

                saved.Add(entry);
            }

            saved.Sort((a, b) =>
            {
                var byTime = b.WrittenUtc.CompareTo(a.WrittenUtc);
                return byTime != 0 ? byTime : string.CompareOrdinal(a.Path, b.Path);
            });
            return saved;
        }

        /// <summary>The newest write time of any saved result in <paramref name="directory"/>, or
        /// <see cref="DateTime.MinValue"/> when there is none. Cheap: no file is read.</summary>
        public static DateTime NewestWriteUtc(string directory)
        {
            var newest = DateTime.MinValue;
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return newest;
            try
            {
                foreach (var file in Directory.GetFiles(directory, FigmaImportResultReader.FilePrefix + "*.json"))
                {
                    var written = File.GetLastWriteTimeUtc(file);
                    if (written > newest) newest = written;
                }
            }
            catch (IOException)
            {
                // The folder changed under us; the next focus reads it again.
            }

            return newest;
        }

        /// <summary>
        /// The newest saved result for a connector folder: one whose import folder is
        /// <paramref name="connectorFolder"/>, or, for a request refused before it had a folder,
        /// one whose Figma file name matches the manifest's <paramref name="manifestFileName"/>.
        /// </summary>
        /// <param name="saved">Results, newest first, as <see cref="ReadAll"/> returns them.</param>
        public static FigmaSavedImportResult FindLatest(IEnumerable<FigmaSavedImportResult> saved, string connectorFolder, string manifestFileName)
        {
            if (saved == null) return null;
            var wantedName = string.IsNullOrEmpty(manifestFileName) ? string.Empty : FigmaImportResultReader.SanitizeName(manifestFileName);
            foreach (var entry in saved)
            {
                var info = entry?.Info;
                if (info == null) continue;
                if (FigmaImportResultReader.IsForFolder(info, connectorFolder)) return entry;
                if (info.Folder.Length == 0 && wantedName.Length > 0 && info.FileName.Length > 0
                    && string.Equals(FigmaImportResultReader.SanitizeName(info.FileName), wantedName, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        /// <summary>True when the connector's manifest in <paramref name="connectorFolder"/> was
        /// written after the saved result: a later push (from Figma, not the helper) replaced that import.</summary>
        public static bool IsOlderThanManifest(FigmaSavedImportResult saved, string connectorFolder)
        {
            if (saved == null || string.IsNullOrEmpty(connectorFolder)) return false;
            var manifest = connectorFolder.TrimEnd('/') + "/" + FigmaPaths.ManifestFileName;
            if (!File.Exists(manifest)) return false;
            return File.GetLastWriteTimeUtc(manifest) > saved.WrittenUtc + StaleMargin;
        }
    }
}
