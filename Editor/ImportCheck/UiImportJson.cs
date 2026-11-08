using System.Collections.Generic;
using System.Text;

namespace Cuvara.UIToolkit.Editor.ImportCheck
{
    /// <summary>Tiny JSON writer so result shapes and key names are exact (JsonUtility cannot emit a "checked" key).</summary>
    internal static class UiImportJson
    {
        public static string Str(string value)
        {
            if (value == null)
                return "null";
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static string StrArray(IEnumerable<string> values)
        {
            var sb = new StringBuilder("[");
            var first = true;
            foreach (var v in values)
            {
                if (!first)
                    sb.Append(',');
                first = false;
                sb.Append(Str(v));
            }
            return sb.Append(']').ToString();
        }

        public static string Bool(bool value)
        {
            return value ? "true" : "false";
        }
    }
}
