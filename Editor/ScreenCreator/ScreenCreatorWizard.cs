namespace Cuvara.UIToolkit.Editor
{
    using System;
    using System.IO;
    using System.Linq;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// An Editor window that scaffolds a new screen, popup or collection item — the UXML,
    /// the USS, the C# (view interface + view + presenter), and a test skeleton — in one
    /// click, emitting code that matches the package's own API rather than the frozen
    /// GameFoundation wizard's.
    /// </summary>
    /// <remarks>
    /// With the source set to an existing UXML (for example a Figma import), it writes only the
    /// C# and the test, and the view loads the chosen UXML's key. The file list comes from
    /// <see cref="ScreenCreatorPlan"/>.
    /// </remarks>
    public sealed class ScreenCreatorWizard : EditorWindow
    {
        private const string WindowTitle = "Screen Creator";
        private const string UxmlPath = "Packages/com.cuvara.uitoolkit/Editor/ScreenCreator/ScreenCreatorWizard.uxml";

        private const string SourceNewUxml      = "New UXML";
        private const string SourceExistingUxml = "Existing UXML (e.g. Figma import)";

        private DropdownField typeDropdown;
        private Toggle        hasModelToggle;
        private TextField     nameField;
        private TextField     outputPathField;
        private Button        browseButton;
        private Button        generateButton;
        private DropdownField sourceDropdown;
        private ObjectField   existingUxmlField;
        private TextField     assetKeyField;

        [MenuItem("Assets/Cuvara/Create Screen")]
        public static void OpenWindow()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Screen Creator cannot be used during Play mode.");
                return;
            }

            var window = GetWindow<ScreenCreatorWizard>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(400, 280);
        }

        public void CreateGUI()
        {
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);

            if (visualTree == null)
            {
                this.rootVisualElement.Add(new Label($"Could not load {UxmlPath}"));
                return;
            }

            this.rootVisualElement.Add(visualTree.Instantiate());

            this.typeDropdown    = this.rootVisualElement.Q<DropdownField>("type-dropdown");
            this.hasModelToggle  = this.rootVisualElement.Q<Toggle>("has-model-toggle");
            this.nameField       = this.rootVisualElement.Q<TextField>("name-field");
            this.outputPathField = this.rootVisualElement.Q<TextField>("output-path-field");
            this.browseButton    = this.rootVisualElement.Q<Button>("browse-button");
            this.generateButton  = this.rootVisualElement.Q<Button>("generate-button");
            this.sourceDropdown    = this.rootVisualElement.Q<DropdownField>("source-dropdown");
            this.existingUxmlField = this.rootVisualElement.Q<ObjectField>("existing-uxml-field");
            this.assetKeyField     = this.rootVisualElement.Q<TextField>("asset-key-field");

            this.typeDropdown.choices = Enum.GetNames(typeof(ScreenType)).ToList();
            this.typeDropdown.value = nameof(ScreenType.Screen);

            this.typeDropdown.RegisterValueChangedCallback(evt =>
            {
                if (Enum.TryParse(evt.newValue, out ScreenType type) && type == ScreenType.Item)
                {
                    this.hasModelToggle.value = true;
                    this.hasModelToggle.SetEnabled(false);
                }
                else
                {
                    this.hasModelToggle.SetEnabled(true);
                }
            });

            this.sourceDropdown.choices = new System.Collections.Generic.List<string> { SourceNewUxml, SourceExistingUxml };
            this.sourceDropdown.value = SourceNewUxml;
            this.existingUxmlField.objectType = typeof(VisualTreeAsset);
            this.existingUxmlField.allowSceneObjects = false;
            this.sourceDropdown.RegisterValueChangedCallback(_ => this.RefreshSourceFields());

            this.existingUxmlField.RegisterValueChangedCallback(evt =>
            {
                var path = evt.newValue != null ? AssetDatabase.GetAssetPath(evt.newValue) : string.Empty;
                this.assetKeyField.value = ScreenCreatorTemplates.DefaultAssetKey(path);

                // Offer the UXML's name as the screen name while the field still holds its default.
                var suggested = Codegen.UxmlIdentifier.ToPascalCase(ScreenCreatorTemplates.DefaultAssetKey(path));
                if (suggested.Length > 0 && (this.nameField.value == "MyScreen" || string.IsNullOrWhiteSpace(this.nameField.value)))
                    this.nameField.value = suggested;
            });

            this.RefreshSourceFields();

            // Default output path from selection
            var selected = Selection.activeObject;
            this.outputPathField.value = selected != null
                ? AssetDatabase.GetAssetPath(selected)
                : "Assets";

            this.browseButton.clicked += () =>
            {
                var path = EditorUtility.OpenFolderPanel("Choose output folder", "Assets", "");
                if (!string.IsNullOrEmpty(path) && path.Contains("Assets"))
                    this.outputPathField.value = FileUtil.GetProjectRelativePath(path);
            };

            this.generateButton.clicked += this.OnGenerate;
        }

        private ScreenSource SelectedSource =>
            this.sourceDropdown != null && this.sourceDropdown.value == SourceExistingUxml
                ? ScreenSource.ExistingUxml
                : ScreenSource.NewUxml;

        private void RefreshSourceFields()
        {
            var existing = this.SelectedSource == ScreenSource.ExistingUxml;
            this.existingUxmlField.style.display = existing ? DisplayStyle.Flex : DisplayStyle.None;
            this.assetKeyField.style.display     = existing ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnGenerate()
        {
            var name = this.nameField.value?.Trim();

            if (string.IsNullOrEmpty(name))
            {
                EditorUtility.DisplayDialog(WindowTitle, "Please enter a name.", "OK");
                return;
            }

            if (!Enum.TryParse(this.typeDropdown.value, out ScreenType type))
            {
                EditorUtility.DisplayDialog(WindowTitle, "Invalid screen type.", "OK");
                return;
            }

            var outputPath = this.outputPathField.value?.Trim();
            if (string.IsNullOrEmpty(outputPath)) outputPath = "Assets";
            if (outputPath.EndsWith("/")) outputPath = outputPath[..^1];

            var hasModel = this.hasModelToggle.value;

            // Derive namespace from the output path
            var ns = outputPath.Replace("Assets/", "").Replace("Assets", "");
            ns = string.IsNullOrWhiteSpace(ns) ? name : ns.Replace("/", ".").Replace(" ", "_");

            var source = this.SelectedSource;
            var request = new ScreenCreatorRequest
            {
                Type      = type,
                HasModel  = hasModel,
                Source    = source,
                Name      = name,
                Namespace = ns,
            };

            if (source == ScreenSource.ExistingUxml)
            {
                var uxmlAsset = this.existingUxmlField.value;
                if (uxmlAsset == null)
                {
                    EditorUtility.DisplayDialog(WindowTitle, "Choose the existing UXML the screen should load.", "OK");
                    return;
                }

                request.ExistingUxmlPath = AssetDatabase.GetAssetPath(uxmlAsset);
                request.AssetKey         = this.assetKeyField.value;
            }

            var projectDir  = Directory.GetParent(Application.dataPath)!.FullName;
            var absoluteDir = Path.Combine(projectDir, outputPath);

            // The connector owns its import folder and rewrites it on every push; code generated
            // there would sit in a folder nobody else is supposed to write to.
            if (ScreenCreatorPlan.IsInsideConnectorImportFolder(absoluteDir, projectDir))
            {
                EditorUtility.DisplayDialog(WindowTitle,
                    "The output folder is inside a Figma import folder (it holds a .figma-manifest.json). "
                    + "Choose a folder your game owns.", "OK");
                return;
            }

            System.Collections.Generic.IReadOnlyList<ScreenCreatorFile> plan;
            try
            {
                plan = ScreenCreatorPlan.Build(request);
            }
            catch (ArgumentException e)
            {
                EditorUtility.DisplayDialog(WindowTitle, e.Message, "OK");
                return;
            }

            if (!Directory.Exists(absoluteDir))
                Directory.CreateDirectory(absoluteDir);

            var filesCreated = 0;

            // Script, then UXML and USS (new UXML only; no USS for items), then the test skeleton.
            foreach (var file in plan)
            {
                if (TryWriteFile(Path.Combine(absoluteDir, file.FileName), file.Content)) filesCreated++;
            }

            AssetDatabase.Refresh();

            Debug.Log($"<color=green>Screen Creator: generated {filesCreated} file(s) for '{name}' at {outputPath}</color>");

            if (filesCreated > 0)
                this.Close();
        }

        private static bool TryWriteFile(string absolutePath, string content)
        {
            if (File.Exists(absolutePath))
            {
                Debug.LogError($"Screen Creator: file already exists — {absolutePath}");
                return false;
            }

            try
            {
                File.WriteAllText(absolutePath, content);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Screen Creator: could not write {absolutePath} — {e.Message}");
                return false;
            }
        }
    }
}
