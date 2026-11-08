using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Cuvara.UIToolkit.Motion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cuvara.UIToolkit.Motion.Tests
{
    /// <summary>
    /// The timeline engine against a manual clock and a recording target: composition, loops, control, detach, take-over and the
    /// transition fast path. No frames, no panel; the real-frame behaviour is in the PlayMode tests.
    /// </summary>
    public class MotionTimelineTests
    {
        static readonly MotionOptions Manual = new MotionOptions { Mode = MotionMode.Manual };
        const float Eps = 0.002f;

        FakeDriver m_Driver;
        FakeTarget m_A, m_B, m_C;

        [SetUp]
        public void SetUp()
        {
            m_Driver = new FakeDriver();
            m_A = new FakeTarget();
            m_B = new FakeTarget();
            m_C = new FakeTarget();
        }

        [TearDown]
        public void TearDown()
        {
            Assert.AreEqual(0, MotionHandle.ActiveCount, "a run was left registered");
            Assert.AreEqual(0, m_A.Owners.Count + m_B.Owners.Count + m_C.Owners.Count, "a transition claim was never released");
        }

        static MotionValues Op(float v) => new MotionValues().WithOpacity(v);

        static MotionClip FadeTo(FakeTarget t, float to, float ms, float delay = 0f) =>
            UiMotion.Tween(t, Op(to), new MotionTiming(ms, MotionEasing.Linear, delay));

        static MotionClip FadeFromTo(FakeTarget t, float from, float to, float ms) =>
            UiMotion.Tween(t, Op(to), MotionTiming.Ms(ms), Op(from));

        #region Tweens

        [Test]
        public void ManualTween_InterpolatesAndCompletes()
        {
            var h = FadeTo(m_A, 0f, 100f).Play(Manual, m_Driver);
            Assert.IsTrue(h.IsRunning);
            m_Driver.Advance(50f);
            Assert.AreEqual(0.5f, m_A.Opacity, Eps);
            Assert.IsTrue(h.IsRunning);
            m_Driver.Advance(60f);
            Assert.AreEqual(0f, m_A.Opacity, Eps);
            Assert.AreEqual(MotionResult.Completed, h.Result);
            Assert.IsFalse(m_Driver.Running, "the driver stops with the run");
            Assert.IsFalse(m_A.HasDetachWatcher, "the detach watcher is removed");
        }

        [Test]
        public void Delay_HoldsTheStartValueThenRuns()
        {
            var h = UiMotion.Tween(m_A, Op(1f), new MotionTiming(100f, MotionEasing.Linear, 100f), Op(0f)).Play(Manual, m_Driver);
            Assert.AreEqual(0f, m_A.Opacity, Eps, "the start value shows at once");
            m_Driver.Advance(50f);
            Assert.AreEqual(0f, m_A.Opacity, Eps);
            m_Driver.Advance(100f); // t = 150, 50 ms into the tween
            Assert.AreEqual(0.5f, m_A.Opacity, Eps);
            m_Driver.Advance(100f);
            Assert.AreEqual(1f, m_A.Opacity, Eps);
            Assert.AreEqual(MotionResult.Completed, h.Result);
        }

        [Test]
        public void EasedTween_FollowsTheCurve()
        {
            var h = UiMotion.Tween(m_A, Op(0f), new MotionTiming(100f, MotionEasing.EaseIn)).Play(Manual, m_Driver);
            m_Driver.Advance(50f);
            Assert.AreEqual(1f - MotionEasing.EaseIn.Evaluate(0.5f), m_A.Opacity, Eps);
            Assert.Less(MotionEasing.EaseIn.Evaluate(0.5f), 0.5f);
            h.Cancel();
        }

        [Test]
        public void MultiChannelTween_WritesEveryChannel()
        {
            var to = new MotionValues().WithOpacity(0f).WithScale(2f).WithTranslate(10f, 20f);
            var h = UiMotion.Tween(m_A, to, MotionTiming.Ms(100f)).Play(Manual, m_Driver);
            m_Driver.Advance(50f);
            Assert.AreEqual(0.5f, m_A.Opacity, Eps);
            Assert.AreEqual(1.5f, m_A.Scale.x, Eps);
            Assert.AreEqual(10f, m_A.Translate.y, Eps);
            h.Complete();
            Assert.AreEqual(2f, m_A.Scale.y, Eps);
        }

        #endregion

        #region Composition

        [Test]
        public void Sequence_SecondStepStartsFromTheFirstEnd()
        {
            var h = UiMotion.Sequence(FadeTo(m_A, 0f, 100f), FadeTo(m_A, 1f, 100f)).Play(Manual, m_Driver);
            m_Driver.Advance(100f);
            Assert.AreEqual(0f, m_A.Opacity, Eps);
            m_Driver.Advance(50f);
            Assert.AreEqual(0.5f, m_A.Opacity, Eps);
            m_Driver.Advance(60f);
            Assert.AreEqual(1f, m_A.Opacity, Eps);
            Assert.AreEqual(MotionResult.Completed, h.Result);
        }

        [Test]
        public void SequenceDuration_IsTheSumIncludingWaits()
        {
            var clip = UiMotion.Sequence(FadeTo(m_A, 0f, 100f), UiMotion.Wait(50f), FadeTo(m_A, 1f, 100f, delay: 25f));
            Assert.AreEqual(275f, clip.DurationMs, 0.01f);
            clip.Play(Manual, m_Driver).Cancel();
        }

        [Test]
        public void Parallel_RunsTogetherAndEndsWithTheLongest()
        {
            var h = UiMotion.Parallel(
                FadeTo(m_A, 0f, 100f),
                UiMotion.Tween(m_B, new MotionValues().WithTranslate(10f, 0f), MotionTiming.Ms(200f))).Play(Manual, m_Driver);
            m_Driver.Advance(100f);
            Assert.AreEqual(0f, m_A.Opacity, Eps);
            Assert.AreEqual(5f, m_B.Translate.x, Eps);
            Assert.IsTrue(h.IsRunning);
            m_Driver.Advance(110f);
            Assert.AreEqual(10f, m_B.Translate.x, Eps);
            Assert.AreEqual(MotionResult.Completed, h.Result);
        }

        [Test]
        public void Stagger_StartsEachClipAStepLater()
        {
            var targets = new[] { m_A, m_B, m_C };
            var clips = new MotionClip[3];
            for (var i = 0; i < 3; i++)
                clips[i] = FadeTo(targets[i], 0f, 100f);
            var h = UiMotion.Stagger(clips, 50f).Play(Manual, m_Driver);
            m_Driver.Advance(75f);
            Assert.AreEqual(0.25f, m_A.Opacity, Eps);
            Assert.AreEqual(0.75f, m_B.Opacity, Eps);
            Assert.AreEqual(1f, m_C.Opacity, Eps, "not started");
            m_Driver.Advance(75f); // t = 150
            Assert.AreEqual(0f, m_A.Opacity, Eps);
            Assert.AreEqual(0f, m_B.Opacity, Eps);
            Assert.AreEqual(0.5f, m_C.Opacity, Eps);
            m_Driver.Advance(60f);
            Assert.AreEqual(MotionResult.Completed, h.Result);
        }

        [Test]
        public void Loop_RepeatsFromTheCapturedStart()
        {
            var h = UiMotion.Loop(FadeFromTo(m_A, 0f, 1f, 100f), 2).Play(Manual, m_Driver);
            m_Driver.Advance(150f);
            Assert.AreEqual(0.5f, m_A.Opacity, Eps, "second iteration");
            m_Driver.Advance(10f);
            Assert.AreEqual(0.6f, m_A.Opacity, Eps);
            m_Driver.Advance(50f);
            Assert.AreEqual(1f, m_A.Opacity, Eps);
            Assert.AreEqual(MotionResult.Completed, h.Result);
        }

        [Test]
        public void Loop_StartValueIsCapturedOnceNotPerIteration()
        {
            // no explicit start: iteration 1 starts from 1, ends at 0; iteration 2 must start from 1 again, not from 0
            var h = UiMotion.Loop(FadeTo(m_A, 0f, 100f), 2).Play(Manual, m_Driver);
            m_Driver.Advance(99f); // end of iteration 0: opacity is about 0
            Assert.AreEqual(0.01f, m_A.Opacity, Eps);
            m_Driver.Advance(51f); // t = 150: 50 ms into iteration 1, which must start from 1 again
            Assert.AreEqual(0.5f, m_A.Opacity, Eps);
            h.Cancel();
        }

        [Test]
        public void Loop_CallFiresOncePerIteration()
        {
            var calls = 0;
            var h = UiMotion.Loop(UiMotion.Sequence(UiMotion.Call(() => calls++), UiMotion.Wait(100f)), 3).Play(Manual, m_Driver);
            m_Driver.Advance(50f);
            Assert.AreEqual(1, calls);
            m_Driver.Advance(100f); // t = 150
            Assert.AreEqual(2, calls);
            m_Driver.Advance(100f); // t = 250
            Assert.AreEqual(3, calls);
            m_Driver.Advance(100f); // t = 350: over
            Assert.AreEqual(3, calls, "no fourth iteration");
            Assert.AreEqual(MotionResult.Completed, h.Result);
        }

        [Test]
        public void Loop_AFrameThatSkipsIterationsStillFiresTheirCalls()
        {
            var calls = 0;
            var h = UiMotion.Loop(UiMotion.Sequence(UiMotion.Call(() => calls++), UiMotion.Wait(100f)), 5).Play(Manual, m_Driver);
            m_Driver.Advance(250f); // iterations 0 and 1 were skipped, 2 is current
            Assert.AreEqual(3, calls);
            h.Complete();
            Assert.AreEqual(5, calls, "complete fires what is left");
        }

        [Test]
        public void Loop_ACallAtTheVeryEndOfAnIterationFires()
        {
            var calls = 0;
            var h = UiMotion.Loop(UiMotion.Sequence(UiMotion.Wait(100f), UiMotion.Call(() => calls++)), 2).Play(Manual, m_Driver);
            m_Driver.Advance(99f);
            Assert.AreEqual(0, calls);
            m_Driver.Advance(2f); // t = 101: iteration 0 ended
            Assert.AreEqual(1, calls);
            m_Driver.Advance(120f);
            Assert.AreEqual(2, calls);
            Assert.AreEqual(MotionResult.Completed, h.Result);
        }

        [Test]
        public void Loop_EveryIterationRestoresTheStartValueBeforeItPlays()
        {
            var h = UiMotion.Loop(FadeFromTo(m_A, 0f, 1f, 100f), 3).Play(Manual, m_Driver);
            m_Driver.Advance(99f);
            Assert.AreEqual(0.99f, m_A.Opacity, Eps);
            m_Driver.Advance(11f); // t = 110: iteration 1, 10 ms in
            Assert.AreEqual(0.1f, m_A.Opacity, Eps);
            h.Cancel();
        }

        [Test]
        public void NestedLoop_InnerIterationsRestartForEveryOuterIteration()
        {
            var calls = 0;
            var inner = UiMotion.Loop(UiMotion.Sequence(UiMotion.Call(() => calls++), UiMotion.Wait(50f)), 2);
            var h = UiMotion.Loop(inner, 2).Play(Manual, m_Driver);
            for (var i = 0; i < 40; i++)
                m_Driver.Advance(10f);
            Assert.AreEqual(4, calls);
            Assert.AreEqual(MotionResult.Completed, h.Result);
        }

        [Test]
        public void PingPong_RejectsACallAnywhereInsideIt()
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                UiMotion.Loop(UiMotion.Sequence(UiMotion.Wait(50f), UiMotion.Call(() => { }), UiMotion.Wait(50f)), 3, pingPong: true));
            StringAssert.Contains("ping-pong", ex.Message);
            StringAssert.Contains("Call", ex.Message);
            Assert.Throws<ArgumentException>(() => UiMotion.Loop(UiMotion.Call(() => { }), 2, pingPong: true), "directly");
            Assert.Throws<ArgumentException>(() => UiMotion.Loop(UiMotion.Parallel(FadeTo(m_A, 0f, 100f), UiMotion.Call(() => { })), 2, pingPong: true), "in a group");
            // nested: a plain loop with a call inside a ping-pong loop, and the same through two levels
            Assert.Throws<ArgumentException>(() =>
                UiMotion.Loop(UiMotion.Loop(UiMotion.Sequence(UiMotion.Call(() => { }), UiMotion.Wait(100f)), 2), 2, pingPong: true), "nested loop");
            Assert.Throws<ArgumentException>(() =>
                UiMotion.Loop(UiMotion.Sequence(UiMotion.Wait(10f), UiMotion.Loop(UiMotion.Call(() => { }), 2)), 2, pingPong: true), "nested loop in a sequence");
        }

        [Test]
        public void PingPong_AroundALoopWithoutCallsStillWorks_AndACallOutsideIsFine()
        {
            var calls = 0;
            var inner = UiMotion.Loop(FadeFromTo(m_A, 0f, 1f, 100f), 2);
            var h = UiMotion.Sequence(UiMotion.Loop(inner, 2, pingPong: true), UiMotion.Call(() => calls++)).Play(Manual, m_Driver);
            m_Driver.Advance(50f);
            Assert.AreEqual(0.5f, m_A.Opacity, Eps);
            m_Driver.Advance(100f); // t = 150: inner iteration 1, 50 ms in
            Assert.AreEqual(0.5f, m_A.Opacity, Eps);
            m_Driver.Advance(100f); // t = 250: the outer iteration is backwards; mirrored inner time is 150
            Assert.AreEqual(0.5f, m_A.Opacity, Eps);
            Assert.AreEqual(0, calls);
            m_Driver.Advance(200f);
            Assert.AreEqual(1, calls, "the call after the ping-pong loop fires once");
            Assert.AreEqual(MotionResult.Completed, h.Result);
        }

        [Test]
        public void Sequence_SnapshotsItsInput_SoALaterMutationCannotSneakACallIntoAPingPongLoop()
        {
            var calls = 0;
            var steps = new[] { UiMotion.Wait(100f), UiMotion.Wait(0f) };
            var loop = UiMotion.Loop(UiMotion.Sequence(steps), 2, pingPong: true);
            steps[1] = UiMotion.Call(() => calls++);
            var h = loop.Play(Manual, m_Driver);
            for (var i = 0; i < 12; i++)
                m_Driver.Advance(25f);
            Assert.AreEqual(0, calls, "the built tree did not change");
            Assert.AreEqual(MotionResult.Completed, h.Result);
        }

        [Test]
        public void Parallel_SnapshotsItsInput_AlsoAfterPlay()
        {
            var calls = 0;
            var steps = new[] { FadeTo(m_A, 0f, 100f), UiMotion.Wait(100f) };
            var loop = UiMotion.Loop(UiMotion.Parallel(steps), 2, pingPong: true);
            var h = loop.Play(Manual, m_Driver);
            steps[1] = UiMotion.Call(() => calls++); // after Play
            for (var i = 0; i < 10; i++)
                m_Driver.Advance(25f);
            Assert.AreEqual(0, calls);
            Assert.AreEqual(MotionResult.Completed, h.Result);
        }

        [Test]
        public void Stagger_SnapshotsItsInputList()
        {
            var calls = 0;
            var list = new List<MotionClip> { FadeTo(m_A, 0f, 100f), UiMotion.Wait(100f) };
            var loop = UiMotion.Loop(UiMotion.Stagger(list, 10f), 2, pingPong: true);
            list[1] = UiMotion.Call(() => calls++);
            var h = loop.Play(Manual, m_Driver);
            for (var i = 0; i < 12; i++)
                m_Driver.Advance(25f);
            Assert.AreEqual(0, calls);
            Assert.AreEqual(MotionResult.Completed, h.Result);
        }

        [Test]
        public void ZeroDurationFiniteLoop_RunsItsRepeatCount()
        {
            var calls = 0;
            var h = UiMotion.Loop(UiMotion.Call(() => calls++), 3).Play(Manual, m_Driver);
            m_Driver.Advance(10f);
            Assert.AreEqual(3, calls);
            Assert.AreEqual(MotionResult.Completed, h.Result);
        }

        [Test]
        public void ZeroDurationFiniteLoop_CompleteRunsItsRepeatCountToo()
        {
            var calls = 0;
            var h = UiMotion.Loop(UiMotion.Call(() => calls++), 3).Play(Manual, m_Driver);
            h.Complete();
            Assert.AreEqual(3, calls);
        }

        [Test]
        public void InfiniteLoop_RunsUntilCancelled()
        {
            var h = UiMotion.Loop(FadeFromTo(m_A, 0f, 1f, 100f)).Play(Manual, m_Driver);
            for (var i = 0; i < 100; i++)
                m_Driver.Advance(37f);
            Assert.IsTrue(h.IsRunning);
            h.Cancel();
            Assert.AreEqual(MotionResult.Cancelled, h.Result);
            Assert.IsFalse(m_Driver.Running);
        }

        [Test]
        public void InfiniteLoop_CompleteSettlesOneIteration()
        {
            var h = UiMotion.Loop(FadeFromTo(m_A, 0f, 1f, 100f)).Play(Manual, m_Driver);
            m_Driver.Advance(30f);
            h.Complete();
            Assert.AreEqual(1f, m_A.Opacity, Eps);
            Assert.AreEqual(MotionResult.Completed, h.Result);
        }

        [Test]
        public void PingPong_PlaysEverySecondIterationBackwards()
        {
            var h = UiMotion.Loop(FadeFromTo(m_A, 0f, 1f, 100f), 2, pingPong: true).Play(Manual, m_Driver);
            m_Driver.Advance(50f);
            Assert.AreEqual(0.5f, m_A.Opacity, Eps);
            m_Driver.Advance(75f); // t = 125: 25 ms into the way back
            Assert.AreEqual(0.75f, m_A.Opacity, Eps);
            m_Driver.Advance(100f);
            Assert.AreEqual(0f, m_A.Opacity, Eps, "an even number of passes ends at the start");
            Assert.AreEqual(MotionResult.Completed, h.Result);
        }

        [Test]
        public void Call_FiresOnceWhenTheTimelineReachesIt()
        {
            var calls = 0;
            var h = UiMotion.Sequence(FadeTo(m_A, 0f, 100f), UiMotion.Call(() => calls++), FadeTo(m_A, 1f, 100f)).Play(Manual, m_Driver);
            m_Driver.Advance(50f);
            Assert.AreEqual(0, calls);
            m_Driver.Advance(60f);
            Assert.AreEqual(1, calls);
            m_Driver.Advance(10f);
            Assert.AreEqual(1, calls, "once");
            h.Complete();
            Assert.AreEqual(1, calls, "Complete does not fire it again");
        }

        [Test]
        public void Call_CompleteFiresPendingCalls_CancelDoesNot()
        {
            var calls = 0;
            UiMotion.Sequence(FadeTo(m_A, 0f, 100f), UiMotion.Call(() => calls++)).Play(Manual, m_Driver).Complete();
            Assert.AreEqual(1, calls);
            UiMotion.Sequence(FadeTo(m_B, 0f, 100f), UiMotion.Call(() => calls++)).Play(Manual, m_Driver).Cancel();
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void Value_AnimatesAFloatWithoutAnyTarget()
        {
            var v = -1f;
            var h = UiMotion.Value(null, 0f, 10f, MotionTiming.Ms(100f), x => v = x).Play(default, m_Driver);
            m_Driver.Advance(50f);
            Assert.AreEqual(5f, v, 0.01f);
            m_Driver.Advance(60f);
            Assert.AreEqual(10f, v, 0.01f);
            Assert.AreEqual(MotionResult.Completed, h.Result);
        }

        #endregion

        #region Control

        [Test]
        public void Cancel_Keep_FreezesTheCurrentValues()
        {
            var h = FadeTo(m_A, 0f, 100f).Play(Manual, m_Driver);
            m_Driver.Advance(50f);
            h.Cancel();
            Assert.AreEqual(MotionResult.Cancelled, h.Result);
            Assert.AreEqual(0.5f, m_A.Opacity, Eps);
            m_Driver.Advance(100f);
            Assert.AreEqual(0.5f, m_A.Opacity, Eps, "nothing writes after the cancel");
        }

        [Test]
        public void Cancel_Restore_PutsBackTheValueTheRunFoundFirst()
        {
            var h = UiMotion.Sequence(FadeTo(m_A, 0f, 100f), FadeTo(m_A, 0.7f, 100f)).Play(Manual, m_Driver);
            m_Driver.Advance(150f);
            h.Cancel(MotionCancel.Restore);
            Assert.AreEqual(1f, m_A.Opacity, Eps);
        }

        [Test]
        public void Cancel_Restore_ReturnsTheOriginalNotTheExplicitStartValue()
        {
            m_A.SetOpacity(0.9f);
            var h = FadeFromTo(m_A, 0f, 1f, 100f).Play(Manual, m_Driver);
            Assert.AreEqual(0f, m_A.Opacity, Eps);
            h.Cancel(MotionCancel.Restore);
            Assert.AreEqual(0.9f, m_A.Opacity, Eps);
        }

        [Test]
        public void Complete_JumpsToTheEndState()
        {
            var h = UiMotion.Sequence(FadeTo(m_A, 0f, 100f), FadeTo(m_A, 0.25f, 100f)).Play(Manual, m_Driver);
            m_Driver.Advance(30f);
            h.Complete();
            Assert.AreEqual(0.25f, m_A.Opacity, Eps);
            Assert.AreEqual(MotionResult.Completed, h.Result);
            h.Complete();
            h.Cancel();
            Assert.AreEqual(MotionResult.Completed, h.Result, "finished is final");
        }

        [Test]
        public void OnFinished_CallsOnceWithTheResult_AndImmediatelyWhenAlreadyFinished()
        {
            var seen = new System.Collections.Generic.List<MotionResult>();
            var h = FadeTo(m_A, 0f, 100f).Play(Manual, m_Driver);
            h.OnFinished(seen.Add);
            m_Driver.Advance(120f);
            Assert.AreEqual(new[] { MotionResult.Completed }, seen);
            h.OnFinished(seen.Add);
            Assert.AreEqual(2, seen.Count);
        }

        [Test]
        public void OnFinished_ACallbackThatThrowsDoesNotStopTheOthers()
        {
            var second = false;
            var h = FadeTo(m_A, 0f, 100f).Play(Manual, m_Driver);
            h.OnFinished(_ => throw new InvalidOperationException("boom"));
            h.OnFinished(_ => second = true);
            LogAssert.Expect(LogType.Exception, new Regex("boom"));
            h.Complete();
            Assert.IsTrue(second);
        }

        [Test]
        public void Awaiter_ReportsCompletionAndResult()
        {
            var h = FadeTo(m_A, 0f, 100f).Play(Manual, m_Driver);
            var awaiter = h.GetAwaiter();
            var continued = false;
            Assert.IsFalse(awaiter.IsCompleted);
            Assert.IsTrue(h.keepWaiting);
            awaiter.OnCompleted(() => continued = true);
            h.Cancel();
            Assert.IsTrue(awaiter.IsCompleted);
            Assert.IsTrue(continued);
            Assert.IsFalse(h.keepWaiting);
            Assert.AreEqual(MotionResult.Cancelled, awaiter.GetResult());
        }

        [Test]
        public void ACallbackThatThrowsInsideTheTimeline_CancelsTheRunAndLogs()
        {
            var h = UiMotion.Sequence(FadeTo(m_A, 0f, 100f), UiMotion.Call(() => throw new InvalidOperationException("kaboom"))).Play(Manual, m_Driver);
            LogAssert.Expect(LogType.Exception, new Regex("kaboom"));
            m_Driver.Advance(150f);
            Assert.AreEqual(MotionResult.Cancelled, h.Result);
            Assert.IsFalse(m_Driver.Running);
        }

        [Test]
        public void EveryExitPath_ReleasesWhatTheLeavesClaimed()
        {
            m_A.Transitions = true;
            var completed = UiMotion.Tween(m_A, Op(0f), MotionTiming.Ms(100f)).Play(default, m_Driver);
            for (var i = 0; i < 4; i++)
                m_Driver.Advance(16f);
            Assert.Greater(m_A.Owners.Count, 0, "the leaf holds a claim while it runs");
            completed.Complete();
            Assert.AreEqual(0, m_A.Owners.Count);

            var cancelled = FadeFromTo(m_B, 0f, 1f, 100f).Play(Manual, m_Driver);
            Assert.AreEqual(1, m_B.Owners.Count);
            cancelled.Cancel();
            Assert.AreEqual(0, m_B.Owners.Count);

            var detached = FadeFromTo(m_C, 0f, 1f, 100f).Play(Manual, m_Driver);
            m_C.Detach();
            Assert.AreEqual(MotionResult.Detached, detached.Result);
            Assert.AreEqual(0, m_C.Owners.Count);
        }

        [Test]
        public void OneTargetObjectCanServeSeveralRuns_EachRunOwnsItsClaim()
        {
            // the same IMotionTarget instance in two runs on different channels: neither run may end or stop the other
            var driverA = new FakeDriver();
            var driverB = new FakeDriver();
            var drivers = new Queue<FakeDriver>(new[] { driverA, driverB });
            var shared = new SharedTarget(m_A, drivers);
            var first = UiMotion.Tween(shared, Op(0f), MotionTiming.Ms(100f)).Play(Manual);
            var second = UiMotion.Tween(shared, new MotionValues().WithScale(2f), MotionTiming.Ms(200f)).Play(Manual);
            Assert.IsTrue(driverA.Running && driverB.Running, "each run has its own driver");

            driverA.Advance(120f);
            Assert.AreEqual(MotionResult.Completed, first.Result);
            Assert.IsTrue(second.IsRunning, "the first run ending did not stop the second");
            Assert.IsTrue(driverB.Running);
            driverB.Advance(100f);
            Assert.AreEqual(1.5f, m_A.Scale.x, Eps);
            driverB.Advance(120f);
            Assert.AreEqual(MotionResult.Completed, second.Result);
        }

        /// <summary>A target object reused for several runs, handing out one driver per run.</summary>
        sealed class SharedTarget : IMotionTarget
        {
            readonly FakeTarget m_Inner;
            readonly Queue<FakeDriver> m_Drivers;

            public SharedTarget(FakeTarget inner, Queue<FakeDriver> drivers)
            {
                m_Inner = inner;
                m_Drivers = drivers;
            }

            public object Key => m_Inner.Key;
            public bool IsAlive => m_Inner.IsAlive;
            public bool SupportsTransitions => false;
            public IMotionDriver CreateDriver() => m_Drivers.Dequeue();
            public Vector4 Get(MotionChannels channel) => m_Inner.Get(channel);
            public void Set(MotionChannels channel, Vector4 value) => m_Inner.Set(channel, value);
            public void SuppressTransitions(object owner, MotionChannels channels) => m_Inner.SuppressTransitions(owner, channels);
            public void BeginTransition(object owner, MotionChannels channels, float durationMs, MotionEasing easing) => m_Inner.BeginTransition(owner, channels, durationMs, easing);
            public void EndTransition(object owner) => m_Inner.EndTransition(owner);
            public void ReleaseTransitions(object owner, bool immediate) => m_Inner.ReleaseTransitions(owner, immediate);
            public void Reset(MotionChannels channels) => m_Inner.Reset(channels);
            public void WatchDetach(Action onDetached) => m_Inner.WatchDetach(onDetached);
            public void UnwatchDetach(Action onDetached) => m_Inner.UnwatchDetach(onDetached);
        }

        #endregion

        #region Detach, take-over

        [Test]
        public void TargetThatIsNotInAPanel_GetsItsEndStateAndTheRunIsDetached()
        {
            m_A.Alive = false;
            var h = FadeTo(m_A, 0f, 100f).Play(Manual, m_Driver);
            Assert.AreEqual(MotionResult.Detached, h.Result);
            Assert.AreEqual(0f, m_A.Opacity, Eps);
            Assert.AreEqual(0, m_Driver.StartCount, "nothing to drive");
        }

        [Test]
        public void DetachMidRun_AppliesTheEndStateAndTheOthersKeepRunning()
        {
            var h = UiMotion.Parallel(FadeTo(m_A, 0f, 100f), FadeTo(m_B, 0f, 100f)).Play(Manual, m_Driver);
            m_Driver.Advance(40f);
            m_A.Detach();
            Assert.AreEqual(0f, m_A.Opacity, Eps, "end state on the element that left");
            Assert.IsTrue(h.IsRunning);
            m_Driver.Advance(30f);
            Assert.AreEqual(0.3f, m_B.Opacity, Eps);
            m_Driver.Advance(40f);
            Assert.AreEqual(MotionResult.Completed, h.Result);
        }

        [Test]
        public void DetachOfEveryTarget_EndsTheRunAsDetached()
        {
            var h = FadeTo(m_A, 0f, 100f).Play(Manual, m_Driver);
            m_Driver.Advance(40f);
            m_A.Detach();
            Assert.AreEqual(MotionResult.Detached, h.Result);
            Assert.AreEqual(0f, m_A.Opacity, Eps);
            Assert.IsFalse(m_Driver.Running);
        }

        [Test]
        public void DetachInASequence_AppliesTheLastStepsEndValue()
        {
            var h = UiMotion.Sequence(FadeTo(m_A, 0f, 100f), FadeTo(m_A, 0.6f, 100f)).Play(Manual, m_Driver);
            m_Driver.Advance(20f);
            m_A.Detach();
            Assert.AreEqual(0.6f, m_A.Opacity, Eps);
            Assert.AreEqual(MotionResult.Detached, h.Result);
        }

        [Test]
        public void DriverOwnerLeaving_HandsTheRunToAnotherTarget()
        {
            var driverA = new FakeDriver();
            var driverB = new FakeDriver();
            m_A.DriverToUse = driverA;
            m_B.DriverToUse = driverB;
            var h = UiMotion.Parallel(FadeTo(m_A, 0f, 100f), FadeTo(m_B, 0f, 100f)).Play(Manual);
            Assert.IsTrue(driverA.Running);
            driverA.Advance(40f);
            m_A.Detach();
            Assert.IsFalse(driverA.Running);
            Assert.IsTrue(driverB.Running, "the run continues on the next live target's driver");
            driverB.Advance(120f);
            Assert.AreEqual(MotionResult.Completed, h.Result);
        }

        [Test]
        public void NewRunOnTheSameProperty_TakesOverAndCancelsTheOlderOne()
        {
            var first = FadeTo(m_A, 0f, 100f).Play(Manual, m_Driver);
            m_Driver.Advance(50f);
            var second = FadeTo(m_A, 1f, 100f).Play(Manual, m_Driver);
            Assert.AreEqual(MotionResult.Cancelled, first.Result);
            Assert.AreEqual(0.5f, m_A.Opacity, Eps, "kept where it was");
            m_Driver.Advance(110f);
            Assert.AreEqual(MotionResult.Completed, second.Result);
            Assert.AreEqual(1f, m_A.Opacity, Eps);
        }

        [Test]
        public void NewRunOnAnotherProperty_LeavesTheOlderOneAlone()
        {
            var first = FadeTo(m_A, 0f, 100f).Play(Manual, m_Driver);
            var second = UiMotion.Tween(m_A, new MotionValues().WithScale(2f), MotionTiming.Ms(100f)).Play(Manual, m_Driver);
            Assert.IsTrue(first.IsRunning);
            Assert.IsTrue(second.IsRunning);
            first.Cancel();
            second.Cancel();
        }

        #endregion

        #region Transition fast path

        [Test]
        public void NamedEasingWithoutStartValue_RunsAsATransition_AfterTheWarmupFrames()
        {
            m_A.Transitions = true;
            var h = UiMotion.Tween(m_A, Op(0f), new MotionTiming(200f, MotionEasing.EaseOut)).Play(default, m_Driver);
            m_Driver.Advance(16f);
            m_Driver.Advance(16f);
            Assert.AreEqual(0, m_A.BeginCount, "a transition needs a frame between the start state and the end value");
            Assert.AreEqual(0, m_A.SetCount);
            m_Driver.Advance(16f);
            Assert.AreEqual(1, m_A.BeginCount);
            Assert.AreEqual(184f, m_A.LastTransitionMs, 1f);
            Assert.AreEqual(MotionEasing.EaseOut, m_A.LastEasing);
            Assert.AreEqual(0f, m_A.Opacity, Eps, "the end value is written once; the style system animates");
            var sets = m_A.SetCount;
            m_Driver.Advance(16f);
            m_Driver.Advance(16f);
            Assert.AreEqual(sets, m_A.SetCount, "no per-frame writes");
            m_Driver.Advance(300f);
            Assert.AreEqual(MotionResult.Completed, h.Result);
            Assert.IsFalse(m_A.InTransition, "the transition declaration is removed at the end");
        }

        [Test]
        public void TransitionTween_CancelKeep_PinsTheAnimatedValueAndRemovesTheDeclaration()
        {
            m_A.Transitions = true;
            var h = UiMotion.Tween(m_A, Op(0f), MotionTiming.Ms(200f)).Play(default, m_Driver);
            for (var i = 0; i < 4; i++)
                m_Driver.Advance(16f);
            Assert.IsTrue(m_A.InTransition);
            m_A.Animated = new Vector4(0.4f, 0f, 0f, 0f);
            h.Cancel();
            Assert.IsFalse(m_A.InTransition);
            Assert.AreEqual(0.4f, m_A.Opacity, Eps);
        }

        [Test]
        public void TransitionTween_CancelRestore_PutsBackTheOriginalValue()
        {
            m_A.Transitions = true;
            var h = UiMotion.Tween(m_A, Op(0f), MotionTiming.Ms(200f)).Play(default, m_Driver);
            for (var i = 0; i < 4; i++)
                m_Driver.Advance(16f);
            m_A.Animated = new Vector4(0.4f, 0f, 0f, 0f);
            h.Cancel(MotionCancel.Restore);
            Assert.IsFalse(m_A.InTransition);
            Assert.AreEqual(1f, m_A.Opacity, Eps);
        }

        [Test]
        public void TransitionTween_CompleteEndsTheDeclarationAndWritesTheEnd()
        {
            m_A.Transitions = true;
            var h = UiMotion.Tween(m_A, Op(0f), MotionTiming.Ms(200f)).Play(default, m_Driver);
            for (var i = 0; i < 4; i++)
                m_Driver.Advance(16f);
            h.Complete();
            Assert.IsFalse(m_A.InTransition);
            Assert.AreEqual(0f, m_A.Opacity, Eps);
        }

        [Test]
        public void ManualMode_NeverUsesTransitions()
        {
            m_A.Transitions = true;
            var h = FadeTo(m_A, 0f, 100f).Play(Manual, m_Driver);
            m_Driver.Advance(30f);
            m_Driver.Advance(30f);
            Assert.AreEqual(0, m_A.BeginCount);
            Assert.Greater(m_A.SetCount, 1, "frame by frame");
            h.Cancel();
        }

        [Test]
        public void TargetWithoutTransitionSupport_RunsFrameByFrame()
        {
            m_A.Transitions = false;
            var h = UiMotion.Tween(m_A, Op(0f), MotionTiming.Ms(100f)).Play(default, m_Driver);
            m_Driver.Advance(50f);
            Assert.AreEqual(0.5f, m_A.Opacity, Eps);
            Assert.AreEqual(0, m_A.BeginCount);
            h.Cancel();
        }

        [Test]
        public void StartValueBezierAndLoop_FallBackToFrameByFrame()
        {
            m_A.Transitions = true;
            var withFrom = FadeFromTo(m_A, 0f, 1f, 100f).Play(default, m_Driver);
            m_Driver.Advance(30f);
            withFrom.Cancel();

            var bezier = UiMotion.Tween(m_A, Op(0f), new MotionTiming(100f, MotionEasing.CubicBezier(0.2f, 0f, 0.2f, 1f))).Play(default, m_Driver);
            m_Driver.Advance(30f);
            bezier.Cancel();

            var looped = UiMotion.Loop(FadeTo(m_A, 0f, 100f), 2).Play(default, m_Driver);
            m_Driver.Advance(30f);
            looped.Cancel();

            Assert.AreEqual(0, m_A.BeginCount);
        }

        #endregion

        #region Misuse

        [Test]
        public void AClipPlaysOnce_AndCannotBeReusedInAGroup()
        {
            var clip = FadeTo(m_A, 0f, 100f);
            var h = clip.Play(Manual, m_Driver);
            Assert.Throws<InvalidOperationException>(() => clip.Play(Manual, m_Driver));
            Assert.Throws<InvalidOperationException>(() => UiMotion.Sequence(clip));
            h.Cancel();

            var child = FadeTo(m_B, 0f, 100f);
            UiMotion.Sequence(child);
            Assert.Throws<InvalidOperationException>(() => UiMotion.Parallel(child));
            Assert.Throws<InvalidOperationException>(() => child.Play(Manual, m_Driver));
        }

        [Test]
        public void InvalidArguments_Throw()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MotionTiming(-1f, MotionEasing.Linear));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MotionTiming(float.NaN, MotionEasing.Linear));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MotionTiming(10f, MotionEasing.Linear, -5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => UiMotion.Wait(-1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => UiMotion.Loop(UiMotion.Wait(10f), 0));
            Assert.Throws<ArgumentException>(() => UiMotion.Loop(UiMotion.Wait(0f)), "an infinite loop of nothing would spin");
            Assert.Throws<ArgumentException>(() => UiMotion.Loop(UiMotion.Loop(UiMotion.Wait(10f))), "no loop inside a loop that never ends");
            Assert.Throws<ArgumentException>(() => UiMotion.Tween(m_A, new MotionValues(), MotionTiming.Ms(10f)), "no channel");
            Assert.Throws<ArgumentException>(() => UiMotion.Tween(m_A, Op(1f), MotionTiming.Ms(10f), new MotionValues().WithScale(1f)),
                "the start values name a channel the end values lack");
            Assert.Throws<ArgumentOutOfRangeException>(() => UiMotion.Stagger(new MotionClip[0], -1f));
            Assert.Throws<ArgumentNullException>(() => UiMotion.Sequence(new MotionClip[] { null }));
        }

        [Test]
        public void ClipWithoutAnyDrivableTarget_NeedsAnExplicitDriver()
        {
            Assert.Throws<InvalidOperationException>(() => UiMotion.Wait(10f).Play());
            var h = UiMotion.Wait(10f).Play(default, m_Driver);
            m_Driver.Advance(20f);
            Assert.AreEqual(MotionResult.Completed, h.Result);
        }

        [Test]
        public void TargetWithoutADriver_ThrowsAClearError()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => FadeTo(m_A, 0f, 100f).Play(Manual));
            StringAssert.Contains("driver", ex.Message);
        }

        #endregion
    }
}
