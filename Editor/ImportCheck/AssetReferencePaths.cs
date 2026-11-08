using System;
using System.Collections.Generic;

namespace Cuvara.UIToolkit.Editor.ImportCheck
{
    internal static class AssetReferencePaths
    {
        /// <summary>
        /// Resolves a reference found in USS url(...) or UXML src="..." to a project asset path
        /// ("Assets/..."), or null when the reference is not a project asset (http, data, empty).
        /// Handles relative paths, "/Assets/..." and "project://database/Assets/...?fileID=..." forms.
        /// </summary>
        public static string Resolve(string baseAssetPath, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            var r = raw.Trim().Replace('\\', '/');
            if (r.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                r.StartsWith("http:", StringComparison.OrdinalIgnoreCase) ||
                r.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
                return null;

            const string projectScheme = "project://database/";
            var isRooted = false;
            if (r.StartsWith(projectScheme, StringComparison.OrdinalIgnoreCase))
            {
                r = r.Substring(projectScheme.Length);
                isRooted = true;
            }
            else if (r.StartsWith("/", StringComparison.Ordinal))
            {
                r = r.TrimStart('/');
                isRooted = true;
            }

            var cut = r.IndexOfAny(new[] { '?', '#' });
            if (cut >= 0)
                r = r.Substring(0, cut);
            r = Uri.UnescapeDataString(r);
            if (r.Length == 0)
                return null;

            if (!isRooted)
            {
                var slash = baseAssetPath.LastIndexOf('/');
                var dir = slash >= 0 ? baseAssetPath.Substring(0, slash) : string.Empty;
                r = dir.Length > 0 ? dir + "/" + r : r;
            }
            return Normalize(r);
        }

        static string Normalize(string path)
        {
            var stack = new List<string>();
            foreach (var part in path.Split('/'))
            {
                if (part.Length == 0 || part == ".")
                    continue;
                if (part == "..")
                {
                    if (stack.Count > 0)
                        stack.RemoveAt(stack.Count - 1);
                    continue;
                }
                stack.Add(part);
            }
            return string.Join("/", stack);
        }
    }
}
