namespace Cuvara.UIToolkit.Ecs.Tests
{
    using System;
    using System.Threading;
    using Cuvara.UIToolkit.Flow;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using Unity.Entities;

    /// <summary>
    /// Tests for <see cref="EcsAutoSinkBinder"/> — the auto-wiring of
    /// <see cref="IViewModelSink{T}"/> on presenters to their matching
    /// <see cref="EcsViewModelBridge{TComponent,TViewModel}"/>.
    /// </summary>
    public sealed class EcsAutoSinkBinderTests
    {
        private World _world;
        private FakeNavigator _navigator;
        private EcsAutoSinkBinder _binder;

        [SetUp]
        public void SetUp()
        {
            _world = new World("EcsAutoSinkBinderTests");
            _navigator = new FakeNavigator();
            _binder = new EcsAutoSinkBinder(_world);
        }

        [TearDown]
        public void TearDown()
        {
            _binder.Dispose();
            if (_world.IsCreated) _world.Dispose();
        }

        [Test]
        public void Attach_SubscribesToNavigatorEvents()
        {
            _binder.Attach(_navigator);
            Assert.IsTrue(_navigator.HasActivatedSubscribers);
            Assert.IsTrue(_navigator.HasDeactivatedSubscribers);
        }

        [Test]
        public void Detach_UnsubscribesFromNavigator()
        {
            _binder.Attach(_navigator);
            _binder.Detach();
            Assert.IsFalse(_navigator.HasActivatedSubscribers);
            Assert.IsFalse(_navigator.HasDeactivatedSubscribers);
        }

        [Test]
        public void DoubleAttach_Throws()
        {
            _binder.Attach(_navigator);
            Assert.Throws<InvalidOperationException>(() => _binder.Attach(_navigator));
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            _binder.Attach(_navigator);
            _binder.Dispose();
            _binder.Dispose(); // no throw
            Assert.IsFalse(_navigator.HasActivatedSubscribers);
        }

        [Test]
        public void AttachNull_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _binder.Attach(null));
        }

        [Test]
        public void ActivatePresenterWithNoSink_DoesNotThrow()
        {
            _binder.Attach(_navigator);
            // A presenter with no IViewModelSink<T> — nothing to bind
            var presenter = new EmptyPresenter();
            Assert.DoesNotThrow(() => _navigator.SimulateActivate(presenter));
        }

        [Test]
        public void DeactivateUnknownPresenter_DoesNotThrow()
        {
            _binder.Attach(_navigator);
            var presenter = new EmptyPresenter();
            Assert.DoesNotThrow(() => _navigator.SimulateDeactivate(presenter));
        }

        // ── Test doubles ──────────────────────────────────────────────────

        private sealed class EmptyPresenter : IUIToolkitScreenPresenter
        {
            public UniTask OnBindAsync(ScreenSubscriptions subs, CancellationToken ct)
                => UniTask.CompletedTask;
        }

        /// <summary>
        /// Minimal navigator that exposes events for testing without a real UI stack.
        /// </summary>
        private sealed class FakeNavigator : IScreenNavigator
        {
            public event Action<IUIToolkitScreenPresenter> ScreenActivated;
            public event Action<IUIToolkitScreenPresenter> ScreenDeactivated;

            public bool HasActivatedSubscribers => ScreenActivated != null;
            public bool HasDeactivatedSubscribers => ScreenDeactivated != null;

            public void SimulateActivate(IUIToolkitScreenPresenter p) => ScreenActivated?.Invoke(p);
            public void SimulateDeactivate(IUIToolkitScreenPresenter p) => ScreenDeactivated?.Invoke(p);

            // IScreenNavigator stub — only events matter for this test
            public int Depth => 0;
            public UniTask PushAsync(IUIToolkitScreenPresenter p, ScreenOptions o = default, CancellationToken ct = default) => UniTask.CompletedTask;
            public UniTask PopAsync(CancellationToken ct = default) => UniTask.CompletedTask;
            public UniTask PopToRootAsync(CancellationToken ct = default) => UniTask.CompletedTask;
            public UniTask ReplaceAsync(IUIToolkitScreenPresenter p, ScreenOptions o = default, CancellationToken ct = default) => UniTask.CompletedTask;
            public void Dispose() { }
        }
    }
}
