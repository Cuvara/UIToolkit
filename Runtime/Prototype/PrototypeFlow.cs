using System;
using UnityEngine;

namespace Cuvara.UIToolkit.Prototype
{
    /// <summary>
    /// Content of <c>&lt;Screen&gt;.flow.json</c> written by the penpot-uitoolkit-bridge converter (Penpot prototype interactions +
    /// structure). The format is a versioned contract: Documentation~/PROTOTYPE-FLOW.md.
    /// </summary>
    [Serializable]
    public sealed class PrototypeFlowFile
    {
        /// <summary>
        /// The major version of the format this runner reads. A file with any other <c>version</c> is rejected: a new major is a
        /// breaking change; additive changes keep the major and older readers ignore the new fields.
        /// </summary>
        public const int SupportedVersion = 1;

        /// <summary>The major version of the format (<see cref="SupportedVersion"/>).</summary>
        public int version;
        public string screen;
        public bool overlay;
        public float[] size;
        public PrototypeFlowDef[] flows;
        public PrototypeInteraction[] interactions;
        public PrototypeControl[] controls;
        public PrototypeList[] lists;
        public PrototypeScroll[] scrolls;
        public PrototypeMotion motion = new PrototypeMotion();

        public static PrototypeFlowFile Parse(string json)
        {
            var file = JsonUtility.FromJson<PrototypeFlowFile>(json);
            if (file == null || file.version != SupportedVersion)
                throw new FormatException("Unsupported or empty flow file (version " + (file == null ? "?" : file.version.ToString()) +
                                          ", this runner reads version " + SupportedVersion + ")");
            return file;
        }
    }

    [Serializable]
    public sealed class PrototypeFlowDef
    {
        public string name;
        public string start;
    }

    [Serializable]
    public sealed class PrototypeInteraction
    {
        public string id;
        public string element;
        /// <summary>click | mouse-enter | mouse-leave | after-delay</summary>
        public string trigger;
        public float delayMs;
        /// <summary>navigate-to | open-overlay | toggle-overlay | close-overlay | previous-screen</summary>
        public string action;
        public string target;
        public string position;
        public float[] manual;
        public bool closeOnOutside;
        public bool scrim;
        /// <summary>The Penpot animation of the interaction; <c>type</c> is empty when it has none (JsonUtility never yields null here).</summary>
        public PrototypeAnimation animation = new PrototypeAnimation();
    }

    /// <summary>Animation of a navigate / overlay interaction, as Penpot stores it (dissolve | slide | push).</summary>
    [Serializable]
    public sealed class PrototypeAnimation
    {
        public string type;
        public float durationMs;
        /// <summary>linear | ease | ease-in | ease-out | ease-in-out</summary>
        public string easing;
        /// <summary>slide: in | out</summary>
        public string way;
        /// <summary>slide and push: the direction of motion (left | right | up | down)</summary>
        public string direction;
        public bool offsetEffect;

        public bool IsSet { get { return !string.IsNullOrEmpty(type) && durationMs >= 1f; } }

        public PrototypeAnimation Clone()
        {
            return (PrototypeAnimation)MemberwiseClone();
        }

        /// <summary>The same animation moving the other way (Penpot's invert-direction: left/right, up/down).</summary>
        public static string Invert(string direction)
        {
            switch (direction)
            {
                case "left": return "right";
                case "right": return "left";
                case "up": return "down";
                case "down": return "up";
                default: return direction;
            }
        }
    }

    /// <summary>What the runner last animated (see <c>PrototypeRunner.LastTransition</c>).</summary>
    public sealed class PrototypeTransitionInfo
    {
        public string Kind;
        public string Name;
        public PrototypeAnimation Animation;
        /// <summary><c>Time.realtimeSinceStartup</c> when the transition and its end state were applied (the animation starts on
        /// that style update); -1 until then, and for a transition finished before it started.</summary>
        public float StartedAt = -1f;
    }

    /// <summary>The profile's motion policy as written into the flow file.</summary>
    [Serializable]
    public sealed class PrototypeMotion
    {
        /// <summary>reverse | none: what <c>previous-screen</c> plays.</summary>
        public string back = "reverse";
        public bool enabled = true;
    }

    [Serializable]
    public sealed class PrototypeControl
    {
        public string name;
        public string type;
        public bool enabled;
        /// <summary>The motion preset of the control (empty when it has none).</summary>
        public string motion;
    }

    [Serializable]
    public sealed class PrototypeList
    {
        public string name;
        public string kind;
        public string template;
        public bool selectable;
    }

    [Serializable]
    public sealed class PrototypeScroll
    {
        public string name;
        public string mode;
    }

    /// <summary>One generated screen: its UXML and (optionally) its flow file.</summary>
    [Serializable]
    public sealed class PrototypeScreenEntry
    {
        public string Name;
        public UnityEngine.UIElements.VisualTreeAsset Uxml;
        public TextAsset Flow;
    }
}
