namespace Cuvara.UIToolkit.Editor.Figma
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>One <c>controlMap</c> row of the connector's mapping asset.</summary>
    public sealed class FigmaControlMapRow
    {
        public FigmaControlMapRow(string figmaName, string uitkControl)
        {
            this.FigmaName   = figmaName;
            this.UitkControl = uitkControl;
        }

        /// <summary>The Figma component or layer name the row matches.</summary>
        public string FigmaName { get; }

        /// <summary>The UI Toolkit control: a built-in name or a fully qualified type.</summary>
        public string UitkControl { get; }
    }

    /// <summary>How <c>ProjectSettings/FigmaMappingState.asset</c> selects the active mapping asset.</summary>
    public enum FigmaMappingSelection
    {
        /// <summary>Nothing chosen (or no state file): the connector adopts the first mapping asset, sorted by path.</summary>
        Unset,

        /// <summary>The user chose "no mapping" (<c>"-"</c>): the connector uses its built-in defaults.</summary>
        Cleared,

        /// <summary>A GUID is selected.</summary>
        Selected,
    }

    /// <summary>
    /// The Cuvara rows for the connector's component mapping, and the Unity-free rules for adding
    /// them.
    /// </summary>
    /// <remarks>
    /// <para>The connector's types are internal, so the preset never references them. The
    /// Editor side opens the mapping asset with <c>SerializedObject</c>; the field names below are
    /// read from <c>com.unity.ui.figma@0.3.0-exp.1</c>
    /// (<c>Editor/Settings/FigmaImportSettings.cs</c>, <c>Editor/Mapping/PropertyBinding.cs</c>).</para>
    /// </remarks>
    public static class FigmaMappingPreset
    {
        /// <summary>GUID of the connector's <c>FigmaImportSettings</c> script (0.3.0-exp.1).</summary>
        public const string SettingsScriptGuid = "0ced1937e6a344ac0a9f905cc9237a5b";

        /// <summary>Where the connector stores the active mapping selection.</summary>
        public const string MappingStatePath = "ProjectSettings/FigmaMappingState.asset";

        /// <summary>The value the connector writes when the user cleared the mapping on purpose.</summary>
        public const string ClearedGuid = "-";

        public const string PropertyControlMap  = "controlMap";
        public const string PropertyClassPrefix = "classPrefix";
        public const string PropertyFigmaName   = "figmaName";
        public const string PropertyUitkControl = "uitkControl";
        public const string PropertyBindings    = "bindings";
        public const string PropertyCaption     = "captionAttribute";
        public const string PropertyIcon        = "iconAttribute";

        /// <summary>The Cuvara controls a Figma component can map to.</summary>
        public static readonly IReadOnlyList<FigmaControlMapRow> CuvaraRows = new[]
        {
            new FigmaControlMapRow("SafeArea", "Cuvara.UIToolkit.Utilities.SafeAreaElement"),
        };

        private static readonly Regex ActiveGuidPattern = new Regex(@"^\s*m_ActiveMappingGuid:[ \t]*(\S*)[ \t]*\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

        /// <summary>
        /// The connector's name match: ASCII letters and digits only, lowercased, so
        /// <c>"Safe Area"</c>, <c>"safe-area"</c> and <c>"SafeArea"</c> are one name.
        /// </summary>
        public static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;

            var builder = new StringBuilder(name.Length);
            foreach (var c in name)
            {
                var alnum = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
                if (alnum) builder.Append(char.ToLowerInvariant(c));
            }

            return builder.ToString();
        }

        /// <summary>
        /// The preset rows whose Figma name no existing row already matches. A user row for the same
        /// name always wins, whatever control it maps to: the preset adds, it never overrides.
        /// </summary>
        public static IReadOnlyList<FigmaControlMapRow> MissingRows(IEnumerable<string> existingFigmaNames, IEnumerable<FigmaControlMapRow> preset)
        {
            var existing = new HashSet<string>(StringComparer.Ordinal);
            if (existingFigmaNames != null)
            {
                foreach (var name in existingFigmaNames) existing.Add(NormalizeName(name));
            }

            var missing = new List<FigmaControlMapRow>();
            if (preset == null) return missing;

            foreach (var row in preset)
            {
                var key = NormalizeName(row.FigmaName);
                if (key.Length == 0 || !existing.Add(key)) continue;
                missing.Add(row);
            }

            return missing;
        }

        /// <summary>
        /// Reads the active mapping selection from the text of
        /// <c>ProjectSettings/FigmaMappingState.asset</c>. Null or unreadable text is
        /// <see cref="FigmaMappingSelection.Unset"/>, as the connector treats a missing file.
        /// </summary>
        public static FigmaMappingSelection ParseMappingState(string stateFileText, out string guid)
        {
            guid = string.Empty;
            if (string.IsNullOrEmpty(stateFileText)) return FigmaMappingSelection.Unset;

            var match = ActiveGuidPattern.Match(stateFileText);
            if (!match.Success) return FigmaMappingSelection.Unset;

            var value = match.Groups[1].Value.Trim().Trim('\'', '"');
            if (value.Length == 0) return FigmaMappingSelection.Unset;
            if (value == ClearedGuid) return FigmaMappingSelection.Cleared;

            guid = value;
            return FigmaMappingSelection.Selected;
        }

        /// <summary>
        /// The <c>uitkControl</c> values that name a type (they contain a dot) and are not in
        /// <paramref name="knownFullNames"/>. Built-in names such as <c>Button</c> are the
        /// connector's business and are not checked.
        /// </summary>
        public static IReadOnlyList<string> UnresolvedControlTypes(IEnumerable<string> uitkControls, ICollection<string> knownFullNames)
        {
            var unresolved = new List<string>();
            if (uitkControls == null) return unresolved;

            foreach (var control in uitkControls)
            {
                var value = control?.Trim();
                if (string.IsNullOrEmpty(value) || value.IndexOf('.') < 0) continue;
                if (knownFullNames != null && knownFullNames.Contains(value)) continue;
                if (!unresolved.Contains(value)) unresolved.Add(value);
            }

            return unresolved;
        }
    }
}
