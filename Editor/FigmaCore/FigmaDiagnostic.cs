namespace Cuvara.UIToolkit.FigmaCore
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    /// <summary>How bad a <see cref="FigmaDiagnostic"/> is. Errors fail the CLI (exit 1).</summary>
    public enum FigmaDiagnosticSeverity
    {
        Info,
        Warning,
        Error,
    }

    /// <summary>
    /// One finding of <see cref="FigmaImportValidator"/>. <see cref="Code"/> is stable
    /// (<c>FIG001</c>...) and documented in <c>Documentation~/FIGMA-VALIDATION.md</c>.
    /// </summary>
    public sealed class FigmaDiagnostic
    {
        /// <summary>Stable rule code, such as <c>FIG020</c>.</summary>
        public string Code;

        public FigmaDiagnosticSeverity Severity;

        /// <summary>Path relative to the import folder, with <c>/</c> separators; empty for
        /// a finding about the folder as a whole.</summary>
        public string File;

        /// <summary>1-based line in <see cref="File"/>, or 0 when there is none.</summary>
        public int Line;

        public string Message;

        /// <summary><c>File(Line): severity Code: Message</c>, the same shape as a compiler line.</summary>
        public override string ToString()
        {
            var location = string.IsNullOrEmpty(File) ? "." : File;
            if (Line > 0) location += "(" + Line.ToString(CultureInfo.InvariantCulture) + ")";
            return location + ": " + SeverityName(Severity) + " " + Code + ": " + Message;
        }

        /// <summary>Lower-case severity name used in text and JSON output.</summary>
        public static string SeverityName(FigmaDiagnosticSeverity severity)
        {
            switch (severity)
            {
                case FigmaDiagnosticSeverity.Error: return "error";
                case FigmaDiagnosticSeverity.Warning: return "warning";
                default: return "info";
            }
        }
    }

    /// <summary>What <see cref="FigmaImportValidator"/> checks and how strictly.</summary>
    public sealed class FigmaValidationOptions
    {
        /// <summary>The connector's USS class prefix the game expects (the profile prefix),
        /// without the trailing <c>-</c>. Null or empty skips the class prefix rule.</summary>
        public string ExpectedClassPrefix;

        /// <summary>Most elements per UXML document that may carry an inline <c>style</c>.
        /// Negative means no limit (the count is still reported as Info).</summary>
        public int MaxInlineStyledElements = -1;

        /// <summary>Most elements per UXML document that may be <c>position: absolute</c>.
        /// Negative means no limit (the count is still reported as Info).</summary>
        public int MaxAbsoluteElements = -1;

        /// <summary>Report literal colours (hex, <c>rgb()</c>, <c>rgba()</c>, named) where a
        /// token reference is expected.</summary>
        public bool RequireTokenForColors = true;
    }

    /// <summary>Aggregates and formats diagnostics for the CLI and the Editor diagnostics window.</summary>
    public static class FigmaDiagnostics
    {
        /// <summary>Number of diagnostics of <paramref name="severity"/>.</summary>
        public static int Count(IReadOnlyList<FigmaDiagnostic> diagnostics, FigmaDiagnosticSeverity severity)
        {
            var count = 0;
            if (diagnostics == null) return 0;
            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic != null && diagnostic.Severity == severity) count++;
            }

            return count;
        }

        /// <summary>The deterministic order every caller sees: folder-level findings first,
        /// then by file (ordinal), line, code and message.</summary>
        public static int Compare(FigmaDiagnostic a, FigmaDiagnostic b)
        {
            var result = string.CompareOrdinal(a.File ?? string.Empty, b.File ?? string.Empty);
            if (result != 0) return result;
            result = a.Line.CompareTo(b.Line);
            if (result != 0) return result;
            result = string.CompareOrdinal(a.Code, b.Code);
            if (result != 0) return result;
            return string.CompareOrdinal(a.Message, b.Message);
        }

        /// <summary>Machine-readable output:
        /// <c>{"folder":..,"errors":n,"warnings":n,"infos":n,"diagnostics":[{"code","severity","file","line","message"}]}</c>.</summary>
        public static string ToJson(string folder, IReadOnlyList<FigmaDiagnostic> diagnostics)
        {
            var builder = new StringBuilder();
            builder.Append("{\"folder\":");
            FigmaJson.WriteString(builder, folder ?? string.Empty);
            builder.Append(",\"errors\":").Append(Count(diagnostics, FigmaDiagnosticSeverity.Error).ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"warnings\":").Append(Count(diagnostics, FigmaDiagnosticSeverity.Warning).ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"infos\":").Append(Count(diagnostics, FigmaDiagnosticSeverity.Info).ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"diagnostics\":[");
            var first = true;
            foreach (var diagnostic in diagnostics ?? Array.Empty<FigmaDiagnostic>())
            {
                if (!first) builder.Append(',');
                first = false;
                builder.Append("{\"code\":");
                FigmaJson.WriteString(builder, diagnostic.Code);
                builder.Append(",\"severity\":");
                FigmaJson.WriteString(builder, FigmaDiagnostic.SeverityName(diagnostic.Severity));
                builder.Append(",\"file\":");
                FigmaJson.WriteString(builder, diagnostic.File ?? string.Empty);
                builder.Append(",\"line\":").Append(diagnostic.Line.ToString(CultureInfo.InvariantCulture));
                builder.Append(",\"message\":");
                FigmaJson.WriteString(builder, diagnostic.Message);
                builder.Append('}');
            }

            builder.Append("]}");
            return builder.ToString();
        }
    }
}
