namespace Cuvara.UIToolkit.Editor.Figma
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Cuvara.UIToolkit.FigmaCore;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Object = UnityEngine.Object;

    /// <summary>The connector mapping asset the connector would use right now.</summary>
    public sealed class FigmaMappingAssetInfo
    {
        public Object Asset;
        public string Path;
        public FigmaMappingSelection Selection;
        public string ClassPrefix;
        public int RowCount;
    }

    /// <summary>
    /// Adds the Cuvara rows to the connector's active <c>FigmaImportSettings</c> asset through
    /// <c>SerializedObject</c>, and checks that every typed <c>uitkControl</c> resolves.
    /// </summary>
    /// <remarks>
    /// <para><b>No connector types.</b> The asset is found by its script GUID and edited by
    /// property path. A renamed property is an error diagnostic and nothing is written; it is
    /// never an exception.</para>
    ///
    /// <para><b>Idempotent and additive.</b> A row is added only when no row matches its Figma
    /// name (the connector's punctuation- and case-blind match). User rows are never removed or
    /// changed; the class prefix is set only when it is empty.</para>
    /// </remarks>
    public static class FigmaMappingPresetCommand
    {
        public const string CodeMappingCleared       = "cuvara.mapping.cleared";
        public const string CodeNoMappingAsset       = "cuvara.mapping.no-asset";
        public const string CodeConnectorScript      = "cuvara.mapping.connector-script-missing";
        public const string CodePropertyMissing      = "cuvara.mapping.property-missing";
        public const string CodeControlUnresolved    = "cuvara.mapping.control-unresolved";
        public const string CodeControlNotUxml       = "cuvara.mapping.control-not-uxml-element";
        public const string CodePrefixDiffers        = "cuvara.mapping.prefix-differs";
        public const string CodeRowsAdded            = "cuvara.mapping.rows-added";

        /// <summary>Menu entry: applies the preset; the prefix comes from the only profile that sets one.</summary>
        [MenuItem("Tools/Cuvara/Figma/Apply Cuvara Mapping Preset")]
        public static void ApplyFromMenu()
        {
            var diagnostics = Apply(SinglePrefixedProfile(), out var added);
            LogDiagnostics("Cuvara mapping preset", diagnostics);
            Debug.Log($"Cuvara mapping preset: {added} row(s) added.");
            FigmaDiagnosticsWindow.RefreshOpenWindows();
        }

        /// <summary>
        /// The profile whose prefix the menu entry applies: the only one with a prefix, or null when
        /// none or several (with different prefixes) set one, so a project-wide setting is never guessed.
        /// </summary>
        public static FigmaProfile SinglePrefixedProfile()
        {
            FigmaProfile chosen = null;
            foreach (var profile in FigmaProfile.FindAll())
            {
                if (profile.ClassPrefix.Length == 0) continue;
                if (chosen != null && chosen.ClassPrefix != profile.ClassPrefix) return null;
                chosen ??= profile;
            }

            return chosen;
        }

        /// <summary>Finds the mapping asset the connector would use, as its own lookup does.</summary>
        public static FigmaMappingAssetInfo LocateActive(List<FigmaDiagnostic> diagnostics)
        {
            var stateText = ReadStateFile();
            var selection = FigmaMappingPreset.ParseMappingState(stateText, out var guid);

            if (selection == FigmaMappingSelection.Cleared)
            {
                diagnostics?.Add(FigmaPaths.Info(CodeMappingCleared, FigmaMappingPreset.MappingStatePath,
                    "The connector's mapping is cleared on purpose (no mapping asset is active). Select one on the connector's Project Settings page to use the preset."));
                return null;
            }

            if (selection == FigmaMappingSelection.Selected)
            {
                var selectedPath = AssetDatabase.GUIDToAssetPath(guid);
                var selected = string.IsNullOrEmpty(selectedPath) ? null : AssetDatabase.LoadMainAssetAtPath(selectedPath);
                if (selected != null && IsMappingAsset(selected)) return Describe(selected, selectedPath, selection);

                // The connector falls back to the first asset when the selected one is gone.
            }

            foreach (var path in CandidatePaths(diagnostics))
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset != null && IsMappingAsset(asset)) return Describe(asset, path, selection);
            }

            diagnostics?.Add(FigmaPaths.Warning(CodeNoMappingAsset, FigmaMappingPreset.MappingStatePath,
                "No connector mapping asset (FigmaImportSettings) exists. Create one on the connector's Project Settings page, then apply the preset."));
            return null;
        }

        /// <summary>Adds the missing Cuvara rows and, when empty, the profile's class prefix.</summary>
        /// <param name="profile">Supplies the class prefix; null leaves the prefix alone.</param>
        /// <param name="added">Rows added by this call; 0 on a second call.</param>
        public static List<FigmaDiagnostic> Apply(FigmaProfile profile, out int added)
        {
            added = 0;
            var diagnostics = new List<FigmaDiagnostic>();
            var info = LocateActive(diagnostics);
            if (info == null) return diagnostics;

            var serialized = new SerializedObject(info.Asset);
            var profileName = profile != null ? profile.name : string.Empty;
            var prefix = profile != null ? profile.ClassPrefix : string.Empty;
            var applied = ApplyTo(serialized, info.Path, prefix, profileName, out added);
            diagnostics.AddRange(applied);
            foreach (var diagnostic in applied)
            {
                if (diagnostic.Severity == FigmaDiagnosticSeverity.Error) return diagnostics;
            }

            AssetDatabase.SaveAssetIfDirty(info.Asset);

            diagnostics.AddRange(ValidateControlTypes(info));
            return diagnostics;
        }

        /// <summary>
        /// The edit itself, on an already opened mapping asset: adds the missing Cuvara rows and,
        /// when the asset's prefix is empty, <paramref name="prefix"/>. On any missing property it
        /// returns an error diagnostic and applies nothing.
        /// </summary>
        public static List<FigmaDiagnostic> ApplyTo(SerializedObject serialized, string path, string prefix, string profileName, out int added)
        {
            added = 0;
            var diagnostics = new List<FigmaDiagnostic>();
            prefix ??= string.Empty;
            serialized.Update();

            var controlMap = serialized.FindProperty(FigmaMappingPreset.PropertyControlMap);
            if (controlMap == null || !controlMap.isArray)
            {
                diagnostics.Add(MissingProperty(path, FigmaMappingPreset.PropertyControlMap));
                return diagnostics;
            }

            var existingNames = new List<string>();
            for (var i = 0; i < controlMap.arraySize; i++)
            {
                var name = controlMap.GetArrayElementAtIndex(i).FindPropertyRelative(FigmaMappingPreset.PropertyFigmaName);
                if (name == null)
                {
                    diagnostics.Add(MissingProperty(path, FigmaMappingPreset.PropertyControlMap + "[].figmaName"));
                    added = 0;
                    return diagnostics;
                }

                existingNames.Add(name.stringValue);
            }

            var changed = false;
            foreach (var row in FigmaMappingPreset.MissingRows(existingNames, FigmaMappingPreset.CuvaraRows))
            {
                var index = controlMap.arraySize;
                controlMap.InsertArrayElementAtIndex(index);
                var element = controlMap.GetArrayElementAtIndex(index);

                var figmaName = element.FindPropertyRelative(FigmaMappingPreset.PropertyFigmaName);
                var control   = element.FindPropertyRelative(FigmaMappingPreset.PropertyUitkControl);
                if (figmaName == null || control == null)
                {
                    // Nothing applied yet: returning before ApplyModifiedProperties discards the insert.
                    diagnostics.Add(MissingProperty(path, FigmaMappingPreset.PropertyControlMap + "[].uitkControl"));
                    added = 0;
                    return diagnostics;
                }

                // The inserted element is a copy of the previous last row: reset every field.
                figmaName.stringValue = row.FigmaName;
                control.stringValue   = row.UitkControl;
                ClearString(element, FigmaMappingPreset.PropertyCaption);
                ClearString(element, FigmaMappingPreset.PropertyIcon);
                var bindings = element.FindPropertyRelative(FigmaMappingPreset.PropertyBindings);
                if (bindings != null && bindings.isArray) bindings.arraySize = 0;

                added++;
                changed = true;
            }

            if (prefix.Length > 0)
            {
                var classPrefix = serialized.FindProperty(FigmaMappingPreset.PropertyClassPrefix);
                if (classPrefix == null)
                {
                    diagnostics.Add(MissingProperty(path, FigmaMappingPreset.PropertyClassPrefix));
                    added = 0;
                    return diagnostics;
                }

                if (string.IsNullOrEmpty(classPrefix.stringValue))
                {
                    classPrefix.stringValue = prefix;
                    changed = true;
                }
                else if (classPrefix.stringValue != prefix)
                {
                    diagnostics.Add(FigmaPaths.Warning(CodePrefixDiffers, path,
                        $"The connector's class prefix is '{classPrefix.stringValue}', profile '{profileName}' expects '{prefix}'. The preset never overwrites a prefix; change it on the connector's Project Settings page if needed."));
                }
            }

            if (changed) serialized.ApplyModifiedProperties();

            if (added > 0)
            {
                diagnostics.Add(FigmaPaths.Info(CodeRowsAdded, path, $"Added {added} Cuvara control mapping row(s)."));
            }

            return diagnostics;
        }

        /// <summary>Every <c>uitkControl</c> that names a type must be a <c>VisualElement</c> the project has.</summary>
        public static List<FigmaDiagnostic> ValidateControlTypes(FigmaMappingAssetInfo info)
        {
            var diagnostics = new List<FigmaDiagnostic>();
            if (info?.Asset == null) return diagnostics;

            var serialized = new SerializedObject(info.Asset);
            var controlMap = serialized.FindProperty(FigmaMappingPreset.PropertyControlMap);
            if (controlMap == null || !controlMap.isArray)
            {
                diagnostics.Add(MissingProperty(info.Path, FigmaMappingPreset.PropertyControlMap));
                return diagnostics;
            }

            var controls = new List<string>();
            for (var i = 0; i < controlMap.arraySize; i++)
            {
                var control = controlMap.GetArrayElementAtIndex(i).FindPropertyRelative(FigmaMappingPreset.PropertyUitkControl);
                if (control == null)
                {
                    diagnostics.Add(MissingProperty(info.Path, FigmaMappingPreset.PropertyControlMap + "[].uitkControl"));
                    return diagnostics;
                }

                controls.Add(control.stringValue);
            }

            var types = VisualElementTypesByFullName();
            foreach (var missing in FigmaMappingPreset.UnresolvedControlTypes(controls, types.Keys))
            {
                diagnostics.Add(FigmaPaths.Error(CodeControlUnresolved, info.Path,
                    $"Mapped control '{missing}' is not a VisualElement type in this project. The connector falls back to a default element for it."));
            }

            foreach (var control in controls)
            {
                var name = control?.Trim();
                if (string.IsNullOrEmpty(name) || !types.TryGetValue(name, out var type)) continue;
                if (type.IsDefined(typeof(UxmlElementAttribute), false)) continue;
                diagnostics.Add(FigmaPaths.Warning(CodeControlNotUxml, info.Path,
                    $"Mapped control '{name}' has no [UxmlElement] attribute, so UXML cannot create it."));
            }

            return diagnostics;
        }

        internal static void LogDiagnostics(string context, IEnumerable<FigmaDiagnostic> diagnostics)
        {
            foreach (var diagnostic in diagnostics)
            {
                var line = $"{context}: [{diagnostic.Code}] {diagnostic.Message} ({diagnostic.File})";
                if (diagnostic.Severity == FigmaDiagnosticSeverity.Info) Debug.Log(line);
                else Debug.LogWarning(line);
            }
        }

        private static Dictionary<string, Type> VisualElementTypesByFullName()
        {
            var types = new Dictionary<string, Type>(StringComparer.Ordinal) { { typeof(VisualElement).FullName, typeof(VisualElement) } };
            foreach (var type in TypeCache.GetTypesDerivedFrom<VisualElement>())
            {
                var fullName = type.FullName;
                if (!string.IsNullOrEmpty(fullName) && !types.ContainsKey(fullName)) types.Add(fullName, type);
            }

            return types;
        }

        private static string ReadStateFile()
        {
            try
            {
                return File.Exists(FigmaMappingPreset.MappingStatePath) ? File.ReadAllText(FigmaMappingPreset.MappingStatePath) : null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        private static IEnumerable<string> CandidatePaths(List<FigmaDiagnostic> diagnostics)
        {
            var scriptPath = AssetDatabase.GUIDToAssetPath(FigmaMappingPreset.SettingsScriptGuid);
            var script = string.IsNullOrEmpty(scriptPath) ? null : AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            if (script == null)
            {
                diagnostics?.Add(FigmaPaths.Error(CodeConnectorScript, FigmaMappingPreset.MappingStatePath,
                    $"The connector's FigmaImportSettings script (GUID {FigmaMappingPreset.SettingsScriptGuid}) was not found. Is com.unity.ui.figma installed at the tested version?"));
                return Array.Empty<string>();
            }

            var typeName = script.GetClass() != null ? script.GetClass().Name : "FigmaImportSettings";
            var paths = new List<string>();
            foreach (var assetGuid in AssetDatabase.FindAssets("t:" + typeName))
            {
                var path = AssetDatabase.GUIDToAssetPath(assetGuid);
                if (!string.IsNullOrEmpty(path)) paths.Add(path);
            }

            // Sorted, as the connector sorts, so several mapping assets always resolve the same way.
            paths.Sort(StringComparer.Ordinal);
            return paths;
        }

        private static bool IsMappingAsset(Object asset)
        {
            var script = new SerializedObject(asset).FindProperty("m_Script");
            if (script == null || script.objectReferenceValue == null) return false;
            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script.objectReferenceValue, out var guid, out long _)
                && guid == FigmaMappingPreset.SettingsScriptGuid;
        }

        private static FigmaMappingAssetInfo Describe(Object asset, string path, FigmaMappingSelection selection)
        {
            var serialized = new SerializedObject(asset);
            var prefix = serialized.FindProperty(FigmaMappingPreset.PropertyClassPrefix);
            var map = serialized.FindProperty(FigmaMappingPreset.PropertyControlMap);
            return new FigmaMappingAssetInfo
            {
                Asset       = asset,
                Path        = path,
                Selection   = selection,
                ClassPrefix = prefix != null ? prefix.stringValue : null,
                RowCount    = map != null && map.isArray ? map.arraySize : 0,
            };
        }

        private static void ClearString(SerializedProperty element, string relativePath)
        {
            var property = element.FindPropertyRelative(relativePath);
            if (property != null && property.propertyType == SerializedPropertyType.String) property.stringValue = string.Empty;
        }

        private static FigmaDiagnostic MissingProperty(string path, string property)
        {
            return FigmaPaths.Error(CodePropertyMissing, path,
                $"The connector mapping asset has no '{property}' property. The connector version may not be the tested 0.3.0-exp.1; nothing was changed.");
        }
    }
}
