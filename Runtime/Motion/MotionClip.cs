using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cuvara.UIToolkit.Motion
{
    /// <summary>
    /// A description of an animation: a tween, a sequence, a parallel group, a stagger, a loop. Build it with <see cref="UiMotion"/>,
    /// then <see cref="Play(MotionOptions)"/> it. A clip plays once and cannot be placed in two groups; build a new one for the
    /// next run (clips are cheap, they hold no frame state until played).
    /// <para>
    /// Start values: a tween without an explicit start value starts from the value the element has when the tween begins
    /// (a tween inside a sequence starts when the step before it has ended). Inside a loop the start value is captured once, in
    /// the first iteration, and every later iteration repeats from it.
    /// </para>
    /// </summary>
    public abstract class MotionClip
    {
        internal bool Played;
        internal bool Parented;

        // The set of subclasses is closed on purpose: only this assembly implements the members below.
        internal MotionClip() { }

        /// <summary>Total length in ms including delays; +infinity for an infinite loop.</summary>
        internal abstract float Duration { get; }

        /// <summary>Apply the state at time <paramref name="t"/> (ms since the start of this clip; negative = before it, +infinity = settled at the end).</summary>
        internal abstract void Evaluate(MotionHandle run, float t);

        internal abstract void Collect(MotionHandle run);
        internal abstract void ForbidTransitions();

        /// <summary>Forget which part of the timeline was passed, so the next evaluation plays this clip from its start again (a loop iteration begins).</summary>
        internal abstract void ResetPhase();

        /// <summary>True when the clip contains a <see cref="UiMotion.Call"/>; a ping-pong loop rejects such children.</summary>
        internal virtual bool HasCall => false;

        /// <summary>Total length in ms including delays; <see cref="float.PositiveInfinity"/> for an infinite loop.</summary>
        public float DurationMs => Duration;

        /// <summary>Start the clip. Returns at once; the handle reports the end.</summary>
        public MotionHandle Play() => MotionHandle.Begin(this, default, null);

        public MotionHandle Play(MotionOptions options) => MotionHandle.Begin(this, options, null);

        /// <summary>Start with an explicit time source (tests, or objects that are not VisualElements).</summary>
        public MotionHandle Play(MotionOptions options, IMotionDriver driver) => MotionHandle.Begin(this, options, driver);

        internal void MarkParented()
        {
            if (Parented)
                throw new InvalidOperationException("A MotionClip can be part of one group only; build a new clip.");
            if (Played)
                throw new InvalidOperationException("This MotionClip has already been played; build a new clip.");
            Parented = true;
        }
    }

    /// <summary>A clip that touches one object (tween or value animation).</summary>
    internal abstract class LeafClip : MotionClip
    {
        /// <summary>Conflict-only bit for value animations: two count-ups on one label conflict although they use no channel.</summary>
        internal const MotionChannels CustomChannel = (MotionChannels)256;

        internal enum Phase : byte { None, Before, Active, After }

        internal IMotionTarget Target;
        internal Phase CurrentPhase;
        internal bool Lost;
        internal bool Dead;

        internal abstract MotionChannels ConflictMask { get; }

        /// <summary>Leave the run now (the run was cancelled).</summary>
        internal abstract void OnCancel(MotionCancel mode);

        /// <summary>The run is over (any result): give back what the leaf claimed on its target.</summary>
        internal abstract void OnFinish();

        internal override void ResetPhase() => CurrentPhase = Phase.None;

        /// <summary>The leaf is skipped when it belongs to a lost target during a "lost pass", and when it is dead.</summary>
        protected bool Skip(MotionHandle run) => Dead || (run.LostPass && !Lost);
    }

    internal sealed class TweenClip : LeafClip
    {
        static readonly MotionChannels[] k_All =
        {
            MotionChannels.Opacity, MotionChannels.Translate, MotionChannels.Scale, MotionChannels.Rotate,
            MotionChannels.BackgroundColor, MotionChannels.Tint, MotionChannels.TextColor
        };

        readonly MotionValues m_To;
        readonly MotionValues m_From;
        readonly MotionChannels m_Mask;
        readonly MotionChannels m_FromMask;
        readonly MotionTiming m_Timing;

        bool m_TransitionAllowed = true;
        bool m_UseTransition;
        bool m_TransitionRunning;
        bool m_HoldFrom;
        bool m_Captured;
        Vector4[] m_Start;
        Vector4[] m_Orig;

        internal TweenClip(IMotionTarget target, MotionValues to, MotionValues from, MotionTiming timing)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (to.Channels == MotionChannels.None) throw new ArgumentException("a tween needs at least one channel", nameof(to));
            if ((from.Channels & ~to.Channels) != 0)
                throw new ArgumentException("the start values name a channel the end values do not", nameof(from));
            Target = target;
            m_To = to;
            m_From = from;
            m_Mask = to.Channels;
            m_FromMask = from.Channels;
            m_Timing = timing;
        }

        internal override float Duration => m_Timing.DelayMs + m_Timing.DurationMs;
        internal override MotionChannels ConflictMask => m_Mask;
        internal bool UsesTransition => m_UseTransition;

        internal override void ForbidTransitions() => m_TransitionAllowed = false;

        internal override void Collect(MotionHandle run)
        {
            run.AddLeaf(this);
            m_UseTransition = run.Options.Mode != MotionMode.Manual && m_TransitionAllowed && m_FromMask == MotionChannels.None
                && m_Timing.Easing.IsNamed && m_Timing.DurationMs > 0f && Target.SupportsTransitions;
            // A start value is shown from the first frame, but only the first tween of a channel may hold it during its delay
            // (a later tween in a sequence must not overwrite what an earlier one shows).
            var claimedBefore = run.ClaimChannels(Target.Key, m_Mask);
            m_HoldFrom = m_FromMask != MotionChannels.None && (claimedBefore & m_FromMask) == MotionChannels.None;
        }

        internal override void Evaluate(MotionHandle run, float t)
        {
            if (Skip(run))
                return;
            var local = t - m_Timing.DelayMs;
            if (local < 0f)
                EnterBefore();
            else if (local >= m_Timing.DurationMs)
                EnterAfter();
            else
                Active(local);
        }

        void EnterBefore()
        {
            if (CurrentPhase == Phase.Before)
                return;
            var wasActiveTransition = CurrentPhase == Phase.Active && m_UseTransition;
            CurrentPhase = Phase.Before;
            if (wasActiveTransition)
                StopTransition();
            if (m_Captured)
                WriteStart();
            else if (m_HoldFrom)
            {
                Capture();
                WriteStart();
            }
        }

        void Active(float local)
        {
            if (CurrentPhase != Phase.Active)
            {
                CurrentPhase = Phase.Active;
                if (!m_Captured)
                    Capture();
                if (m_UseTransition)
                {
                    Target.BeginTransition(this, m_Mask, m_Timing.DurationMs - local, m_Timing.Easing);
                    m_TransitionRunning = true;
                    WriteEnd();
                }
            }
            if (m_UseTransition)
                return; // the style system animates it
            var e = m_Timing.Easing.Evaluate(local / m_Timing.DurationMs);
            for (var i = 0; i < k_All.Length; i++)
                if ((m_Mask & k_All[i]) != 0)
                    Target.Set(k_All[i], Vector4.LerpUnclamped(m_Start[i], m_To.Get(k_All[i]), e));
        }

        void EnterAfter()
        {
            if (CurrentPhase == Phase.After)
                return;
            CurrentPhase = Phase.After;
            if (!m_Captured)
                Capture();
            StopTransition();
            WriteEnd();
        }

        void Capture()
        {
            m_Captured = true;
            // From the first write on, the channels must jump (a transition declared by a style sheet or another run would lag
            // the timeline and animate Complete / Restore); a transition of this leaf replaces the claim when it begins.
            Target.SuppressTransitions(this, m_Mask);
            m_Start = new Vector4[k_All.Length];
            m_Orig = new Vector4[k_All.Length];
            for (var i = 0; i < k_All.Length; i++)
            {
                if ((m_Mask & k_All[i]) == 0)
                    continue;
                m_Orig[i] = Target.Get(k_All[i]);
                m_Start[i] = (m_FromMask & k_All[i]) != 0 ? m_From.Get(k_All[i]) : m_Orig[i];
            }
        }

        void StopTransition()
        {
            if (!m_TransitionRunning)
                return;
            m_TransitionRunning = false;
            Target.EndTransition(this);
        }

        void WriteStart()
        {
            for (var i = 0; i < k_All.Length; i++)
                if ((m_Mask & k_All[i]) != 0)
                    Target.Set(k_All[i], m_Start[i]);
        }

        void WriteEnd()
        {
            for (var i = 0; i < k_All.Length; i++)
                if ((m_Mask & k_All[i]) != 0)
                    Target.Set(k_All[i], m_To.Get(k_All[i]));
        }

        internal override void OnFinish()
        {
            if (m_Captured)
                Target.ReleaseTransitions(this, Dead);
        }

        internal override void OnCancel(MotionCancel mode)
        {
            if (Dead || !m_Captured)
                return;
            if (mode == MotionCancel.Restore)
            {
                StopTransition();
                if (m_Captured)
                    for (var i = 0; i < k_All.Length; i++)
                        if ((m_Mask & k_All[i]) != 0)
                            Target.Set(k_All[i], m_Orig[i]);
                return;
            }
            if (m_TransitionRunning)
            {
                // Freeze: read the in-between values before the transition declaration goes away, then pin them inline.
                var current = new Vector4[k_All.Length];
                for (var i = 0; i < k_All.Length; i++)
                    if ((m_Mask & k_All[i]) != 0)
                        current[i] = Target.Get(k_All[i]);
                StopTransition();
                for (var i = 0; i < k_All.Length; i++)
                    if ((m_Mask & k_All[i]) != 0)
                        Target.Set(k_All[i], current[i]);
            }
        }
    }

    /// <summary>Animates one float and hands it to a setter (text count-up and anything a style cannot express).</summary>
    internal sealed class ValueClip : LeafClip
    {
        readonly float m_From, m_To;
        readonly MotionTiming m_Timing;
        readonly Action<float> m_Setter;

        internal ValueClip(IMotionTarget liveness, float from, float to, MotionTiming timing, Action<float> setter)
        {
            Target = liveness;
            m_From = from;
            m_To = to;
            m_Timing = timing;
            m_Setter = setter ?? throw new ArgumentNullException(nameof(setter));
        }

        internal override float Duration => m_Timing.DelayMs + m_Timing.DurationMs;
        internal override MotionChannels ConflictMask => Target == null ? MotionChannels.None : CustomChannel;
        internal override void ForbidTransitions() { }

        internal override void Collect(MotionHandle run)
        {
            if (Target != null)
                run.AddLeaf(this);
        }

        internal override void Evaluate(MotionHandle run, float t)
        {
            if (Skip(run))
                return;
            var local = t - m_Timing.DelayMs;
            if (local < 0f)
            {
                if (CurrentPhase == Phase.Before)
                    return;
                var had = CurrentPhase != Phase.None;
                CurrentPhase = Phase.Before;
                if (had)
                    m_Setter(m_From);
            }
            else if (local >= m_Timing.DurationMs)
            {
                if (CurrentPhase == Phase.After)
                    return;
                CurrentPhase = Phase.After;
                m_Setter(m_To);
            }
            else
            {
                CurrentPhase = Phase.Active;
                m_Setter(m_From + (m_To - m_From) * m_Timing.Easing.Evaluate(local / m_Timing.DurationMs));
            }
        }

        internal override void OnCancel(MotionCancel mode)
        {
            if (mode == MotionCancel.Restore && CurrentPhase != Phase.None && !Dead)
                m_Setter(m_From);
        }

        internal override void OnFinish() { }
    }

    /// <summary>Calls an action once each time the timeline passes it.</summary>
    internal sealed class CallClip : MotionClip
    {
        readonly Action m_Action;
        LeafClip.Phase m_Phase;

        internal CallClip(Action action)
        {
            m_Action = action ?? throw new ArgumentNullException(nameof(action));
        }

        internal override float Duration => 0f;
        internal override bool HasCall => true;
        internal override void Collect(MotionHandle run) { }
        internal override void ForbidTransitions() { }
        internal override void ResetPhase() => m_Phase = LeafClip.Phase.None;

        internal override void Evaluate(MotionHandle run, float t)
        {
            if (run.LostPass)
                return;
            if (t < 0f)
                m_Phase = LeafClip.Phase.Before;
            else if (m_Phase != LeafClip.Phase.After)
            {
                m_Phase = LeafClip.Phase.After;
                m_Action();
            }
        }
    }

    internal sealed class WaitClip : MotionClip
    {
        readonly float m_Ms;

        internal WaitClip(float ms)
        {
            m_Ms = ms;
        }

        internal override float Duration => m_Ms;
        internal override void Evaluate(MotionHandle run, float t) { }
        internal override void Collect(MotionHandle run) { }
        internal override void ForbidTransitions() { }
        internal override void ResetPhase() { }
    }

    /// <summary>Children with a start offset each: a sequence (offsets add up), a parallel group (all 0) or a stagger (i * step).</summary>
    internal sealed class GroupClip : MotionClip
    {
        readonly MotionClip[] m_Children;
        readonly float[] m_Offsets;
        readonly float m_Duration;

        internal GroupClip(MotionClip[] children, float[] offsets)
        {
            // Snapshot: the caller keeps its arrays, and changing them after the group was built (and validated, for example by a
            // ping-pong loop around it) must not change the built tree.
            children = (MotionClip[])children.Clone();
            offsets = (float[])offsets.Clone();
            for (var i = 0; i < children.Length; i++)
                if (children[i] == null)
                    throw new ArgumentNullException(nameof(children), "clip " + i + " is null");
            m_Children = children;
            m_Offsets = offsets;
            var end = 0f;
            for (var i = 0; i < children.Length; i++)
            {
                children[i].MarkParented();
                end = Mathf.Max(end, offsets[i] + children[i].Duration);
            }
            m_Duration = end;
        }

        internal override float Duration => m_Duration;

        internal override bool HasCall
        {
            get
            {
                for (var i = 0; i < m_Children.Length; i++)
                    if (m_Children[i].HasCall)
                        return true;
                return false;
            }
        }

        internal override void Collect(MotionHandle run)
        {
            for (var i = 0; i < m_Children.Length; i++)
                m_Children[i].Collect(run);
        }

        internal override void ForbidTransitions()
        {
            for (var i = 0; i < m_Children.Length; i++)
                m_Children[i].ForbidTransitions();
        }

        internal override void ResetPhase()
        {
            for (var i = 0; i < m_Children.Length; i++)
                m_Children[i].ResetPhase();
        }

        internal override void Evaluate(MotionHandle run, float t)
        {
            for (var i = 0; i < m_Children.Length; i++)
                m_Children[i].Evaluate(run, t - m_Offsets[i]);
        }
    }

    /// <summary>
    /// Repeats a child. A forward loop starts every iteration with the child's timeline phases reset, so every <c>Call</c> in it fires
    /// once per iteration and every tween restores its start value. When a long frame skips iterations, the calls of each skipped
    /// iteration still fire once (at most <see cref="MaxReplayedIterations"/> per frame; older ones are dropped), so a counter driven
    /// by a loop stays right; the values of a skipped iteration are overwritten by the current one.
    /// <para>
    /// A ping-pong loop plays every second iteration backwards. What a <c>Call</c> would mean on the way back is not defined, so a
    /// ping-pong loop (or anything inside one) cannot contain a <c>Call</c>; building one throws. Use <c>OnFinished</c> on the handle
    /// for the end of the whole loop, or put the loop with the call around the ping-pong loop instead of inside it.
    /// </para>
    /// </summary>
    internal sealed class LoopClip : MotionClip
    {
        internal const int MaxReplayedIterations = 16;

        readonly MotionClip m_Child;
        readonly int m_Count;
        readonly bool m_PingPong;
        int m_LastIteration = -1;

        internal LoopClip(MotionClip child, int count, bool pingPong)
        {
            if (count != MotionLoop.Infinite && count < 1)
                throw new ArgumentOutOfRangeException(nameof(count), "count must be >= 1 or MotionLoop.Infinite");
            if (float.IsInfinity(child.Duration))
                throw new ArgumentException("a loop cannot contain an infinite loop", nameof(child));
            if (count == MotionLoop.Infinite && child.Duration <= 0f)
                throw new ArgumentException("an infinite loop needs a child with a duration > 0", nameof(child));
            if (pingPong && child.HasCall)
                throw new ArgumentException(
                    "a ping-pong loop cannot contain UiMotion.Call: it is not defined when a call fires while the loop plays backwards. "
                    + "Use handle.OnFinished for the end of the loop, or move the Call outside the ping-pong loop.", nameof(child));
            child.MarkParented();
            child.ForbidTransitions(); // a USS transition cannot repeat or play backwards: loops run frame by frame
            m_Child = child;
            m_Count = count;
            m_PingPong = pingPong;
        }

        internal override float Duration => m_Count == MotionLoop.Infinite ? float.PositiveInfinity : m_Child.Duration * m_Count;
        internal override bool HasCall => m_Child.HasCall;
        internal override void Collect(MotionHandle run) => m_Child.Collect(run);
        internal override void ForbidTransitions() { }

        internal override void ResetPhase()
        {
            m_LastIteration = -1;
            m_Child.ResetPhase();
        }

        internal override void Evaluate(MotionHandle run, float t)
        {
            if (t < 0f)
            {
                m_Child.Evaluate(run, -1f);
                return;
            }
            var d = m_Child.Duration;
            int iteration;
            float tau;
            if (float.IsPositiveInfinity(t))
            {
                iteration = (m_Count == MotionLoop.Infinite ? 1 : m_Count) - 1;
                tau = d;
            }
            else if (d <= 0f)
            {
                // zero-length iterations all happen "now": run the repeat count under the replay policy (infinite is rejected)
                iteration = m_Count - 1;
                tau = 0f;
            }
            else
            {
                iteration = (int)(t / d);
                tau = t - iteration * d;
                if (m_Count != MotionLoop.Infinite && iteration >= m_Count)
                {
                    iteration = m_Count - 1;
                    tau = d;
                }
            }
            if (m_PingPong && (iteration & 1) == 1)
                tau = d - tau;

            // Ping-pong loops hold no calls, so the child's phases alone restore start values when direction flips: nothing to track.
            // A "lost pass" only writes the end state of lost targets; it must not touch the iteration bookkeeping or replay calls.
            if (!m_PingPong && !run.LostPass && iteration != m_LastIteration)
            {
                if (m_LastIteration >= 0)
                    m_Child.Evaluate(run, d); // calls at the very end of the iteration that just ended
                var first = Mathf.Max(m_LastIteration + 1, iteration - MaxReplayedIterations);
                for (var skipped = first; skipped < iteration; skipped++)
                {
                    m_Child.ResetPhase();
                    m_Child.Evaluate(run, d); // the skipped iteration ran to its end: its calls fire
                }
                m_Child.ResetPhase();
                m_LastIteration = iteration;
            }
            m_Child.Evaluate(run, tau);
        }
    }
}
