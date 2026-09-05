using Cuvara.UIToolkit.Flow;
using NUnit.Framework;

namespace Cuvara.UIToolkit.Tests.Flow
{
    public sealed class SettingsModelTests
    {
        private SettingsModel _model;

        [SetUp]
        public void SetUp()
        {
            _model = new SettingsModel();
            _model.Load();
        }

        [Test]
        public void Load_SetsDefaults()
        {
            Assert.AreEqual(1f, _model.MasterVolume, 0.01f);
            Assert.AreEqual(0.8f, _model.MusicVolume, 0.01f);
            Assert.AreEqual(1f, _model.SfxVolume, 0.01f);
            Assert.AreEqual(2, _model.GraphicsQuality);
            Assert.IsTrue(_model.Fullscreen);
            Assert.IsTrue(_model.VSync);
            Assert.AreEqual("en", _model.Language);
        }

        [Test]
        public void SetMasterVolume_Clamped()
        {
            _model.SetMasterVolume(1.5f);
            Assert.AreEqual(1f, _model.MasterVolume, 0.01f);

            _model.SetMasterVolume(-0.5f);
            Assert.AreEqual(0f, _model.MasterVolume, 0.01f);
        }

        [Test]
        public void SetGraphicsQuality_Clamped()
        {
            _model.SetGraphicsQuality(10);
            Assert.AreEqual(3, _model.GraphicsQuality);

            _model.SetGraphicsQuality(-1);
            Assert.AreEqual(0, _model.GraphicsQuality);
        }

        [Test]
        public void Changed_Fires()
        {
            bool fired = false;
            _model.Changed += () => fired = true;
            _model.SetMasterVolume(0.5f);
            Assert.IsTrue(fired);
        }

        [Test]
        public void SetLanguage_NullBecomesEn()
        {
            _model.SetLanguage(null);
            Assert.AreEqual("en", _model.Language);
        }

        [Test]
        public void SetLanguage_Stores()
        {
            _model.SetLanguage("vi");
            Assert.AreEqual("vi", _model.Language);
        }
    }
}
