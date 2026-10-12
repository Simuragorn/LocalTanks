using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LocalTanks.Tests
{
    public sealed class TankAudioPlayModeTests
    {
        [UnityTest]
        public IEnumerator AudioController_LoadsLoopsAndReactsToWeaponFire()
        {
            AudioClip engineClip = AudioClip.Create("engine", 4800, 1, 48000, false);
            AudioClip shotClip = AudioClip.Create("shot", 2400, 1, 48000, false);
            AudioClip turretClip = AudioClip.Create("turret", 4800, 1, 48000, false);
            FakeClipProvider clips = new FakeClipProvider(new Dictionary<string, AudioClip>
            {
                ["engine_test"] = engineClip,
                ["shot_test"] = shotClip,
                ["turret_test"] = turretClip
            });

            TankDefinition definition = ScriptableObject.CreateInstance<TankDefinition>();
            WeaponDefinition weaponDefinition = ScriptableObject.CreateInstance<WeaponDefinition>();
            ShellDefinition shell = ScriptableObject.CreateInstance<ShellDefinition>();
            shell.speed = 5f;
            shell.lifetimeSeconds = 1f;
            shell.radius = 0.05f;
            weaponDefinition.reloadSeconds = 1f;
            weaponDefinition.shell = shell;
            definition.weapon = weaponDefinition;
            definition.maxForwardSpeed = 2f;
            definition.maxReverseSpeed = 1f;
            definition.acceleration = 5f;
            definition.groundResistance = 2f;
            definition.braking = 4f;
            definition.hullTurnSpeed = 20f;
            definition.turretTurnSpeed = 20f;
            definition.audio = new TankAudioProfile
            {
                engineClipId = "engine_test",
                shotClipId = "shot_test",
                turretClipId = "turret_test",
                engineVolume = 0.5f,
                shotVolume = 0.8f,
                turretVolume = 0.2f,
                enginePitch = 0.9f,
                shotPitch = 1.1f,
                turretPitch = 1f
            };

            GameObject root = new GameObject("AudioTank");
            root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            TankMotor motor = root.AddComponent<TankMotor>();
            motor.Configure(definition);
            TankHealth health = root.AddComponent<TankHealth>();
            health.Configure(definition);

            GameObject turretObject = new GameObject("Turret");
            turretObject.transform.SetParent(root.transform, false);
            TurretAiming turret = turretObject.AddComponent<TurretAiming>();
            turret.Configure(definition);
            GameObject muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(turretObject.transform, false);

            GameObject projectileObject = new GameObject("ProjectilePrefab");
            Projectile2D projectile = projectileObject.AddComponent<Projectile2D>();
            WeaponController weapon = root.AddComponent<WeaponController>();
            weapon.Configure(definition, projectile, muzzle.transform);

            TankAudioController audio = root.AddComponent<TankAudioController>();
            audio.Configure(definition, motor, turret, weapon, health, clips);
            yield return null;

            Assert.That(audio.HasEngineClip, Is.True);
            Assert.That(audio.HasShotClip, Is.True);
            Assert.That(audio.EngineSource.clip, Is.SameAs(engineClip));
            Assert.That(audio.TurretSource.clip, Is.SameAs(turretClip));
            Assert.That(audio.EngineSource.loop, Is.True);
            Assert.That(audio.WeaponSource.loop, Is.False);
            Assert.That(clips.LoadCounts["engine_test"], Is.EqualTo(1));
            Assert.That(clips.LoadCounts["shot_test"], Is.EqualTo(1));

            Assert.That(weapon.TryFire(), Is.True);
            Assert.That(audio.ShotPlayCount, Is.EqualTo(1));
            Assert.That(audio.WeaponSource.pitch, Is.EqualTo(1.1f).Within(0.001f));

            Object.Destroy(root);
            Object.Destroy(projectileObject);
            Object.Destroy(definition);
            Object.Destroy(weaponDefinition);
            Object.Destroy(shell);
            Object.Destroy(engineClip);
            Object.Destroy(shotClip);
            Object.Destroy(turretClip);
            yield return null;
        }

        private sealed class FakeClipProvider : ITankAudioClipProvider
        {
            private readonly IReadOnlyDictionary<string, AudioClip> clips;

            public FakeClipProvider(IReadOnlyDictionary<string, AudioClip> clips)
            {
                this.clips = clips;
            }

            public Dictionary<string, int> LoadCounts { get; } = new Dictionary<string, int>();

            public AudioClip Load(string clipId)
            {
                LoadCounts.TryGetValue(clipId, out int count);
                LoadCounts[clipId] = count + 1;
                return clips.TryGetValue(clipId, out AudioClip clip) ? clip : null;
            }
        }
    }
}
