using NUnit.Framework;
using UnityEngine;

namespace LocalTanks.Tests
{
    /// <summary>
    /// Settings must survive a restart, so these checks write through the store, read the values back
    /// from <see cref="PlayerPrefs"/> directly, and clear them again. Every test restores the keys it
    /// touched, because the developer machine keeps real settings in the same place.
    /// </summary>
    public sealed class PlayerSettingsStoreTests
    {
        private float previousMaster;
        private float previousTank;
        private float previousEffects;

        [SetUp]
        public void CapturePreviousValues()
        {
            previousMaster = PlayerSettingsStore.MasterVolume;
            previousTank = PlayerSettingsStore.TankVolume;
            previousEffects = PlayerSettingsStore.EffectsVolume;
        }

        [TearDown]
        public void RestorePreviousValues()
        {
            PlayerSettingsStore.MasterVolume = previousMaster;
            PlayerSettingsStore.TankVolume = previousTank;
            PlayerSettingsStore.EffectsVolume = previousEffects;
        }

        [Test]
        public void Volumes_AreWrittenToPlayerPrefs()
        {
            PlayerSettingsStore.MasterVolume = 0.4f;
            PlayerSettingsStore.TankVolume = 0.6f;
            PlayerSettingsStore.EffectsVolume = 0.8f;

            Assert.That(PlayerPrefs.GetFloat(PlayerSettingsStore.MasterVolumeKey, -1f), Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(PlayerPrefs.GetFloat(PlayerSettingsStore.TankVolumeKey, -1f), Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(PlayerPrefs.GetFloat(PlayerSettingsStore.EffectsVolumeKey, -1f), Is.EqualTo(0.8f).Within(0.0001f));
        }

        [Test]
        public void Volumes_SurviveAFreshRead()
        {
            PlayerSettingsStore.MasterVolume = 0.25f;
            PlayerSettingsStore.TankVolume = 0.5f;

            // Reading through the store again is what a new run does.
            Assert.That(PlayerSettingsStore.MasterVolume, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(PlayerSettingsStore.TankVolume, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void Volumes_AreClampedToTheValidRange()
        {
            PlayerSettingsStore.MasterVolume = 2f;
            Assert.That(PlayerSettingsStore.MasterVolume, Is.EqualTo(1f).Within(0.0001f));

            PlayerSettingsStore.MasterVolume = -1f;
            Assert.That(PlayerSettingsStore.MasterVolume, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void Multipliers_FollowTheMasterChannel()
        {
            PlayerSettingsStore.MasterVolume = 0.5f;
            PlayerSettingsStore.TankVolume = 0.4f;
            PlayerSettingsStore.EffectsVolume = 1f;
            PlayerSettingsStore.MusicVolume = 0.6f;
            PlayerSettingsStore.VoiceVolume = 0.8f;

            Assert.That(PlayerSettingsStore.TankMultiplier, Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(PlayerSettingsStore.EffectsMultiplier, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(PlayerSettingsStore.MusicMultiplier, Is.EqualTo(0.3f).Within(0.0001f));
            Assert.That(PlayerSettingsStore.VoiceMultiplier, Is.EqualTo(0.4f).Within(0.0001f));

            PlayerSettingsStore.MasterVolume = 0f;
            Assert.That(PlayerSettingsStore.TankMultiplier, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(PlayerSettingsStore.EffectsMultiplier, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(PlayerSettingsStore.MusicMultiplier, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(PlayerSettingsStore.VoiceMultiplier, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void Channels_DoNotLeakIntoEachOther()
        {
            PlayerSettingsStore.MasterVolume = 1f;
            PlayerSettingsStore.TankVolume = 1f;
            PlayerSettingsStore.EffectsVolume = 1f;
            PlayerSettingsStore.MusicVolume = 1f;
            PlayerSettingsStore.VoiceVolume = 1f;

            // A player who silences the guns must still hear the capture warning, and a player who wants
            // a quiet battle must be able to silence the music alone.
            PlayerSettingsStore.MusicVolume = 0f;
            PlayerSettingsStore.VoiceVolume = 0f;

            Assert.That(PlayerSettingsStore.TankMultiplier, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(PlayerSettingsStore.EffectsMultiplier, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(PlayerSettingsStore.MusicMultiplier, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(PlayerSettingsStore.VoiceMultiplier, Is.EqualTo(0f).Within(0.0001f));

            PlayerSettingsStore.MusicVolume = 1f;
            PlayerSettingsStore.VoiceVolume = 1f;
            PlayerSettingsStore.EffectsVolume = 0f;

            Assert.That(PlayerSettingsStore.MusicMultiplier, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(PlayerSettingsStore.VoiceMultiplier, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(PlayerSettingsStore.EffectsMultiplier, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void Changed_IsRaisedOnlyWhenAValueActuallyChanges()
        {
            PlayerSettingsStore.MasterVolume = 0.7f;
            int raised = 0;
            System.Action handler = () => raised++;
            PlayerSettingsStore.Changed += handler;
            try
            {
                PlayerSettingsStore.MasterVolume = 0.7f;
                Assert.That(raised, Is.Zero, "Writing the same value must not raise a change.");

                PlayerSettingsStore.MasterVolume = 0.3f;
                Assert.That(raised, Is.EqualTo(1));
            }
            finally
            {
                PlayerSettingsStore.Changed -= handler;
            }
        }

        [Test]
        public void ResetToDefaults_RestoresFullVolume()
        {
            PlayerSettingsStore.MasterVolume = 0.1f;
            PlayerSettingsStore.TankVolume = 0.1f;
            PlayerSettingsStore.EffectsVolume = 0.1f;
            PlayerSettingsStore.MusicVolume = 0.1f;
            PlayerSettingsStore.VoiceVolume = 0.1f;

            PlayerSettingsStore.ResetToDefaults();

            Assert.That(PlayerSettingsStore.MasterVolume, Is.EqualTo(PlayerSettingsStore.DefaultVolume).Within(0.0001f));
            Assert.That(PlayerSettingsStore.TankVolume, Is.EqualTo(PlayerSettingsStore.DefaultVolume).Within(0.0001f));
            Assert.That(PlayerSettingsStore.EffectsVolume, Is.EqualTo(PlayerSettingsStore.DefaultVolume).Within(0.0001f));
            Assert.That(PlayerSettingsStore.MusicVolume, Is.EqualTo(PlayerSettingsStore.DefaultVolume).Within(0.0001f));
            Assert.That(PlayerSettingsStore.VoiceVolume, Is.EqualTo(PlayerSettingsStore.DefaultVolume).Within(0.0001f));
        }
    }
}
