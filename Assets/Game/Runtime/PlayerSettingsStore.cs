using UnityEngine;

namespace LocalTanks
{
    /// <summary>
    /// Player-facing settings that survive a restart. Values are stored through <see cref="PlayerPrefs"/>,
    /// which is the standard place for user preferences and needs no file handling or write permissions.
    /// </summary>
    /// <remarks>
    /// The three channels follow the audio buses the project documents in `Docs/WOT_MEDIA_AUDIT.md`:
    /// `Master` scales everything, `Tank` covers engine, tracks and turret mechanism, and `Effects`
    /// covers weapons, hits and destruction. The panel exposes one slider per channel.
    /// </remarks>
    public static class PlayerSettingsStore
    {
        public const string MasterVolumeKey = "localtanks.audio.masterVolume";
        public const string TankVolumeKey = "localtanks.audio.tankVolume";
        public const string EffectsVolumeKey = "localtanks.audio.effectsVolume";

        public const float DefaultVolume = 1f;

        /// <summary>Raised whenever a value changes, so live audio can follow the sliders.</summary>
        public static event System.Action Changed;

        public static float MasterVolume
        {
            get => Get(MasterVolumeKey);
            set => Set(MasterVolumeKey, value);
        }

        public static float TankVolume
        {
            get => Get(TankVolumeKey);
            set => Set(TankVolumeKey, value);
        }

        public static float EffectsVolume
        {
            get => Get(EffectsVolumeKey);
            set => Set(EffectsVolumeKey, value);
        }

        /// <summary>Multiplier for engine, tracks and turret mechanism sources.</summary>
        public static float TankMultiplier => MasterVolume * TankVolume;

        /// <summary>Multiplier for weapon, hit and destruction sources.</summary>
        public static float EffectsMultiplier => MasterVolume * EffectsVolume;

        public static void ResetToDefaults()
        {
            MasterVolume = DefaultVolume;
            TankVolume = DefaultVolume;
            EffectsVolume = DefaultVolume;
        }

        private static float Get(string key)
        {
            return Mathf.Clamp01(PlayerPrefs.GetFloat(key, DefaultVolume));
        }

        private static void Set(string key, float value)
        {
            float clamped = Mathf.Clamp01(value);
            if (Mathf.Approximately(PlayerPrefs.GetFloat(key, DefaultVolume), clamped))
            {
                return;
            }

            PlayerPrefs.SetFloat(key, clamped);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
