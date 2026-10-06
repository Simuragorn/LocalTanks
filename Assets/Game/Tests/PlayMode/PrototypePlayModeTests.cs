using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LocalTanks.Tests
{
    public sealed class PrototypePlayModeTests
    {
        [UnityTest]
        public IEnumerator BattleTestRange_LoadsPlayerAndCamera()
        {
            SceneManager.LoadScene("Battle_TestRange");
            yield return null;

            GameObject player = GameObject.Find("TigerII_Player");
            Assert.That(player, Is.Not.Null);
            Assert.That(Camera.main, Is.Not.Null);
            PolygonCollider2D hullCollider = player.GetComponent<PolygonCollider2D>();
            Assert.That(hullCollider, Is.Not.Null);
            Assert.That(hullCollider.points.Length, Is.GreaterThanOrEqualTo(12));
            Assert.That(player.GetComponent<BoxCollider2D>(), Is.Null);

            string[] targetNames = { "TigerII_Target", "E100_Target", "T34_Target", "PzKpfwIV_Target" };
            foreach (string targetName in targetNames)
            {
                GameObject target = GameObject.Find(targetName);
                Assert.That(target, Is.Not.Null, targetName);
                Assert.That(target.GetComponent<RotatingTankDisplay>(), Is.Not.Null, targetName);
                Assert.That(target.GetComponent<PolygonCollider2D>().points.Length, Is.GreaterThanOrEqualTo(10), targetName);
            }
        }

        [UnityTest]
        public IEnumerator Selector_ReplacesPlayerAndRetargetsCamera()
        {
            SceneManager.LoadScene("Battle_TestRange");
            yield return null;

            PlayerTankSelector selector = Object.FindFirstObjectByType<PlayerTankSelector>();
            CameraFollow2D follow = Camera.main.GetComponent<CameraFollow2D>();

            Assert.That(selector, Is.Not.Null);
            Assert.That(selector.SelectTank(2), Is.True);
            yield return null;

            Assert.That(selector.CurrentTankIndex, Is.EqualTo(2));
            Assert.That(selector.CurrentTank.GetComponent<TankHealth>().Definition.id, Is.EqualTo("t_34_76"));
            Assert.That(selector.CurrentTank.GetComponent<PlayerTankInput>().enabled, Is.True);
            Assert.That(follow.Target, Is.EqualTo(selector.CurrentTank.transform));
        }

        [UnityTest]
        public IEnumerator DeveloperReload_PersistsWhenPlayerSelectsAnotherTank()
        {
            SceneManager.LoadScene("Battle_TestRange");
            yield return null;

            PlayerTankSelector selector = Object.FindFirstObjectByType<PlayerTankSelector>();
            WeaponController initialWeapon = selector.CurrentTank.GetComponent<WeaponController>();
            Assert.That(initialWeapon.EffectiveReloadSeconds, Is.GreaterThan(1f));

            selector.SetDeveloperFastReload(true);
            Assert.That(initialWeapon.EffectiveReloadSeconds, Is.EqualTo(1f));
            Assert.That(selector.SelectTank(1), Is.True);
            yield return null;

            Assert.That(selector.DeveloperFastReloadEnabled, Is.True);
            Assert.That(selector.CurrentTank.GetComponent<WeaponController>().EffectiveReloadSeconds, Is.EqualTo(1f));
        }

        [UnityTest]
        public IEnumerator TargetRespawn_RecreatesAiTanksWithoutReplacingPlayer()
        {
            SceneManager.LoadScene("Battle_TestRange");
            yield return null;

            PlayerTankSelector selector = Object.FindFirstObjectByType<PlayerTankSelector>();
            TestRangeTargetRespawner respawner = Object.FindFirstObjectByType<TestRangeTargetRespawner>();
            GameObject player = selector.CurrentTank;
            TankHealth playerHealth = player.GetComponent<TankHealth>();
            playerHealth.ApplyDamage(25);
            int playerHitPoints = playerHealth.CurrentHitPoints;
            Vector3 playerPosition = player.transform.position;

            GameObject oldTarget = GameObject.Find("E100_Target");
            TankHealth oldHealth = oldTarget.GetComponent<TankHealth>();
            oldHealth.ApplyDamage(oldHealth.MaximumHitPoints);
            oldTarget.transform.position = Vector3.zero;

            respawner.RespawnAllTargets();
            yield return null;

            GameObject newTarget = GameObject.Find("E100_Target");
            Assert.That(selector.CurrentTank, Is.SameAs(player));
            Assert.That(playerHealth.CurrentHitPoints, Is.EqualTo(playerHitPoints));
            Assert.That(player.transform.position, Is.EqualTo(playerPosition));
            Assert.That(newTarget, Is.Not.SameAs(oldTarget));
            Assert.That(newTarget.GetComponent<TankHealth>().CurrentHitPoints,
                Is.EqualTo(newTarget.GetComponent<TankHealth>().MaximumHitPoints));
            Assert.That(newTarget.GetComponent<RotatingTankDisplay>(), Is.Not.Null);
            Assert.That(newTarget.GetComponent<PlayerTankInput>().enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator DisplayTank_RotatesWhenAlive()
        {
            SceneManager.LoadScene("Battle_TestRange");
            yield return null;

            GameObject target = GameObject.Find("E100_Target");
            float initialAngle = target.GetComponent<Rigidbody2D>().rotation;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            float rotationDelta = Mathf.Abs(Mathf.DeltaAngle(initialAngle, target.GetComponent<Rigidbody2D>().rotation));
            Assert.That(rotationDelta, Is.GreaterThan(0.01f));
        }

        [UnityTest]
        public IEnumerator Projectile_IsDestroyedWhenLifetimeExpires()
        {
            GameObject projectileObject = new GameObject("LifetimeTestProjectile");
            Projectile2D projectile = projectileObject.AddComponent<Projectile2D>();
            projectile.Initialize(Vector2.up, 0f, 0.05f, 0.01f, 10f, null);

            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.That(projectile == null, Is.True);
        }

        [UnityTest]
        public IEnumerator PenetratingProjectile_ReducesTankHitPoints()
        {
            TankDefinition tank = CreateTankDefinition(100, 10f);
            ShellDefinition shell = CreateShellDefinition(40, 100f);
            GameObject target = CreateArmoredTarget("PenetrationTarget", new Vector2(0f, 1f), 0f, tank);
            TankHealth health = target.GetComponent<TankHealth>();
            Projectile2D projectile = CreateProjectile(Vector2.zero, Vector2.up, shell);
            Physics2D.SyncTransforms();

            for (int frame = 0; frame < 20 && projectile != null; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.That(health.CurrentHitPoints, Is.EqualTo(60));
            Assert.That(projectile == null, Is.True);

            Object.Destroy(target);
            Object.Destroy(tank);
            Object.Destroy(shell);
        }

        [UnityTest]
        public IEnumerator RicochetedProjectile_CanPenetrateSecondTarget()
        {
            TankDefinition ricochetTank = CreateTankDefinition(100, 1000f);
            TankDefinition weakTank = CreateTankDefinition(100, 1f);
            ShellDefinition shell = CreateShellDefinition(100, 100f);
            shell.speed = 12f;
            shell.maximumRange = 20f;

            GameObject first = CreateArmoredTarget("RicochetTarget", new Vector2(0f, 1f), 70f, ricochetTank);
            GameObject second = CreateArmoredTarget("SecondTarget", new Vector2(1.45f, 2.7f), 0f, weakTank);
            TankHealth secondHealth = second.GetComponent<TankHealth>();
            Projectile2D projectile = CreateProjectile(Vector2.zero, Vector2.up, shell);
            Physics2D.SyncTransforms();

            for (int frame = 0; frame < 80 && !secondHealth.IsDestroyed; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.That(secondHealth.IsDestroyed, Is.True);

            if (projectile != null)
            {
                Object.Destroy(projectile.gameObject);
            }

            Object.Destroy(first);
            Object.Destroy(second);
            Object.Destroy(ricochetTank);
            Object.Destroy(weakTank);
            Object.Destroy(shell);
        }

        [UnityTest]
        public IEnumerator DestroyedTank_DisablesCombatControlsButRemainsAnObstacle()
        {
            SceneManager.LoadScene("Battle_TestRange");
            yield return null;

            GameObject target = GameObject.Find("TigerII_Target");
            Assert.That(target, Is.Not.Null);
            TankHealth health = target.GetComponent<TankHealth>();

            health.ApplyDamage(health.MaximumHitPoints);
            yield return null;

            Assert.That(health.IsDestroyed, Is.True);
            Assert.That(target.GetComponent<TankMotor>().enabled, Is.False);
            Assert.That(target.GetComponent<WeaponController>().enabled, Is.False);
            Assert.That(target.GetComponent<Collider2D>().enabled, Is.True);
        }

        private static TankDefinition CreateTankDefinition(int hitPoints, float armor)
        {
            TankDefinition definition = ScriptableObject.CreateInstance<TankDefinition>();
            definition.maxHitPoints = hitPoints;
            definition.armor = new ArmorProfile(armor, armor, armor, armor);
            return definition;
        }

        private static ShellDefinition CreateShellDefinition(int damage, float penetration)
        {
            ShellDefinition definition = ScriptableObject.CreateInstance<ShellDefinition>();
            definition.damage = damage;
            definition.penetration = penetration;
            definition.speed = 10f;
            definition.radius = 0.02f;
            definition.lifetimeSeconds = 2f;
            definition.maximumRange = 10f;
            definition.ricochetAngle = 70f;
            definition.ricochetSpeedMultiplier = 0.7f;
            definition.ricochetPenetrationMultiplier = 0.65f;
            definition.maximumRicochets = 2;
            return definition;
        }

        private static GameObject CreateArmoredTarget(
            string objectName,
            Vector2 position,
            float rotation,
            TankDefinition definition)
        {
            GameObject target = new GameObject(objectName);
            target.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, rotation));
            BoxCollider2D collider = target.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(2f, 0.1f);
            TankHealth health = target.AddComponent<TankHealth>();
            health.Configure(definition);
            TankArmor armor = target.AddComponent<TankArmor>();
            armor.Configure(definition, health);
            return target;
        }

        private static Projectile2D CreateProjectile(Vector2 position, Vector2 direction, ShellDefinition shell)
        {
            GameObject projectileObject = new GameObject("CombatTestProjectile");
            projectileObject.transform.position = position;
            Projectile2D projectile = projectileObject.AddComponent<Projectile2D>();
            projectile.Initialize(direction, shell, null);
            return projectile;
        }
    }
}
