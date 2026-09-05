using System;
using UnityEngine;

namespace Cuvara.UIToolkit.Flow
{
    /// <summary>
    /// Data model for common game settings. Loads from and saves to PlayerPrefs.
    /// </summary>
    public sealed class SettingsModel
    {
        private const string KeyMasterVolume = "settings.master_volume";
        private const string KeyMusicVolume = "settings.music_volume";
        private const string KeySfxVolume = "settings.sfx_volume";
        private const string KeyGraphicsQuality = "settings.graphics_quality";
        private const string KeyFullscreen = "settings.fullscreen";
        private const string KeyVSync = "settings.vsync";
        private const string KeyLanguage = "settings.language";

        /// <summary>Raised when any setting changes.</summary>
        public event Action Changed;

        /// <summary>Master volume 0–1.</summary>
        public float MasterVolume { get; private set; }

        /// <summary>Music volume 0–1.</summary>
        public float MusicVolume { get; private set; }

        /// <summary>SFX volume 0–1.</summary>
        public float SfxVolume { get; private set; }

        /// <summary>Graphics quality index (0=Low, 1=Medium, 2=High, 3=Ultra).</summary>
        public int GraphicsQuality { get; private set; }

        /// <summary>Fullscreen mode.</summary>
        public bool Fullscreen { get; private set; }

        /// <summary>VSync enabled.</summary>
        public bool VSync { get; private set; }

        /// <summary>Language code (e.g. "en", "vi").</summary>
        public string Language { get; private set; }

        /// <summary>Loads all settings from PlayerPrefs with defaults.</summary>
        public void Load()
        {
            MasterVolume = PlayerPrefs.GetFloat(KeyMasterVolume, 1f);
            MusicVolume = PlayerPrefs.GetFloat(KeyMusicVolume, 0.8f);
            SfxVolume = PlayerPrefs.GetFloat(KeySfxVolume, 1f);
            GraphicsQuality = PlayerPrefs.GetInt(KeyGraphicsQuality, 2);
            Fullscreen = PlayerPrefs.GetInt(KeyFullscreen, 1) == 1;
            VSync = PlayerPrefs.GetInt(KeyVSync, 1) == 1;
            Language = PlayerPrefs.GetString(KeyLanguage, "en");
        }

        /// <summary>Saves all settings to PlayerPrefs.</summary>
        public void Save()
        {
            PlayerPrefs.SetFloat(KeyMasterVolume, MasterVolume);
            PlayerPrefs.SetFloat(KeyMusicVolume, MusicVolume);
            PlayerPrefs.SetFloat(KeySfxVolume, SfxVolume);
            PlayerPrefs.SetInt(KeyGraphicsQuality, GraphicsQuality);
            PlayerPrefs.SetInt(KeyFullscreen, Fullscreen ? 1 : 0);
            PlayerPrefs.SetInt(KeyVSync, VSync ? 1 : 0);
            PlayerPrefs.SetString(KeyLanguage, Language);
            PlayerPrefs.Save();
        }

        public void SetMasterVolume(float v) { MasterVolume = Mathf.Clamp01(v); Changed?.Invoke(); }
        public void SetMusicVolume(float v) { MusicVolume = Mathf.Clamp01(v); Changed?.Invoke(); }
        public void SetSfxVolume(float v) { SfxVolume = Mathf.Clamp01(v); Changed?.Invoke(); }
        public void SetGraphicsQuality(int q) { GraphicsQuality = Mathf.Clamp(q, 0, 3); Changed?.Invoke(); }
        public void SetFullscreen(bool f) { Fullscreen = f; Changed?.Invoke(); }
        public void SetVSync(bool v) { VSync = v; Changed?.Invoke(); }
        public void SetLanguage(string lang) { Language = lang ?? "en"; Changed?.Invoke(); }

        /// <summary>Applies graphics settings to Unity runtime.</summary>
        public void Apply()
        {
            // AudioListener requires com.unity.modules.audio — guard for standalone installs
            var listenerType = System.Type.GetType("UnityEngine.AudioListener, UnityEngine.AudioModule");
            if (listenerType != null)
            {
                var volumeProp = listenerType.GetProperty("volume", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                volumeProp?.SetValue(null, MasterVolume);
            }

            QualitySettings.SetQualityLevel(GraphicsQuality, true);
            Screen.fullScreen = Fullscreen;
            QualitySettings.vSyncCount = VSync ? 1 : 0;
        }
    }
}
