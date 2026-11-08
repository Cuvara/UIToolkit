using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cuvara.UIToolkit.Motion
{
    /// <summary>
    /// A running (or finished) <see cref="MotionClip"/>. Also a yield instruction (<c>yield return handle</c>) and awaitable
    /// (<c>await handle</c>, continues on the main thread).
    /// <para>
    /// End states, explicitly: a run that reaches its end leaves the end values inline on the elements.
    /// <see cref="Complete"/> jumps to that same end state. <see cref="Cancel"/> either keeps the values as they are right now
    /// (<see cref="MotionCancel.Keep"/>, the default) or writes back the values each property had when the run first touched
    /// it (<see cref="MotionCancel.Restore"/>). Nothing is ever left half-declared: the inline USS transition of a transition
    /// tween is removed on every exit path. When an element leaves its panel (or the panel is destroyed) the run applies the end
    /// state to that element and stops driving it; when no element is left the run ends with <see cref="MotionResult.Detached"/>.
    /// A new run that animates the same property of the same element takes it over: the older run is cancelled with Keep.
    /// </para>
    /// </summary>
    public sealed class MotionHandle : CustomYieldInstruction
    {
        // Transitions need one frame between the start state and the end value ("the first frame has no previous state").
        const int k_TransitionWarmupTicks = 2;

        static readonly Dictionary<object, List<MotionHandle>> s_Active = new Dictionary<object, List<MotionHandle>>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Active.Clear(); // domain reload may be off: statics outlive Play Mode
        }

        /// <summary>Runs currently registered (tests).</summary>
        internal static int ActiveCount
        {
            get
            {
                var n = 0;
                foreach (var kv in s_Active)
                    n += kv.Value.Count;
                return n;
            }
        }

        readonly MotionClip m_Root;
        internal readonly MotionOptions Options;
        readonly List<LeafClip> m_Leaves = new List<LeafClip>();
        readonly List<IMotionTarget> m_Targets = new List<IMotionTarget>();
        readonly Dictionary<object, MotionChannels> m_Claimed = new Dictionary<object, MotionChannels>();
        readonly bool m_DriverFixed;

        IMotionDriver m_Driver;
        IMotionTarget m_DriverOwner;
        Action[] m_Handlers;
        bool[] m_TargetLost;
        int m_Alive;
        double m_Origin;
        int m_Warmup;
        IPanel m_BlockPanel;
        Action<MotionResult> m_Callbacks;

        internal bool LostPass;

        MotionHandle(MotionClip root, MotionOptions options, IMotionDriver driver)
        {
            m_Root = root;
            Options = options;
            m_Driver = driver;
            m_DriverFixed = driver != null;
        }

        public MotionResult Result { get; private set; }
        public bool IsRunning => Result == MotionResult.Running;
        public override bool keepWaiting => Result == MotionResult.Running;

        #region Start

        internal static MotionHandle Begin(MotionClip clip, MotionOptions options, IMotionDriver driver)
        {
            if (clip == null)
                throw new ArgumentNullException(nameof(clip));
            if (clip.Parented)
                throw new InvalidOperationException("This MotionClip is part of a group; play the group.");
            if (clip.Played)
                throw new InvalidOperationException("This MotionClip has already been played; build a new clip.");
            clip.Played = true;
            var handle = new MotionHandle(clip, options, driver);
            handle.Start();
            return handle;
        }

        void Start()
        {
            m_Root.Collect(this);
            if (m_Targets.Count == 0 && m_Driver == null)
                throw new InvalidOperationException("This clip has no VisualElement to drive it; pass an IMotionDriver to Play.");

            // Targets that are not in a panel cannot animate: apply their end state now.
            m_Alive = m_Targets.Count;
            m_TargetLost = new bool[m_Targets.Count];
            var anyLost = false;
            for (var i = 0; i < m_Targets.Count; i++)
            {
                if (m_Targets[i].IsAlive)
                    continue;
                m_TargetLost[i] = true;
                m_Alive--;
                MarkLost(m_Targets[i].Key);
                anyLost = true;
            }
            if (anyLost)
                ApplyLostPass();
            if (m_Targets.Count > 0 && m_Alive == 0)
            {
                Finish(MotionResult.Detached);
                return;
            }
            if (m_Driver == null)
            {
                m_Driver = PickDriver();
                if (m_Driver == null)
                    throw new InvalidOperationException("None of the targets has a frame driver; pass an IMotionDriver to Play.");
            }

            TakeOver();
            Register();

            m_Handlers = new Action[m_Targets.Count];
            for (var i = 0; i < m_Targets.Count; i++)
            {
                if (m_TargetLost[i])
                    continue;
                var index = i;
                m_Handlers[i] = () => OnTargetDetached(index);
                m_Targets[i].WatchDetach(m_Handlers[i]);
            }

            if (Options.BlockInput)
            {
                var root = Options.BlockRoot;
                if (root == null)
                    for (var i = 0; i < m_Targets.Count && root == null; i++)
                        if (!m_TargetLost[i] && m_Targets[i] is VisualElementTarget v)
                            root = v.Element;
                m_BlockPanel = MotionInputBlock.Acquire(root);
                if (m_BlockPanel == null)
                    Debug.LogWarning("Motion: BlockInput was requested but there is no panel to block.");
            }

            var hasTransition = false;
            for (var i = 0; i < m_Leaves.Count; i++)
                if (m_Leaves[i] is TweenClip t && t.UsesTransition)
                    hasTransition = true;
            m_Warmup = hasTransition ? k_TransitionWarmupTicks : 0;

            Evaluate(-1f); // start values that must show during a delay
            m_Origin = m_Driver.NowMs;
            m_Driver.Start(Tick);
        }

        internal void AddLeaf(LeafClip leaf)
        {
            m_Leaves.Add(leaf);
            var target = leaf.Target;
            if (target == null)
                return;
            for (var i = 0; i < m_Targets.Count; i++)
                if (ReferenceEquals(m_Targets[i].Key, target.Key))
                    return;
            m_Targets.Add(target);
        }

        /// <summary>Marks channels of a target as used by a tween; returns the channels that were used before.</summary>
        internal MotionChannels ClaimChannels(object key, MotionChannels mask)
        {
            m_Claimed.TryGetValue(key, out var before);
            m_Claimed[key] = before | mask;
            return before;
        }

        IMotionDriver PickDriver()
        {
            for (var i = 0; i < m_Targets.Count; i++)
            {
                if (m_TargetLost[i] || !m_Targets[i].IsAlive)
                    continue;
                var driver = m_Targets[i].CreateDriver(); // one per run: stopping this run must not stop another
                if (driver == null || !driver.IsAlive)
                    continue;
                m_DriverOwner = m_Targets[i];
                return driver;
            }
            return null;
        }

        bool Touches(object key, MotionChannels mask)
        {
            for (var i = 0; i < m_Leaves.Count; i++)
            {
                var leaf = m_Leaves[i];
                if (!leaf.Dead && ReferenceEquals(leaf.Target.Key, key) && (leaf.ConflictMask & mask) != 0)
                    return true;
            }
            return false;
        }

        /// <summary>A new run owns the properties it animates: older runs on them are cancelled (Keep).</summary>
        void TakeOver()
        {
            List<MotionHandle> victims = null;
            for (var i = 0; i < m_Leaves.Count; i++)
            {
                var leaf = m_Leaves[i];
                if (leaf.Dead || !s_Active.TryGetValue(leaf.Target.Key, out var list))
                    continue;
                for (var j = 0; j < list.Count; j++)
                {
                    var other = list[j];
                    if (other == this || !other.Touches(leaf.Target.Key, leaf.ConflictMask))
                        continue;
                    if (victims == null)
                        victims = new List<MotionHandle>();
                    if (!victims.Contains(other))
                        victims.Add(other);
                }
            }
            if (victims == null)
                return;
            for (var i = 0; i < victims.Count; i++)
                victims[i].Cancel(MotionCancel.Keep);
        }

        void Register()
        {
            for (var i = 0; i < m_Targets.Count; i++)
            {
                if (m_TargetLost[i])
                    continue;
                var key = m_Targets[i].Key;
                if (!s_Active.TryGetValue(key, out var list))
                    s_Active[key] = list = new List<MotionHandle>(2);
                list.Add(this);
            }
        }

        void Unregister()
        {
            for (var i = 0; i < m_Targets.Count; i++)
            {
                var key = m_Targets[i].Key;
                if (!s_Active.TryGetValue(key, out var list))
                    continue;
                list.Remove(this);
                if (list.Count == 0)
                    s_Active.Remove(key);
            }
        }

        #endregion

        #region Control

        /// <summary>Jump to the end state and finish with <see cref="MotionResult.Completed"/>. An infinite loop settles as one iteration. No-op when finished.</summary>
        public void Complete()
        {
            if (Result != MotionResult.Running)
                return;
            try
            {
                m_Root.Evaluate(this, float.PositiveInfinity);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            Finish(MotionResult.Completed);
        }

        /// <summary>Stop now, see <see cref="MotionCancel"/> for what stays. No-op when finished.</summary>
        public void Cancel(MotionCancel mode = MotionCancel.Keep)
        {
            if (Result != MotionResult.Running)
                return;
            try
            {
                for (var i = m_Leaves.Count - 1; i >= 0; i--)
                    m_Leaves[i].OnCancel(mode);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            Finish(MotionResult.Cancelled);
        }

        /// <summary>Call <paramref name="callback"/> when the run ends (at once, if it already has). Returns this handle.</summary>
        public MotionHandle OnFinished(Action<MotionResult> callback)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));
            if (Result == MotionResult.Running)
                m_Callbacks += callback;
            else
                Invoke(callback, Result);
            return this;
        }

        public MotionAwaiter GetAwaiter() => new MotionAwaiter(this);

        public readonly struct MotionAwaiter : INotifyCompletion
        {
            readonly MotionHandle m_Handle;

            internal MotionAwaiter(MotionHandle handle)
            {
                m_Handle = handle;
            }

            public bool IsCompleted => m_Handle.Result != MotionResult.Running;
            public void OnCompleted(Action continuation) => m_Handle.OnFinished(_ => continuation());
            public MotionResult GetResult() => m_Handle.Result;
        }

        #endregion

        #region Frame loop

        void Tick()
        {
            if (Result != MotionResult.Running)
                return;
            var now = m_Driver.NowMs;
            if (m_Warmup > 0)
            {
                m_Warmup--;
                m_Origin = now;
                return;
            }
            var elapsed = (float)(now - m_Origin);
            if (!Evaluate(elapsed))
                return;
            if (elapsed >= m_Root.Duration)
                Finish(MotionResult.Completed);
        }

        /// <summary>False when a callback threw (the run was cancelled).</summary>
        bool Evaluate(float t)
        {
            try
            {
                m_Root.Evaluate(this, t);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Cancel(MotionCancel.Keep);
                return false;
            }
        }

        #endregion

        #region Detach

        void MarkLost(object key)
        {
            for (var i = 0; i < m_Leaves.Count; i++)
                if (ReferenceEquals(m_Leaves[i].Target.Key, key))
                    m_Leaves[i].Lost = true;
        }

        /// <summary>Apply the end state to the leaves of lost targets only, then stop evaluating them.</summary>
        void ApplyLostPass()
        {
            LostPass = true;
            try
            {
                m_Root.Evaluate(this, float.PositiveInfinity);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                LostPass = false;
            }
            for (var i = 0; i < m_Leaves.Count; i++)
            {
                if (!m_Leaves[i].Lost)
                    continue;
                m_Leaves[i].Lost = false;
                m_Leaves[i].Dead = true;
                m_Leaves[i].OnFinish(); // the element left its panel: nothing can animate, give the claims back now
            }
        }

        void OnTargetDetached(int index)
        {
            if (Result != MotionResult.Running || m_TargetLost[index])
                return;
            var target = m_Targets[index];
            m_TargetLost[index] = true;
            m_Alive--;
            MarkLost(target.Key);
            ApplyLostPass();
            if (m_Alive <= 0)
            {
                Finish(MotionResult.Detached);
                return;
            }
            if (!m_DriverFixed && ReferenceEquals(m_DriverOwner, target))
            {
                m_Driver.Stop();
                m_Driver = PickDriver();
                if (m_Driver == null)
                {
                    Finish(MotionResult.Detached);
                    return;
                }
                m_Driver.Start(Tick);
            }
        }

        #endregion

        void Finish(MotionResult result)
        {
            if (Result != MotionResult.Running)
                return;
            Result = result;
            m_Driver?.Stop();
            if (m_Handlers != null)
                for (var i = 0; i < m_Handlers.Length; i++)
                    if (m_Handlers[i] != null)
                        m_Targets[i].UnwatchDetach(m_Handlers[i]);
            Unregister();
            for (var i = 0; i < m_Leaves.Count; i++)
                m_Leaves[i].OnFinish();
            if (m_BlockPanel != null)
            {
                MotionInputBlock.Release(m_BlockPanel);
                m_BlockPanel = null;
            }
            var callbacks = m_Callbacks;
            m_Callbacks = null;
            if (callbacks == null)
                return;
            foreach (var d in callbacks.GetInvocationList())
                Invoke((Action<MotionResult>)d, result);
        }

        static void Invoke(Action<MotionResult> callback, MotionResult result)
        {
            try
            {
                callback(result);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
