using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cuvara.UIToolkit.Motion
{
    /// <summary>
    /// A timing function: the five names Penpot allows (<c>linear</c>, <c>ease</c>, <c>ease-in</c>, <c>ease-out</c>,
    /// <c>ease-in-out</c>, same curves as CSS and USS) or a CSS <c>cubic-bezier(x1, y1, x2, y2)</c>. The named five run as a
    /// USS transition; a bezier cannot (USS has no <c>cubic-bezier()</c>), so it always runs frame by frame.
    /// <c>default(MotionEasing)</c> is <see cref="Linear"/>.
    /// </summary>
    public readonly struct MotionEasing : IEquatable<MotionEasing>
    {
        public enum Kind : byte { Linear = 0, Ease, EaseIn, EaseOut, EaseInOut, Bezier }

        readonly Kind m_Kind;
        readonly float m_X1, m_Y1, m_X2, m_Y2;

        MotionEasing(Kind kind, float x1, float y1, float x2, float y2)
        {
            m_Kind = kind;
            m_X1 = x1;
            m_Y1 = y1;
            m_X2 = x2;
            m_Y2 = y2;
        }

        public static MotionEasing Linear => default;
        public static MotionEasing Ease => new MotionEasing(Kind.Ease, 0.25f, 0.1f, 0.25f, 1f);
        public static MotionEasing EaseIn => new MotionEasing(Kind.EaseIn, 0.42f, 0f, 1f, 1f);
        public static MotionEasing EaseOut => new MotionEasing(Kind.EaseOut, 0f, 0f, 0.58f, 1f);
        public static MotionEasing EaseInOut => new MotionEasing(Kind.EaseInOut, 0.42f, 0f, 0.58f, 1f);

        /// <summary>All four values must be finite; x1 and x2 must be in [0, 1] (CSS rule); y values may overshoot.</summary>
        public static MotionEasing CubicBezier(float x1, float y1, float x2, float y2)
        {
            if (!IsValidBezier(x1, y1, x2, y2))
                throw new ArgumentOutOfRangeException(nameof(x1), "cubic-bezier needs finite values with x1 and x2 in [0, 1]");
            return new MotionEasing(Kind.Bezier, x1, y1, x2, y2);
        }

        public Kind Type => m_Kind;

        /// <summary>True for the five names: they exist in USS, so the easing can drive a transition.</summary>
        public bool IsNamed => m_Kind != Kind.Bezier;

        /// <summary>Parses a Penpot / USS easing name or <c>cubic-bezier(a, b, c, d)</c>.</summary>
        public static bool TryParse(string text, out MotionEasing easing)
        {
            easing = default;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            text = text.Trim().ToLowerInvariant();
            switch (text)
            {
                case "linear": easing = Linear; return true;
                case "ease": easing = Ease; return true;
                case "ease-in": easing = EaseIn; return true;
                case "ease-out": easing = EaseOut; return true;
                case "ease-in-out": easing = EaseInOut; return true;
            }
            const string prefix = "cubic-bezier(";
            if (!text.StartsWith(prefix, StringComparison.Ordinal) || !text.EndsWith(")", StringComparison.Ordinal))
                return false;
            var parts = text.Substring(prefix.Length, text.Length - prefix.Length - 1).Split(',');
            if (parts.Length != 4)
                return false;
            var v = new float[4];
            for (var i = 0; i < 4; i++)
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]))
                    return false;
            if (!IsValidBezier(v[0], v[1], v[2], v[3]))
                return false;
            easing = CubicBezier(v[0], v[1], v[2], v[3]);
            return true;
        }

        static bool IsValidBezier(float x1, float y1, float x2, float y2) =>
            IsFinite(x1) && IsFinite(y1) && IsFinite(x2) && IsFinite(y2) && x1 >= 0f && x1 <= 1f && x2 >= 0f && x2 <= 1f;

        static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        /// <summary>The USS timing function for a transition. Only valid when <see cref="IsNamed"/>.</summary>
        internal EasingMode ToEasingMode()
        {
            switch (m_Kind)
            {
                case Kind.Ease: return EasingMode.Ease;
                case Kind.EaseIn: return EasingMode.EaseIn;
                case Kind.EaseOut: return EasingMode.EaseOut;
                case Kind.EaseInOut: return EasingMode.EaseInOut;
                default: return EasingMode.Linear;
            }
        }

        /// <summary>Eased progress for a linear progress t in [0, 1]. The result may leave [0, 1] for an overshooting bezier.</summary>
        public float Evaluate(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            if (m_Kind == Kind.Linear) return t;

            // CSS UnitBezier: solve x(s) = t for s, return y(s).
            float cx = 3f * m_X1, bx = 3f * (m_X2 - m_X1) - cx, ax = 1f - cx - bx;
            float cy = 3f * m_Y1, by = 3f * (m_Y2 - m_Y1) - cy, ay = 1f - cy - by;

            var s = t;
            for (var i = 0; i < 8; i++) // Newton
            {
                var x = ((ax * s + bx) * s + cx) * s - t;
                if (Mathf.Abs(x) < 1e-5f)
                    return ((ay * s + by) * s + cy) * s;
                var d = (3f * ax * s + 2f * bx) * s + cx;
                if (Mathf.Abs(d) < 1e-6f)
                    break;
                s -= x / d;
            }
            float lo = 0f, hi = 1f; // bisection fallback
            s = t;
            for (var i = 0; i < 24; i++)
            {
                var x = ((ax * s + bx) * s + cx) * s;
                if (Mathf.Abs(x - t) < 1e-5f)
                    break;
                if (t > x) lo = s; else hi = s;
                s = (lo + hi) * 0.5f;
            }
            return ((ay * s + by) * s + cy) * s;
        }

        public bool Equals(MotionEasing o) =>
            m_Kind == o.m_Kind && m_X1 == o.m_X1 && m_Y1 == o.m_Y1 && m_X2 == o.m_X2 && m_Y2 == o.m_Y2;

        public override bool Equals(object obj) => obj is MotionEasing o && Equals(o);
        public override int GetHashCode() => ((int)m_Kind * 397) ^ m_X1.GetHashCode() ^ (m_Y1.GetHashCode() << 1) ^ (m_X2.GetHashCode() << 2) ^ (m_Y2.GetHashCode() << 3);

        public override string ToString()
        {
            switch (m_Kind)
            {
                case Kind.Linear: return "linear";
                case Kind.Ease: return "ease";
                case Kind.EaseIn: return "ease-in";
                case Kind.EaseOut: return "ease-out";
                case Kind.EaseInOut: return "ease-in-out";
                default:
                    return string.Format(CultureInfo.InvariantCulture, "cubic-bezier({0}, {1}, {2}, {3})", m_X1, m_Y1, m_X2, m_Y2);
            }
        }
    }
}
