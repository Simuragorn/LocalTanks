using UnityEngine;

namespace LocalTanks
{
    /// <summary>
    /// Player-facing settings that survive a restart. Values are stored through <see cref="PlayerPrefs"/>,
    /// which is the standard place for user preferences and needs no file handling or write permissions.
    /// </summary>
    /// <remarks>
    /// The channels follow the audio buses the project documents in `Docs/WOT_MEDIA_AUDIT.md`:
    /// `Master` scales everything, `Tank` covers engine, tracks and turret mechanism, `Effects` covers
    /// weapons, hits and destruction, `Music` covers the arena music and ambience, and `Voice` covers
    /// radio lines and battle signals. The panel exposes one slider per channel. Music and voice are
    /// separate from effects on purpose: a player who silences the guns must still be able to hear the
    /// capture warning, and a player who wants a quiet battle must be able to silence the music alone.
    /// </remarks>
    public static class PlayerSettingsStore
    {
        public const string MasterVolumeKey = "localtanks.audio.masterVolume";
        public const string TankVolumeKey = "localtanks.audio.tankVolume";
        public const string EffectsVolumeKey = "localtanks.audio.effectsVolume";
        public const string MusicVolumeKey = "localtanks.audio.musicVolume";
        public const string VoiceVolumeKey = "localtanks.audio.voiceVolume";

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

        public static float MusicVolume
        {
            get => Get(MusicVolumeKey);
            set => Set(MusicVolumeKey, value);
        }

        public static float VoiceVolume
        {
            get => Get(VoiceVolumeKey);
            set => Set(VoiceVolumeKey, value);
        }

        /// <summary>Multiplier for engine, tracks and turret mechanism sources.</summary>
        public static float TankMultiplier => MasterVolume * TankVolume;

        /// <summary>Multiplier for weapon, hit and destruction sources.</summary>
        public static float EffectsMultiplier => MasterVolume * EffectsVolume;

        /// <summary>Multiplier for arena music and ambience.</summary>
        public static float MusicMultiplier => MasterVolume * MusicVolume;

        /// <summary>Multiplier for radio voice lines and battle signals.</summary>
        public static float VoiceMultiplier => MasterVolume * VoiceVolume;

        public static void ResetToDefaults()
        {
            MasterVolume = DefaultVolume;
            TankVolume = DefaultVolume;
            EffectsVolume = DefaultVolume;
            MusicVolume = DefaultVolume;
            VoiceVolume = DefaultVolume;
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
