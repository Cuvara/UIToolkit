using Cuvara.UIToolkit.Flow;
using NUnit.Framework;

namespace Cuvara.UIToolkit.Tests.Flow
{
    public sealed class LoadingProgressTrackerTests
    {
        private LoadingProgressTracker _tracker;

        [SetUp]
        public void SetUp() => _tracker = new LoadingProgressTracker();

        [Test]
        public void Empty_TotalProgressIsZero()
        {
            Assert.AreEqual(0f, _tracker.TotalProgress, 0.001f);
        }

        [Test]
        public void SingleStep_ProgressTracksDirectly()
        {
            _tracker.AddStep("a", "Loading A");
            _tracker.SetProgress("a", 0.5f);
            Assert.AreEqual(0.5f, _tracker.TotalProgress, 0.001f);
        }

        [Test]
        public void TwoEqualSteps_AverageProgress()
        {
            _tracker.AddStep("a", "A");
            _tracker.AddStep("b", "B");
            _tracker.SetProgress("a", 1f);
            _tracker.SetProgress("b", 0f);
            Assert.AreEqual(0.5f, _tracker.TotalProgress, 0.001f);
        }

        [Test]
        public void WeightedSteps_CorrectTotal()
        {
            _tracker.AddStep("assets", "Assets", weight: 3);
            _tracker.AddStep("connect", "Connect", weight: 1);
            // assets=100%, connect=0% → total = 3/4 = 0.75
            _tracker.SetProgress("assets", 1f);
            Assert.AreEqual(0.75f, _tracker.TotalProgress, 0.001f);
        }

        [Test]
        public void CompleteStep_SetsTo1()
        {
            _tracker.AddStep("a", "A");
            _tracker.CompleteStep("a");
            Assert.AreEqual(1f, _tracker.GetStepProgress("a"), 0.001f);
        }

        [Test]
        public void IsComplete_AllStepsDone()
        {
            _tracker.AddStep("a", "A");
            _tracker.AddStep("b", "B");
            Assert.IsFalse(_tracker.IsComplete);

            _tracker.CompleteStep("a");
            Assert.IsFalse(_tracker.IsComplete);

            _tracker.CompleteStep("b");
            Assert.IsTrue(_tracker.IsComplete);
        }

        [Test]
        public void CurrentStepLabel_FirstIncomplete()
        {
            _tracker.AddStep("a", "Loading A");
            _tracker.AddStep("b", "Loading B");
            Assert.AreEqual("Loading A", _tracker.CurrentStepLabel);

            _tracker.CompleteStep("a");
            Assert.AreEqual("Loading B", _tracker.CurrentStepLabel);
        }

        [Test]
        public void ProgressChanged_Fires()
        {
            float received = -1f;
            _tracker.ProgressChanged += p => received = p;

            _tracker.AddStep("a", "A");
            _tracker.SetProgress("a", 0.5f);

            Assert.AreEqual(0.5f, received, 0.001f);
        }

        [Test]
        public void Completed_FiresWhenAllDone()
        {
            bool completed = false;
            _tracker.Completed += () => completed = true;

            _tracker.AddStep("a", "A");
            _tracker.AddStep("b", "B");
            _tracker.CompleteStep("a");
            Assert.IsFalse(completed);

            _tracker.CompleteStep("b");
            Assert.IsTrue(completed);
        }

        [Test]
        public void Reset_SetsAllToZero()
        {
            _tracker.AddStep("a", "A");
            _tracker.CompleteStep("a");
            _tracker.Reset();
            Assert.AreEqual(0f, _tracker.TotalProgress, 0.001f);
            Assert.AreEqual(1, _tracker.StepCount);
        }

        [Test]
        public void Clear_RemovesAllSteps()
        {
            _tracker.AddStep("a", "A");
            _tracker.Clear();
            Assert.AreEqual(0, _tracker.StepCount);
        }

        [Test]
        public void GetStepProgress_UnknownId_ReturnsNegative()
        {
            Assert.AreEqual(-1f, _tracker.GetStepProgress("unknown"), 0.001f);
        }

        [Test]
        public void SetProgress_Clamped()
        {
            _tracker.AddStep("a", "A");
            _tracker.SetProgress("a", 2f);
            Assert.AreEqual(1f, _tracker.GetStepProgress("a"), 0.001f);

            _tracker.SetProgress("a", -1f);
            Assert.AreEqual(0f, _tracker.GetStepProgress("a"), 0.001f);
        }
    }
}
