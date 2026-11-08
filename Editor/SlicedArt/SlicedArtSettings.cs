using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Cuvara.UIToolkit.Editor.SlicedArt
{
    /// <summary>
    /// Switch and scope of <see cref="SlicedArtImporter"/>. The importer is OFF unless
    /// <c>ProjectSettings/CuvaraSlicedArtSettings.json</c> enables it:
    /// <c>{"enabled":true,"roots":["Assets/UI/Art/"]}</c>. Only PNGs under one of the roots are touched.
    /// Enabled with no roots means nothing is in scope.
    /// No EditorPrefs: the value is the same for every machine and every batch run.
    /// </summary>
    /// <remarks>
    /// Opt-in on purpose: two importers that both act on the same <c>.slice.json</c> must never run in
    /// one project. A project that still uses the penpot-uitoolkit-bridge importer keeps this one off
    /// until the bridge switches to it. See <c>Documentation~/PENPOT-BRIDGE-MOVE.md</c>.
    /// </remarks>
    public static class SlicedArtSettings
    {
        public const string SettingsFile = "ProjectSettings/CuvaraSlicedArtSettings.json";

        [Serializable]
        class Data
        {
            public bool enabled;
            public string[] roots;
        }

        static bool? s_Enabled;
        static string[] s_Roots;

        /// <summary>True when the settings file enables the importer (or a test overrode it).</summary>
        public static bool Enabled
        {
            get
            {
                if (s_Enabled == null)
                    Load();
                return s_Enabled.Value;
            }
        }

        public static IReadOnlyList<string> Roots
        {
            get
            {
                if (s_Roots == null)
                    Load();
                return s_Roots;
            }
        }

        /// <summary>Overrides the switch in memory (used by tests). Call <see cref="Reload"/> to go back to the file.</summary>
        public static void SetEnabled(bool enabled)
        {
            s_Enabled = enabled;
        }

        /// <summary>Overrides the roots in memory (used by tests). Call <see cref="Reload"/> to go back to the file.</summary>
        public static void SetRoots(IEnumerable<string> roots)
        {
            s_Roots = Normalize(roots);
        }

        public static void Reload()
        {
            s_Enabled = null;
            s_Roots = null;
        }

        /// <summary>True when the importer is enabled and the path is under one of the roots.</summary>
        public static bool IsInScope(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !Enabled)
                return false;
            var path = assetPath.Replace('\\', '/');
            foreach (var root in Roots)
            {
                if (path.StartsWith(root, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>Absolute project root (the folder that contains Assets). Main thread only.</summary>
        public static string ProjectRoot
        {
            get { return Path.GetDirectoryName(Application.dataPath).Replace('\\', '/'); }
        }

        static void Load()
        {
            var enabled = false;
            var roots = new string[0];
            try
            {
                var file = Path.Combine(ProjectRoot, SettingsFile);
                if (File.Exists(file))
                {
                    var data = JsonUtility.FromJson<Data>(File.ReadAllText(file));
                    if (data != null)
                    {
                        enabled = data.enabled;
                        if (data.roots != null)
                            roots = data.roots;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Cuvara.UIToolkit] Cannot read " + SettingsFile + ": " + e.Message);
            }
            if (s_Enabled == null)
                s_Enabled = enabled;
            if (s_Roots == null)
                s_Roots = Normalize(roots);
        }

        static string[] Normalize(IEnumerable<string> roots)
        {
            var list = new List<string>();
            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root))
                    continue;
                var r = root.Trim().Replace('\\', '/');
                if (!r.EndsWith("/", StringComparison.Ordinal))
                    r += "/";
                list.Add(r);
            }
            return list.ToArray();
        }
    }
}
