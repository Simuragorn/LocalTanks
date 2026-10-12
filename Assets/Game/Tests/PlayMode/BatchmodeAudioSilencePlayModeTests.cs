using System.Collections;
using LocalTanks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LocalTanks.Tests
{
    /// <summary>
    /// The automated suites run headless, and a build machine must stay silent: audio clips are
    /// usually absent there and nobody expects a test run to play engine or gun sounds. The
    /// simulation itself is unchanged, because every audio path already tolerates a missing clip.
    /// </summary>
    public sealed class BatchmodeAudioSilencePlayModeTests
    {
        [UnityTest]
        public IEnumerator ResourcesProvider_ReturnsNoClipInBatchmode()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("This guarantee only applies to a batchmode run.");
            }

            ResourcesTankAudioClipProvider provider = new ResourcesTankAudioClipProvider();

            Assert.That(provider.Load("engine_eng_gaz_aa_pc"), Is.Null,
                "A batchmode run must not resolve local WoT clips.");
            Assert.That(provider.Load("gun_wpn_small_pc"), Is.Null);

            yield return null;
        }

        [UnityTest]
        public IEnumerator CombatAudioEmitter_CreatesNothingInBatchmode()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("This guarantee only applies to a batchmode run.");
            }

            CombatAudioEmitter.ResetClipProvider();
            int before = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Length;

            CombatAudioEmitter.PlayImpact(null, ImpactOutcome.Penetrated, Vector3.zero);
            CombatAudioEmitter.PlayAtPoint("impact_small_pierced", Vector3.zero, 0.3f);

            yield return null;
            int after = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Length;

            Assert.That(after, Is.EqualTo(before),
                "A batchmode impact must not create an audio source.");
        }

        [UnityTest]
        public IEnumerator AudioController_LoadsNoClipsInBatchmode()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("This guarantee only applies to a batchmode run.");
            }

            TankDefinition definition = ScriptableObject.CreateInstance<TankDefinition>();
            definition.audio = new TankAudioProfile
            {
                engineClipId = "engine_eng_gaz_aa_pc",
                tracksClipId = "tracks_trds_med_npc",
                shotClipId = "gun_wpn_small_pc",
                reloadClipId = "reload_gun_rld_20_45mm",
                turretClipId = "turret_tur_man_trav_04",
                destructionClipId = "destruction_expl_crew_death_s_npc"
            };

            GameObject root = new GameObject("SilentAudioTank");
            try
            {
                root.AddComponent<Rigidbody2D>().gravityScale = 0f;
                TankMotor motor = root.AddComponent<TankMotor>();
                motor.Configure(definition);
                GameObject turretObject = new GameObject("Turret");
                turretObject.transform.SetParent(root.transform, false);
                TurretAiming turret = turretObject.AddComponent<TurretAiming>();
                turret.Configure(definition);
                GameObject muzzle = new GameObject("Muzzle");
                muzzle.transform.SetParent(turretObject.transform, false);
                WeaponController weapon = root.AddComponent<WeaponController>();
                weapon.Configure(definition, null, muzzle.transform);
                TankAudioController audio = root.AddComponent<TankAudioController>();

                audio.Configure(definition, motor, turret, weapon);

                Assert.That(audio.HasEngineClip, Is.False);
                Assert.That(audio.HasTracksClip, Is.False);
                Assert.That(audio.HasShotClip, Is.False);
            }
            finally
            {
                Object.Destroy(root);
                Object.Destroy(definition);
            }

            yield return null;
        }
    }
}
