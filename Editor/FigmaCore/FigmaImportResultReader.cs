namespace Cuvara.UIToolkit.FigmaCore
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    /// <summary>
    /// One fact of a connector import result (<c>facts[]</c>): the connector's import note as
    /// data. <see cref="Kind"/> is the connector's note kind, such as <c>MissingFonts</c>.
    /// </summary>
    public sealed class FigmaImportResultFact
    {
        /// <summary>The connector's note kind name; never renamed by the connector.</summary>
        public string Kind;

        /// <summary><c>warning</c> or <c>message</c> (connector 0.3.0-exp.1).</summary>
        public string Severity;

        /// <summary>The headline count (layers, frames, fonts...).</summary>
        public int Count;

        /// <summary>What the fact points at: font families, layer or component names. The
        /// connector caps the list at 10 entries of 48 characters in a stored reply.</summary>
        public IReadOnlyList<string> Items;
    }

    /// <summary>
    /// What this package reads from a saved connector import result: the <c>FigmaImportResult</c>
    /// that <c>figma_import</c> returns and <c>figma_import_status</c> carries in
    /// <c>result</c>, plus the request fields the <c>Tools~/FigmaImport</c> helper adds.
    /// </summary>
    public sealed class FigmaImportResultInfo
    {
        /// <summary>The import converted and every generated file imported cleanly.</summary>
        public bool Ok;

        /// <summary>False when the JSON had no <c>ok</c> field: then it is not an import result.</summary>
        public bool HasOk;

        /// <summary>Error code, such as <c>files_not_imported</c> or <c>plugin_not_connected</c>; empty when ok.</summary>
        public string Code = string.Empty;

        public string Error = string.Empty;

        /// <summary>The request state when known: <c>done</c>, <c>failed</c>, <c>expired</c>.</summary>
        public string State = string.Empty;

        /// <summary>False when the request was still open (<c>queued</c>, <c>exporting</c>,
        /// <c>importing</c>) when the helper stopped waiting: there is no import result yet.</summary>
        public bool Finished = true;

        /// <summary>Why the plugin failed a request, when it said.</summary>
        public string Reason = string.Empty;

        public string RequestId = string.Empty;

        /// <summary>The Figma file name, when known.</summary>
        public string FileName = string.Empty;

        /// <summary>The Figma link the helper imported.</summary>
        public string Url = string.Empty;

        /// <summary>When the helper saved the result (ISO 8601, UTC), or empty.</summary>
        public string SavedAt = string.Empty;

        /// <summary>The import folder, a project path such as <c>Assets/Figma/Shop-Demo</c>; empty when refused.</summary>
        public string Folder = string.Empty;

        /// <summary>The screen UXML the import wrote.</summary>
        public string Uxml = string.Empty;

        public bool Converted;
        public bool UssErrors;
        public bool SchemaMismatch;
        public long ElapsedMs;

        public IReadOnlyList<string> Files = Array.Empty<string>();
        public IReadOnlyList<string> Removed = Array.Empty<string>();
        public IReadOnlyList<string> InvalidFiles = Array.Empty<string>();
        public IReadOnlyList<string> Notes = Array.Empty<string>();
        public IReadOnlyList<string> InputWarnings = Array.Empty<string>();
        public IReadOnlyList<FigmaImportResultFact> Facts = Array.Empty<FigmaImportResultFact>();
    }

    /// <summary>
    /// Reads a saved connector import result and turns it into <see cref="FigmaDiagnostic"/>
    /// items: why an import failed, and what it dropped or degraded (missing fonts, images that
    /// never reached Unity, unsupported features, USS errors, files Unity did not import).
    /// </summary>
    /// <remarks>
    /// <para><b>Unity-free</b>, like the rest of <c>Editor/FigmaCore</c>. Tolerant, because the
    /// connector is experimental: unknown fields are ignored, missing ones read as empty, and a
    /// field of the wrong type reads as missing. It accepts the bare result, a
    /// <c>figma_import_status</c> reply (<c>{state, result}</c>), a Pipeline envelope
    /// (<c>{success, result}</c>) around either, and the helper's saved file (the result with a
    /// <c>request</c> object). Shapes were checked against connector 0.3.0-exp.1
    /// (<c>FigmaImportResult.cs</c>, <c>FigmaRequestStore.Describe</c>).</para>
    ///
    /// <para>Severity: a failed import (<c>ok</c> false), USS errors, files Unity did not import
    /// and a schema mismatch are errors. A request the helper stopped waiting for while it was
    /// still <c>queued</c>, <c>exporting</c> or <c>importing</c> is an incomplete import: a
    /// warning, not a failure. Facts keep the connector's own severity (<c>warning</c>
    /// is a warning, <c>message</c> is info; an unknown severity is a warning, as an unknown note
    /// kind is a warning in the connector). Input warnings are warnings. Notes, the connector's
    /// localized text, are used only when a result carries no facts.</para>
    /// </remarks>
    public static class FigmaImportResultReader
    {
        /// <summary>Project-relative folder the helper saves results in by default.</summary>
        public const string DefaultFolder = "Library/CuvaraFigma";

        /// <summary>File name prefix of a saved result: <c>last-import-&lt;file&gt;.json</c>.</summary>
        public const string FilePrefix = "last-import-";

        public const string CodeUnreadable     = "cuvara.import.result-unreadable";
        public const string CodeFailed         = "cuvara.import.failed";
        public const string CodeSchemaMismatch = "cuvara.import.schema-mismatch";
        public const string CodeUssErrors      = "cuvara.import.uss-errors";
        public const string CodeInvalidFile    = "cuvara.import.file-not-imported";
        public const string CodeInputWarning   = "cuvara.import.input-warning";
        public const string CodeNote           = "cuvara.import.note";
        public const string CodeSummary        = "cuvara.import.summary";
        public const string CodeIncomplete     = "cuvara.import.incomplete";

        /// <summary>Prefix of a fact's code; the connector's kind follows, as in <c>cuvara.import.fact.MissingFonts</c>.</summary>
        public const string CodeFactPrefix = "cuvara.import.fact.";

        // What each connector note kind means for this package, in one line (connector 0.3.0-exp.1,
        // Editor/Mapping/FigmaImportNote.cs and Editor/Localization/FigmaStrings.Report.cs).
        private static readonly Dictionary<string, string> KindText = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "MissingFonts", "Fonts not in the project or the connector's font map; that text falls back to the default font. Add the font or map it in the connector's Font map, then push again" },
            { "MissingImages", "Layers exported as images never reached Unity and appear blank. Push again; if they stay blank, check the Console" },
            { "ImagePending", "Layers with image or vector fills Figma could not export appear blank. Push again, or flatten them to an image in Figma" },
            { "DroppedGap", "Auto layout spacing (gap) is not imported before Unity 6000.7.0a4. Add margins in a game-owned USS file" },
            { "NegativeGap", "Negative auto layout spacing cannot be imported; the layers do not overlap" },
            { "DroppedShadows", "Drop shadows on frames or shapes are skipped (USS has no box-shadow); text shadows import" },
            { "DroppedBackgroundBlur", "Background blur is not imported before Unity 6000.6.0a6" },
            { "DroppedGradients", "Gradient fills could not be imported, so those layers have no fill" },
            { "RenderedGradients", "Layers with a gradient were imported as images (fixed resolution, no theme or token updates)" },
            { "DroppedTextStrokes", "Text strokes are not imported; the text has no outline" },
            { "CenteredTextStrokes", "Text strokes inside or outside the letters are drawn centered" },
            { "ClippedTextOutlines", "Text outlines thicker than the font asset can draw are thinner" },
            { "TextShadowUnderOutline", "Text with a stroke and a shadow casts a smaller shadow" },
            { "BlendModes", "Blend modes are not imported; those layers may look different" },
            { "GridAsFixedPositions", "Grid layouts were imported as fixed positions (USS grid needs Unity 6000.7)" },
            { "MissingLayers", "Layers could not be read from Figma and are missing from the import" },
            { "DroppedStates", "Variant states over the limit (128 per push, 32 per component) were not imported" },
            { "DroppedTextStates", "Text styling changes between states are not imported for components with several text layers" },
            { "SkippedStateVariants", "Components with a State axis were imported without interaction styles" },
            { "UnmappedStates", "Variant states with unrecognized names have no interaction style" },
            { "UnlinkedTextProps", "Text properties not linked to a layer in the placed variant; the layer's own text was used" },
            { "NearMissNames", "Components that look like controls but are not named like one imported as plain elements. Tag them in the plugin or add a Control map row" },
            { "DiscardedLayers", "Controls had layers they cannot hold; those layers were not imported" },
            { "TabsOutsideTabView", "Tabs outside a TabView will not work as tabs" },
            { "RadioButtonsOutsideGroup", "Radio buttons outside a RadioButtonGroup will not clear each other" },
            { "EmptyTabs", "Tabs are empty" },
            { "MissingMasters", "Component instances were imported with their styling inline instead of a shared class" },
            { "MappingDecay", "Control map rows that matched components in this file before match nothing now" },
            { "TokenCollision", "Several Figma variables became one token; only the first was exported" },
            { "MultiMode", "Theme modes exported as mode stylesheets" },
            { "ScreenRenamed", "A screen was saved under another name because the name was taken" },
        };

        private const int MaxItemsShown = 10;

        /// <summary>Parses saved result JSON.</summary>
        /// <exception cref="FormatException">The text is not JSON, or not a JSON object.</exception>
        public static FigmaImportResultInfo Read(string jsonText)
        {
            var root = FigmaJson.Parse(jsonText);
            if (!(root is Dictionary<string, object>))
            {
                throw new FormatException("Invalid import result: the top-level JSON value is not an object.");
            }

            var info = new FigmaImportResultInfo();

            // A Pipeline envelope ({success, result}) around a reply.
            if (FigmaJson.Member(root, "success") is bool && FigmaJson.Member(root, "result") is Dictionary<string, object> inner)
            {
                root = inner;
            }

            // A figma_import_status reply: the request fields, and the import under "result" once
            // the request finished (an open request has no "result" yet).
            var result = root;
            if (FigmaJson.Member(root, "state") is string || FigmaJson.Member(root, "requestId") is string)
            {
                ReadRequest(root, info);
                if (FigmaJson.Member(root, "result") is Dictionary<string, object> nested) result = nested;
            }

            // The helper's saved file: the result with a "request" object beside it.
            if (FigmaJson.Member(result, "request") is Dictionary<string, object> request) ReadRequest(request, info);

            var ok = FigmaJson.Member(result, "ok");
            if (ok is bool flag)
            {
                info.Ok = flag;
                info.HasOk = true;
            }
            else if (!ReferenceEquals(result, root) && FigmaJson.Member(root, "ok") is bool replyOk)
            {
                info.Ok = replyOk;
                info.HasOk = true;
            }

            info.Code = FirstNonEmpty(FigmaJson.MemberString(result, "code"), info.Code);
            info.Error = FirstNonEmpty(FigmaJson.MemberString(result, "error"), info.Error);
            info.Folder = NormalizeFolder(FigmaJson.MemberString(result, "folder"));
            info.Uxml = FigmaJson.MemberString(result, "uxml") ?? string.Empty;
            info.Converted = FigmaJson.Member(result, "converted") is bool converted && converted;
            info.UssErrors = FigmaJson.Member(result, "ussErrors") is bool ussErrors && ussErrors;
            info.SchemaMismatch = FigmaJson.Member(result, "schemaMismatch") is bool mismatch && mismatch;
            info.ElapsedMs = FigmaJson.Member(result, "elapsedMs") is double elapsed ? (long)elapsed : 0;
            info.Files = Strings(result, "files");
            info.Removed = Strings(result, "removed");
            info.InvalidFiles = Strings(result, "invalidFiles");
            info.Notes = Strings(result, "notes");
            info.InputWarnings = Strings(result, "inputWarnings");
            info.Facts = Facts(result);

            // A request the connector still had open has no import result yet, whatever "ok" says.
            if (info.State == "queued" || info.State == "exporting" || info.State == "importing") info.Finished = false;
            return info;
        }

        /// <summary>The diagnostics a result yields, in the order <see cref="FigmaDiagnostics.Compare"/> gives.</summary>
        /// <param name="info">The parsed result.</param>
        /// <param name="file">What folder-level diagnostics point at: the saved result's path.</param>
        public static List<FigmaDiagnostic> ToDiagnostics(FigmaImportResultInfo info, string file)
        {
            var diagnostics = new List<FigmaDiagnostic>();
            if (info == null) return diagnostics;
            file = file ?? string.Empty;

            if (!info.HasOk)
            {
                diagnostics.Add(Make(CodeUnreadable, FigmaDiagnosticSeverity.Error, file,
                    "The saved file has no 'ok' field, so it is not a connector import result."));
                return diagnostics;
            }

            diagnostics.Add(Make(CodeSummary, FigmaDiagnosticSeverity.Info, file, Summary(info)));

            if (!info.Finished)
            {
                diagnostics.Add(Make(CodeIncomplete, FigmaDiagnosticSeverity.Warning, file,
                    $"The last import did not finish while the helper waited (request {(info.RequestId.Length > 0 ? info.RequestId : "?")} was still '{info.State}'). "
                    + "It may still complete: check the Console and the import folder, or run figma-import.py again with the same link (it returns the same request)."));
            }
            else if (!info.Ok)
            {
                var message = new StringBuilder("The last import failed");
                if (info.Code.Length > 0) message.Append(" (").Append(info.Code).Append(')');
                if (info.Error.Length > 0) message.Append(": ").Append(info.Error.TrimEnd('.')).Append('.');
                else message.Append('.');
                if (info.Reason.Length > 0) message.Append(" Plugin: ").Append(info.Reason);
                diagnostics.Add(Make(CodeFailed, FigmaDiagnosticSeverity.Error, file, message.ToString()));
            }

            if (info.SchemaMismatch)
            {
                diagnostics.Add(Make(CodeSchemaMismatch, FigmaDiagnosticSeverity.Error, file,
                    "The Figma plugin and the connector speak different wire schemas. Update the plugin or the connector package."));
            }

            if (info.UssErrors)
            {
                diagnostics.Add(Make(CodeUssErrors, FigmaDiagnosticSeverity.Error, file,
                    "Unity reported USS errors in the generated stylesheets. See the Console, and the files listed as not imported."));
            }

            foreach (var invalid in info.InvalidFiles)
            {
                diagnostics.Add(Make(CodeInvalidFile, FigmaDiagnosticSeverity.Error, invalid,
                    "Written by the connector but not imported cleanly by Unity."));
            }

            foreach (var warning in info.InputWarnings)
            {
                diagnostics.Add(Make(CodeInputWarning, FigmaDiagnosticSeverity.Warning, file, "Plugin input: " + warning));
            }

            foreach (var fact in info.Facts)
            {
                diagnostics.Add(Make(CodeFactPrefix + fact.Kind, FactSeverity(fact.Severity), file, FactMessage(fact)));
            }

            // Notes repeat the facts as localized text; read them only when there are no facts.
            if (info.Facts.Count == 0)
            {
                foreach (var note in info.Notes)
                {
                    diagnostics.Add(Make(CodeNote, FigmaDiagnosticSeverity.Warning, file, note));
                }
            }

            diagnostics.Sort(FigmaDiagnostics.Compare);
            return diagnostics;
        }

        /// <summary>Reads and converts in one step; unreadable JSON becomes one error diagnostic.</summary>
        public static List<FigmaDiagnostic> Diagnose(string jsonText, string file)
        {
            try
            {
                return ToDiagnostics(Read(jsonText), file);
            }
            catch (FormatException exception)
            {
                return new List<FigmaDiagnostic>
                {
                    Make(CodeUnreadable, FigmaDiagnosticSeverity.Error, file ?? string.Empty,
                        "The saved import result could not be read: " + exception.Message),
                };
            }
        }

        /// <summary>True when the result's import folder is <paramref name="connectorFolder"/>
        /// (slash style, surrounding blanks and case ignored).</summary>
        public static bool IsForFolder(FigmaImportResultInfo info, string connectorFolder)
        {
            if (info == null) return false;
            var folder = NormalizeFolder(connectorFolder);
            return folder.Length > 0 && string.Equals(info.Folder, folder, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The connector's file-name rule (<c>FigmaPathGuard.SanitizeName</c>): anything outside
        /// <c>[A-Za-z0-9_-]</c> becomes <c>-</c>, outer dashes are trimmed. The helper names saved
        /// results with it; empty input gives <c>import</c>.
        /// </summary>
        public static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "import";
            var chars = name.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                var c = chars[i];
                var allowed = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (!allowed) chars[i] = '-';
            }

            var result = new string(chars).Trim('-');
            return result.Length == 0 ? "import" : result;
        }

        private static string Summary(FigmaImportResultInfo info)
        {
            var builder = new StringBuilder("Last import");
            if (info.FileName.Length > 0) builder.Append(" of '").Append(info.FileName).Append('\'');
            if (info.SavedAt.Length > 0) builder.Append(" at ").Append(info.SavedAt);
            builder.Append(": ").Append(!info.Finished ? "incomplete" : info.Ok ? "ok" : "failed");
            if (info.State.Length > 0) builder.Append(" (").Append(info.State).Append(')');
            builder.Append(", ").Append(info.Files.Count.ToString(CultureInfo.InvariantCulture)).Append(" file(s) written, ");
            builder.Append(info.Removed.Count.ToString(CultureInfo.InvariantCulture)).Append(" removed");
            if (info.ElapsedMs > 0) builder.Append(", ").Append(info.ElapsedMs.ToString(CultureInfo.InvariantCulture)).Append(" ms");
            builder.Append('.');
            return builder.ToString();
        }

        private static string FactMessage(FigmaImportResultFact fact)
        {
            var builder = new StringBuilder();
            builder.Append(fact.Kind.Length > 0 ? fact.Kind : "(unnamed)");
            if (fact.Count > 0) builder.Append(" (").Append(fact.Count.ToString(CultureInfo.InvariantCulture)).Append(')');
            builder.Append(": ");
            builder.Append(KindText.TryGetValue(fact.Kind, out var text) ? text : "Connector import note");
            builder.Append('.');

            if (fact.Items.Count > 0)
            {
                builder.Append(" Items: ");
                var shown = Math.Min(fact.Items.Count, MaxItemsShown);
                for (var i = 0; i < shown; i++)
                {
                    if (i > 0) builder.Append(", ");
                    builder.Append('\'').Append(fact.Items[i]).Append('\'');
                }

                var more = Math.Max(fact.Count, fact.Items.Count) - shown;
                if (more > 0) builder.Append(", +").Append(more.ToString(CultureInfo.InvariantCulture)).Append(" more");
                builder.Append('.');
            }

            return builder.ToString();
        }

        private static FigmaDiagnosticSeverity FactSeverity(string severity)
        {
            switch (severity)
            {
                case "message": return FigmaDiagnosticSeverity.Info;
                case "error": return FigmaDiagnosticSeverity.Error;
                default: return FigmaDiagnosticSeverity.Warning;
            }
        }

        private static void ReadRequest(object node, FigmaImportResultInfo info)
        {
            info.RequestId = FirstNonEmpty(FigmaJson.MemberString(node, "requestId"), info.RequestId);
            info.State = FirstNonEmpty(FigmaJson.MemberString(node, "state"), info.State);
            info.FileName = FirstNonEmpty(FigmaJson.MemberString(node, "fileName"), info.FileName);
            info.Reason = FirstNonEmpty(FigmaJson.MemberString(node, "reason"), info.Reason);
            info.Url = FirstNonEmpty(FigmaJson.MemberString(node, "url"), info.Url);
            info.SavedAt = FirstNonEmpty(FigmaJson.MemberString(node, "savedAt"), info.SavedAt);
            if (FigmaJson.Member(node, "finished") is bool finished && !finished) info.Finished = false;
            info.Code = FirstNonEmpty(FigmaJson.MemberString(node, "code"), info.Code);
            info.Error = FirstNonEmpty(FigmaJson.MemberString(node, "error"), info.Error);
        }

        private static List<FigmaImportResultFact> Facts(object result)
        {
            var facts = new List<FigmaImportResultFact>();
            foreach (var node in FigmaJson.MemberArray(result, "facts"))
            {
                if (!(node is Dictionary<string, object>)) continue;
                facts.Add(new FigmaImportResultFact
                {
                    Kind = FigmaJson.MemberString(node, "kind") ?? string.Empty,
                    Severity = FigmaJson.MemberString(node, "severity") ?? string.Empty,
                    Count = FigmaJson.Member(node, "count") is double count ? (int)count : 0,
                    Items = Strings(node, "items"),
                });
            }

            return facts;
        }

        private static List<string> Strings(object node, string key)
        {
            var values = new List<string>();
            foreach (var entry in FigmaJson.MemberArray(node, key))
            {
                if (entry is string text && text.Length > 0) values.Add(text);
            }

            return values;
        }

        private static string NormalizeFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return string.Empty;
            return folder.Trim().Replace('\\', '/').TrimEnd('/');
        }

        private static string FirstNonEmpty(string value, string fallback)
        {
            return string.IsNullOrEmpty(value) ? fallback ?? string.Empty : value;
        }

        private static FigmaDiagnostic Make(string code, FigmaDiagnosticSeverity severity, string file, string message)
        {
            return new FigmaDiagnostic { Code = code, Severity = severity, File = file ?? string.Empty, Line = 0, Message = message };
        }
    }
}
