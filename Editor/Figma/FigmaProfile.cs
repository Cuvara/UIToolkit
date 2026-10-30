namespace Cuvara.UIToolkit.Editor.Figma
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// How one game (or one Figma file) uses the UI Toolkit Connector for Figma: where the
    /// connector imports to, which class prefix it should emit, how Figma tokens fill the Cuvara
    /// theme roles, and where the game's own files go.
    /// </summary>
    /// <remarks>
    /// <para>Editor-only. A project can hold several profiles, one per game or Figma file;
    /// <see cref="FindAll"/> finds every one of them. The connector itself knows nothing about
    /// profiles: they drive the token bridge, the mapping preset and the diagnostics window,
    /// which read the connector's files and never write into its import folder.</para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Cuvara/UI Toolkit/Figma Profile", fileName = "FigmaProfile")]
    public sealed class FigmaProfile : ScriptableObject
    {
        [Tooltip("The connector's import folder for this Figma file, e.g. Assets/Figma/<file>. Read only: nothing is ever written here.")]
        [SerializeField] private string connectorFolder = "Assets/Figma";

        [Tooltip("The USS class prefix the connector should emit for this game, e.g. \"rpg\". The mapping preset fills the connector's empty prefix with it; diagnostics flag imports that use another.")]
        [SerializeField] private string classPrefix = string.Empty;

        [Tooltip("Cuvara theme variable -> Figma token. Generates aliases only, e.g. --cuvara-color-primary: var(--brand-primary).")]
        [SerializeField] private List<FigmaTokenRole> tokenRoles = FigmaTokenBridge.CreateDefaultRoles();

        [Tooltip("Game-owned folder that receives cuvara-theme-bridge.uss. Must be under Assets/ and outside the connector folder.")]
        [SerializeField] private string bridgeOutputFolder = "Assets/UI/Theme";

        [Tooltip("Mode sheets the connector writes for this file (e.g. light.uss, dark.uss), relative to the connector folder.")]
        [SerializeField] private List<string> modeSheets = new List<string>();

        [Tooltip("Namespace for screens created from this file's UXML.")]
        [SerializeField] private string screensNamespace = string.Empty;

        [Tooltip("Game-owned folder for screens created from this file's UXML.")]
        [SerializeField] private string screensFolder = "Assets/UI/Screens";

        public string ConnectorFolder
        {
            get => FigmaPaths.Normalize(this.connectorFolder);
            set => this.connectorFolder = value;
        }

        public string ClassPrefix
        {
            get => (this.classPrefix ?? string.Empty).Trim();
            set => this.classPrefix = value;
        }

        public List<FigmaTokenRole> TokenRoles
        {
            get => this.tokenRoles ??= new List<FigmaTokenRole>();
            set => this.tokenRoles = value;
        }

        public string BridgeOutputFolder
        {
            get => FigmaPaths.Normalize(this.bridgeOutputFolder);
            set => this.bridgeOutputFolder = value;
        }

        public List<string> ModeSheets
        {
            get => this.modeSheets ??= new List<string>();
            set => this.modeSheets = value;
        }

        public string ScreensNamespace
        {
            get => (this.screensNamespace ?? string.Empty).Trim();
            set => this.screensNamespace = value;
        }

        public string ScreensFolder
        {
            get => FigmaPaths.Normalize(this.screensFolder);
            set => this.screensFolder = value;
        }

        /// <summary><c>&lt;bridge folder&gt;/cuvara-theme-bridge.uss</c>.</summary>
        public string BridgePath => FigmaPaths.BridgePath(this.BridgeOutputFolder);

        /// <summary>Every profile in the project, sorted by asset path so the order is stable.</summary>
        public static List<FigmaProfile> FindAll()
        {
            var paths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(FigmaProfile)))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path)) paths.Add(path);
            }

            paths.Sort(StringComparer.Ordinal);

            var profiles = new List<FigmaProfile>(paths.Count);
            foreach (var path in paths)
            {
                var profile = AssetDatabase.LoadAssetAtPath<FigmaProfile>(path);
                if (profile != null) profiles.Add(profile);
            }

            return profiles;
        }

        private void Reset()
        {
            this.tokenRoles = FigmaTokenBridge.CreateDefaultRoles();
        }
    }
}
