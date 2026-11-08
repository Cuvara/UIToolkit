using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cuvara.UIToolkit.Motion
{
    /// <summary>
    /// The <c>motion</c> profile section of penpot-uitoolkit-bridge (see Documentation~/MOTION.md) in a form JsonUtility reads, so the presets a designer tags in
    /// Penpot (<c>rpg.motion</c>) also drive code animations. JSON shape:
    /// <code>
    /// { "version": 1,
    ///   "durations": [ { "name": "fast", "ms": 120 }, { "name": "normal", "ms": 200 } ],
    ///   "presets": [ { "name": "button", "duration": "fast", "easing": "ease-out", "delayMs": 0,
    ///                  "properties": [ "scale", "translate", "opacity" ], "crossfade": false,
    ///                  "states": [ { "state": "hover", "scale": "1.04" },
    ///                              { "state": "pressed", "scale": "0.96", "translate": "0 2px" },
    ///                              { "state": "disabled", "opacity": "0.6" } ] } ] }
    /// </code>
    /// <c>duration</c> is a name from <c>durations</c> or a number of ms. State values are strings (JsonUtility has no optional
    /// numbers): <c>opacity</c> "0.6", <c>scale</c> "1.04" or "1.04 0.9", <c>translate</c> "0 2px" (px only), <c>rotate</c> "10deg",
    /// <c>color</c> / <c>backgroundColor</c> / <c>tint</c> "#RRGGBB" or "#RRGGBBAA". Unknown JSON fields are ignored.
    /// </summary>
    [Serializable]
    public sealed class MotionPresetSet
    {
        public int version = 1;
        public List<MotionNamedDuration> durations = new List<MotionNamedDuration>();
        public List<MotionPresetEntry> presets = new List<MotionPresetEntry>();

        /// <summary>Parse and validate. Invalid presets are left out of <paramref name="resolved"/> and described in <paramref name="errors"/>.</summary>
        public static bool TryResolveJson(string json, out Dictionary<string, MotionPreset> resolved, out List<string> errors)
        {
            errors = new List<string>();
            resolved = new Dictionary<string, MotionPreset>();
            MotionPresetSet set;
            try
            {
                set = JsonUtility.FromJson<MotionPresetSet>(json);
            }
            catch (ArgumentException e)
            {
                errors.Add("motion presets: invalid JSON: " + e.Message);
                return false;
            }
            if (set == null)
            {
                errors.Add("motion presets: empty JSON");
                return false;
            }
            resolved = set.Resolve(errors);
            return errors.Count == 0;
        }

        public Dictionary<string, MotionPreset> Resolve(List<string> errors)
        {
            var result = new Dictionary<string, MotionPreset>(StringComparer.Ordinal);
            var names = new Dictionary<string, int>(StringComparer.Ordinal);
            if (durations != null)
                foreach (var d in durations)
                    if (d != null && !string.IsNullOrEmpty(d.name))
                        names[d.name] = d.ms;
            if (presets == null)
                return result;
            foreach (var entry in presets)
            {
                if (entry == null)
                    continue;
                var before = errors.Count;
                var preset = MotionPreset.Build(entry, names, errors);
                if (preset == null || errors.Count != before)
                    continue;
                if (result.ContainsKey(entry.name))
                    errors.Add("motion preset '" + entry.name + "': defined twice");
                else
                    result[entry.name] = preset;
            }
            return result;
        }
    }

    [Serializable]
    public sealed class MotionNamedDuration
    {
        public string name;
        public int ms;
    }

    [Serializable]
    public sealed class MotionPresetEntry
    {
        public string name;
        public string duration;
        public string easing;
        public int delayMs;
        public List<string> properties = new List<string>();
        public List<MotionStateEntry> states = new List<MotionStateEntry>();
        public bool crossfade;
    }

    [Serializable]
    public sealed class MotionStateEntry
    {
        public string state;
        public string opacity;
        public string scale;
        public string translate;
        public string rotate;
        public string color;
        public string backgroundColor;
        public string tint;
    }

    /// <summary>A validated preset: timing, the properties it may animate and the end values of its states.</summary>
    public sealed class MotionPreset
    {
        /// <summary>The state that means "back to rest": identity values for every non-colour property of the preset.</summary>
        public const string RestState = "rest";

        readonly Dictionary<string, MotionValues> m_States = new Dictionary<string, MotionValues>(StringComparer.Ordinal);

        public string Name { get; private set; }
        public MotionTiming Timing { get; private set; }
        public MotionChannels Channels { get; private set; }
        /// <summary>Carried for penpot-uitoolkit-bridge (stacked state-art layers); the runtime helpers do not use it.</summary>
        public bool Crossfade { get; private set; }

        public bool TryGetState(string state, out MotionValues values)
        {
            if (state == RestState)
            {
                values = default;
                if ((Channels & MotionChannels.Opacity) != 0) values = values.WithOpacity(1f);
                if ((Channels & MotionChannels.Translate) != 0) values = values.WithTranslate(0f, 0f);
                if ((Channels & MotionChannels.Scale) != 0) values = values.WithScale(1f);
                if ((Channels & MotionChannels.Rotate) != 0) values = values.WithRotate(0f);
                return values.Channels != MotionChannels.None;
            }
            return m_States.TryGetValue(state ?? string.Empty, out values);
        }

        /// <summary>
        /// A tween of <paramref name="element"/> to the end values of <paramref name="state"/> ("hover", "pressed", "checked", "focus",
        /// "disabled" or <see cref="RestState"/>) with this preset's timing, so a non-Button element can feel like a tagged button.
        /// An unknown state logs a warning and returns an empty clip.
        /// </summary>
        public MotionClip To(VisualElement element, string state)
        {
            if (!TryGetState(state, out var values))
            {
                Debug.LogWarning("Motion preset '" + Name + "' has no state '" + state + "'.");
                return UiMotion.Wait(0f);
            }
            return UiMotion.Tween(element, values, Timing);
        }

        internal static MotionPreset Build(MotionPresetEntry e, Dictionary<string, int> durationNames, List<string> errors)
        {
            var label = "motion preset '" + e.name + "'";
            if (string.IsNullOrEmpty(e.name))
            {
                errors.Add("motion preset without a name");
                return null;
            }
            var p = new MotionPreset { Name = e.name, Crossfade = e.crossfade };

            float ms = 0f;
            if (!string.IsNullOrEmpty(e.duration))
            {
                if (int.TryParse(e.duration, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                    ms = n;
                else if (durationNames.TryGetValue(e.duration, out var named))
                    ms = named;
                else
                    errors.Add(label + ": unknown duration '" + e.duration + "'");
            }
            else
                errors.Add(label + ": duration is missing");
            if (ms < 0f)
                errors.Add(label + ": negative duration");

            var easing = MotionEasing.Linear;
            if (!string.IsNullOrEmpty(e.easing) && !MotionEasing.TryParse(e.easing, out easing))
                errors.Add(label + ": easing '" + e.easing + "' is not linear, ease, ease-in, ease-out, ease-in-out or cubic-bezier(...)");
            if (e.delayMs < 0)
                errors.Add(label + ": negative delay");

            foreach (var prop in e.properties ?? new List<string>())
            {
                switch (prop)
                {
                    case "opacity": p.Channels |= MotionChannels.Opacity; break;
                    case "translate": p.Channels |= MotionChannels.Translate; break;
                    case "scale": p.Channels |= MotionChannels.Scale; break;
                    case "rotate": p.Channels |= MotionChannels.Rotate; break;
                    case "background-color": p.Channels |= MotionChannels.BackgroundColor; break;
                    case "-unity-background-image-tint-color": p.Channels |= MotionChannels.Tint; break;
                    case "color": p.Channels |= MotionChannels.TextColor; break;
                    // Allowed in a profile (no layout, no geometry) but not something these helpers drive.
                    case "border-top-color":
                    case "border-right-color":
                    case "border-bottom-color":
                    case "border-left-color":
                    case "-unity-text-outline-color":
                        break;
                    default:
                        errors.Add(label + ": property '" + prop + "' is not allowed (only opacity, translate, scale, rotate and colour properties)");
                        break;
                }
            }

            if (ms >= 0f && e.delayMs >= 0)
                p.Timing = new MotionTiming(ms, easing, e.delayMs);

            foreach (var s in e.states ?? new List<MotionStateEntry>())
            {
                if (s == null || string.IsNullOrEmpty(s.state))
                {
                    errors.Add(label + ": state without a name");
                    continue;
                }
                if (s.state == RestState)
                {
                    errors.Add(label + ": 'rest' is reserved");
                    continue;
                }
                var values = ParseState(s, p.Channels, label + " state '" + s.state + "'", errors);
                if (values.Channels != MotionChannels.None)
                    p.m_States[s.state] = values;
            }
            return p;
        }

        static MotionValues ParseState(MotionStateEntry s, MotionChannels allowed, string label, List<string> errors)
        {
            var v = new MotionValues();
            if (!string.IsNullOrEmpty(s.opacity))
            {
                if (Check(MotionChannels.Opacity, "opacity", allowed, label, errors) && TryFloat(s.opacity, out var f))
                    v = v.WithOpacity(f);
                else
                    errors.Add(label + ": bad opacity '" + s.opacity + "'");
            }
            if (!string.IsNullOrEmpty(s.scale))
            {
                if (Check(MotionChannels.Scale, "scale", allowed, label, errors) && TryPair(s.scale, "", false, out var xy))
                    v = v.WithScale(xy);
                else
                    errors.Add(label + ": bad scale '" + s.scale + "'");
            }
            if (!string.IsNullOrEmpty(s.translate))
            {
                if (Check(MotionChannels.Translate, "translate", allowed, label, errors) && TryPair(s.translate, "px", true, out var xy))
                    v = v.WithTranslate(xy);
                else
                    errors.Add(label + ": bad translate '" + s.translate + "' (pixels only)");
            }
            if (!string.IsNullOrEmpty(s.rotate))
            {
                var text = s.rotate.EndsWith("deg", StringComparison.Ordinal) ? s.rotate.Substring(0, s.rotate.Length - 3) : s.rotate;
                if (Check(MotionChannels.Rotate, "rotate", allowed, label, errors) && TryFloat(text, out var f))
                    v = v.WithRotate(f);
                else
                    errors.Add(label + ": bad rotate '" + s.rotate + "'");
            }
            if (!string.IsNullOrEmpty(s.color))
            {
                if (Check(MotionChannels.TextColor, "color", allowed, label, errors) && ColorUtility.TryParseHtmlString(s.color, out var c))
                    v = v.WithTextColor(c);
                else
                    errors.Add(label + ": bad color '" + s.color + "'");
            }
            if (!string.IsNullOrEmpty(s.backgroundColor))
            {
                if (Check(MotionChannels.BackgroundColor, "background-color", allowed, label, errors) && ColorUtility.TryParseHtmlString(s.backgroundColor, out var c))
                    v = v.WithBackgroundColor(c);
                else
                    errors.Add(label + ": bad backgroundColor '" + s.backgroundColor + "'");
            }
            if (!string.IsNullOrEmpty(s.tint))
            {
                if (Check(MotionChannels.Tint, "-unity-background-image-tint-color", allowed, label, errors) && ColorUtility.TryParseHtmlString(s.tint, out var c))
                    v = v.WithTint(c);
                else
                    errors.Add(label + ": bad tint '" + s.tint + "'");
            }
            return v;
        }

        static bool Check(MotionChannels channel, string property, MotionChannels allowed, string label, List<string> errors)
        {
            if ((allowed & channel) != 0)
                return true;
            errors.Add(label + ": sets " + property + " but the preset's properties do not list it");
            return false;
        }

        static bool TryFloat(string text, out float value) =>
            float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary>One or two numbers; with <paramref name="unit"/> each may carry that suffix (a percent sign is rejected).</summary>
        static bool TryPair(string text, string unit, bool secondDefaultsToZero, out Vector2 value)
        {
            value = default;
            var parts = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1 || parts.Length > 2)
                return false;
            var n = new float[2];
            for (var i = 0; i < parts.Length; i++)
            {
                var token = parts[i];
                if (unit.Length > 0 && token.EndsWith(unit, StringComparison.Ordinal))
                    token = token.Substring(0, token.Length - unit.Length);
                if (!TryFloat(token, out n[i]))
                    return false;
            }
            if (parts.Length == 1)
                n[1] = secondDefaultsToZero ? 0f : n[0];
            value = new Vector2(n[0], n[1]);
            return true;
        }
    }

    /// <summary>
    /// The presets game code looks up by name. Load the generated JSON (or a <see cref="MotionPresetAsset"/>) once at start-up.
    /// A missing preset logs a warning and falls back to <see cref="MotionTiming.Normal"/>, so a data mismatch never throws in a build.
    /// </summary>
    public static class MotionPresets
    {
        static Dictionary<string, MotionPreset> s_Presets = new Dictionary<string, MotionPreset>(StringComparer.Ordinal);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Presets = new Dictionary<string, MotionPreset>(StringComparer.Ordinal);
        }

        public static int Count => s_Presets.Count;

        /// <summary>Replace the registry with the valid presets of <paramref name="json"/>. Every problem is logged as an error; presets with a problem are skipped. Returns true when there was none.</summary>
        public static bool Load(string json)
        {
            var ok = MotionPresetSet.TryResolveJson(json, out var resolved, out var errors);
            foreach (var e in errors)
                Debug.LogError(e);
            s_Presets = resolved;
            return ok;
        }

        public static bool Load(TextAsset json) => json != null && Load(json.text);

        public static void Use(Dictionary<string, MotionPreset> resolved)
        {
            s_Presets = resolved ?? throw new ArgumentNullException(nameof(resolved));
        }

        public static bool TryGet(string name, out MotionPreset preset) =>
            s_Presets.TryGetValue(name ?? string.Empty, out preset);

        public static MotionTiming Timing(string name)
        {
            if (TryGet(name, out var p))
                return p.Timing;
            Debug.LogWarning("Motion preset '" + name + "' is not loaded; using the normal timing.");
            return MotionTiming.Normal;
        }
    }

    /// <summary>Designer-owned presets as a project asset: a generated JSON file, or values typed into the inspector.</summary>
    [CreateAssetMenu(menuName = "Cuvara/UI Toolkit/Motion Presets", fileName = "MotionPresets")]
    public sealed class MotionPresetAsset : ScriptableObject
    {
        [SerializeField] TextAsset m_Json = null;
        [SerializeField] MotionPresetSet m_Inline = new MotionPresetSet();

        /// <summary>Load these presets into <see cref="MotionPresets"/>. The JSON file wins when one is assigned.</summary>
        public bool Apply()
        {
            if (m_Json != null)
                return MotionPresets.Load(m_Json.text);
            var errors = new List<string>();
            var resolved = m_Inline.Resolve(errors);
            foreach (var e in errors)
                Debug.LogError(e);
            MotionPresets.Use(resolved);
            return errors.Count == 0;
        }
    }
}
