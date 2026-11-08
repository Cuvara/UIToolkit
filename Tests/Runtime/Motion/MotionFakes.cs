using System;
using System.Collections.Generic;
using Cuvara.UIToolkit.Motion;
using UnityEngine;

namespace Cuvara.UIToolkit.Motion.Tests
{
    /// <summary>A manual clock: <see cref="Advance"/> moves time and fires the tick, so timeline tests need no frames.</summary>
    sealed class FakeDriver : IMotionDriver
    {
        Action m_Tick;
        public double Now;
        public bool Alive = true;
        public int StartCount;

        public double NowMs => Now;
        public bool IsAlive => Alive;
        public bool Running => m_Tick != null;

        public void Start(Action tick)
        {
            m_Tick = tick;
            StartCount++;
        }

        public void Stop()
        {
            m_Tick = null;
        }

        /// <summary>Advance <paramref name="ms"/> in one tick.</summary>
        public void Advance(float ms)
        {
            Now += ms;
            m_Tick?.Invoke();
        }
    }

    /// <summary>Records every call the engine makes on a target.</summary>
    sealed class FakeTarget : IMotionTarget
    {
        readonly Dictionary<MotionChannels, Vector4> m_Values = new Dictionary<MotionChannels, Vector4>
        {
            { MotionChannels.Opacity, new Vector4(1f, 0f, 0f, 0f) },
            { MotionChannels.Translate, Vector4.zero },
            { MotionChannels.Scale, new Vector4(1f, 1f, 0f, 0f) },
            { MotionChannels.Rotate, Vector4.zero },
            { MotionChannels.BackgroundColor, Vector4.zero },
            { MotionChannels.Tint, Vector4.one },
            { MotionChannels.TextColor, Vector4.one },
        };

        Action m_Detach;

        public bool Alive = true;
        public bool Transitions;
        public IMotionDriver DriverToUse;
        public int SetCount, BeginCount, EndCount, SuppressCount, ReleaseCount;
        public float LastTransitionMs;
        public MotionEasing LastEasing;
        public bool InTransition;
        /// <summary>Owners that claimed something and have not released it.</summary>
        public readonly HashSet<object> Owners = new HashSet<object>();
        /// <summary>What <see cref="Get"/> returns while a transition runs (the style system's animated value).</summary>
        public Vector4 Animated;

        public object Key => this;
        public bool IsAlive => Alive;
        public bool SupportsTransitions => Transitions;
        public IMotionDriver CreateDriver() => DriverToUse;

        public float Opacity => m_Values[MotionChannels.Opacity].x;
        public Vector2 Translate => m_Values[MotionChannels.Translate];
        public Vector2 Scale => m_Values[MotionChannels.Scale];

        public FakeTarget SetOpacity(float v)
        {
            m_Values[MotionChannels.Opacity] = new Vector4(v, 0f, 0f, 0f);
            return this;
        }

        public Vector4 Get(MotionChannels channel) => InTransition ? Animated : m_Values[channel];

        public void Set(MotionChannels channel, Vector4 value)
        {
            SetCount++;
            m_Values[channel] = value;
        }

        public void SuppressTransitions(object owner, MotionChannels channels)
        {
            SuppressCount++;
            Owners.Add(owner);
        }

        public void BeginTransition(object owner, MotionChannels channels, float durationMs, MotionEasing easing)
        {
            BeginCount++;
            Owners.Add(owner);
            InTransition = true;
            LastTransitionMs = durationMs;
            LastEasing = easing;
        }

        public void EndTransition(object owner)
        {
            EndCount++;
            InTransition = false;
        }

        public void ReleaseTransitions(object owner, bool immediate)
        {
            ReleaseCount++;
            Owners.Remove(owner);
        }

        public void Reset(MotionChannels channels) { }

        public void WatchDetach(Action onDetached) => m_Detach += onDetached;
        public void UnwatchDetach(Action onDetached) => m_Detach -= onDetached;

        public bool HasDetachWatcher => m_Detach != null;

        /// <summary>The element leaves its panel.</summary>
        public void Detach()
        {
            Alive = false;
            m_Detach?.Invoke();
        }
    }
}
