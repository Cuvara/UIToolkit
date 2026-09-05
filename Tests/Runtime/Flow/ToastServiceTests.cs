using System;
using Cuvara.UIToolkit.Flow;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Cuvara.UIToolkit.Tests.Flow
{
    public sealed class ToastServiceTests
    {
        private VisualElement _root;
        private ToastService _service;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            _service = new ToastService(_root);
        }

        [TearDown]
        public void TearDown()
        {
            _service.Dispose();
        }

        [Test]
        public void Show_AddsToastToContainer()
        {
            _service.Show("Hello");
            Assert.AreEqual(1, _service.ActiveCount);
        }

        [Test]
        public void Show_Multiple_Stacks()
        {
            _service.Show("A");
            _service.Show("B");
            _service.Show("C");
            Assert.AreEqual(3, _service.ActiveCount);
        }

        [Test]
        public void Clear_RemovesAll()
        {
            _service.Show("A");
            _service.Show("B");
            _service.Clear();
            Assert.AreEqual(0, _service.ActiveCount);
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            _service.Dispose();
            _service.Dispose(); // no throw
        }

        [Test]
        public void ShowAfterDispose_DoesNotThrow()
        {
            _service.Dispose();
            Assert.DoesNotThrow(() => _service.Show("ignored"));
        }

        [Test]
        public void NullParent_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ToastService(null));
        }

        [Test]
        public void ToastType_AllValuesExist()
        {
            Assert.AreEqual(0, (int)ToastType.Info);
            Assert.AreEqual(1, (int)ToastType.Success);
            Assert.AreEqual(2, (int)ToastType.Warning);
            Assert.AreEqual(3, (int)ToastType.Error);
        }
    }
}
