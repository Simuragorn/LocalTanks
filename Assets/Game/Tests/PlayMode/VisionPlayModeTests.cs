using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LocalTanks.Tests
{
    public sealed class VisionPlayModeTests
    {
        [UnityTest]
        public IEnumerator OpenTarget_AppearsAndDisappearsAcrossDetectionDistance()
        {
            VisionRules rules = CreateRules();
            TeamVisionSystem system = new GameObject("VisionSystem").AddComponent<TeamVisionSystem>();
            system.Configure(rules, TeamId.TeamA);
            TeamMember observer = CreateTank("Observer", TeamId.TeamA, Vector2.zero, 10f, 0f);
            TeamMember target = CreateTank("Target", TeamId.TeamB, new Vector2(0f, 5f), 10f, 0.2f);

            system.ForceEvaluateAll();
            Assert.That(system.IsVisibleTo(TeamId.TeamA, target), Is.True);
            Assert.That(target.VisibilityPresenter.IsVisible, Is.True);

            target.transform.position = new Vector2(0f, 9f);
            Physics2D.SyncTransforms();
            system.ForceEvaluateAll();
            Assert.That(system.IsVisibleTo(TeamId.TeamA, target), Is.False);
            Assert.That(target.VisibilityPresenter.IsVisible, Is.False);

            Cleanup(system.gameObject, observer.gameObject, target.gameObject, rules);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HardBlocker_WinsInsideGuaranteedRange()
        {
            VisionRules rules = CreateRules();
            TeamVisionSystem system = new GameObject("VisionSystem").AddComponent<TeamVisionSystem>();
            system.Configure(rules, TeamId.TeamA);
            TeamMember observer = CreateTank("Observer", TeamId.TeamA, Vector2.zero, 10f, 0f, 3f);
            TeamMember target = CreateTank("Target", TeamId.TeamB, new Vector2(0f, 2f), 10f, 0f);
            GameObject blocker = new GameObject("Blocker");
            blocker.transform.position = new Vector2(0f, 1f);
            BoxCollider2D collider = blocker.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(2f, 0.2f);
            collider.isTrigger = true;
            blocker.AddComponent<VisionBlocker>();
            Physics2D.SyncTransforms();

            DetectionResult result = system.Evaluate(observer, target, out _);
            Assert.That(result.Detected, Is.False);
            Assert.That(result.Reason, Is.EqualTo(DetectionReason.HardBlocker));

            Cleanup(system.gameObject, observer.gameObject, target.gameObject, blocker, rules);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BushAndFiring_ChangeDetectionWithoutBlockingShots()
        {
            VisionRules rules = CreateRules();
            TeamVisionSystem system = new GameObject("VisionSystem").AddComponent<TeamVisionSystem>();
            system.Configure(rules, TeamId.TeamA);
            TeamMember observer = CreateTank("Observer", TeamId.TeamA, Vector2.zero, 10f, 0f);
            TeamMember target = CreateTank("Target", TeamId.TeamB, new Vector2(0f, 7f), 10f, 0.15f);
            target.Definition.firingRevealPenalty = 0.3f;
            target.Definition.firingRevealDuration = 5f;
            GameObject bush = new GameObject("Bush");
            bush.transform.position = new Vector2(0f, 3.5f);
            BoxCollider2D collider = bush.AddComponent<BoxCollider2D>();
            collider.size = Vector2.one;
            collider.isTrigger = true;
            bush.AddComponent<ConcealmentZone>().Configure(0.25f);
            Physics2D.SyncTransforms();

            DetectionResult concealed = system.Evaluate(observer, target, out float bushBonus);
            target.RecordFiring(Time.time);
            DetectionResult revealed = system.Evaluate(observer, target, out _);

            Assert.That(bushBonus, Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(concealed.Detected, Is.False);
            Assert.That(revealed.Detected, Is.True);
            Assert.That(collider.isTrigger, Is.True);

            Cleanup(system.gameObject, observer.gameObject, target.gameObject, bush, rules);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TeamContact_IsSharedAndHiddenDamageDoesNotLeakToRoster()
        {
            VisionRules rules = CreateRules();
            GameObject systems = new GameObject("Systems");
            TeamVisionSystem vision = systems.AddComponent<TeamVisionSystem>();
            vision.Configure(rules, TeamId.TeamA);
            BattleRoster roster = systems.AddComponent<BattleRoster>();
            roster.Configure(TeamId.TeamA, vision);
            TeamMember distantAlly = CreateTank("DistantAlly", TeamId.TeamA, new Vector2(0f, -20f), 10f, 0f);
            TeamMember scout = CreateTank("Scout", TeamId.TeamA, Vector2.zero, 10f, 0f);
            TeamMember enemy = CreateTank("Enemy", TeamId.TeamB, new Vector2(0f, 5f), 10f, 0f);
            yield return null;

            vision.ForceEvaluateAll();
            BattleRosterEntry entry = roster.Find(enemy);
            Assert.That(vision.IsVisibleTo(TeamId.TeamA, enemy), Is.True, "scout shares contact");
            Assert.That(entry.IsCurrentlyVisible, Is.True);

            enemy.transform.position = new Vector2(0f, 15f);
            Physics2D.SyncTransforms();
            vision.ForceEvaluateAll();
            int lastKnown = entry.LastKnownHitPoints;
            enemy.Health.ApplyDamage(30);

            Assert.That(entry.IsCurrentlyVisible, Is.False);
            Assert.That(entry.LastKnownHitPoints, Is.EqualTo(lastKnown));
            Assert.That(enemy.Health.CurrentHitPoints, Is.EqualTo(lastKnown - 30));

            Cleanup(systems, distantAlly.gameObject, scout.gameObject, enemy.gameObject, rules);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DestroyedTank_StopsScoutingAndDecrementsRosterOnce()
        {
            VisionRules rules = CreateRules();
            GameObject systems = new GameObject("Systems");
            TeamVisionSystem vision = systems.AddComponent<TeamVisionSystem>();
            vision.Configure(rules, TeamId.TeamA);
            BattleRoster roster = systems.AddComponent<BattleRoster>();
            roster.Configure(TeamId.TeamA, vision);
            TeamMember ally = CreateTank("Ally", TeamId.TeamA, Vector2.zero, 10f, 0f);
            TeamMember enemy = CreateTank("Enemy", TeamId.TeamB, new Vector2(0f, 5f), 10f, 0f);
            yield return null;

            Assert.That(roster.EnemiesAlive, Is.EqualTo(1));
            enemy.Health.ApplyDamage(1000);
            enemy.Health.ApplyDamage(1000);
            vision.ForceEvaluateAll();

            Assert.That(roster.EnemiesAlive, Is.EqualTo(0));
            Assert.That(roster.Find(enemy).LastKnownHitPoints, Is.EqualTo(0));
            Assert.That(vision.IsVisibleTo(TeamId.TeamA, enemy), Is.False);

            Cleanup(systems, ally.gameObject, enemy.gameObject, rules);
            yield return null;
        }

        private static TeamMember CreateTank(
            string name,
            TeamId team,
            Vector2 position,
            float viewRange,
            float concealment,
            float guaranteedRange = 1.5f)
        {
            TankDefinition definition = ScriptableObject.CreateInstance<TankDefinition>();
            definition.id = name.ToLowerInvariant();
            definition.displayName = name;
            definition.maxHitPoints = 100;
            definition.viewRange = viewRange;
            definition.stationaryConcealment = concealment;
            definition.movementRevealPenalty = 0.1f;
            definition.firingRevealDuration = 5f;
            definition.guaranteedDetectionRange = guaranteedRange;

            GameObject tank = new GameObject(name);
            tank.transform.position = position;
            tank.AddComponent<SpriteRenderer>();
            TankVisibilityPresenter presenter = tank.AddComponent<TankVisibilityPresenter>();
            TankHealth health = tank.AddComponent<TankHealth>();
            health.Configure(definition);
            TeamMember member = tank.AddComponent<TeamMember>();
            member.Configure(team, team == TeamId.TeamA);
            presenter.SetVisible(true);
            return member;
        }

        private static VisionRules CreateRules()
        {
            VisionRules rules = ScriptableObject.CreateInstance<VisionRules>();
            rules.maximumConcealment = 0.8f;
            rules.maximumBushBonus = 0.45f;
            rules.minimumVisibilityFactor = 0.2f;
            rules.checkInterval = 100f;
            rules.checksPerFrame = 32;
            rules.contactMemorySeconds = 5f;
            return rules;
        }

        private static void Cleanup(params Object[] objects)
        {
            foreach (Object item in objects)
            {
                if (item != null)
                {
                    Object.Destroy(item);
                }
            }
        }
    }
}
