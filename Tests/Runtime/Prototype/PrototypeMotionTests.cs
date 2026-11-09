#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Cuvara.UIToolkit.Prototype;
using Cuvara.UIToolkit.Motion;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Cuvara.UIToolkit.Prototype.Tests
{
    /// <summary>
    /// Motion helpers on a real panel with real frames. Transitions and timers follow the wall clock (see docs/SPIKE-ANIMATION.md
    /// 2.4), so durations carry generous tolerances and values are sampled, never compared frame for frame.
    /// </summary>
    public class PrototypeMotionTests
    {
        GameObject m_Object;
        PanelSettings m_Settings;
        RenderTexture m_Target;
        VisualElement m_Root;
        VisualElement m_Box;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_Settings = ScriptableObject.CreateInstance<PanelSettings>();
            m_Settings.hideFlags = HideFlags.HideAndDontSave;
            m_Settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            m_Target = new RenderTexture(640, 360, 24) { hideFlags = HideFlags.HideAndDontSave };
            m_Target.Create();
            m_Settings.targetTexture = m_Target;
            m_Settings.themeStyleSheet = TestTheme.Load();

            m_Object = new GameObject("MotionTest");
            m_Object.SetActive(false);
            var document = m_Object.AddComponent<UIDocument>();
            document.panelSettings = m_Settings;
            m_Object.SetActive(true);
            yield return null;
            m_Root = document.rootVisualElement;
            m_Root.style.flexGrow = 1f;
            m_Box = Box("box", 20f, 20f);
            yield return Frames(3);
        }

        [TearDown]
        public void TearDown()
        {
            var leaked = MotionHandle.ActiveCount;
            if (m_Object != null)
                Object.DestroyImmediate(m_Object);
            if (m_Settings != null)
                Object.DestroyImmediate(m_Settings);
            if (m_Target != null)
            {
                m_Target.Release();
                Object.DestroyImmediate(m_Target);
            }
            // asserted after the cleanup above, so a leaked run cannot also leak the panel and render texture
            Assert.AreEqual(0, leaked, "a run was left registered");
        }

        VisualElement Box(string name, float x, float y)
        {
            var e = new VisualElement { name = name };
            e.style.position = Position.Absolute;
            e.style.left = x;
            e.style.top = y;
            e.style.width = 100f;
            e.style.height = 60f;
            e.style.backgroundColor = Color.white;
            m_Root.Add(e);
            return e;
        }

        static IEnumerator Frames(int n)
        {
            for (var i = 0; i < n; i++)
                yield return null;
        }

        /// <summary>Wait for the handle, failing after <paramref name="timeoutS"/> seconds.</summary>
        static IEnumerator Finish(MotionHandle handle, float timeoutS = 3f)
        {
            var until = Time.realtimeSinceStartup + timeoutS;
            while (handle.IsRunning && Time.realtimeSinceStartup < until)
                yield return null;
            Assert.IsFalse(handle.IsRunning, "the motion did not finish within " + timeoutS + " s");
        }

        static readonly MotionOptions Manual = new MotionOptions { Mode = MotionMode.Manual };

        /// <summary>
        /// Samples while the handle runs. Every distinct value strictly between the two ends is an in-between value. On a slow
        /// Editor a short animation may be seen in only a few frames, so callers check <see cref="Observation.Frames"/> first
        /// and report the test as inconclusive instead of asserting something that was never observable.
        /// </summary>
        sealed class Observation
        {
            public int Frames;
            public readonly List<float> Values = new List<float>();

            public void RequireFrames(int minimum, string what)
            {
                if (Frames < minimum)
                    Assert.Inconclusive(what + ": only " + Frames + " frames were rendered while it ran (need " + minimum + "); the Editor is too slow for this check");
            }
        }

        static IEnumerator Sample(MotionHandle handle, System.Func<float> read, float low, float high, Observation into)
        {
            var until = Time.realtimeSinceStartup + 5f;
            while (handle.IsRunning && Time.realtimeSinceStartup < until)
            {
                var v = read();
                into.Frames++;
                if (v > low + 0.01f && v < high - 0.01f && !into.Values.Contains(v))
                    into.Values.Add(v);
                yield return null;
            }
        }

        /// <summary>The run must still be going at this point of the test, otherwise the Editor was too slow to observe it.</summary>
        static void RequireRunning(MotionHandle handle, string what)
        {
            if (!handle.IsRunning)
                Assert.Inconclusive(what + ": the motion had already ended when the check was due (slow Editor)");
        }

        #region Primitives

        [UnityTest]
        public IEnumerator Fade_RunsAsAUssTransition_AndLeavesNothingDeclared()
        {
            var runs = 0;
            m_Box.RegisterCallback<TransitionRunEvent>(e => runs++);
            var started = Time.realtimeSinceStartup;
            var handle = UiMotion.Fade(m_Box, 0f, MotionTiming.Ms(600f)).Play();
            var seen = new Observation();
            yield return Sample(handle, () => m_Box.resolvedStyle.opacity, 0f, 1f, seen);
            var elapsed = Time.realtimeSinceStartup - started;
            Assert.AreEqual(MotionResult.Completed, handle.Result);
            Assert.AreEqual(1, runs, "one transition run: the style system animated it");
            Assert.GreaterOrEqual(elapsed, 0.5f, "not shorter than the duration");
            seen.RequireFrames(6, "transition fade");
            Assert.LessOrEqual(elapsed, 2.5f, "duration plus the warm-up frames and scheduler slack");
            Assert.GreaterOrEqual(seen.Values.Count, 3, "in-between values were rendered: " + string.Join(",", seen.Values));
            yield return Frames(2);
            Assert.AreEqual(0f, m_Box.resolvedStyle.opacity, 0.01f);
            Assert.AreEqual(StyleKeyword.Null, m_Box.style.transitionProperty.keyword, "the inline transition declaration is gone");
            Assert.AreEqual(0f, m_Box.style.opacity.value, 1e-4f, "the end value stays inline");
        }

        [UnityTest]
        public IEnumerator Fade_Manual_WritesEveryFrame_WithoutTransitionEvents()
        {
            var runs = 0;
            m_Box.RegisterCallback<TransitionRunEvent>(e => runs++);
            var started = Time.realtimeSinceStartup;
            var handle = UiMotion.Fade(m_Box, 0f, MotionTiming.Ms(600f)).Play(Manual);
            var seen = new Observation();
            yield return Sample(handle, () => m_Box.style.opacity.value, 0f, 1f, seen);
            var elapsed = Time.realtimeSinceStartup - started;
            Assert.AreEqual(MotionResult.Completed, handle.Result);
            Assert.AreEqual(0, runs);
            Assert.GreaterOrEqual(elapsed, 0.59f);
            seen.RequireFrames(8, "manual fade");
            Assert.LessOrEqual(elapsed, 2.5f);
            Assert.GreaterOrEqual(seen.Values.Count, 5, "frame by frame: many distinct values");
            Assert.AreEqual(0f, m_Box.style.opacity.value, 1e-4f);
        }

        [UnityTest]
        public IEnumerator MoveScaleRotateTint_ReachTheirEndValues()
        {
            var to = new MotionValues().WithTranslate(40f, -10f).WithScale(1.5f).WithRotate(30f).WithBackgroundColor(Color.red).WithTint(Color.green);
            var handle = UiMotion.Tween(m_Box, to, MotionTiming.Ms(150f)).Play();
            yield return Finish(handle);
            yield return Frames(2);
            var r = m_Box.resolvedStyle;
            Assert.AreEqual(40f, r.translate.x, 0.5f);
            Assert.AreEqual(-10f, r.translate.y, 0.5f);
            Assert.AreEqual(1.5f, r.scale.value.x, 0.01f);
            Assert.AreEqual(30f, r.rotate.angle.ToDegrees(), 0.1f);
            Assert.AreEqual(Color.red, r.backgroundColor);
            Assert.AreEqual(Color.green, r.unityBackgroundImageTintColor);
        }

        [UnityTest]
        public IEnumerator EachEasingMode_FinishesAtTheEnd()
        {
            foreach (var easing in new[]
            {
                MotionEasing.Linear, MotionEasing.Ease, MotionEasing.EaseIn, MotionEasing.EaseOut, MotionEasing.EaseInOut,
                MotionEasing.CubicBezier(0.3f, -0.4f, 0.7f, 1.4f)
            })
            {
                m_Box.style.translate = new Translate(0f, 0f);
                yield return Frames(2);
                var handle = UiMotion.Move(m_Box, new Vector2(50f, 0f), new MotionTiming(120f, easing)).Play();
                yield return Finish(handle);
                yield return Frames(2);
                Assert.AreEqual(50f, m_Box.resolvedStyle.translate.x, 0.5f, easing.ToString());
            }
        }

        [UnityTest]
        public IEnumerator Delay_HoldsThenRuns()
        {
            var started = Time.realtimeSinceStartup;
            var handle = UiMotion.Fade(m_Box, 0f, new MotionTiming(100f, MotionEasing.Linear, 600f)).Play(Manual);
            yield return Frames(3);
            if (Time.realtimeSinceStartup - started > 0.4f)
            {
                handle.Cancel(); // do not leave a run registered when the check cannot be made
                Assert.Inconclusive("three frames took longer than the delay allows checking");
            }
            Assert.AreEqual(1f, m_Box.resolvedStyle.opacity, 0.01f, "still waiting");
            yield return Finish(handle);
            Assert.GreaterOrEqual(Time.realtimeSinceStartup - started, 0.69f);
            Assert.AreEqual(0f, m_Box.style.opacity.value, 1e-4f);
        }

        #endregion

        #region Control

        [UnityTest]
        public IEnumerator Cancel_Keep_HoldsTheValue_ForTransitionAndManualRuns([ValueSource(nameof(Modes))] MotionMode mode)
        {
            var handle = UiMotion.Fade(m_Box, 0f, MotionTiming.Ms(600f)).Play(new MotionOptions { Mode = mode });
            yield return new WaitForSecondsRealtime(0.3f);
            RequireRunning(handle, "cancel keep");
            handle.Cancel();
            Assert.AreEqual(MotionResult.Cancelled, handle.Result);
            yield return Frames(2);
            var held = m_Box.resolvedStyle.opacity;
            Assert.Less(held, 0.95f, "it had started");
            Assert.Greater(held, 0.05f, "it had not finished");
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.AreEqual(held, m_Box.resolvedStyle.opacity, 0.02f, "frozen after the cancel");
            Assert.AreEqual(StyleKeyword.Null, m_Box.style.transitionProperty.keyword);
        }

        static IEnumerable<MotionMode> Modes() => new[] { MotionMode.Auto, MotionMode.Manual };

        [UnityTest]
        public IEnumerator Cancel_Restore_PutsTheOriginalValueBack()
        {
            var handle = UiMotion.Fade(m_Box, 0f, MotionTiming.Ms(600f)).Play();
            yield return new WaitForSecondsRealtime(0.3f);
            RequireRunning(handle, "cancel restore");
            handle.Cancel(MotionCancel.Restore);
            yield return Frames(3);
            Assert.AreEqual(1f, m_Box.resolvedStyle.opacity, 0.02f);
        }

        [UnityTest]
        public IEnumerator Complete_JumpsToTheEnd()
        {
            var handle = UiMotion.Sequence(
                UiMotion.Fade(m_Box, 0.2f, MotionTiming.Ms(500f)),
                UiMotion.Scale(m_Box, 2f, MotionTiming.Ms(500f))).Play();
            yield return new WaitForSecondsRealtime(0.1f);
            RequireRunning(handle, "complete");
            handle.Complete();
            Assert.AreEqual(MotionResult.Completed, handle.Result);
            yield return Frames(2);
            Assert.AreEqual(0.2f, m_Box.resolvedStyle.opacity, 0.01f);
            Assert.AreEqual(2f, m_Box.resolvedStyle.scale.value.x, 0.01f);
            Assert.AreEqual(StyleKeyword.Null, m_Box.style.transitionProperty.keyword);
        }

        [UnityTest]
        public IEnumerator TakeOver_ANewFadeCancelsTheRunningOne()
        {
            var first = UiMotion.Fade(m_Box, 0f, MotionTiming.Ms(800f)).Play();
            yield return new WaitForSecondsRealtime(0.2f);
            RequireRunning(first, "take over");
            var second = UiMotion.Fade(m_Box, 1f, MotionTiming.Ms(200f)).Play();
            Assert.AreEqual(MotionResult.Cancelled, first.Result);
            yield return Finish(second);
            yield return Frames(2);
            Assert.AreEqual(1f, m_Box.resolvedStyle.opacity, 0.01f);
        }

        [UnityTest]
        public IEnumerator Await_YieldingTheHandleWaitsForTheEnd()
        {
            var handle = UiMotion.Fade(m_Box, 0f, MotionTiming.Ms(150f)).Play();
            yield return handle;
            Assert.AreEqual(MotionResult.Completed, handle.Result);
            var called = MotionResult.Running;
            handle.OnFinished(r => called = r);
            Assert.AreEqual(MotionResult.Completed, called, "a late callback fires at once");
        }

        #endregion

        #region Transition ownership

        static float TranslateX(VisualElement e) => e.resolvedStyle.translate.x;

        [UnityTest]
        public IEnumerator ParallelOnOneElement_UnequalDurations_NeitherDisturbsTheOther()
        {
            var handle = UiMotion.Parallel(
                UiMotion.Fade(m_Box, 0f, MotionTiming.Ms(250f)),
                UiMotion.Move(m_Box, new Vector2(80f, 0f), MotionTiming.Ms(900f))).Play();
            var samples = new List<float>();
            var opacityDoneAt = -1f;
            var started = Time.realtimeSinceStartup;
            while (handle.IsRunning && Time.realtimeSinceStartup - started < 5f)
            {
                var t = Time.realtimeSinceStartup - started;
                if (opacityDoneAt < 0f && m_Box.resolvedStyle.opacity < 0.01f)
                    opacityDoneAt = t;
                if (opacityDoneAt >= 0f)
                    samples.Add(TranslateX(m_Box));
                yield return null;
            }
            Assert.AreEqual(MotionResult.Completed, handle.Result);
            if (opacityDoneAt < 0f || samples.Count < 4)
                Assert.Inconclusive("the opacity tween ended too close to the end of the move to observe the overlap");
            for (var i = 1; i < samples.Count; i++)
                Assert.GreaterOrEqual(samples[i], samples[i - 1] - 0.5f, "the move kept animating smoothly after the fade ended (sample " + i + ")");
            Assert.Greater(samples[0], 0f);
            Assert.Less(samples[0], 70f, "the move had not jumped to its end when the fade ended");
            yield return Frames(3);
            Assert.AreEqual(80f, TranslateX(m_Box), 0.5f);
            Assert.AreEqual(0f, m_Box.resolvedStyle.opacity, 0.01f);
            Assert.AreEqual(0, TransitionLedger.ClaimCount(m_Box), "every claim was released");
            Assert.AreEqual(StyleKeyword.Null, m_Box.style.transitionProperty.keyword);
        }

        [UnityTest]
        public IEnumerator IndependentRunsOnDifferentChannels_AreNotDisturbedByEachOthersEnd()
        {
            var fade = UiMotion.Fade(m_Box, 0f, MotionTiming.Ms(200f)).Play();
            var move = UiMotion.Move(m_Box, new Vector2(60f, 0f), MotionTiming.Ms(800f)).Play();
            yield return Finish(fade);
            RequireRunning(move, "independent runs");
            var before = TranslateX(m_Box);
            yield return Frames(3);
            Assert.GreaterOrEqual(TranslateX(m_Box), before - 0.5f, "still moving forward");
            Assert.IsTrue(move.IsRunning || move.Result == MotionResult.Completed);
            yield return Finish(move);
            yield return Frames(3);
            Assert.AreEqual(60f, TranslateX(m_Box), 0.5f);
            Assert.AreEqual(StyleKeyword.Null, m_Box.style.transitionProperty.keyword);
        }

        void DeclareDesignerTransition(VisualElement e, float ms)
        {
            // what a generated motion sheet would declare on a tagged control: an element-wide inline stand-in
            e.style.transitionProperty = new List<StylePropertyName>
            {
                new StylePropertyName("opacity"), new StylePropertyName("translate"), new StylePropertyName("scale")
            };
            e.style.transitionDuration = new List<TimeValue> { new TimeValue(ms, TimeUnit.Millisecond) };
            e.style.transitionTimingFunction = new List<EasingFunction> { new EasingFunction(EasingMode.Linear) };
        }

        [UnityTest]
        public IEnumerator ManualRun_OverADesignerTransition_FollowsTheTimelineAndRestoresTheDeclaration()
        {
            DeclareDesignerTransition(m_Box, 1200f);
            yield return Frames(2);
            var started = Time.realtimeSinceStartup;
            var handle = UiMotion.Fade(m_Box, 0f, 1f, MotionTiming.Ms(600f)).Play(); // a start value: frame by frame
            var worst = 0f;
            var samples = 0;
            while (handle.IsRunning && Time.realtimeSinceStartup - started < 5f)
            {
                var t = (Time.realtimeSinceStartup - started) / 0.6f;
                if (t > 0.15f && t < 0.9f)
                {
                    worst = Mathf.Max(worst, Mathf.Abs(m_Box.resolvedStyle.opacity - t));
                    samples++;
                }
                yield return null;
            }
            Assert.AreEqual(MotionResult.Completed, handle.Result);
            if (samples < 4)
                Assert.Inconclusive("too few frames to compare with the timeline");
            Assert.Less(worst, 0.3f, "the value followed the timeline instead of lagging behind a 1200 ms transition");
            yield return Frames(4); // the declaration comes back a frame after the last write
            Assert.AreEqual(1f, m_Box.resolvedStyle.opacity, 0.01f);
            Assert.AreEqual(1200f, m_Box.style.transitionDuration.value[0].value, 0.1f, "the designer's declaration is back");
            Assert.AreEqual(3, m_Box.style.transitionProperty.value.Count);
        }

        [UnityTest]
        public IEnumerator CompleteAndCancelRestore_JumpEvenWhenADesignerTransitionIsDeclared()
        {
            DeclareDesignerTransition(m_Box, 1500f);
            yield return Frames(2);
            var run = UiMotion.Fade(m_Box, 0f, 1f, MotionTiming.Ms(2000f)).Play();
            yield return new WaitForSecondsRealtime(0.2f);
            run.Complete();
            yield return Frames(2);
            Assert.AreEqual(1f, m_Box.resolvedStyle.opacity, 0.02f, "Complete jumped, it did not animate towards the end");

            m_Box.style.opacity = 0.5f;
            yield return Frames(4);
            var second = UiMotion.Fade(m_Box, 0f, 1f, MotionTiming.Ms(2000f)).Play();
            yield return new WaitForSecondsRealtime(0.2f);
            second.Cancel(MotionCancel.Restore);
            yield return Frames(2);
            Assert.AreEqual(0.5f, m_Box.resolvedStyle.opacity, 0.02f, "Restore jumped back");
            yield return Frames(4);
        }

        [UnityTest]
        public IEnumerator OneTargetObjectForTwoRuns_EachRunIsIndependent()
        {
            var target = UiMotion.Target(m_Box);
            var fade = UiMotion.Tween(target, new MotionValues().WithOpacity(0.2f), MotionTiming.Ms(900f)).Play();
            var scale = UiMotion.Tween(target, new MotionValues().WithScale(1.6f), MotionTiming.Ms(900f)).Play();
            Assert.IsTrue(fade.IsRunning && scale.IsRunning);
            yield return new WaitForSecondsRealtime(0.25f);
            RequireRunning(fade, "reusable target");
            scale.Cancel();
            Assert.IsTrue(fade.IsRunning, "cancelling one run left the other running");
            yield return Finish(fade);
            Assert.AreEqual(MotionResult.Completed, fade.Result);
            yield return Frames(3);
            Assert.AreEqual(0.2f, m_Box.resolvedStyle.opacity, 0.02f);
        }

        [UnityTest]
        public IEnumerator ReusedTargetObject_StoppingOneDriverDoesNotStopTheOther()
        {
            var target = UiMotion.Target(m_Box);
            var first = UiMotion.Tween(target, new MotionValues().WithOpacity(0.1f), MotionTiming.Ms(300f)).Play(Manual);
            var second = UiMotion.Tween(target, new MotionValues().WithRotate(90f), MotionTiming.Ms(900f)).Play(Manual);
            yield return Finish(first);
            RequireRunning(second, "driver independence");
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.IsTrue(second.IsRunning, "the second run keeps being ticked after the first one ended");
            yield return Finish(second);
            Assert.AreEqual(MotionResult.Completed, second.Result);
        }

        #endregion

        #region Detach and destroy

        [UnityTest]
        public IEnumerator ElementRemovedMidRun_EndsDetachedWithTheEndState([ValueSource(nameof(Modes))] MotionMode mode)
        {
            var handle = UiMotion.Fade(m_Box, 0f, MotionTiming.Ms(600f)).Play(new MotionOptions { Mode = mode });
            yield return new WaitForSecondsRealtime(0.2f);
            RequireRunning(handle, "remove mid run");
            m_Box.RemoveFromHierarchy();
            Assert.AreEqual(MotionResult.Detached, handle.Result);
            Assert.AreEqual(0f, m_Box.style.opacity.value, 1e-4f, "end state applied to the element that left");
            Assert.AreEqual(StyleKeyword.Null, m_Box.style.transitionProperty.keyword);
            yield return Frames(2);
        }

        [UnityTest]
        public IEnumerator ElementNeverInAPanel_FinishesAtOnceWithTheEndState()
        {
            var loose = new VisualElement();
            var handle = UiMotion.Fade(loose, 0f, MotionTiming.Ms(300f)).Play();
            Assert.AreEqual(MotionResult.Detached, handle.Result);
            Assert.AreEqual(0f, loose.style.opacity.value, 1e-4f);
            yield break;
        }

        [UnityTest]
        public IEnumerator PanelDestroyedMidRun_TheRunEnds([ValueSource(nameof(Modes))] MotionMode mode)
        {
            var handle = UiMotion.Fade(m_Box, 0f, MotionTiming.Ms(2000f)).Play(new MotionOptions { Mode = mode });
            yield return new WaitForSecondsRealtime(0.1f);
            Object.Destroy(m_Object);
            m_Object = null;
            yield return Frames(5);
            Assert.IsFalse(handle.IsRunning, "destroying the UIDocument detaches its elements, which ends the run");
            Assert.AreEqual(MotionResult.Detached, handle.Result);
        }

        #endregion

        #region Composition

        [UnityTest]
        public IEnumerator Pop_ScalesPastOneAndSettlesAtOne()
        {
            var peak = 0f;
            var frames = 0;
            var handle = MotionExamples.Pop(m_Box, 600f).Play();
            var until = Time.realtimeSinceStartup + 5f;
            while (handle.IsRunning && Time.realtimeSinceStartup < until)
            {
                peak = Mathf.Max(peak, m_Box.resolvedStyle.scale.value.x);
                frames++;
                yield return null;
            }
            if (frames < 6)
                Assert.Inconclusive("pop was rendered in only " + frames + " frames");
            Assert.AreEqual(MotionResult.Completed, handle.Result);
            yield return Frames(2);
            Assert.Greater(peak, 1.02f, "overshoot reached 1.05");
            Assert.AreEqual(1f, m_Box.resolvedStyle.scale.value.x, 0.01f);
            Assert.AreEqual(1f, m_Box.resolvedStyle.opacity, 0.01f);
        }

        [UnityTest]
        public IEnumerator Shake_ReturnsToRest()
        {
            m_Box.style.translate = new Translate(10f, 5f);
            yield return Frames(2);
            var handle = MotionExamples.Shake(m_Box, 12f, 4, 600f).Play();
            var min = float.MaxValue;
            var max = float.MinValue;
            var frames = 0;
            var until = Time.realtimeSinceStartup + 5f;
            while (handle.IsRunning && Time.realtimeSinceStartup < until)
            {
                var x = m_Box.resolvedStyle.translate.x;
                min = Mathf.Min(min, x);
                max = Mathf.Max(max, x);
                frames++;
                yield return null;
            }
            if (frames < 8)
                Assert.Inconclusive("shake was rendered in only " + frames + " frames");
            Assert.AreEqual(MotionResult.Completed, handle.Result);
            yield return Frames(2);
            Assert.Greater(max - min, 8f, "it moved to both sides");
            Assert.AreEqual(10f, m_Box.resolvedStyle.translate.x, 0.5f);
            Assert.AreEqual(5f, m_Box.resolvedStyle.translate.y, 0.5f);
        }

        [UnityTest]
        public IEnumerator PulseLoop_RunsUntilCancelled()
        {
            var handle = MotionExamples.PulseLoop(m_Box, 1.1f, 300f).Play();
            var peak = 0f;
            var until = Time.realtimeSinceStartup + 0.9f; // three periods
            while (Time.realtimeSinceStartup < until)
            {
                peak = Mathf.Max(peak, m_Box.resolvedStyle.scale.value.x);
                yield return null;
            }
            Assert.IsTrue(handle.IsRunning);
            Assert.Greater(peak, 1.05f);
            Assert.LessOrEqual(peak, 1.11f);
            handle.Cancel();
            Assert.AreEqual(MotionResult.Cancelled, handle.Result);
        }

        [UnityTest]
        public IEnumerator Stagger_StartsItemsOneAfterAnother()
        {
            var items = new List<VisualElement> { m_Box, Box("b", 20f, 90f), Box("c", 20f, 160f) };
            var handle = MotionExamples.StaggerIn(items, 300f, 600f, 10f).Play();
            Assert.AreEqual(0f, items[2].style.opacity.value, 1e-4f, "every item is hidden from the first frame");
            yield return new WaitForSecondsRealtime(0.25f);
            RequireRunning(handle, "stagger");
            var a = items[0].style.opacity.value;
            var c = items[2].style.opacity.value;
            Assert.Greater(a, c, "the first item is further along than the last");
            yield return Finish(handle);
            foreach (var item in items)
                Assert.AreEqual(1f, item.style.opacity.value, 1e-4f);
        }

        [UnityTest]
        public IEnumerator CountUp_WritesIncreasingNumbersAndEndsOnTheTarget()
        {
            var label = new Label("0");
            m_Root.Add(label);
            yield return Frames(2);
            var texts = new List<int>();
            var handle = MotionExamples.CountUp(label, 0f, 100f, 300f).Play();
            var until = Time.realtimeSinceStartup + 3f;
            while (handle.IsRunning && Time.realtimeSinceStartup < until)
            {
                if (int.TryParse(label.text, out var n) && (texts.Count == 0 || texts[texts.Count - 1] != n))
                    texts.Add(n);
                yield return null;
            }
            Assert.AreEqual(MotionResult.Completed, handle.Result);
            Assert.AreEqual("100", label.text);
            Assert.GreaterOrEqual(texts.Count, 4);
            for (var i = 1; i < texts.Count; i++)
                Assert.GreaterOrEqual(texts[i], texts[i - 1], "never counts backwards");
        }

        #endregion

        #region Input blocking

        [UnityTest]
        public IEnumerator BlockInput_SwallowsClicksWhileRunningAndReleasesAfterwards()
        {
            var clicks = 0;
            var button = new Button { name = "under", text = "x" };
            button.style.position = Position.Absolute;
            button.style.left = 200f;
            button.style.top = 20f;
            button.style.width = 100f;
            button.style.height = 40f;
            button.clicked += () => clicks++;
            m_Root.Add(button);
            yield return Frames(3);

            var handle = UiMotion.Fade(m_Box, 0.5f, MotionTiming.Ms(400f)).Play(new MotionOptions { BlockInput = true });
            Assert.AreEqual(1, MotionInputBlock.ActiveBlockers);
            Assert.IsNotNull(m_Root.panel.visualTree.Q<VisualElement>(MotionInputBlock.BlockerName));
            yield return Frames(2);
            UiInputSimulator.Click(m_Root.panel, UiInputSimulator.Center(button));
            yield return Frames(2);
            Assert.AreEqual(0, clicks, "the click did not reach the button");

            yield return Finish(handle);
            Assert.AreEqual(0, MotionInputBlock.ActiveBlockers);
            Assert.IsNull(m_Root.panel.visualTree.Q<VisualElement>(MotionInputBlock.BlockerName));
            UiInputSimulator.Click(m_Root.panel, UiInputSimulator.Center(button));
            yield return Frames(2);
            Assert.AreEqual(1, clicks, "the same click works after the end");
        }

        [UnityTest]
        public IEnumerator BlockInput_IsSharedAndReleasedOnCancel()
        {
            var one = UiMotion.Fade(m_Box, 0.5f, MotionTiming.Ms(2000f)).Play(new MotionOptions { BlockInput = true });
            var b = Box("b", 20f, 90f);
            var two = UiMotion.Fade(b, 0.5f, MotionTiming.Ms(2000f)).Play(new MotionOptions { BlockInput = true });
            Assert.AreEqual(1, MotionInputBlock.ActiveBlockers, "one blocker per panel");
            one.Cancel();
            Assert.AreEqual(1, MotionInputBlock.ActiveBlockers, "still needed by the second run");
            two.Cancel();
            Assert.AreEqual(0, MotionInputBlock.ActiveBlockers);
            yield break;
        }

        #endregion

        #region Presets

        [UnityTest]
        public IEnumerator Preset_StateTweenDrivesAnElementWithTheDesignersTiming()
        {
            const string json = @"{ ""durations"": [ { ""name"": ""fast"", ""ms"": 120 } ],
              ""presets"": [ { ""name"": ""button"", ""duration"": ""fast"", ""easing"": ""ease-out"",
                ""properties"": [ ""scale"", ""opacity"" ],
                ""states"": [ { ""state"": ""hover"", ""scale"": ""1.2"" }, { ""state"": ""disabled"", ""opacity"": ""0.5"" } ] } ] }";
            Assert.IsTrue(MotionPresets.Load(json));
            try
            {
                Assert.IsTrue(MotionPresets.TryGet("button", out var button));
                var started = Time.realtimeSinceStartup;
                var handle = button.To(m_Box, "hover").Play();
                yield return Finish(handle);
                Assert.Less(Time.realtimeSinceStartup - started, 0.6f);
                yield return Frames(2);
                Assert.AreEqual(1.2f, m_Box.resolvedStyle.scale.value.x, 0.01f);
                yield return Finish(button.To(m_Box, MotionPreset.RestState).Play());
                yield return Frames(2);
                Assert.AreEqual(1f, m_Box.resolvedStyle.scale.value.x, 0.01f);
                yield return Finish(UiMotion.Fade(m_Box, 0f, MotionPresets.Timing("button")).Play());
            }
            finally
            {
                MotionPresets.Use(new Dictionary<string, MotionPreset>());
            }
        }

        #endregion

        #region Cost

        [UnityTest]
        public IEnumerator Allocations_ManualTweenAddsLittlePerFrame()
        {
            // Compare 40 idle frames with 40 frames of a manual tween; UI Toolkit itself allocates a little, the engine must not add much.
            yield return Frames(5);
            var before = System.GC.GetAllocatedBytesForCurrentThread();
            yield return Frames(40);
            var idle = System.GC.GetAllocatedBytesForCurrentThread() - before;

            var handle = UiMotion.Tween(m_Box, new MotionValues().WithOpacity(0.2f).WithScale(1.3f).WithTranslate(30f, 0f),
                MotionTiming.Ms(5000f)).Play(Manual);
            yield return Frames(5);
            before = System.GC.GetAllocatedBytesForCurrentThread();
            yield return Frames(40);
            var running = System.GC.GetAllocatedBytesForCurrentThread() - before;
            handle.Cancel();
            var perFrame = (running - idle) / 40f;
            Debug.Log("Motion allocations: idle " + idle + " B, manual tween " + running + " B over 40 frames, engine share " + perFrame + " B/frame");
            Assert.Less(perFrame, 256f, "the per-frame tick allocates almost nothing");
        }

        #endregion
    }
}
#endif
