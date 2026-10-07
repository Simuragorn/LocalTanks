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
            SpriteRenderer hiddenRenderer = target.GetComponent<SpriteRenderer>();
            Assert.That(hiddenRenderer.enabled, Is.True, "testing mode keeps a hidden enemy rendered");
            Assert.That(hiddenRenderer.color.a, Is.EqualTo(0.28f).Within(0.01f));

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
        public IEnumerator TargetBehindHull_IsOutsideViewArc()
        {
            VisionRules rules = CreateRules();
            TeamVisionSystem system = new GameObject("VisionSystem").AddComponent<TeamVisionSystem>();
            system.Configure(rules, TeamId.TeamA);
            TeamMember observer = CreateTank("Observer", TeamId.TeamA, Vector2.zero, 10f, 0f);
            TeamMember target = CreateTank("RearTarget", TeamId.TeamB, new Vector2(0f, -1f), 10f, 0f);

            DetectionResult result = system.Evaluate(observer, target, out _);
            system.ForceEvaluateAll();

            Assert.That(result.Detected, Is.False);
            Assert.That(result.Reason, Is.EqualTo(DetectionReason.OutsideViewArc));
            Assert.That(system.IsVisibleTo(TeamId.TeamA, target), Is.False);
            Assert.That(target.VisibilityPresenter.IsVisible, Is.False);
            Assert.That(target.GetComponent<SpriteRenderer>().color.a, Is.EqualTo(0.28f).Within(0.01f));
            Cleanup(system.gameObject, observer.gameObject, target.gameObject, rules);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TurretDirection_DrivesDetectionAndPlayerArcVisual()
        {
            VisionRules rules = CreateRules();
            GameObject systems = new GameObject("Systems");
            TeamVisionSystem vision = systems.AddComponent<TeamVisionSystem>();
            vision.Configure(rules, TeamId.TeamA);
            VisionArcPresenter presenter = systems.AddComponent<VisionArcPresenter>();
            presenter.Configure(vision, null);
            TeamMember observer = CreateTank("TurretObserver", TeamId.TeamA, Vector2.zero, 10f, 0f, 1.5f, true);
            observer.Definition.vehicleClass = VehicleClass.HeavyTank;
            observer.VisionDirection.rotation = Quaternion.Euler(0f, 0f, -90f);
            TeamMember target = CreateTank("RightTarget", TeamId.TeamB, new Vector2(5f, 0f), 10f, 0f);

            DetectionResult result = vision.Evaluate(observer, target, out _);
            presenter.RefreshNow();

            Assert.That(result.Detected, Is.True);
            Assert.That(presenter.TrackedMember, Is.EqualTo(observer));
            Assert.That(presenter.CurrentViewAngle, Is.EqualTo(rules.heavyTankViewAngle));
            Assert.That(presenter.OutsideOverlayRenderer, Is.Not.Null);
            Assert.That(presenter.LeftBoundary, Is.Not.Null);
            Assert.That(presenter.RightBoundary, Is.Not.Null);
            Cleanup(systems, observer.gameObject, target.gameObject, rules);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlayerReplacement_RebuildsArcForNewVehicleClass()
        {
            VisionRules rules = CreateRules();
            GameObject systems = new GameObject("Systems");
            TeamVisionSystem vision = systems.AddComponent<TeamVisionSystem>();
            vision.Configure(rules, TeamId.TeamA);
            VisionArcPresenter presenter = systems.AddComponent<VisionArcPresenter>();
            presenter.Configure(vision, null);
            TeamMember heavy = CreateTank("HeavyPlayer", TeamId.TeamA, Vector2.zero, 10f, 0f);
            heavy.Definition.vehicleClass = VehicleClass.HeavyTank;

            presenter.RefreshNow();
            Assert.That(presenter.CurrentViewAngle, Is.EqualTo(rules.heavyTankViewAngle));

            heavy.gameObject.SetActive(false);
            TeamMember scout = CreateTank("LightPlayer", TeamId.TeamA, Vector2.zero, 10f, 0f);
            scout.Definition.vehicleClass = VehicleClass.LightTank;
            presenter.RefreshNow();

            Assert.That(presenter.TrackedMember, Is.EqualTo(scout));
            Assert.That(presenter.CurrentViewAngle, Is.EqualTo(rules.lightTankViewAngle));
            Cleanup(systems, heavy.gameObject, scout.gameObject, rules);
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

        [UnityTest]
        public IEnumerator NearbyPlayer_MakesConcealmentVegetationTransparent()
        {
            TeamMember player = CreateTank("Player", TeamId.TeamA, Vector2.zero, 10f, 0f);
            GameObject bush = new GameObject("Bush");
            bush.transform.position = new Vector2(0.5f, 0f);
            SpriteRenderer renderer = bush.AddComponent<SpriteRenderer>();
            ConcealmentZone zone = bush.AddComponent<ConcealmentZone>();
            zone.Configure(0.18f);

            yield return new WaitForSeconds(0.3f);

            Assert.That(zone.CurrentAlpha, Is.LessThan(0.5f));
            Assert.That(renderer.color.a, Is.LessThan(0.5f));

            player.transform.position = new Vector2(10f, 0f);
            yield return new WaitForSeconds(0.3f);

            Assert.That(zone.CurrentAlpha, Is.GreaterThan(0.8f));
            Cleanup(player.gameObject, bush);
            yield return null;
        }

        [UnityTest]
        public IEnumerator EnemyTanks_CaptureBaseAtCappedCombinedRate()
        {
            CaptureRules rules = CreateCaptureRules(3f, 3, 2f);
            CaptureBase captureBase = CreateBase("TestBase", TeamId.TeamA, rules);
            TeamMember first = CreateTank("FirstEnemy", TeamId.TeamB, Vector2.zero, 10f, 0f);
            TeamMember second = CreateTank("SecondEnemy", TeamId.TeamB, Vector2.zero, 10f, 0f);
            TeamMember third = CreateTank("ThirdEnemy", TeamId.TeamB, Vector2.zero, 10f, 0f);
            int captureEvents = 0;
            captureBase.Captured += (_, _, _) => captureEvents++;
            captureBase.SetPresence(first, true);
            captureBase.SetPresence(second, true);
            captureBase.SetPresence(third, true);

            captureBase.Tick(1f);

            Assert.That(captureBase.Owner, Is.EqualTo(TeamId.TeamB));
            Assert.That(captureBase.State, Is.EqualTo(BaseCaptureState.Owned));
            Assert.That(captureEvents, Is.EqualTo(1));

            Cleanup(captureBase.gameObject, first.gameObject, second.gameObject, third.gameObject, rules);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ContestedBase_PausesAndAbandonedProgressRecovers()
        {
            CaptureRules rules = CreateCaptureRules(180f, 3, 60f);
            CaptureBase captureBase = CreateBase("TestBase", TeamId.TeamA, rules);
            TeamMember attacker = CreateTank("Attacker", TeamId.TeamB, Vector2.zero, 10f, 0f);
            TeamMember defender = CreateTank("Defender", TeamId.TeamA, Vector2.zero, 10f, 0f);
            captureBase.SetPresence(attacker, true);
            captureBase.Tick(30f);
            float progress = captureBase.Progress;

            captureBase.SetPresence(defender, true);
            captureBase.Tick(30f);
            Assert.That(captureBase.State, Is.EqualTo(BaseCaptureState.Contested));
            Assert.That(captureBase.Progress, Is.EqualTo(progress).Within(0.0001f));

            captureBase.SetPresence(defender, false);
            captureBase.SetPresence(attacker, false);
            captureBase.Tick(10f);
            Assert.That(captureBase.State, Is.EqualTo(BaseCaptureState.Owned));
            Assert.That(captureBase.Progress, Is.EqualTo(0f).Within(0.0001f));

            Cleanup(captureBase.gameObject, attacker.gameObject, defender.gameObject, rules);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DestroyedTank_DoesNotCaptureBase()
        {
            CaptureRules rules = CreateCaptureRules(1f, 3, 1f);
            CaptureBase captureBase = CreateBase("TestBase", TeamId.TeamA, rules);
            TeamMember attacker = CreateTank("DestroyedAttacker", TeamId.TeamB, Vector2.zero, 10f, 0f);
            attacker.Health.ApplyDamage(1000);
            captureBase.SetPresence(attacker, true);

            captureBase.Tick(10f);

            Assert.That(captureBase.Owner, Is.EqualTo(TeamId.TeamA));
            Assert.That(captureBase.Progress, Is.EqualTo(0f));

            Cleanup(captureBase.gameObject, attacker.gameObject, rules);
            yield return null;
        }

        private static TeamMember CreateTank(
            string name,
            TeamId team,
            Vector2 position,
            float viewRange,
            float concealment,
            float guaranteedRange = 1.5f,
            bool addTurret = false)
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
            if (addTurret)
            {
                GameObject turret = new GameObject("TurretPivot");
                turret.transform.SetParent(tank.transform, false);
                turret.AddComponent<TurretAiming>();
            }
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
            rules.lightTankViewAngle = 160f;
            rules.mediumTankViewAngle = 140f;
            rules.heavyTankViewAngle = 120f;
            rules.tankDestroyerViewAngle = 100f;
            rules.artilleryViewAngle = 80f;
            return rules;
        }

        private static CaptureRules CreateCaptureRules(float seconds, int maximumTanks, float recoverySeconds)
        {
            CaptureRules rules = ScriptableObject.CreateInstance<CaptureRules>();
            rules.baseCaptureSeconds = seconds;
            rules.maximumContributingTanks = maximumTanks;
            rules.fullRecoverySeconds = recoverySeconds;
            return rules;
        }

        private static CaptureBase CreateBase(string name, TeamId owner, CaptureRules rules)
        {
            GameObject root = new GameObject(name);
            CircleCollider2D collider = root.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            CaptureBase captureBase = root.AddComponent<CaptureBase>();
            captureBase.Configure(name.ToLowerInvariant(), name, owner, rules, null);
            return captureBase;
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
