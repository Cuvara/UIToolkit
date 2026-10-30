namespace Cuvara.UIToolkit.FigmaCore
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>One <c>property: value</c> pair, with the 1-based line it starts on.</summary>
    public sealed class UssDeclaration
    {
        public string Property;
        public string Value;
        public int Line;
    }

    /// <summary>One <c>selector { declarations }</c> rule.</summary>
    public sealed class UssRule
    {
        public string Selector;
        public int Line;
        public List<UssDeclaration> Declarations = new List<UssDeclaration>();
    }

    /// <summary>One top-level <c>@import</c> statement: its target, unquoted, and its line.</summary>
    public sealed class UssImport
    {
        public string Target;
        public int Line;
    }

    /// <summary>The result of <see cref="FigmaUss.Parse"/>. <see cref="Problem"/> is
    /// non-null when the sheet is not well formed (unterminated rule or comment, a stray
    /// brace, text outside a rule).</summary>
    public sealed class UssSheet
    {
        public List<UssRule> Rules = new List<UssRule>();

        /// <summary>Top-level <c>@import</c> statements, in order.</summary>
        public List<UssImport> Imports = new List<UssImport>();

        public string Problem;
        public int ProblemLine;
    }

    /// <summary>
    /// A minimal USS reader: enough structure to audit what the connector writes (rules,
    /// declarations, colours, <c>var()</c>, <c>url()</c>). It is not a validating parser;
    /// Unity's importer stays the authority on syntax. Unity-free.
    /// </summary>
    public static class FigmaUss
    {
        private static readonly Regex HexColor = new Regex(@"(?<![\w-])#(?:[0-9a-fA-F]{8}|[0-9a-fA-F]{6}|[0-9a-fA-F]{4}|[0-9a-fA-F]{3})(?![\w-])", RegexOptions.CultureInvariant);
        private static readonly Regex RgbColor = new Regex(@"(?<![\w-])rgba?\s*\([^)]*\)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex Identifier = new Regex(@"(?<![\w-])[a-zA-Z]+(?![\w(-])", RegexOptions.CultureInvariant);
        private static readonly Regex VarReference = new Regex(@"var\(\s*(--[A-Za-z0-9_-]+)\s*(,)?", RegexOptions.CultureInvariant);
        // Quoted and unquoted forms; a backslash escapes the next character in all three
        // (url(a\(1\).png) names "a(1).png").
        private static readonly Regex UrlReference = new Regex(@"url\(\s*(?:""((?:\\.|[^""\\])*)""|'((?:\\.|[^'\\])*)'|((?:\\.|[^)""'\\])*?))\s*\)", RegexOptions.CultureInvariant);
        private static readonly Regex ImportTarget = new Regex(@"^@import\s+(?:url\(\s*(?:""((?:\\.|[^""\\])*)""|'((?:\\.|[^'\\])*)'|((?:\\.|[^)""'\\])*?))\s*\)|""((?:\\.|[^""\\])*)""|'((?:\\.|[^'\\])*)')", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex CssEscape = new Regex(@"\\(?:([0-9a-fA-F]{1,6})\s?|(.))", RegexOptions.CultureInvariant | RegexOptions.Singleline);
        private static readonly Regex VarCall = new Regex(@"(?<![\w-])var\s*\(", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex ClassSelector = new Regex(@"\.(-?[_a-zA-Z][_a-zA-Z0-9-]*)", RegexOptions.CultureInvariant);

        // CSS named colours (USS accepts the CSS set). 'transparent' and 'currentcolor' are
        // deliberately absent: they are not values a design token would carry.
        private static readonly HashSet<string> NamedColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "aliceblue", "antiquewhite", "aqua", "aquamarine", "azure", "beige", "bisque", "black", "blanchedalmond",
            "blue", "blueviolet", "brown", "burlywood", "cadetblue", "chartreuse", "chocolate", "coral", "cornflowerblue",
            "cornsilk", "crimson", "cyan", "darkblue", "darkcyan", "darkgoldenrod", "darkgray", "darkgreen", "darkgrey",
            "darkkhaki", "darkmagenta", "darkolivegreen", "darkorange", "darkorchid", "darkred", "darksalmon",
            "darkseagreen", "darkslateblue", "darkslategray", "darkslategrey", "darkturquoise", "darkviolet", "deeppink",
            "deepskyblue", "dimgray", "dimgrey", "dodgerblue", "firebrick", "floralwhite", "forestgreen", "fuchsia",
            "gainsboro", "ghostwhite", "gold", "goldenrod", "gray", "green", "greenyellow", "grey", "honeydew", "hotpink",
            "indianred", "indigo", "ivory", "khaki", "lavender", "lavenderblush", "lawngreen", "lemonchiffon",
            "lightblue", "lightcoral", "lightcyan", "lightgoldenrodyellow", "lightgray", "lightgreen", "lightgrey",
            "lightpink", "lightsalmon", "lightseagreen", "lightskyblue", "lightslategray", "lightslategrey",
            "lightsteelblue", "lightyellow", "lime", "limegreen", "linen", "magenta", "maroon", "mediumaquamarine",
            "mediumblue", "mediumorchid", "mediumpurple", "mediumseagreen", "mediumslateblue", "mediumspringgreen",
            "mediumturquoise", "mediumvioletred", "midnightblue", "mintcream", "mistyrose", "moccasin", "navajowhite",
            "navy", "oldlace", "olive", "olivedrab", "orange", "orangered", "orchid", "palegoldenrod", "palegreen",
            "paleturquoise", "palevioletred", "papayawhip", "peachpuff", "peru", "pink", "plum", "powderblue", "purple",
            "rebeccapurple", "red", "rosybrown", "royalblue", "saddlebrown", "salmon", "sandybrown", "seagreen",
            "seashell", "sienna", "silver", "skyblue", "slateblue", "slategray", "slategrey", "snow", "springgreen",
            "steelblue", "tan", "teal", "thistle", "tomato", "turquoise", "violet", "wheat", "white", "whitesmoke",
            "yellow", "yellowgreen",
        };

        /// <summary>Parses a stylesheet into rules. Comments are dropped; line numbers refer
        /// to the original text.</summary>
        public static UssSheet Parse(string text)
        {
            var sheet = new UssSheet();
            text = text ?? string.Empty;
            var clean = StripComments(text, out var unterminatedCommentAt);
            var lines = new LineIndex(clean);
            if (unterminatedCommentAt >= 0)
            {
                sheet.Problem = "unterminated comment";
                sheet.ProblemLine = lines.LineAt(unterminatedCommentAt);
            }

            var position = 0;
            while (position < clean.Length)
            {
                position += LeadingWhitespace(clean, position);
                if (position >= clean.Length) break;

                // A top-level statement at-rule (@import, @charset) ends at ';' and has no block.
                if (clean[position] == '@')
                {
                    var statementEnd = ScanTo(clean, position, clean.Length, "{;");
                    if (statementEnd >= 0 && clean[statementEnd] == ';')
                    {
                        ReadImport(sheet, clean.Substring(position, statementEnd - position), lines.LineAt(position));
                        position = statementEnd + 1;
                        continue;
                    }
                }

                var open = ScanTo(clean, position, clean.Length, "{");
                if (open < 0)
                {
                    var stray = ScanTo(clean, position, clean.Length, "}");
                    if (stray == position) SetProblem(sheet, "unexpected '}'", lines.LineAt(stray));
                    else SetProblem(sheet, "text outside any rule", lines.LineAt(position));
                    break;
                }

                // A '}' before the selector closes nothing: report it, and read the selector after it.
                var selectorStart = position;
                for (var stray = ScanTo(clean, selectorStart, open, "}"); stray >= 0; stray = ScanTo(clean, selectorStart, open, "}"))
                {
                    SetProblem(sheet, "unexpected '}'", lines.LineAt(stray));
                    selectorStart = stray + 1;
                }

                selectorStart += LeadingWhitespace(clean, selectorStart);
                var rule = new UssRule
                {
                    Selector = clean.Substring(selectorStart, open - selectorStart).Trim(),
                    Line = lines.LineAt(Math.Min(selectorStart, open)),
                };
                sheet.Rules.Add(rule);

                // USS has no nested blocks: a '{' before the closing '}' is a missing '}'.
                var close = -1;
                for (var scan = open + 1; ;)
                {
                    var hit = ScanTo(clean, scan, clean.Length, "{}");
                    if (hit < 0) break;
                    if (clean[hit] == '}')
                    {
                        close = hit;
                        break;
                    }

                    SetProblem(sheet, "unexpected '{' inside rule '" + rule.Selector + "'", lines.LineAt(hit));
                    scan = hit + 1;
                }

                if (close < 0)
                {
                    SetProblem(sheet, "unterminated rule '" + rule.Selector + "'", rule.Line);
                    rule.Declarations.AddRange(ParseDeclarations(clean, open + 1, clean.Length, lines));
                    break;
                }

                rule.Declarations.AddRange(ParseDeclarations(clean, open + 1, close, lines));
                position = close + 1;
            }

            return sheet;
        }

        /// <summary><c>@import url(x)</c> / <c>@import "x"</c>; other statement at-rules are ignored.</summary>
        private static void ReadImport(UssSheet sheet, string statement, int line)
        {
            var match = ImportTarget.Match(statement);
            if (!match.Success) return;

            for (var group = 1; group < match.Groups.Count; group++)
            {
                if (!match.Groups[group].Success) continue;
                sheet.Imports.Add(new UssImport { Target = Unescape(match.Groups[group].Value).Trim(), Line = line });
                return;
            }
        }

        private static void SetProblem(UssSheet sheet, string problem, int line)
        {
            if (sheet.Problem != null) return;
            sheet.Problem = problem;
            sheet.ProblemLine = line;
        }

        /// <summary>Resolves CSS escapes: <c>\(</c> is <c>(</c>, <c>\26 </c> is <c>&amp;</c>.</summary>
        private static string Unescape(string text)
        {
            if (text.IndexOf('\\') < 0) return text;
            return CssEscape.Replace(text, match =>
            {
                if (!match.Groups[1].Success) return match.Groups[2].Value;
                var code = int.Parse(match.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
                return code == 0 || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF) ? "\uFFFD" : char.ConvertFromUtf32(code);
            });
        }

        /// <summary>Parses an inline <c>style="..."</c> attribute value. Every declaration gets
        /// <paramref name="line"/>.</summary>
        public static List<UssDeclaration> ParseInline(string style, int line)
        {
            var declarations = ParseDeclarations(style ?? string.Empty, 0, (style ?? string.Empty).Length, null);
            foreach (var declaration in declarations) declaration.Line = line;
            return declarations;
        }

        /// <summary>The class names a selector targets (<c>.a:hover &gt; .b</c> gives a, b).</summary>
        public static List<string> SelectorClasses(string selector)
        {
            var classes = new List<string>();
            foreach (Match match in ClassSelector.Matches(selector ?? string.Empty))
            {
                classes.Add(match.Groups[1].Value);
            }

            return classes;
        }

        /// <summary>Literal colours in a declaration value: hex, <c>rgb()</c>/<c>rgba()</c>
        /// anywhere, and CSS named colours in colour-carrying properties. Text inside
        /// <c>var()</c>, <c>url()</c> and quotes is ignored, so a token fallback is not
        /// reported.</summary>
        public static List<string> FindLiteralColors(string property, string value)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(value)) return found;

            var scrubbed = Scrub(value);
            foreach (Match match in HexColor.Matches(scrubbed)) found.Add(match.Value);
            foreach (Match match in RgbColor.Matches(scrubbed)) found.Add(Regex.Replace(match.Value, @"\s+", string.Empty));

            if (IsColorProperty(property))
            {
                var withoutFunctions = RgbColor.Replace(scrubbed, " ");
                foreach (Match match in Identifier.Matches(withoutFunctions))
                {
                    if (NamedColors.Contains(match.Value)) found.Add(match.Value.ToLowerInvariant());
                }
            }

            return found;
        }

        /// <summary><c>var(--name)</c> references in a value; <c>HasFallback</c> is true for
        /// <c>var(--name, fallback)</c>.</summary>
        public static List<KeyValuePair<string, bool>> FindVarReferences(string value)
        {
            var found = new List<KeyValuePair<string, bool>>();
            foreach (Match match in VarReference.Matches(value ?? string.Empty))
            {
                found.Add(new KeyValuePair<string, bool>(match.Groups[1].Value, match.Groups[2].Success));
            }

            return found;
        }

        /// <summary>The targets of every <c>url(...)</c> in a value, unquoted.</summary>
        public static List<string> FindUrls(string value)
        {
            var found = new List<string>();
            foreach (Match match in UrlReference.Matches(value ?? string.Empty))
            {
                var target = match.Groups[1].Success ? match.Groups[1].Value
                    : match.Groups[2].Success ? match.Groups[2].Value
                    : match.Groups[3].Value;
                found.Add(Unescape(target).Trim());
            }

            return found;
        }

        /// <summary>True when <paramref name="value"/> calls <c>var()</c> outside a quoted string
        /// or a <c>url()</c>, with or without a fallback.</summary>
        public static bool UsesVariable(string value)
        {
            return !string.IsNullOrEmpty(value) && VarCall.IsMatch(Scrub(value));
        }

        /// <summary>True for <c>position: absolute</c>.</summary>
        public static bool IsAbsolutePosition(UssDeclaration declaration)
        {
            return string.Equals(declaration.Property, "position", StringComparison.OrdinalIgnoreCase)
                && string.Equals((declaration.Value ?? string.Empty).Trim(), "absolute", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsColorProperty(string property)
        {
            if (string.IsNullOrEmpty(property)) return false;
            var name = property.ToLowerInvariant();
            return name.Contains("color") || name == "text-shadow" || name == "-unity-text-outline";
        }

        // Blanks out quoted strings and the insides of var(...) and url(...), keeping length.
        private static string Scrub(string value)
        {
            var chars = value.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (chars[i] == '"' || chars[i] == '\'')
                {
                    var quote = chars[i];
                    var j = i + 1;
                    while (j < chars.Length && value[j] != quote)
                    {
                        if (value[j] == '\\' && j + 1 < chars.Length) chars[j++] = ' ';
                        chars[j++] = ' ';
                    }
                    i = j;
                    continue;
                }

                if (StartsWithAt(value, i, "var(") || StartsWithAt(value, i, "url("))
                {
                    var depth = 0;
                    var j = i + 3;
                    for (; j < chars.Length; j++)
                    {
                        if (value[j] == '(') depth++;
                        else if (value[j] == ')' && --depth == 0) break;
                        if (j > i + 3) chars[j] = ' ';
                    }

                    i = j;
                }
            }

            return new string(chars);
        }

        private static bool StartsWithAt(string text, int index, string token)
        {
            return string.Compare(text, index, token, 0, token.Length, StringComparison.OrdinalIgnoreCase) == 0
                && (index == 0 || !(char.IsLetterOrDigit(text[index - 1]) || text[index - 1] == '-'));
        }

        private static List<UssDeclaration> ParseDeclarations(string text, int start, int end, LineIndex lines)
        {
            var declarations = new List<UssDeclaration>();
            var segmentStart = start;
            var quote = '\0';
            var parens = 0;
            for (var i = start; i <= end; i++)
            {
                var atEnd = i == end;
                var c = atEnd ? ';' : text[i];
                if (!atEnd && c == '\\' && i + 1 < end)
                {
                    i++; // an escaped character is never a quote, parenthesis or separator
                    continue;
                }

                if (!atEnd && quote != '\0')
                {
                    if (c == quote || c == '\n') quote = '\0';
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    quote = c;
                    continue;
                }

                if (c == '(') parens++;
                else if (c == ')' && parens > 0) parens--;
                else if (c == ';' && (parens == 0 || atEnd))
                {
                    AddDeclaration(declarations, text, segmentStart, i, lines);
                    segmentStart = i + 1;
                }
            }

            return declarations;
        }

        private static void AddDeclaration(List<UssDeclaration> declarations, string text, int start, int end, LineIndex lines)
        {
            var segment = text.Substring(start, end - start);
            var colon = segment.IndexOf(':');
            if (colon <= 0) return;
            var property = segment.Substring(0, colon).Trim();
            if (property.Length == 0) return;
            declarations.Add(new UssDeclaration
            {
                Property = property,
                Value = segment.Substring(colon + 1).Trim(),
                Line = lines == null ? 0 : lines.LineAt(start + LeadingWhitespace(text, start)),
            });
        }

        /// <summary>
        /// The first index in [<paramref name="start"/>, <paramref name="end"/>) holding one of
        /// <paramref name="stops"/> outside a quoted string, or -1. Escapes are skipped, and a
        /// string ends at its closing quote or at a newline (USS strings cannot span lines
        /// unescaped), the same rules <see cref="StripComments"/> follows.
        /// </summary>
        private static int ScanTo(string text, int start, int end, string stops)
        {
            var quote = '\0';
            for (var i = start; i < end; i++)
            {
                var c = text[i];
                if (c == '\\')
                {
                    i++;
                    continue;
                }

                if (quote != '\0')
                {
                    if (c == quote || c == '\n') quote = '\0';
                    continue;
                }

                if (c == '"' || c == '\'') quote = c;
                else if (stops.IndexOf(c) >= 0) return i;
            }

            return -1;
        }

        // Replaces /* ... */ with spaces, keeping newlines so offsets and lines stay valid.
        private static string StripComments(string text, out int unterminatedAt)
        {
            unterminatedAt = -1;
            var builder = new StringBuilder(text);
            var i = 0;
            var quote = '\0';
            while (i < text.Length - 1)
            {
                var c = text[i];
                if (quote != '\0')
                {
                    // Inside a quoted string: skip escapes, stop at the closing quote or a newline
                    // (USS strings cannot span lines unescaped).
                    if (c == '\\') { i += 2; continue; }
                    if (c == quote || c == '\n') quote = '\0';
                    i++;
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    quote = c;
                    i++;
                    continue;
                }

                if (c == '/' && text[i + 1] == '*')
                {
                    var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    var stop = end < 0 ? text.Length : end + 2;
                    if (end < 0) unterminatedAt = i;
                    for (var j = i; j < stop; j++)
                    {
                        if (builder[j] != '\n') builder[j] = ' ';
                    }

                    i = stop;
                    continue;
                }

                i++;
            }

            return builder.ToString();
        }

        private static int LeadingWhitespace(string text, int start)
        {
            var count = 0;
            while (start + count < text.Length && char.IsWhiteSpace(text[start + count])) count++;
            return count;
        }

        /// <summary>Offset to 1-based line, by binary search over the line starts.</summary>
        private sealed class LineIndex
        {
            private readonly List<int> _starts = new List<int> { 0 };

            public LineIndex(string text)
            {
                for (var i = 0; i < text.Length; i++)
                {
                    if (text[i] == '\n') _starts.Add(i + 1);
                }
            }

            public int LineAt(int offset)
            {
                var index = _starts.BinarySearch(offset);
                return index >= 0 ? index + 1 : ~index;
            }
        }
    }
}
