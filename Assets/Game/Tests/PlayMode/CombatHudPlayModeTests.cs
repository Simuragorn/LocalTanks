using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LocalTanks.Tests
{
    public sealed class CombatHudPlayModeTests
    {
        [UnityTest]
        public IEnumerator ManualReload_UsesTheFullDurationAndCannotRestartWhileActive()
        {
            TankDefinition tank = ScriptableObject.CreateInstance<TankDefinition>();
            WeaponDefinition weaponDefinition = ScriptableObject.CreateInstance<WeaponDefinition>();
            ShellDefinition shell = ScriptableObject.CreateInstance<ShellDefinition>();
            shell.damage = 10;
            shell.penetration = 100f;
            shell.speed = 10f;
            shell.radius = 0.01f;
            shell.lifetimeSeconds = 1f;
            shell.maximumRange = 10f;
            weaponDefinition.shell = shell;
            weaponDefinition.magazineSize = 3;
            weaponDefinition.reloadSeconds = 0.08f;
            weaponDefinition.shotIntervalSeconds = 0f;
            tank.weapon = weaponDefinition;

            GameObject projectileObject = new GameObject("ProjectilePrefab");
            Projectile2D projectile = projectileObject.AddComponent<Projectile2D>();
            GameObject owner = new GameObject("WeaponOwner");
            GameObject muzzleObject = new GameObject("Muzzle");
            muzzleObject.transform.SetParent(owner.transform, false);
            WeaponController weapon = owner.AddComponent<WeaponController>();
            weapon.Configure(tank, projectile, muzzleObject.transform);
            int reloadEvents = 0;
            weapon.ReloadStarted += _ => reloadEvents++;

            Assert.That(weapon.TryFire(), Is.True);
            Assert.That(weapon.RoundsRemaining, Is.EqualTo(2));
            Assert.That(weapon.TryStartReload(), Is.True);
            Assert.That(weapon.RoundsRemaining, Is.Zero);
            Assert.That(weapon.IsReloading, Is.True);
            Assert.That(weapon.RemainingReloadSeconds, Is.EqualTo(weapon.EffectiveReloadSeconds).Within(0.001f));
            Assert.That(weapon.TryStartReload(), Is.False);
            Assert.That(reloadEvents, Is.EqualTo(1));

            yield return new WaitForSeconds(0.1f);

            Assert.That(weapon.IsReloading, Is.False);
            Assert.That(weapon.RoundsRemaining, Is.EqualTo(3));
            Assert.That(weapon.IsReady, Is.True);

            foreach (Projectile2D spawned in Object.FindObjectsByType<Projectile2D>())
            {
                Object.Destroy(spawned.gameObject);
            }
            Object.Destroy(owner);
            Object.Destroy(tank);
            Object.Destroy(weaponDefinition);
            Object.Destroy(shell);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ImpactObservation_ReportsOnlyActuallyAppliedDamage()
        {
            TankDefinition definition = ScriptableObject.CreateInstance<TankDefinition>();
            definition.maxHitPoints = 25;
            definition.armor = new ArmorProfile(1f, 1f, 1f, 1f);
            GameObject target = new GameObject("DamageTarget");
            TankHealth health = target.AddComponent<TankHealth>();
            health.Configure(definition);
            TankArmor armor = target.AddComponent<TankArmor>();
            armor.Configure(definition, health);
            ArmorImpactObservation observed = default;
            armor.ImpactResolved += (_, observation) => observed = observation;

            ImpactResult result = armor.ResolveImpact(
                Vector2.up,
                target.transform.position,
                Vector2.down,
                1000f,
                10f,
                40,
                70f,
                0.7f,
                0.65f,
                1,
                attackerTeam: TeamId.TeamB);

            Assert.That(result.Outcome, Is.EqualTo(ImpactOutcome.Penetrated));
            Assert.That(health.CurrentHitPoints, Is.Zero);
            Assert.That(observed.Damage, Is.EqualTo(25),
                "A popup must show the applied 25 HP, not the requested 40 damage.");
            Assert.That(observed.AttackerTeam, Is.EqualTo(TeamId.TeamB),
                "The popup must classify the hit from the attacker's team, not from the local player's roster.");

            Object.Destroy(target);
            Object.Destroy(definition);
            yield return null;
        }
    }
}
