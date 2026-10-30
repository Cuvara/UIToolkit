namespace Cuvara.UIToolkit.FigmaCore
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    /// <summary>
    /// A small, tolerant JSON reader for connector files (<c>.figma-manifest.json</c>) and
    /// Pipeline replies. It returns plain objects: <see cref="Dictionary{TKey,TValue}"/>
    /// (string to object) for objects, <see cref="List{T}"/> of object for arrays,
    /// <see cref="string"/>, <see cref="double"/>, <see cref="bool"/> and <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>This file must stay Unity-free</b>, like <c>Editor/Codegen/Core</c>: the
    /// <c>Tools~/UxmlCodegenCli</c> CLI compiles it under plain .NET. No Newtonsoft and no
    /// System.Text.Json (Unity does not ship the latter).</para>
    ///
    /// <para>Tolerance: a UTF-8 BOM, trailing commas and duplicate keys (last wins) are
    /// accepted. Anything that is not JSON throws <see cref="FormatException"/> with the
    /// line of the problem.</para>
    /// </remarks>
    public static class FigmaJson
    {
        /// <summary>Parses one JSON value; trailing whitespace is allowed, trailing text is not.</summary>
        /// <exception cref="FormatException">The text is not JSON.</exception>
        public static object Parse(string text)
        {
            if (text == null) throw new FormatException("JSON text is null.");
            var parser = new Parser(text);
            parser.SkipWhitespace();
            if (parser.AtEnd) throw parser.Error("empty document");
            var value = parser.ReadValue(0);
            parser.SkipWhitespace();
            if (!parser.AtEnd) throw parser.Error("unexpected text after the JSON value");
            return value;
        }

        /// <summary>The object member <paramref name="key"/>, or <c>null</c> when
        /// <paramref name="node"/> is not an object or lacks it.</summary>
        public static object Member(object node, string key)
        {
            return node is Dictionary<string, object> map && map.TryGetValue(key, out var value) ? value : null;
        }

        /// <summary>The member as a string, or <c>null</c> when absent or not a string.</summary>
        public static string MemberString(object node, string key)
        {
            return Member(node, key) as string;
        }

        /// <summary>The member as a list, or an empty list when absent or not an array.</summary>
        public static List<object> MemberArray(object node, string key)
        {
            return Member(node, key) as List<object> ?? new List<object>();
        }

        /// <summary>Writes <paramref name="value"/> as a JSON string literal (with quotes).</summary>
        public static void WriteString(StringBuilder builder, string value)
        {
            if (value == null)
            {
                builder.Append("null");
                return;
            }

            builder.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                        {
                            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            builder.Append('"');
        }

        private sealed class Parser
        {
            // Deep nesting is not something a manifest needs; the cap keeps a hostile file from
            // overflowing the stack.
            private const int MaxDepth = 256;

            private readonly string _text;
            private int _position;

            public Parser(string text)
            {
                _text = text;
                if (_text.Length > 0 && _text[0] == (char)0xFEFF) _position = 1;
            }

            public bool AtEnd => _position >= _text.Length;

            public FormatException Error(string message)
            {
                var line = 1;
                for (var i = 0; i < _position && i < _text.Length; i++)
                {
                    if (_text[i] == '\n') line++;
                }

                return new FormatException($"Invalid JSON at line {line}: {message}.");
            }

            public void SkipWhitespace()
            {
                while (_position < _text.Length)
                {
                    var c = _text[_position];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') _position++;
                    else break;
                }
            }

            public object ReadValue(int depth)
            {
                if (depth > MaxDepth) throw Error("nesting too deep");
                SkipWhitespace();
                if (AtEnd) throw Error("unexpected end of document");

                var c = _text[_position];
                switch (c)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return ReadString();
                    case 't': ReadLiteral("true"); return true;
                    case 'f': ReadLiteral("false"); return false;
                    case 'n': ReadLiteral("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw Error($"unexpected character '{c}'");
                }
            }

            private Dictionary<string, object> ReadObject(int depth)
            {
                var map = new Dictionary<string, object>(StringComparer.Ordinal);
                _position++; // '{'
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd) throw Error("unterminated object");
                    if (_text[_position] == '}')
                    {
                        _position++;
                        return map;
                    }

                    if (_text[_position] != '"') throw Error("expected a member name");
                    var key = ReadString();
                    SkipWhitespace();
                    if (AtEnd || _text[_position] != ':') throw Error("expected ':' after a member name");
                    _position++;
                    map[key] = ReadValue(depth + 1);
                    SkipWhitespace();
                    if (AtEnd) throw Error("unterminated object");
                    if (_text[_position] == ',')
                    {
                        _position++;
                        continue;
                    }

                    if (_text[_position] == '}')
                    {
                        _position++;
                        return map;
                    }

                    throw Error("expected ',' or '}' in an object");
                }
            }

            private List<object> ReadArray(int depth)
            {
                var list = new List<object>();
                _position++; // '['
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd) throw Error("unterminated array");
                    if (_text[_position] == ']')
                    {
                        _position++;
                        return list;
                    }

                    list.Add(ReadValue(depth + 1));
                    SkipWhitespace();
                    if (AtEnd) throw Error("unterminated array");
                    if (_text[_position] == ',')
                    {
                        _position++;
                        continue;
                    }

                    if (_text[_position] == ']')
                    {
                        _position++;
                        return list;
                    }

                    throw Error("expected ',' or ']' in an array");
                }
            }

            private string ReadString()
            {
                _position++; // opening quote
                var builder = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw Error("unterminated string");
                    var c = _text[_position++];
                    if (c == '"') return builder.ToString();
                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }

                    if (AtEnd) throw Error("unterminated escape");
                    var escape = _text[_position++];
                    switch (escape)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (_position + 4 > _text.Length) throw Error("truncated \\u escape");
                            if (!int.TryParse(_text.Substring(_position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                            {
                                throw Error("invalid \\u escape");
                            }

                            builder.Append((char)code);
                            _position += 4;
                            break;
                        default:
                            throw Error($"invalid escape '\\{escape}'");
                    }
                }
            }

            private double ReadNumber()
            {
                var start = _position;
                while (_position < _text.Length)
                {
                    var c = _text[_position];
                    if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') _position++;
                    else break;
                }

                var token = _text.Substring(start, _position - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    throw Error($"invalid number '{token}'");
                }

                return number;
            }

            private void ReadLiteral(string literal)
            {
                if (string.CompareOrdinal(_text, _position, literal, 0, literal.Length) != 0)
                {
                    throw Error($"expected '{literal}'");
                }

                _position += literal.Length;
            }
        }
    }
}
