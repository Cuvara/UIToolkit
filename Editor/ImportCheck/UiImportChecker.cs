using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Cuvara.UIToolkit.Editor.SlicedArt;

namespace Cuvara.UIToolkit.Editor.ImportCheck
{
    public sealed class UiImportCheckResult
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Logs = new List<string>();
        public int Checked;

        /// <summary>No errors and no warnings at all.</summary>
        public bool Pass
        {
            get { return Errors.Count == 0 && Warnings.Count == 0; }
        }

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"pass\":").Append(UiImportJson.Bool(Pass));
            sb.Append(",\"errors\":").Append(UiImportJson.StrArray(Errors));
            sb.Append(",\"warnings\":").Append(UiImportJson.StrArray(Warnings));
            sb.Append(",\"logs\":").Append(UiImportJson.StrArray(Logs));
            sb.Append(",\"checked\":").Append(Checked);
            return sb.Append('}').ToString();
        }
    }

    /// <summary>
    /// Clean import and resolvable references for a UXML/USS/PNG tree: re-imports the given assets while capturing
    /// every Unity log message, then validates UXML, USS and sprite references (a PNG with a <c>.slice.json</c>
    /// sidecar must carry the sidecar's sprite border).
    /// Strict on purpose: any Warning, Error, Assert or Exception logged during the window fails the check.
    /// </summary>
    public static class UiImportChecker
    {
        // Own copy: the checker must not depend on the sliced art importer's settings (it only reads the sidecar format).
        static string ProjectRoot
        {
            get { return Path.GetDirectoryName(Application.dataPath).Replace('\\', '/'); }
        }

        static readonly Regex UssUrl = new Regex(
            "url\\(\\s*(?:\"([^\"]*)\"|'([^']*)'|([^)\\s]+))\\s*\\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex UssImportString = new Regex(
            "@import\\s+(?:\"([^\"]*)\"|'([^']*)')", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex UxmlSrc = new Regex("\\bsrc\\s*=\\s*\"([^\"]+)\"", RegexOptions.Compiled);
        static readonly Regex CssComment = new Regex("/\\*.*?\\*/", RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>Entry for batch mode, menus and Unity MCP script-execute. Paths are project-relative, comma separated.</summary>
        public static string CheckToJson(string commaSeparatedPaths)
        {
            var paths = (commaSeparatedPaths ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            return Check(paths).ToJson();
        }

        /// <summary>
        /// Checks .uxml, .uss and .png assets. A folder expands to every such asset below it.
        /// </summary>
        public static UiImportCheckResult Check(string[] assetPaths)
        {
            var result = new UiImportCheckResult();
            var collector = new LogCollector();
            Application.logMessageReceivedThreaded += collector.Handle;
            try
            {
                // Dependency closure: every stylesheet, template and sprite reachable from the given assets
                // is force-reimported and validated too, so a selected UXML cannot pass on a stale dependency.
                var paths = Closure(Expand(assetPaths, result));
                result.Checked = paths.Count;

                foreach (var path in paths)
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

                var spritesChecked = new HashSet<string>();
                foreach (var path in paths)
                {
                    var ext = Path.GetExtension(path).ToLowerInvariant();
                    if (ext == ".uss")
                        CheckUss(path, result, spritesChecked);
                    else if (ext == ".uxml")
                        CheckUxml(path, result, spritesChecked);
                    else if (ext == ".png")
                        CheckSprite(path, result, spritesChecked);
                }
            }
            catch (Exception e)
            {
                result.Errors.Add("Checker exception: " + e);
            }
            finally
            {
                Application.logMessageReceivedThreaded -= collector.Handle;
            }
            collector.DrainInto(result);
            return result;
        }

        /// <summary>Raw reference strings in a USS (url(), @import) or UXML (src="...") file.</summary>
        static List<string> ReferencesOf(string path)
        {
            var refs = new List<string>();
            var full = Path.Combine(ProjectRoot, path);
            if (!File.Exists(full))
                return refs;
            var text = File.ReadAllText(full);
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".uss")
            {
                text = CssComment.Replace(text, string.Empty);
                foreach (Match m in UssUrl.Matches(text))
                    refs.Add(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value);
                foreach (Match m in UssImportString.Matches(text))
                    refs.Add(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
            }
            else if (ext == ".uxml")
            {
                foreach (Match m in UxmlSrc.Matches(text))
                    refs.Add(m.Groups[1].Value);
            }
            return new List<string>(new HashSet<string>(refs));
        }

        static List<string> Closure(List<string> seeds)
        {
            var seen = new HashSet<string>(seeds);
            var queue = new Queue<string>(seeds);
            while (queue.Count > 0)
            {
                var path = queue.Dequeue();
                foreach (var raw in ReferencesOf(path))
                {
                    var target = AssetReferencePaths.Resolve(path, raw);
                    // Missing targets are reported by CheckReference; here only existing checkable assets are followed.
                    if (target == null || !IsCheckable(target) ||
                        !File.Exists(Path.Combine(ProjectRoot, target)))
                        continue;
                    if (seen.Add(target))
                        queue.Enqueue(target);
                }
            }
            var list = new List<string>(seen);
            list.Sort((a, b) =>
            {
                var c = Rank(a).CompareTo(Rank(b));
                return c != 0 ? c : string.CompareOrdinal(a, b);
            });
            return list;
        }

        static List<string> Expand(string[] assetPaths, UiImportCheckResult result)
        {
            var found = new SortedDictionary<string, bool>(StringComparer.Ordinal);
            foreach (var raw in assetPaths ?? new string[0])
            {
                var path = (raw ?? string.Empty).Trim().Replace('\\', '/');
                if (path.Length == 0)
                    continue;
                if (AssetDatabase.IsValidFolder(path))
                {
                    foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] { path }))
                    {
                        var p = AssetDatabase.GUIDToAssetPath(guid);
                        if (!AssetDatabase.IsValidFolder(p) && IsCheckable(p))
                            found[p] = true;
                    }
                }
                else if (!File.Exists(Path.Combine(ProjectRoot, path)))
                {
                    result.Errors.Add("Asset not found: " + path);
                }
                else if (IsCheckable(path))
                {
                    found[path] = true;
                }
                else
                {
                    result.Warnings.Add("Skipped (not .uxml/.uss/.png): " + path);
                }
            }

            // PNGs first so sprites exist when USS/UXML are imported, then USS, then UXML.
            var list = new List<string>(found.Keys);
            list.Sort((a, b) =>
            {
                var c = Rank(a).CompareTo(Rank(b));
                return c != 0 ? c : string.CompareOrdinal(a, b);
            });
            return list;
        }

        static bool IsCheckable(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".uxml" || ext == ".uss" || ext == ".png";
        }

        static int Rank(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".png" ? 0 : ext == ".uss" ? 1 : 2;
        }

        static void CheckUss(string path, UiImportCheckResult result, HashSet<string> spritesChecked)
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (sheet == null)
            {
                result.Errors.Add(path + ": cannot load as StyleSheet.");
                return;
            }
            if (sheet.importedWithErrors)
                result.Errors.Add(path + ": StyleSheet importedWithErrors.");
            if (sheet.importedWithWarnings)
                result.Errors.Add(path + ": StyleSheet importedWithWarnings.");

            foreach (var raw in ReferencesOf(path))
                CheckReference(path, raw, result, spritesChecked);
        }

        static void CheckUxml(string path, UiImportCheckResult result, HashSet<string> spritesChecked)
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            if (tree == null)
            {
                result.Errors.Add(path + ": cannot load as VisualTreeAsset.");
                return;
            }
            try
            {
                // Missing templates and styles are logged during the clone and caught by the collector.
                var clone = tree.CloneTree();
                if (clone == null)
                    result.Errors.Add(path + ": CloneTree returned null.");
            }
            catch (Exception e)
            {
                result.Errors.Add(path + ": CloneTree threw " + e.GetType().Name + ": " + e.Message);
            }

            foreach (var raw in ReferencesOf(path))
                CheckReference(path, raw, result, spritesChecked);
        }

        static void CheckReference(string basePath, string raw, UiImportCheckResult result, HashSet<string> spritesChecked)
        {
            var target = AssetReferencePaths.Resolve(basePath, raw);
            if (target == null)
                return;
            var exists = File.Exists(Path.Combine(ProjectRoot, target)) &&
                         AssetDatabase.AssetPathToGUID(target).Length > 0;
            if (!exists)
            {
                result.Errors.Add(basePath + ": reference \"" + raw + "\" does not resolve to an existing asset (" + target + ").");
                return;
            }
            if (target.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                CheckSprite(target, result, spritesChecked);
        }

        static void CheckSprite(string pngPath, UiImportCheckResult result, HashSet<string> spritesChecked)
        {
            if (!spritesChecked.Add(pngPath))
                return;
            var sidecarPath = SliceSidecar.SidecarPathFor(pngPath);
            if (!File.Exists(Path.Combine(ProjectRoot, sidecarPath)))
                return;

            if (!SliceSidecar.TryLoad(sidecarPath, out var sidecar, out var error))
            {
                result.Errors.Add(error);
                return;
            }
            var importer = AssetImporter.GetAtPath(pngPath) as TextureImporter;
            if (importer == null)
            {
                result.Errors.Add(pngPath + ": no TextureImporter.");
                return;
            }
            if (importer.textureType != TextureImporterType.Sprite)
                result.Errors.Add(pngPath + ": textureType is " + importer.textureType + ", expected Sprite (is a sprite importer configured for this file?).");

            var expected = sidecar.ToSpriteBorder();
            var actual = importer.spriteBorder;
            if (actual == expected)
                return; // includes an unsliced sprite whose sidecar says [0,0,0,0]
            if (actual == Vector4.zero)
                result.Errors.Add(pngPath + ": sprite border is zero (sidecar " + sidecarPath + " expects " + expected + ").");
            else
                result.Errors.Add(pngPath + ": sprite border " + actual + " != sidecar " + expected + " (left, bottom, right, top).");
        }

        sealed class LogCollector
        {
            readonly object m_Lock = new object();
            readonly List<string> m_Errors = new List<string>();
            readonly List<string> m_Warnings = new List<string>();
            readonly List<string> m_Logs = new List<string>();

            public void Handle(string condition, string stackTrace, LogType type)
            {
                var line = "[" + type + "] " + condition;
                lock (m_Lock)
                {
                    m_Logs.Add(line);
                    if (type == LogType.Warning)
                        m_Warnings.Add(line);
                    else if (type != LogType.Log)
                        m_Errors.Add(line); // Error, Assert, Exception
                }
            }

            public void DrainInto(UiImportCheckResult result)
            {
                lock (m_Lock)
                {
                    result.Errors.AddRange(m_Errors);
                    result.Warnings.AddRange(m_Warnings);
                    result.Logs.AddRange(m_Logs);
                }
            }
        }
    }
}
