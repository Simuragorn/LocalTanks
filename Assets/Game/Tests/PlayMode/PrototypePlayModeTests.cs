using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace LocalTanks.Tests
{
    public sealed class PrototypePlayModeTests
    {
        [UnityTest]
        public IEnumerator DeveloperTimeScale_TogglesThreeTimesSpeedAndRestoresNormalTime()
        {
            GameObject root = new GameObject("DeveloperTimeScale");
            DeveloperTimeScaleController controller = root.AddComponent<DeveloperTimeScaleController>();
            yield return null;

            controller.SetAccelerated(true);
            Assert.That(controller.IsAccelerated, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(3f));

            controller.SetAccelerated(false);
            Assert.That(controller.IsAccelerated, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Object.Destroy(root);
            yield return null;
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

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
                Assert.That(target.GetComponent<TankHealthBar>(), Is.Not.Null, targetName);
                Assert.That(target.GetComponent<PolygonCollider2D>().points.Length, Is.GreaterThanOrEqualTo(10), targetName);
            }
        }

        [UnityTest]
        public IEnumerator CameraZoom_ClampsToConfiguredRange()
        {
            SceneManager.LoadScene("Battle_TestRange");
            yield return null;

            CameraFollow2D follow = Camera.main.GetComponent<CameraFollow2D>();
            follow.SetZoom(100f, true);
            Assert.That(follow.TargetZoom, Is.EqualTo(follow.MaximumZoom));
            Assert.That(follow.CurrentZoom, Is.EqualTo(follow.MaximumZoom));

            follow.SetZoom(-100f, true);
            Assert.That(follow.TargetZoom, Is.EqualTo(follow.MinimumZoom));
            Assert.That(follow.CurrentZoom, Is.EqualTo(follow.MinimumZoom));
        }

        [UnityTest]
        public IEnumerator Selector_ReplacesPlayerAndRetargetsCamera()
        {
            SceneManager.LoadScene("Battle_TestRange");
            yield return null;

            PlayerTankSelector selector = Object.FindAnyObjectByType<PlayerTankSelector>();
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

            PlayerTankSelector selector = Object.FindAnyObjectByType<PlayerTankSelector>();
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

            PlayerTankSelector selector = Object.FindAnyObjectByType<PlayerTankSelector>();
            TestRangeTargetRespawner respawner = Object.FindAnyObjectByType<TestRangeTargetRespawner>();
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
        public IEnumerator TargetHealthBarAndRotationPause_PersistThroughRespawn()
        {
            SceneManager.LoadScene("Battle_TestRange");
            yield return null;

            TestRangeTargetRespawner respawner = Object.FindAnyObjectByType<TestRangeTargetRespawner>();
            GameObject target = GameObject.Find("E100_Target");
            TankHealth health = target.GetComponent<TankHealth>();
            TankHealthBar healthBar = target.GetComponent<TankHealthBar>();
            health.ApplyDamage(health.MaximumHitPoints / 2);
            Assert.That(healthBar.HealthRatio, Is.EqualTo((float)health.CurrentHitPoints / health.MaximumHitPoints));
            Assert.That(ColorsMatch(healthBar.FillColor, TeamPalette.Enemy), Is.True);

            respawner.SetRotationPaused(true);
            float pausedAngle = target.GetComponent<Rigidbody2D>().rotation;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(pausedAngle, target.GetComponent<Rigidbody2D>().rotation)), Is.LessThan(0.001f));

            respawner.RespawnAllTargets();
            yield return null;

            GameObject respawnedTarget = GameObject.Find("E100_Target");
            Assert.That(respawnedTarget.GetComponent<TankHealthBar>().HealthRatio, Is.EqualTo(1f));
            Assert.That(ColorsMatch(respawnedTarget.GetComponent<TankHealthBar>().FillColor, TeamPalette.Enemy), Is.True);
            Assert.That(respawnedTarget.GetComponent<RotatingTankDisplay>().IsPaused, Is.True);

            respawner.SetRotationPaused(false);
            float resumedAngle = respawnedTarget.GetComponent<Rigidbody2D>().rotation;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(resumedAngle, respawnedTarget.GetComponent<Rigidbody2D>().rotation)), Is.GreaterThan(0.01f));
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
        public IEnumerator NavigationRange_LoadsMapAndMovingAgents()
        {
            SceneManager.LoadScene("Battle_NavigationRange");
            yield return null;

            NavigationMap map = Object.FindAnyObjectByType<NavigationMap>();
            NavigationAgent[] agents = Object.FindObjectsByType<NavigationAgent>();
            Assert.That(map, Is.Not.Null);
            Assert.That(map.Width, Is.EqualTo(40));
            Assert.That(map.Height, Is.EqualTo(24));
            Assert.That(Object.FindObjectsByType<Tilemap>().Length, Is.EqualTo(2));
            Assert.That(agents.Length, Is.EqualTo(3));
            foreach (NavigationAgent navigationAgent in agents)
            {
                Assert.That(navigationAgent.HasPath, Is.True, $"{navigationAgent.name} has no initial path");
            }

            GameObject movingAgent = GameObject.Find("NavAgent_T34");
            Vector3 initialPosition = movingAgent.transform.position;
            for (int frame = 0; frame < 180; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.That(Vector3.Distance(initialPosition, movingAgent.transform.position), Is.GreaterThan(0.01f));
        }

        [UnityTest]
        public IEnumerator NavigationRange_AppliesTerrainMovementModifiers()
        {
            SceneManager.LoadScene("Battle_NavigationRange");
            yield return null;

            NavigationMap map = Object.FindAnyObjectByType<NavigationMap>();
            GameObject player = GameObject.Find("Navigation_Player");
            TankMotor motor = player.GetComponent<TankMotor>();
            TerrainMotorModifier modifier = player.GetComponent<TerrainMotorModifier>();

            player.transform.position = map.CellToWorld(new Vector2Int(5, 2));
            yield return new WaitForFixedUpdate();
            Assert.That(modifier.CurrentTerrain, Is.EqualTo(TerrainKind.Road));
            Assert.That(motor.TerrainSpeedMultiplier, Is.EqualTo(1f));

            player.transform.position = map.CellToWorld(new Vector2Int(22, 7));
            yield return new WaitForFixedUpdate();
            Assert.That(modifier.CurrentTerrain, Is.EqualTo(TerrainKind.Mud));
            Assert.That(motor.TerrainSpeedMultiplier, Is.EqualTo(0.55f).Within(0.001f));
            Assert.That(motor.TerrainAccelerationMultiplier, Is.EqualTo(0.45f).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator RiverCrossing_LoadsThreeRoutesBasesAndEnvironment()
        {
            SceneManager.LoadScene("Battle_RiverCrossing");
            yield return null;

            NavigationMap map = Object.FindAnyObjectByType<NavigationMap>();
            NavigationAgent[] agents = Object.FindObjectsByType<NavigationAgent>();
            Assert.That(map, Is.Not.Null);
            Assert.That(map.Width, Is.EqualTo(72));
            Assert.That(map.Height, Is.EqualTo(44));
            Assert.That(GameObject.Find("Base_A"), Is.Not.Null);
            Assert.That(GameObject.Find("Base_B"), Is.Not.Null);
            Assert.That(GameObject.Find("NorthStoneBridge"), Is.Not.Null);
            Assert.That(GameObject.Find("CentralStoneBridge"), Is.Not.Null);
            Assert.That(GameObject.Find("SouthWoodBridge"), Is.Not.Null);
            Assert.That(GameObject.Find("Farmhouse_27_27"), Is.Not.Null);
            Assert.That(Object.FindObjectsByType<Tilemap>().Length, Is.EqualTo(3));
            Assert.That(agents.Length, Is.EqualTo(29));
            foreach (NavigationAgent navigationAgent in agents)
            {
                Assert.That(navigationAgent.HasPath || navigationAgent.DestinationReached, Is.True,
                    $"{navigationAgent.name} has neither a path nor a reached first waypoint");
            }
            Assert.That(Object.FindAnyObjectByType<TeamVisionSystem>(), Is.Not.Null);
            Assert.That(Object.FindAnyObjectByType<BattleRoster>(), Is.Not.Null);
            Assert.That(Object.FindAnyObjectByType<BattleHudController>(), Is.Not.Null);
            Assert.That(Object.FindObjectsByType<TeamMember>().Length, Is.EqualTo(30));
            Assert.That(Object.FindObjectsByType<TeamMember>().Count(member =>
                member.Definition != null && member.Definition.id == "bt_2"), Is.EqualTo(2));
            BattleDirector director = Object.FindAnyObjectByType<BattleDirector>();
            Assert.That(director, Is.Not.Null);
            Assert.That(director.TeamAAlive, Is.EqualTo(15));
            Assert.That(director.TeamBAlive, Is.EqualTo(15));
            Assert.That(Object.FindObjectsByType<CombatTankAI>().Length, Is.EqualTo(29));
            Assert.That(Object.FindObjectsByType<ConcealmentZone>().Length, Is.GreaterThan(0));
            Assert.That(GameObject.Find("VisionBlockers").GetComponent<VisionBlocker>(), Is.Not.Null);
            BattleRoster roster = Object.FindAnyObjectByType<BattleRoster>();
            BattleHudController hud = Object.FindAnyObjectByType<BattleHudController>();
            yield return null;
            Assert.That(roster.Allies.Count, Is.EqualTo(15));
            Assert.That(roster.Enemies.Count, Is.EqualTo(15));
            Assert.That(hud.AllyList.childCount, Is.EqualTo(15));
            Assert.That(hud.EnemyList.childCount, Is.EqualTo(15));
            Assert.That(roster.Allies, Has.All.Matches<BattleRosterEntry>(entry => entry.ClassIcon != null));
            Assert.That(roster.Enemies, Has.All.Matches<BattleRosterEntry>(entry => entry.ClassIcon != null));
            Assert.That(Object.FindObjectsByType<TankClassIconPresenter>().Length, Is.EqualTo(30));
            CaptureBase[] captureBases = Object.FindObjectsByType<CaptureBase>();
            Assert.That(captureBases.Length, Is.EqualTo(2));
            CaptureBase alliedBase = captureBases.Single(item => item.BaseId == "A");
            Assert.That(alliedBase.Owner, Is.EqualTo(TeamId.TeamA));
            CaptureBase enemyBase = captureBases.Single(item => item.BaseId == "B");
            Assert.That(enemyBase.Owner, Is.EqualTo(TeamId.TeamB));
            Assert.That(alliedBase.SpawnPointCount, Is.EqualTo(15));
            Assert.That(enemyBase.SpawnPointCount, Is.EqualTo(15));
            TeamMember playerMember = director.PlayerMember;
            Assert.That(playerMember, Is.Not.Null);
            Assert.That(alliedBase.GetComponent<CircleCollider2D>().OverlapPoint(playerMember.transform.position), Is.True);
            TeamMember[] enemyMembers = Object.FindObjectsByType<TeamMember>()
                .Where(item => item.Team == TeamId.TeamB)
                .ToArray();
            Assert.That(enemyMembers, Has.All.Matches<TeamMember>(member =>
                enemyBase.GetComponent<CircleCollider2D>().OverlapPoint(member.transform.position)));
            Assert.That(enemyMembers, Has.All.Matches<TeamMember>(member =>
                ColorsMatch(member.GetComponent<TankHealthBar>().FillColor, TeamPalette.Enemy)));
            Assert.That(enemyMembers, Has.All.Matches<TeamMember>(member =>
                ColorsMatch(member.GetComponent<TankClassIconPresenter>().TeamColor, TeamPalette.Enemy)));
            foreach (CombatTankAI ai in Object.FindObjectsByType<CombatTankAI>())
            {
                ai.enabled = false;
            }
            for (int first = 0; first < enemyMembers.Length; first++)
            {
                Collider2D firstCollider = enemyMembers[first].GetComponent<Collider2D>();
                for (int second = first + 1; second < enemyMembers.Length; second++)
                {
                    Collider2D secondCollider = enemyMembers[second].GetComponent<Collider2D>();
                    Assert.That(firstCollider.Distance(secondCollider).isOverlapped, Is.False,
                        $"{enemyMembers[first].name} overlaps {enemyMembers[second].name} on Base B");
                }
            }

            foreach (TeamMember enemy in enemyMembers)
            {
                enemy.transform.position = new Vector3(100f, 100f, 0f);
            }

            Physics2D.SyncTransforms();
            yield return new WaitForFixedUpdate();
            enemyBase.SetPresence(playerMember, true);
            enemyBase.Tick(1f);
            yield return new WaitForSeconds(0.15f);
            Assert.That(enemyBase.State, Is.EqualTo(BaseCaptureState.Capturing));
            Assert.That(enemyBase.RemainingSeconds, Is.LessThan(180f));
            Assert.That(hud.BaseStatusList.childCount, Is.EqualTo(1));
            enemyBase.SetPresence(playerMember, false);

            Assert.That(map.IsBlocked(new Vector2Int(35, 35)), Is.False, "north bridge");
            Assert.That(map.IsBlocked(new Vector2Int(35, 22)), Is.False, "central bridge");
            Assert.That(map.IsBlocked(new Vector2Int(35, 9)), Is.False, "south bridge");
            Assert.That(map.IsBlocked(new Vector2Int(35, 29)), Is.True, "deep river");
            Assert.That(map.FindCellPath(new Vector2Int(7, 22), new Vector2Int(64, 22), 1), Is.Not.Empty);

            CameraFollow2D follow = Camera.main.GetComponent<CameraFollow2D>();
            Assert.That(follow.Target, Is.EqualTo(playerMember.transform));
            Assert.That(follow.MaximumZoom, Is.EqualTo(22f));
        }

        private static bool ColorsMatch(Color actual, Color expected)
        {
            return Mathf.Abs(actual.r - expected.r) < 0.001f &&
                   Mathf.Abs(actual.g - expected.g) < 0.001f &&
                   Mathf.Abs(actual.b - expected.b) < 0.001f &&
                   Mathf.Abs(actual.a - expected.a) < 0.001f;
        }

        [UnityTest]
        public IEnumerator RiverCrossing_CombatAiLeavesBothBasesAndStopsWhenDestroyed()
        {
            SceneManager.LoadScene("Battle_RiverCrossing");
            yield return null;

            CombatTankAI[] agents = Object.FindObjectsByType<CombatTankAI>();
            Vector3[] initialPositions = agents.Select(item => item.transform.position).ToArray();
            float timeout = Time.time + 1.2f;
            while (Time.time < timeout)
            {
                yield return new WaitForFixedUpdate();
            }

            int moved = agents.Where((item, index) =>
                item != null && Vector3.Distance(initialPositions[index], item.transform.position) > 0.01f).Count();
            Assert.That(moved, Is.GreaterThanOrEqualTo(20), "most AI tanks should leave their initial slots");

            CombatTankAI destroyed = agents.First(item => item != null && item.enabled);
            TankHealth health = destroyed.GetComponent<TankHealth>();
            health.ApplyDamage(health.MaximumHitPoints);
            yield return null;

            Assert.That(destroyed.State, Is.EqualTo(CombatAiState.Destroyed));
            Assert.That(destroyed.GetComponent<NavigationAgent>().enabled, Is.False);
            Assert.That(destroyed.GetComponent<WeaponController>().enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator RiverCrossing_CombatAiAimsAndFiresAtSharedVisibleContact()
        {
            SceneManager.LoadScene("Battle_RiverCrossing");
            yield return null;

            NavigationMap map = Object.FindAnyObjectByType<NavigationMap>();
            TeamVisionSystem vision = Object.FindAnyObjectByType<TeamVisionSystem>();
            CombatTankAI shooter = Object.FindObjectsByType<CombatTankAI>()
                .First(item => item.GetComponent<TeamMember>().Team == TeamId.TeamA &&
                               item.GetComponent<TankHealth>().Definition.id == "t_34_76");
            TeamMember target = Object.FindObjectsByType<TeamMember>()
                .First(item => item.Team == TeamId.TeamB && item.Definition.id == "t_34_76");

            foreach (CombatTankAI ai in Object.FindObjectsByType<CombatTankAI>())
            {
                if (ai != shooter) ai.enabled = false;
            }

            TeamMember shooterMember = shooter.GetComponent<TeamMember>();
            shooter.transform.SetPositionAndRotation(map.CellToWorld(new Vector2Int(30, 22)), Quaternion.Euler(0f, 0f, -90f));
            target.transform.SetPositionAndRotation(map.CellToWorld(new Vector2Int(34, 22)), Quaternion.Euler(0f, 0f, 90f));
            shooterMember.VisionDirection.rotation = Quaternion.Euler(0f, 0f, -90f);
            Physics2D.SyncTransforms();
            vision.ForceEvaluateAll();

            bool fired = false;
            WeaponController weapon = shooter.GetComponent<WeaponController>();
            weapon.Fired += _ => fired = true;
            int initialHitPoints = target.Health.CurrentHitPoints;
            float timeout = Time.time + 2f;
            while (!fired && Time.time < timeout)
            {
                yield return null;
            }

            Assert.That(vision.IsVisibleTo(TeamId.TeamA, target), Is.True);
            Assert.That(shooter.CurrentTarget, Is.EqualTo(target));
            Assert.That(fired, Is.True, $"AI did not fire; block reason: {shooter.CurrentFireBlockReason}");

            timeout = Time.time + 1f;
            while (target.Health.CurrentHitPoints == initialHitPoints && Time.time < timeout)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.That(target.Health.CurrentHitPoints, Is.LessThan(initialHitPoints),
                "Projectile fired by a BattleDirector child tank must hit another spawned tank.");
        }

        [UnityTest]
        public IEnumerator RiverCrossing_CloseEnemyInsideTurretArc_IsFullyVisible()
        {
            SceneManager.LoadScene("Battle_RiverCrossing");
            yield return null;

            BattleDirector director = Object.FindAnyObjectByType<BattleDirector>();
            TeamMember player = director.PlayerMember;
            TeamMember enemy = Object.FindObjectsByType<TeamMember>()
                .First(item => item.Team == TeamId.TeamB);
            TeamVisionSystem vision = Object.FindAnyObjectByType<TeamVisionSystem>();
            PlayerTankInput input = player.GetComponent<PlayerTankInput>();
            TurretAiming turret = player.GetComponentInChildren<TurretAiming>();
            NavigationAgent agent = enemy.GetComponent<NavigationAgent>();
            CombatTankAI combatAi = enemy.GetComponent<CombatTankAI>();
            if (input != null) input.enabled = false;
            if (turret != null) turret.enabled = false;
            if (agent != null) agent.enabled = false;
            if (combatAi != null) combatAi.enabled = false;

            TankMotor playerMotor = player.GetComponent<TankMotor>();
            TankMotor enemyMotor = enemy.GetComponent<TankMotor>();
            if (playerMotor != null) playerMotor.enabled = false;
            if (enemyMotor != null) enemyMotor.enabled = false;
            Rigidbody2D playerBody = player.GetComponent<Rigidbody2D>();
            Rigidbody2D enemyBody = enemy.GetComponent<Rigidbody2D>();
            if (playerBody != null) playerBody.linearVelocity = Vector2.zero;
            if (enemyBody != null) enemyBody.linearVelocity = Vector2.zero;

            player.VisionDirection.rotation = Quaternion.identity;
            enemy.transform.position = (Vector2)player.transform.position + Vector2.up;
            Physics2D.SyncTransforms();

            DetectionResult result = vision.Evaluate(player, enemy, out _);
            vision.ForceEvaluateAll();

            Assert.That(result.Detected, Is.True, result.Reason.ToString());
            Assert.That(vision.IsVisibleTo(TeamId.TeamA, enemy), Is.True);
            Assert.That(enemy.VisibilityPresenter.IsVisible, Is.True);
            Assert.That(enemy.GetComponentsInChildren<SpriteRenderer>(),
                Has.All.Matches<SpriteRenderer>(renderer => renderer.color.a > 0.99f));
        }

        [UnityTest]
        public IEnumerator DestroyedWall_OpensCellsAndShortensRoute()
        {
            SceneManager.LoadScene("Battle_NavigationRange");
            yield return null;

            NavigationMap map = Object.FindAnyObjectByType<NavigationMap>();
            DestructibleObstacle wall = Object.FindAnyObjectByType<DestructibleObstacle>();
            Vector2Int start = new Vector2Int(19, 4);
            Vector2Int destination = new Vector2Int(19, 19);
            var closedPath = map.FindCellPath(start, destination);
            int version = map.Version;

            wall.ApplyDamage(10000);
            var openPath = map.FindCellPath(start, destination);

            Assert.That(wall.IsDestroyed, Is.True);
            Assert.That(map.Version, Is.GreaterThan(version));
            Assert.That(map.IsBlocked(new Vector2Int(19, 12)), Is.False);
            Assert.That(map.GetTerrain(new Vector2Int(19, 12)), Is.EqualTo(TerrainKind.Grass));
            Assert.That(map.GetNavigationCost(new Vector2Int(19, 12)), Is.EqualTo(1.25f));
            Assert.That(openPath, Is.Not.Empty);
            Assert.That(openPath.Count, Is.LessThan(closedPath.Count));
        }

        [UnityTest]
        public IEnumerator PenetratingProjectile_ReducesTankHitPoints()
        {
            SceneManager.LoadScene("Battle_TestRange");
            yield return null;

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
            SceneManager.LoadScene("Battle_TestRange");
            yield return null;

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
