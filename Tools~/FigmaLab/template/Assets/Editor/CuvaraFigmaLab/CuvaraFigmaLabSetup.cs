// Copied into a throwaway lab project by Tools~/FigmaLab/create-lab-project.py. Not part of the
// com.cuvara.uitoolkit package (Tools~ is never imported); it compiles into the lab project's
// Assembly-CSharp-Editor only.
namespace CuvaraFigmaLab
{
    using System.IO;
    using System.Text.RegularExpressions;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Once per Editor session: turns on Pipeline's "Allow Browser Clients" (the Figma plugin runs in
    /// a sandboxed browser frame and needs it) when the documented settings file already holds that
    /// field, and otherwise prints the manual steps. Never creates the settings file and never edits
    /// a field it does not find.
    /// </summary>
    [InitializeOnLoad]
    internal static class CuvaraFigmaLabSetup
    {
        // Documented in com.unity.pipeline 0.9.1-exp.1, Documentation~/commands/editor-lifecycle-and-observability.md.
        private const string SettingsPath = "ProjectSettings/Packages/com.unity.pipeline/EditorPipelineConfig.json";

        // The serialized field behind Project Settings > Pipeline > Editor > Allow Browser Clients.
        private const string Field = "m_AllowBrowserClients";

        private const string SessionKey = "CuvaraFigmaLab.SetupDone";

        static CuvaraFigmaLabSetup()
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            SessionState.SetBool(SessionKey, true);
            EditorApplication.delayCall += Run;
        }

        [MenuItem("Tools/Cuvara Figma Lab/Check Allow Browser Clients")]
        private static void Run()
        {
            Debug.Log("[Cuvara Figma Lab] " + EnableBrowserClients());
            Debug.Log("[Cuvara Figma Lab] Next: Window > UI Toolkit > Connector for Figma design > Copy token, paste it in the Figma plugin "
                + "\"Unity UI Toolkit Connector\" (Settings > Pipeline token > Connect to Unity), select the test frame and Push. "
                + "Procedure and evidence checklist: Documentation~/FIGMA-REAL-IMPORT.md in com.cuvara.uitoolkit.");
        }

        private static string EnableBrowserClients()
        {
            const string manual = "Enable it by hand: Edit > Project Settings > Pipeline > Editor > Allow Browser Clients, then restart the Pipeline server there.";
            if (!File.Exists(SettingsPath))
            {
                return $"{SettingsPath} does not exist yet (Pipeline writes it the first time a setting changes). {manual}";
            }

            string text;
            try
            {
                text = File.ReadAllText(SettingsPath);
            }
            catch (IOException exception)
            {
                return $"Cannot read {SettingsPath}: {exception.Message}. {manual}";
            }

            var pattern = new Regex("(\"" + Field + "\"\\s*:\\s*)(true|false)");
            var match = pattern.Match(text);
            if (!match.Success) return $"{SettingsPath} has no {Field} field (another Pipeline version?). {manual}";
            if (match.Groups[2].Value == "true") return "Allow Browser Clients is already enabled.";

            File.WriteAllText(SettingsPath, pattern.Replace(text, "${1}true", 1));
            return $"Allow Browser Clients enabled in {SettingsPath}. It applies on the next server start: Edit > Project Settings > Pipeline > Editor > restart the server (or restart the Editor).";
        }
    }
}
