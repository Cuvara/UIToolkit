using System;
using System.Collections.Generic;

namespace Cuvara.UIToolkit.Flow
{
    /// <summary>
    /// Accumulates named loading steps with progress and weight, reporting total progress 0–1.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A loading screen needs to show: "Loading assets... 45%", "Connecting to server... 60%",
    /// "Fetching content... 80%". Each step has different duration and importance. This tracker
    /// lets the caller register weighted steps and update each independently; the total is
    /// computed from the weighted sum.
    /// </para>
    /// <para>
    /// <b>Thread-safe: no.</b> Drive it from the main thread, same as everything that touches UI.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var tracker = new LoadingProgressTracker();
    /// tracker.AddStep("assets", "Loading assets...", weight: 3);
    /// tracker.AddStep("connect", "Connecting to server...", weight: 1);
    /// tracker.AddStep("content", "Fetching game content...", weight: 2);
    ///
    /// // From asset loader callback:
    /// tracker.SetProgress("assets", 0.5f);  // Total ≈ 25%
    ///
    /// // From connection callback:
    /// tracker.SetProgress("connect", 1f);   // Total ≈ 41%
    ///
    /// // UI reads:
    /// float total = tracker.TotalProgress;   // 0.0 → 1.0
    /// string label = tracker.CurrentStepLabel; // "Loading assets..."
    /// </code>
    /// </example>
    public sealed class LoadingProgressTracker
    {
        private readonly List<LoadingStep> _steps = new();
        private float _totalWeight;

        /// <summary>Raised when any step's progress changes.</summary>
        public event Action<float> ProgressChanged;

        /// <summary>Raised when all steps reach 1.0.</summary>
        public event Action Completed;

        /// <summary>Total progress 0–1, weighted across all steps.</summary>
        public float TotalProgress
        {
            get
            {
                if (_totalWeight <= 0f) return 0f;
                float sum = 0f;
                foreach (var step in _steps) sum += step.Progress * step.Weight;
                return sum / _totalWeight;
            }
        }

        /// <summary>Whether all steps have reached 1.0.</summary>
        public bool IsComplete
        {
            get
            {
                foreach (var step in _steps)
                    if (step.Progress < 1f) return false;
                return _steps.Count > 0;
            }
        }

        /// <summary>Label of the first incomplete step, or the last step's label if all complete.</summary>
        public string CurrentStepLabel
        {
            get
            {
                foreach (var step in _steps)
                    if (step.Progress < 1f) return step.Label;
                return _steps.Count > 0 ? _steps[_steps.Count - 1].Label : "";
            }
        }

        /// <summary>Number of registered steps.</summary>
        public int StepCount => _steps.Count;

        /// <summary>
        /// Registers a loading step.
        /// </summary>
        /// <param name="id">Unique identifier for this step.</param>
        /// <param name="label">Human-readable label shown on the loading screen.</param>
        /// <param name="weight">
        /// Relative weight in the total progress. A step with weight 3 contributes 3x as much
        /// as a step with weight 1.
        /// </param>
        public void AddStep(string id, string label, float weight = 1f)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Id cannot be empty.", nameof(id));
            weight = Math.Max(0.01f, weight);

            _steps.Add(new LoadingStep(id, label, weight));
            _totalWeight += weight;
        }

        /// <summary>
        /// Updates a step's progress. Clamped to 0–1.
        /// </summary>
        public void SetProgress(string id, float progress)
        {
            progress = Math.Clamp(progress, 0f, 1f);

            for (int i = 0; i < _steps.Count; i++)
            {
                if (_steps[i].Id == id)
                {
                    var step = _steps[i];
                    step.Progress = progress;
                    _steps[i] = step;

                    var total = TotalProgress;
                    ProgressChanged?.Invoke(total);

                    if (IsComplete) Completed?.Invoke();
                    return;
                }
            }
        }

        /// <summary>Marks a step as complete (progress = 1.0).</summary>
        public void CompleteStep(string id) => SetProgress(id, 1f);

        /// <summary>Gets the progress of a specific step. -1 if not found.</summary>
        public float GetStepProgress(string id)
        {
            foreach (var step in _steps)
                if (step.Id == id) return step.Progress;
            return -1f;
        }

        /// <summary>Resets all steps to 0 progress.</summary>
        public void Reset()
        {
            for (int i = 0; i < _steps.Count; i++)
            {
                var step = _steps[i];
                step.Progress = 0f;
                _steps[i] = step;
            }
        }

        /// <summary>Removes all steps.</summary>
        public void Clear()
        {
            _steps.Clear();
            _totalWeight = 0f;
        }

        private struct LoadingStep
        {
            public readonly string Id;
            public readonly string Label;
            public readonly float Weight;
            public float Progress;

            public LoadingStep(string id, string label, float weight)
            {
                Id = id;
                Label = label;
                Weight = weight;
                Progress = 0f;
            }
        }
    }
}
