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
        public IEnumerator WeaponMagazine_UsesShotIntervalThenReloadsWholeMagazine()
        {
            TankDefinition definition = ScriptableObject.CreateInstance<TankDefinition>();
            WeaponDefinition weaponDefinition = ScriptableObject.CreateInstance<WeaponDefinition>();
            ShellDefinition shell = ScriptableObject.CreateInstance<ShellDefinition>();
            weaponDefinition.reloadSeconds = 0.04f;
            weaponDefinition.magazineSize = 3;
            weaponDefinition.shotIntervalSeconds = 0.01f;
            weaponDefinition.shell = shell;
            definition.weapon = weaponDefinition;
            shell.speed = 1f;
            shell.radius = 0.01f;
            shell.lifetimeSeconds = 0.02f;
            shell.maximumRange = 1f;

            GameObject root = new GameObject("MagazineTank");
            GameObject muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(root.transform, false);
            GameObject projectileObject = new GameObject("ProjectilePrefab");
            Projectile2D projectile = projectileObject.AddComponent<Projectile2D>();
            WeaponController weapon = root.AddComponent<WeaponController>();
            weapon.Configure(definition, projectile, muzzle.transform);

            Assert.That(weapon.TryFire(), Is.True);
            Assert.That(weapon.RoundsRemaining, Is.EqualTo(2));
            Assert.That(weapon.TryFire(), Is.False);
            yield return new WaitForSeconds(0.02f);
            Assert.That(weapon.TryFire(), Is.True);
            yield return new WaitForSeconds(0.02f);
            Assert.That(weapon.TryFire(), Is.True);
            Assert.That(weapon.RoundsRemaining, Is.Zero);
            Assert.That(weapon.IsReady, Is.False);
            yield return new WaitForSeconds(0.06f);
            Assert.That(weapon.IsReady, Is.True);
            Assert.That(weapon.RoundsRemaining, Is.EqualTo(3));

            Object.Destroy(root);
            Object.Destroy(projectileObject);
            Object.Destroy(definition);
            Object.Destroy(weaponDefinition);
            Object.Destroy(shell);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AudioController_LoadsLoopsAndReactsToWeaponFire()
        {
            AudioClip engineClip = AudioClip.Create("engine", 4800, 1, 48000, false);
            AudioClip shotClip = AudioClip.Create("shot", 2400, 1, 48000, false);
            AudioClip turretClip = AudioClip.Create("turret", 4800, 1, 48000, false);
            AudioClip tracksClip = AudioClip.Create("tracks", 4800, 1, 48000, false);
            AudioClip reloadClip = AudioClip.Create("reload", 2400, 1, 48000, false);
            AudioClip destructionClip = AudioClip.Create("destruction", 2400, 1, 48000, false);
            FakeClipProvider clips = new FakeClipProvider(new Dictionary<string, AudioClip>
            {
                ["engine_test"] = engineClip,
                ["shot_test"] = shotClip,
                ["turret_test"] = turretClip,
                ["tracks_test"] = tracksClip,
                ["reload_test"] = reloadClip,
                ["destruction_test"] = destructionClip
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
                tracksClipId = "tracks_test",
                shotClipId = "shot_test",
                reloadClipId = "reload_test",
                turretClipId = "turret_test",
                destructionClipId = "destruction_test",
                engineVolume = 0.5f,
                tracksVolume = 0.25f,
                shotVolume = 0.8f,
                reloadVolume = 0.4f,
                turretVolume = 0.2f,
                destructionVolume = 0.7f,
                enginePitch = 0.9f,
                tracksPitch = 1f,
                shotPitch = 1.1f,
                reloadPitch = 1f,
                turretPitch = 1f,
                destructionPitch = 0.95f
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
            Assert.That(audio.HasTracksClip, Is.True);
            Assert.That(audio.EngineSource.clip, Is.SameAs(engineClip));
            Assert.That(audio.TracksSource.clip, Is.SameAs(tracksClip));
            Assert.That(audio.TurretSource.clip, Is.SameAs(turretClip));
            Assert.That(audio.EngineSource.loop, Is.True);
            Assert.That(audio.TracksSource.loop, Is.True);
            Assert.That(audio.WeaponSource.loop, Is.False);
            Assert.That(clips.LoadCounts["engine_test"], Is.EqualTo(1));
            Assert.That(clips.LoadCounts["shot_test"], Is.EqualTo(1));
            Assert.That(clips.LoadCounts["tracks_test"], Is.EqualTo(1));
            Assert.That(clips.LoadCounts["reload_test"], Is.EqualTo(1));
            Assert.That(clips.LoadCounts["destruction_test"], Is.EqualTo(1));

            Assert.That(weapon.TryFire(), Is.True);
            Assert.That(audio.ShotPlayCount, Is.EqualTo(1));
            Assert.That(audio.WeaponSource.pitch, Is.EqualTo(1.1f).Within(0.001f));

            health.ApplyDamage(definition.maxHitPoints);
            yield return null;
            Assert.That(health.IsDestroyed, Is.True);
            Assert.That(audio.DestructionPlayCount, Is.EqualTo(1));
            Assert.That(audio.DestructionSource.pitch, Is.EqualTo(0.95f).Within(0.001f));

            Object.Destroy(root);
            Object.Destroy(projectileObject);
            Object.Destroy(definition);
            Object.Destroy(weaponDefinition);
            Object.Destroy(shell);
            Object.Destroy(engineClip);
            Object.Destroy(shotClip);
            Object.Destroy(turretClip);
            Object.Destroy(tracksClip);
            Object.Destroy(reloadClip);
            Object.Destroy(destructionClip);
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
