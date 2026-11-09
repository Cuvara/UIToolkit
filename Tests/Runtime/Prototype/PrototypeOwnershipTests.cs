#if UNITY_EDITOR
using System.Collections;
using Cuvara.UIToolkit.Prototype;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Cuvara.UIToolkit.Prototype.Tests
{
    /// <summary>Resource ownership of <see cref="PrototypeRunner"/>.</summary>
    public class PrototypeOwnershipTests
    {
        GameObject m_Object;
        PrototypeRunner m_Runner;
        PanelSettings m_Shared;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            m_Object = new GameObject("OwnershipTest");
            m_Object.AddComponent<UIDocument>();
            m_Runner = m_Object.AddComponent<PrototypeRunner>();
            m_Runner.AutoStart = false;
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            if (m_Object != null)
                Object.DestroyImmediate(m_Object);
            if (m_Shared != null)
                Object.DestroyImmediate(m_Shared);
        }

        [Test]
        public void ARepeatedUseRenderTargetReleasesThePreviousTarget()
        {
            var first = m_Runner.UseRenderTarget(320, 180);
            Assert.IsTrue(first.IsCreated());
            var second = m_Runner.UseRenderTarget(640, 360);
            Assert.IsTrue(first == null, "the previous target is destroyed, not leaked");
            Assert.IsTrue(second.IsCreated());
            Assert.AreSame(second, m_Runner.RenderTarget);
            Assert.AreSame(second, m_Runner.Document.panelSettings.targetTexture);
        }

        [Test]
        public void ASharedPanelSettingsAssetIsNeverWrittenTo()
        {
            m_Shared = PrototypeRunner.CreateDefaultPanelSettings();
            m_Runner.PanelSettingsAsset = m_Shared;
            var target = m_Runner.UseRenderTarget(320, 180);
            Assert.IsNull(m_Shared.targetTexture, "the shared asset keeps no render target");
            Assert.AreNotSame(m_Shared, m_Runner.Document.panelSettings, "the document renders through a copy");
            Assert.AreSame(target, m_Runner.Document.panelSettings.targetTexture);
            Assert.AreSame(m_Runner.OwnedPanelSettings, m_Runner.Document.panelSettings);
        }

        [Test]
        public void DestroyingTheRunnerDestroysTheTargetAndTheSettingsItOwns()
        {
            m_Shared = PrototypeRunner.CreateDefaultPanelSettings();
            m_Runner.PanelSettingsAsset = m_Shared;
            var target = m_Runner.UseRenderTarget(320, 180);
            var owned = m_Runner.OwnedPanelSettings;
            Assert.IsNotNull(owned);
            Object.DestroyImmediate(m_Object);
            m_Object = null;
            Assert.IsTrue(target == null, "the render target is gone");
            Assert.IsTrue(owned == null, "the owned PanelSettings copy is gone");
            Assert.IsNotNull(m_Shared, "the shared asset is not destroyed");
            Assert.IsNull(m_Shared.targetTexture);
        }

        [Test]
        public void TheFallbackPanelSettingsIsOwnedAndDestroyedToo()
        {
            m_Runner.UseRenderTarget(320, 180);
            var owned = m_Runner.OwnedPanelSettings;
            Assert.IsNotNull(owned, "a runner without an asset creates and owns its PanelSettings");
            Assert.AreSame(owned, m_Runner.Document.panelSettings);
            Object.DestroyImmediate(m_Object);
            m_Object = null;
            Assert.IsTrue(owned == null);
        }
    }
}
#endif
