using System.Collections;
using System.Collections.Generic;
using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace LocalTanks.Tests
{
    public sealed class CombatRandomizationPlayModeTests
    {
        [UnityTest]
        public IEnumerator RepeatedShots_VaryButStayInsideAuthoredRangesAndKeepOneRollPerShot()
        {
            CombatRules rules = new CombatRules();
            ShotRandomStream stream = new ShotRandomStream(1701);
            TankDefinition definition = ScriptableObject.CreateInstance<TankDefinition>();
            definition.maxHitPoints = 10000;
            definition.armor = new ArmorProfile(100f, 100f, 100f, 100f);
            GameObject target = new GameObject("RandomizedArmorTarget");
            TankHealth health = target.AddComponent<TankHealth>();
            health.Configure(definition);
            TankArmor armor = target.AddComponent<TankArmor>();
            armor.Configure(definition, health);
            HashSet<int> appliedDamages = new HashSet<int>();
            HashSet<ImpactOutcome> outcomes = new HashSet<ImpactOutcome>();

            for (int index = 0; index < 64; index++)
            {
                ShotRandomization roll = stream.Next(rules);
                float penetration = CombatRandomMath.ApplyPenetration(100f, roll.PenetrationMultiplier);
                int damage = CombatRandomMath.ApplyDamage(100, roll.DamageMultiplier);
                ImpactResult result = armor.ResolveImpact(
                    Vector2.down,
                    target.transform.position,
                    Vector2.up,
                    penetration,
                    10f,
                    damage,
                    70f,
                    0.7f,
                    0.65f,
                    0,
                    100f,
                    rules);
                outcomes.Add(result.Outcome);
                if (armor.LastAppliedDamage > 0) appliedDamages.Add(armor.LastAppliedDamage);
                Assert.That(penetration, Is.InRange(75f, 125f));
                Assert.That(damage, Is.InRange(75, 125));
                Assert.That(result.PenetrationChance, Is.EqualTo(0.5f).Within(0.0001f));
            }

            Assert.That(outcomes, Does.Contain(ImpactOutcome.Penetrated));
            Assert.That(outcomes, Does.Contain(ImpactOutcome.Blocked));
            Assert.That(appliedDamages.Count, Is.GreaterThan(1));

            Object.Destroy(target);
            Object.Destroy(definition);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ProjectileCarriesTheOriginalRollInsteadOfRerollingOnInitialize()
        {
            ShellDefinition shell = ScriptableObject.CreateInstance<ShellDefinition>();
            shell.id = "randomized_test_shell";
            shell.damage = 100;
            shell.penetration = 100f;
            shell.speed = 1f;
            shell.radius = 0.01f;
            shell.lifetimeSeconds = 10f;
            shell.maximumRange = 10f;
            GameObject owner = new GameObject("RandomizedProjectileOwner");
            GameObject projectileObject = new GameObject("RandomizedProjectile");
            Projectile2D projectile = projectileObject.AddComponent<Projectile2D>();
            ShotRandomization roll = new ShotRandomization(7, 1.2f, 0.8f);

            projectile.Initialize(Vector2.up, shell, owner.transform, new MatchRules(), roll);

            Assert.That(projectile.Randomization.Sequence, Is.EqualTo(7));
            Assert.That(projectile.Randomization.PenetrationMultiplier, Is.EqualTo(1.2f));
            Assert.That(projectile.Penetration, Is.EqualTo(120f).Within(0.001f));

            Object.Destroy(projectileObject);
            Object.Destroy(owner);
            Object.Destroy(shell);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AimProbe_UsesPresentedArmorButNeverRevealsHiddenTank()
        {
            GameObject owner = new GameObject("AimProbeOwner");
            owner.transform.position = new Vector2(2000f, 2000f);
            owner.AddComponent<BoxCollider2D>().size = Vector2.one;
            GameObject target = new GameObject("AimProbeTarget");
            target.transform.position = new Vector2(2000f, 2004f);
            target.AddComponent<BoxCollider2D>().size = Vector2.one;
            TankDefinition definition = ScriptableObject.CreateInstance<TankDefinition>();
            definition.maxHitPoints = 100;
            definition.armor = new ArmorProfile(100f, 100f, 100f, 100f);
            TankHealth health = target.AddComponent<TankHealth>();
            health.Configure(definition);
            TankArmor armor = target.AddComponent<TankArmor>();
            armor.Configure(definition, health);
            TankVisibilityPresenter visibility = target.AddComponent<TankVisibilityPresenter>();
            visibility.SetVisible(true);
            Physics2D.SyncTransforms();

            bool visibleSolution = ArmorAimProbe.TryEvaluate(
                owner.transform,
                new Vector2(2000f, 2001f),
                target.transform.position,
                100f,
                new CombatRules(),
                out ArmorAimSolution solution);

            Assert.That(visibleSolution, Is.True);
            Assert.That(solution.Armor, Is.SameAs(armor));
            Assert.That(solution.PenetrationChance, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(ArmorAimProbe.TryEvaluateAlongDirection(
                owner.transform,
                target.transform,
                new Vector2(2000f, 2001f),
                Vector2.up,
                10f,
                100f,
                new CombatRules(),
                out _), Is.True);
            Assert.That(ArmorAimProbe.TryEvaluateAlongDirection(
                owner.transform,
                target.transform,
                new Vector2(2000f, 2001f),
                Vector2.right,
                10f,
                100f,
                new CombatRules(),
                out _), Is.False,
                "The forecast must follow the barrel instead of bending its ray toward the cursor target.");

            visibility.SetVisible(false);
            Assert.That(ArmorAimProbe.TryEvaluate(
                owner.transform,
                new Vector2(2000f, 2001f),
                target.transform.position,
                100f,
                new CombatRules(),
                out _), Is.False);

            Object.Destroy(owner);
            Object.Destroy(target);
            Object.Destroy(definition);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HoverOutline_UsesRelationAndDisappearsWithVisibilityOrDeath()
        {
            TankDefinition definition = ScriptableObject.CreateInstance<TankDefinition>();
            definition.maxHitPoints = 100;
            definition.armor = new ArmorProfile(25f, 20f, 20f, 15f);
            GameObject owner = CreateHoverTank("HoverOwner", new Vector2(2200f, 2200f), TeamId.TeamA, definition);
            GameObject ally = CreateHoverTank("HoverAlly", new Vector2(2200f, 2204f), TeamId.TeamA, definition);
            GameObject enemy = CreateHoverTank("HoverEnemy", new Vector2(2204f, 2204f), TeamId.TeamB, definition);
            GameObject cameraObject = new GameObject("HoverReticleCamera");
            PlayerAimReticle reticle = cameraObject.AddComponent<Camera>().gameObject.AddComponent<PlayerAimReticle>();
            reticle.Configure(owner.transform);
            Physics2D.SyncTransforms();

            Assert.That(reticle.RefreshHoverTargetAt(ally.transform.position), Is.True);
            TankDebugSelectionOutline allyOutline = ally.GetComponent<TankDebugSelectionOutline>();
            Assert.That(reticle.HoveredTank, Is.SameAs(ally.transform));
            Assert.That(allyOutline.IsVisible, Is.True);
            Assert.That(allyOutline.CurrentColor, Is.EqualTo(TeamPalette.Ally));

            allyOutline.SetVisible(true);
            reticle.ClearHoverOutline();
            Assert.That(allyOutline.IsVisible, Is.True,
                "Releasing hover must preserve the independent F4/debug selection request.");
            Assert.That(allyOutline.CurrentColor, Is.Not.EqualTo(TeamPalette.Ally));
            allyOutline.SetVisible(false);

            Assert.That(reticle.RefreshHoverTargetAt(enemy.transform.position), Is.True);
            TankDebugSelectionOutline enemyOutline = enemy.GetComponent<TankDebugSelectionOutline>();
            Assert.That(allyOutline.IsVisible, Is.False, "Moving the pointer must release the previous hull.");
            Assert.That(enemyOutline.IsVisible, Is.True);
            Assert.That(enemyOutline.CurrentColor, Is.EqualTo(TeamPalette.Enemy));

            enemy.GetComponent<TankVisibilityPresenter>().SetVisible(false);
            Assert.That(reticle.RefreshHoverTargetAt(enemy.transform.position), Is.False);
            Assert.That(enemyOutline.IsVisible, Is.False);

            enemy.GetComponent<TankVisibilityPresenter>().SetVisible(true);
            Assert.That(reticle.RefreshHoverTargetAt(enemy.transform.position), Is.True);
            enemy.GetComponent<TankHealth>().ApplyDamage(definition.maxHitPoints);
            Assert.That(reticle.RefreshHoverTargetAt(enemy.transform.position), Is.False);
            Assert.That(enemyOutline.IsVisible, Is.False);

            Object.Destroy(cameraObject);
            Object.Destroy(enemy);
            Object.Destroy(ally);
            Object.Destroy(owner);
            Object.Destroy(definition);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Telemetry_WritesBothMultipliersAndTheShotSequence()
        {
            string path = Path.Combine(Path.GetTempPath(), "LocalTanks_P5_" + Guid.NewGuid().ToString("N") + ".json");
            GameObject directorObject = new GameObject("TelemetryDirector");
            BattleDirector director = directorObject.AddComponent<BattleDirector>();
            GameObject shooterObject = new GameObject("TelemetryShooter");
            TeamMember shooter = shooterObject.AddComponent<TeamMember>();
            shooter.Configure(TeamId.TeamA, true);
            BattleTelemetry telemetry = BattleTelemetry.Attach(director, path);
            ShotRandomization roll = new ShotRandomization(12, 1.24f, 0.76f);

            telemetry.RecordFire(shooter, "test_shell", roll);
            telemetry.RecordShot(shooter, null, "Blocked", 0, "test_shell", roll, 0.31f);
            string json = telemetry.Stop();

            Assert.That(json, Does.Contain("sequence=12"));
            Assert.That(json, Does.Contain("\"schemaVersion\":2"));
            Assert.That(json, Does.Match("\\\"decisionFingerprint\\\":\\\"[0-9a-f]{16}\\\""));
            Assert.That(telemetry.FingerprintedDecisionCount, Is.EqualTo(2));
            Assert.That(json, Does.Contain("\"randomSeed\":1701"));
            Assert.That(json, Does.Contain("penetrationMultiplier=1.24"));
            Assert.That(json, Does.Contain("damageMultiplier=0.76"));
            Assert.That(json, Does.Contain("penetrationChance=0.31"));

            Object.Destroy(shooterObject);
            Object.Destroy(directorObject);
            if (File.Exists(path)) File.Delete(path);
            yield return null;
        }

        private static GameObject CreateHoverTank(
            string name,
            Vector2 position,
            TeamId team,
            TankDefinition definition)
        {
            GameObject tank = new GameObject(name);
            tank.transform.position = position;
            PolygonCollider2D collider = tank.AddComponent<PolygonCollider2D>();
            collider.points = new[]
            {
                new Vector2(-0.6f, -1f),
                new Vector2(0.6f, -1f),
                new Vector2(0.6f, 1f),
                new Vector2(-0.6f, 1f)
            };
            TankHealth health = tank.AddComponent<TankHealth>();
            health.Configure(definition);
            tank.AddComponent<TankVisibilityPresenter>().SetVisible(true);
            tank.AddComponent<TeamMember>().Configure(team, tank.name == "HoverOwner");
            return tank;
        }
    }
}
