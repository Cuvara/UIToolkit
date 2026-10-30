namespace Cuvara.UIToolkit.Editor.Figma
{
    using Cuvara.UIToolkit.FigmaCore;
    using UnityEditor;
    using UnityEngine;

    /// <summary>The profile inspector: its fields, plus the actions that use them.</summary>
    [CustomEditor(typeof(FigmaProfile))]
    internal sealed class FigmaProfileEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            this.DrawDefaultInspector();

            var profile = (FigmaProfile)this.target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Bridge file", profile.BridgePath);

            if (GUILayout.Button("Regenerate token bridge"))
            {
                var diagnostics = FigmaProfileValidator.RegenerateBridge(profile, FigmaProfile.FindAll(), out var written);
                foreach (var diagnostic in diagnostics)
                {
                    if (diagnostic.Severity == FigmaDiagnosticSeverity.Info) continue;
                    Debug.LogWarning($"Token bridge '{profile.name}': [{diagnostic.Code}] {diagnostic.Message}", profile);
                }

                Debug.Log(written
                    ? $"Token bridge '{profile.name}': wrote {profile.BridgePath}."
                    : $"Token bridge '{profile.name}': {profile.BridgePath} not written (up to date, or refused above).", profile);
                FigmaDiagnosticsWindow.RefreshOpenWindows();
            }

            if (GUILayout.Button("Validate"))
            {
                FigmaProfileValidator.LogSummary(FigmaProfileValidator.Validate(profile, FigmaProfile.FindAll()));
            }

            if (GUILayout.Button("Open Figma Diagnostics"))
            {
                FigmaDiagnosticsWindow.Open();
            }
        }
    }
}
