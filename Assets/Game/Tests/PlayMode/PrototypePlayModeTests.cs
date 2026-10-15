using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;

namespace LocalTanks.Tests
{
    public sealed class PrototypePlayModeTests
    {
        [UnityTest]
        public IEnumerator AiDuelLab_AllConfiguredCasesProduceCompleteAuditReports()
        {
            string[] expectedIds = { "open_center", "north_cover", "south_cover" };
            float previousCaptureDeltaTime = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            SceneManager.LoadScene("Battle_AiDuelLab");

            for (int caseIndex = 0; caseIndex < expectedIds.Length; caseIndex++)
            {
                AiDuelLabController lab = null;
                for (int frame = 0; frame < 90; frame++)
                {
                    yield return null;
                    lab = Object.FindAnyObjectByType<AiDuelLabController>();
                    if (lab != null && lab.IsReady && lab.ActiveCaseIndex == caseIndex) break;
                }

                Assert.That(lab, Is.Not.Null, $"AI Duel Lab case {caseIndex} did not load.");
                Assert.That(lab.IsReady, Is.True, $"AI Duel Lab case {caseIndex} did not become ready.");
                Assert.That(lab.ActiveCase.id, Is.EqualTo(expectedIds[caseIndex]));
                Assert.That(Object.FindObjectsByType<CombatTankAI>(), Has.Length.EqualTo(2));

                Time.timeScale = 3f;
                while (!lab.IsComplete) yield return null;
                Time.timeScale = 1f;

                string report = lab.FinalReport;
                Debug.Log($"AI_DUEL_DIAGNOSTIC case={lab.ActiveCase.id} seed={lab.ActiveCase.seed}\n" +
                          report + "\n" + lab.Auditor.BuildTransitionReport());
                int minimumSamples = Mathf.FloorToInt(lab.ActiveCase.durationSeconds / 0.2f) * 2;
                Assert.That(lab.Auditor.SampleCount, Is.GreaterThanOrEqualTo(minimumSamples));
                Assert.That(report, Does.Contain("duel_a_ms1"));
                Assert.That(report, Does.Contain("duel_b_leichttraktor"));

                if (caseIndex + 1 < expectedIds.Length)
                    lab.ReloadCase(caseIndex + 1);
                else
                    lab.ReloadCase(0);
            }

            yield return null;
            Time.timeScale = 1f;
            Time.captureDeltaTime = previousCaptureDeltaTime;
        }

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

        /// <summary>
        /// The key cycles 1, 3, 10 and then back, and the slider can dial any rate in that range. Ten
        /// times is the whole point on an eight-hundred-metre map, where an approach takes a minute to
        /// watch; raising the time scale alone would look like a slideshow, so the controller also raises
        /// the captured frame delta.
        /// <para>
        /// Two things must not change. The physics step, or fast runs describe a different game. And at
        /// normal speed the captured delta must be switched off entirely: capturing a fixed delta makes
        /// every frame simulate exactly that much whatever the frame really cost, so on any machine not
        /// hitting the physics rate the battle runs at the wrong speed and tanks appear to drive faster.
        /// That is a real defect this check now guards.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator DeveloperTimeScale_CyclesUpToTenTimesWithoutTouchingThePhysicsStep()
        {
            GameObject root = new GameObject("DeveloperTimeScale");
            DeveloperTimeScaleController controller = root.AddComponent<DeveloperTimeScaleController>();
            float fixedStep = Time.fixedDeltaTime;
            yield return null;

            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(Time.captureDeltaTime, Is.Zero,
                "At normal speed the captured frame delta must be off, or the battle runs at the wrong speed.");

            controller.Toggle();
            Assert.That(Time.timeScale, Is.EqualTo(3f), "the first press should reach three times");
            Assert.That(Time.captureDeltaTime, Is.GreaterThan(0f), "a raised rate needs a captured delta");
            controller.Toggle();
            Assert.That(Time.timeScale, Is.EqualTo(10f), "the second press should reach ten times");
            controller.Toggle();
            Assert.That(Time.timeScale, Is.EqualTo(1f), "the cycle should return to normal");
            Assert.That(Time.captureDeltaTime, Is.Zero,
                "Returning to normal speed must switch the captured delta off again.");

            controller.SetScale(7f);
            Assert.That(Time.timeScale, Is.EqualTo(7f), "the slider sets an arbitrary rate");
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(fixedStep),
                "the physics step must never change, or fast runs describe a different game");

            controller.SetScale(1f);
            Assert.That(Time.captureDeltaTime, Is.Zero);

            Object.Destroy(root);
            yield return null;
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(Time.captureDeltaTime, Is.Zero,
                "Leaving the scene must not leave a captured delta behind for the next battle.");
        }

        [UnityTest]
        public IEnumerator AiDuelLab_LoadsDeterministicOneVersusOneWithoutStrategicLlm()
        {
            SceneManager.LoadScene("Battle_AiDuelLab");
            yield return null;
            AiDuelLabController lab = Object.FindAnyObjectByType<AiDuelLabController>();
            for (int frame = 0; lab != null && !lab.IsReady && frame < 30; frame++)
                yield return null;

            BattleDirector director = Object.FindAnyObjectByType<BattleDirector>();
            Assert.That(lab, Is.Not.Null);
            Assert.That(lab.IsReady, Is.True);
            Assert.That(lab.ActiveCase.id, Is.EqualTo("open_center"));
            Assert.That(lab.ActiveCase.seed, Is.EqualTo(1701));
            Assert.That(director.Scenario.kind, Is.EqualTo(BattleScenarioKind.AiDuelLab));
            Assert.That(director.SpawnedMembers, Has.Count.EqualTo(2));
            Assert.That(director.SpawnedMembers.Count(item => item.Team == TeamId.TeamA), Is.EqualTo(1));
            Assert.That(director.SpawnedMembers.Count(item => item.Team == TeamId.TeamB), Is.EqualTo(1));
            Assert.That(director.SpawnedMembers.Any(item => item.IsPlayerControlled), Is.False);
            Assert.That(Object.FindObjectsByType<CombatTankAI>(), Has.Length.EqualTo(2));
            Assert.That(director.StrategicPlanner, Is.Null);
            Assert.That(director.StrategicCoordinator, Is.Null);
            Assert.That(lab.Auditor, Is.Not.Null);

            float previousScale = Time.timeScale;
            try
            {
                Time.timeScale = 3f;
                while (!lab.IsComplete) yield return null;
            }
            finally
            {
                Time.timeScale = previousScale;
            }
            string report = lab.FinalReport;
            Debug.Log(report);
            Assert.That(lab.Auditor.SampleCount, Is.GreaterThanOrEqualTo(400));
            Assert.That(report, Does.Contain("duel_a_ms1"));
            Assert.That(report, Does.Contain("duel_b_leichttraktor"));
        }

        [UnityTest]
        public IEnumerator AiSquadLab_RunsThreeVersusThreeAuditAcrossThreeSeedsWithoutStrategicLlm()
        {
            string[] expectedCases = { "group_seed_2711", "group_seed_2712", "group_seed_2713" };
            string[] expectedBots =
            {
                "squad_a_north", "squad_a_center", "squad_a_south",
                "squad_b_north", "squad_b_center", "squad_b_south"
            };
            float previousScale = Time.timeScale;
            float previousCaptureDeltaTime = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            SceneManager.LoadScene("Battle_AiSquadLab");
            Dictionary<string, (string Fingerprint, int Count)> expectedSignatures =
                new Dictionary<string, (string Fingerprint, int Count)>
                {
                    ["group_seed_2711"] = ("61052e2f801eced2", 118),
                    ["group_seed_2712"] = ("c23e1413f4e2ae30", 123),
                    ["group_seed_2713"] = ("ca46e4d1afdb080d", 141)
                };

            try
            {
                for (int caseIndex = 0; caseIndex < expectedCases.Length; caseIndex++)
                {
                    AiSquadLabController lab = null;
                    for (int frame = 0; frame < 120; frame++)
                    {
                        yield return null;
                        lab = Object.FindAnyObjectByType<AiSquadLabController>();
                        if (lab != null && lab.IsReady && lab.ActiveCaseIndex == caseIndex) break;
                    }

                    BattleDirector director = Object.FindAnyObjectByType<BattleDirector>();
                    Assert.That(lab, Is.Not.Null, $"AI Squad Lab case {caseIndex} did not load.");
                    Assert.That(lab.IsReady, Is.True, $"AI Squad Lab case {caseIndex} did not become ready.");
                    Assert.That(lab.ActiveCase.id, Is.EqualTo(expectedCases[caseIndex]));
                    Assert.That(director.Scenario.kind, Is.EqualTo(BattleScenarioKind.AiSquadLab));
                    Assert.That(director.SpawnedMembers.Count(item => item.Team == TeamId.TeamA), Is.EqualTo(3));
                    Assert.That(director.SpawnedMembers.Count(item => item.Team == TeamId.TeamB), Is.EqualTo(3));
                    Assert.That(director.SpawnedMembers.Any(item => item.IsPlayerControlled), Is.False);
                    Assert.That(Object.FindObjectsByType<CombatTankAI>(), Has.Length.EqualTo(6));
                    Assert.That(director.StrategicPlanner, Is.Null);
                    Assert.That(director.StrategicCoordinator, Is.Null);
                    BattleTelemetry telemetry = BattleTelemetry.Attach(director, writeOutput: false);

                    lab.SetPaused(false);
                    Time.timeScale = 10f;
                    while (!lab.IsComplete) yield return null;
                    Time.timeScale = 1f;

                    string report = lab.FinalReport;
                    int minimumSamples = Mathf.FloorToInt(lab.ActiveCase.durationSeconds / 0.2f) * 6;
                    Assert.That(lab.Auditor.SampleCount, Is.GreaterThanOrEqualTo(minimumSamples));
                    foreach (string botId in expectedBots) Assert.That(report, Does.Contain(botId));
                    Debug.Log($"AI_SQUAD_DIAGNOSTIC case={lab.ActiveCase.id} seed={lab.ActiveCase.seed}\n" +
                              report + "\n" + lab.Auditor.BuildTransitionReport() +
                              $"\nfingerprint={telemetry.DecisionFingerprint} decisions={telemetry.FingerprintedDecisionCount}");

                    var signature = (telemetry.DecisionFingerprint, telemetry.FingerprintedDecisionCount);
                    Assert.That(signature, Is.EqualTo(expectedSignatures[lab.ActiveCase.id]),
                        $"Decision baseline diverged for {lab.ActiveCase.id}.\n{telemetry.DecisionTrace}");

                    if (caseIndex + 1 < expectedCases.Length) lab.ReloadCase(caseIndex + 1);
                }
            }
            finally
            {
                Time.timeScale = previousScale;
                Time.captureDeltaTime = previousCaptureDeltaTime;
            }
        }

        [UnityTest]
        public IEnumerator PlayerAiTestLab_WaitsForSelectionThenSpawnsConfiguredTierFilteredBattle()
        {
            PlayerAiTestLabSettings settings = PlayerAiTestLabController.CurrentSettings;
            settings.playerTankId = "ms_1";
            settings.alliedBotCount = 1;
            settings.enemyBotCount = 2;
            settings.botTier = 1;
            settings.seed = 9231;
            SceneManager.LoadScene("Battle_PlayerAiTestLab");
            yield return null;
            yield return null;

            PlayerAiTestLabController lab = Object.FindAnyObjectByType<PlayerAiTestLabController>();
            BattleDirector director = Object.FindAnyObjectByType<BattleDirector>();
            Assert.That(lab, Is.Not.Null);
            Assert.That(director, Is.Not.Null);
            Assert.That(lab.IsBattleStarted, Is.False);
            Assert.That(director.SpawnedMembers, Is.Empty);
            PlayerAimReticle reticle = Camera.main.GetComponent<PlayerAimReticle>();
            Assert.That(reticle.UiInteractionActive, Is.True);

            lab.StartBattle();
            yield return null;

            Assert.That(lab.IsBattleStarted, Is.True);
            Assert.That(director.Scenario.kind, Is.EqualTo(BattleScenarioKind.PlayerAiTestLab));
            Assert.That(director.SpawnedMembers, Has.Count.EqualTo(4));
            Assert.That(director.SpawnedMembers.Count(item => item.Team == TeamId.TeamA), Is.EqualTo(2));
            Assert.That(director.SpawnedMembers.Count(item => item.Team == TeamId.TeamB), Is.EqualTo(2));
            Assert.That(director.SpawnedMembers.Count(item => item.IsPlayerControlled), Is.EqualTo(1));
            Assert.That(director.PlayerMember.Definition.id, Is.EqualTo("ms_1"));
            Assert.That(director.SpawnedMembers.Where(item => !item.IsPlayerControlled)
                .All(item => item.Definition.tier == 1), Is.True);
            Assert.That(director.StrategicPlanner, Is.Not.Null);
            Assert.That(director.StrategicPlanner.Diagnostics.providerName, Is.EqualTo("Deterministic"));
            Assert.That(director.StrategicCoordinator, Is.Not.Null);
            Assert.That(reticle.UiInteractionActive, Is.False);

            lab.FinishBattle("TestComplete");
            Assert.That(reticle.UiInteractionActive, Is.True);
            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator BattleTestRange_LoadsPlayerAndCamera()
        {
            SceneManager.LoadScene("Battle_TestRange");
            yield return null;

            GameObject player = GameObject.Find("MS1_Player");
            Assert.That(player, Is.Not.Null);
            Assert.That(Camera.main, Is.Not.Null);
            PolygonCollider2D hullCollider = player.GetComponent<PolygonCollider2D>();
            Assert.That(hullCollider, Is.Not.Null);
            Assert.That(hullCollider.points.Length, Is.GreaterThanOrEqualTo(12));
            Assert.That(player.GetComponent<BoxCollider2D>(), Is.Null);
            Assert.That(player.GetComponent<GunDispersionController>(), Is.Not.Null);
            PlayerAimReticle reticle = Camera.main.GetComponent<PlayerAimReticle>();
            Assert.That(reticle, Is.Not.Null);
            Assert.That(reticle.ControlledTank, Is.EqualTo(player.transform));

            PlayerTankSelector selector = Object.FindAnyObjectByType<PlayerTankSelector>();
            TestRangeTargetRespawner respawner = Object.FindAnyObjectByType<TestRangeTargetRespawner>();
            Assert.That(selector, Is.Not.Null);
            Assert.That(respawner, Is.Not.Null);
            // The range must offer every playable tank and nothing else. Checking the actual tank ids
            // keeps this honest without hard-coding a count that changes whenever a tank is added.
            string[] offered = respawner.CurrentTargets
                .Select(target => target.GetComponent<TankHealth>().Definition.id)
                .OrderBy(id => id)
                .ToArray();
            Assert.That(selector.AvailableTankCount, Is.EqualTo(respawner.CurrentTargets.Length));
            Assert.That(offered, Does.Contain("ms_1"));
            Assert.That(offered, Does.Contain("leichttraktor"));
            Assert.That(offered, Does.Contain("t1_cunningham"));
            Assert.That(offered, Does.Not.Contain("tiger_ii"));
            Assert.That(offered.Distinct().Count(), Is.EqualTo(offered.Length), "Each tank appears once.");
            Assert.That(respawner.CurrentTargets.Select(target =>
                    target.GetComponent<TankHealth>().Definition.id).Distinct().Count(),
                Is.EqualTo(selector.AvailableTankCount));
            Assert.That(respawner.CurrentTargets.Any(target =>
                target.GetComponent<TankHealth>().Definition.id == "ms_1"), Is.True);
            Assert.That(respawner.CurrentTargets.Any(target =>
                target.GetComponent<TankHealth>().Definition.id == "leichttraktor"), Is.True);
            Assert.That(respawner.CurrentTargets.Any(target =>
                target.GetComponent<TankHealth>().Definition.id == "tiger_ii"), Is.False);
            foreach (GameObject target in respawner.CurrentTargets)
            {
                Assert.That(target, Is.Not.Null);
                Assert.That(target.GetComponent<RotatingTankDisplay>(), Is.Not.Null, target.name);
                Assert.That(target.GetComponent<TankHealthBar>(), Is.Not.Null, target.name);
                Assert.That(target.GetComponent<PolygonCollider2D>().points.Length, Is.GreaterThanOrEqualTo(10), target.name);
            }
        }

        [UnityTest]
        public IEnumerator CameraZoom_ClampsToConfiguredRange()
        {
            SceneManager.LoadScene("Battle_TestRange");
            yield return null;

            CameraFollow2D follow = Camera.main.GetComponent<CameraFollow2D>();
            Assert.That(follow.MinimumZoom, Is.EqualTo(CameraFollow2D.CloseInspectionZoom));
            follow.SetZoom(100f, true);
            Assert.That(follow.TargetZoom, Is.EqualTo(follow.MaximumZoom));
            Assert.That(follow.CurrentZoom, Is.EqualTo(follow.MaximumZoom));

            follow.SetZoom(-100f, true);
            Assert.That(follow.TargetZoom, Is.EqualTo(follow.MinimumZoom));
            Assert.That(follow.CurrentZoom, Is.EqualTo(follow.MinimumZoom));
        }

        [UnityTest]
        public IEnumerator CameraAimMode_ShiftsForwardAndReturnsWithoutChangingZoom()
        {
            SceneManager.LoadScene("Battle_TestRange");
            yield return null;

            CameraFollow2D follow = Camera.main.GetComponent<CameraFollow2D>();
            follow.SetZoom(5f, true);
            float zoom = follow.CurrentZoom;
            follow.SetAimMode(true);
            yield return new WaitForSeconds(0.5f);

            Assert.That(follow.IsAimMode, Is.True);
            Assert.That(Vector2.Distance(follow.transform.position, follow.Target.position), Is.GreaterThan(1.5f));
            Assert.That(follow.CurrentZoom, Is.EqualTo(zoom).Within(0.001f));

            follow.SetAimMode(false);
            yield return new WaitForSeconds(0.65f);
            Assert.That(Vector2.Distance(follow.transform.position, follow.Target.position), Is.LessThan(0.25f));
            Assert.That(follow.CurrentZoom, Is.EqualTo(zoom).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator Selector_ReplacesPlayerAndRetargetsCamera()
        {
            SceneManager.LoadScene("Battle_TestRange");
            yield return null;

            PlayerTankSelector selector = Object.FindAnyObjectByType<PlayerTankSelector>();
            CameraFollow2D follow = Camera.main.GetComponent<CameraFollow2D>();

            Assert.That(selector, Is.Not.Null);
            Assert.That(selector.SelectTank(1), Is.True);
            yield return null;

            Assert.That(selector.CurrentTankIndex, Is.EqualTo(1));
            Assert.That(selector.CurrentTank.GetComponent<TankHealth>().Definition.id, Is.EqualTo("leichttraktor"));
            Assert.That(selector.CurrentTank.GetComponent<PlayerTankInput>().enabled, Is.True);
            Assert.That(follow.Target, Is.EqualTo(selector.CurrentTank.transform));
            Assert.That(Camera.main.GetComponent<PlayerAimReticle>().ControlledTank,
                Is.EqualTo(selector.CurrentTank.transform));
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

            GameObject oldTarget = GameObject.Find("Leichttraktor_Target");
            TankHealth oldHealth = oldTarget.GetComponent<TankHealth>();
            oldHealth.ApplyDamage(oldHealth.MaximumHitPoints);
            oldTarget.transform.position = Vector3.zero;

            respawner.RespawnAllTargets();
            yield return null;

            GameObject newTarget = GameObject.Find("Leichttraktor_Target");
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

            GameObject target = GameObject.Find("Leichttraktor_Target");
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
            GameObject target = GameObject.Find("Leichttraktor_Target");
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

            GameObject respawnedTarget = GameObject.Find("Leichttraktor_Target");
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

            GameObject movingAgent = GameObject.Find("NavAgent_Leichttraktor_North");
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
            Assert.That(Object.FindAnyObjectByType<NavigationDebugOverlay>().Visible, Is.True,
                "The dedicated navigation range may start with its diagnostic visible.");

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
            Assert.That(Object.FindAnyObjectByType<NavigationDebugOverlay>().Visible, Is.False,
                "A playable map must wait for F2 before showing navigation.");
            Assert.That(map.Width, Is.EqualTo(72));
            Assert.That(map.Height, Is.EqualTo(44));
            Vector2Int riverEdge = new Vector2Int(34, 1);
            Vector2Int riverCore = new Vector2Int(35, 1);
            Assert.That(map.GetTerrain(riverEdge), Is.EqualTo(TerrainKind.ShallowWater));
            Assert.That(map.IsBlocked(riverEdge), Is.False);
            Assert.That(map.GetSpeedMultiplier(riverEdge), Is.EqualTo(0.05f).Within(0.001f));
            Assert.That(map.GetAccelerationMultiplier(riverEdge), Is.EqualTo(0.05f).Within(0.001f));
            Assert.That(map.IsBlocked(riverCore), Is.True);
            TeamMember riverPlayerMember = Object.FindObjectsByType<TeamMember>()
                .Single(member => member.IsPlayerControlled);
            TankMotor riverPlayerMotor = riverPlayerMember.GetComponent<TankMotor>();
            TerrainMotorModifier riverPlayerTerrain = riverPlayerMember.GetComponent<TerrainMotorModifier>();
            Vector3 originalPlayerPosition = riverPlayerMember.transform.position;
            riverPlayerMember.transform.position = map.CellToWorld(riverEdge);
            yield return new WaitForFixedUpdate();
            Assert.That(riverPlayerTerrain.CurrentTerrain, Is.EqualTo(TerrainKind.ShallowWater));
            Assert.That(riverPlayerMotor.TerrainResistanceMultiplier, Is.EqualTo(8f));
            riverPlayerMember.transform.position = originalPlayerPosition;
            Physics2D.SyncTransforms();
            LineOfFireResult riverShot = LineOfFireProbe.Evaluate(
                riverPlayerMember.transform,
                map.CellToWorld(new Vector2Int(30, 1)),
                map.CellToWorld(new Vector2Int(40, 1)));
            Assert.That(riverShot.IsBlocked, Is.False);
            LineOfFireAssessment botRiverShot = CombatLineOfFire.Evaluate(
                riverPlayerMember.transform,
                TeamId.TeamA,
                map.CellToWorld(new Vector2Int(30, 1)),
                map.CellToWorld(new Vector2Int(40, 1)),
                null);
            Assert.That(botRiverShot.IsClear, Is.True);
            Assert.That(GameObject.Find("Base_A"), Is.Not.Null);
            Assert.That(GameObject.Find("Base_B"), Is.Not.Null);
            Assert.That(GameObject.Find("NorthStoneBridge"), Is.Not.Null);
            Assert.That(GameObject.Find("CentralStoneBridge"), Is.Not.Null);
            Assert.That(GameObject.Find("SouthWoodBridge"), Is.Not.Null);
            MapPrimitiveVisual[] mapVisuals = Object.FindObjectsByType<MapPrimitiveVisual>();
            MapPrimitiveVisual[] hardObstacles = mapVisuals
                .Where(item => item.Kind == MapPrimitiveKind.HardObstacle)
                .ToArray();
            MapPrimitiveVisual[] bushes = mapVisuals
                .Where(item => item.Kind == MapPrimitiveKind.Concealment)
                .ToArray();
            Assert.That(hardObstacles, Is.Not.Empty);
            Assert.That(bushes, Is.Not.Empty);
            Assert.That(hardObstacles, Has.All.Matches<MapPrimitiveVisual>(item =>
                item.GetComponent<SpriteRenderer>() != null &&
                Vector4.Distance(item.GetComponent<SpriteRenderer>().color, MapPrimitiveVisual.HardObstacleColor) < 0.001f));
            Assert.That(bushes, Has.All.Matches<MapPrimitiveVisual>(item =>
                item.GetComponent<CircleCollider2D>() != null &&
                item.GetComponent<SpriteRenderer>() != null &&
                Vector4.Distance(item.GetComponent<SpriteRenderer>().color, MapPrimitiveVisual.ConcealmentColor) < 0.001f));
            Assert.That(GameObject.Find("Farmhouse_27_27"), Is.Null);
            Assert.That(Object.FindObjectsByType<Tilemap>().Length, Is.EqualTo(4));
            Assert.That(GameObject.Find("NavigationOnlyBlockers").GetComponent<FirePassThroughObstacle>(), Is.Not.Null);
            Assert.That(agents.Length, Is.EqualTo(BattleScenario.TeamSize * 2 - 1));
            foreach (NavigationAgent navigationAgent in agents)
            {
                Assert.That(navigationAgent.HasPath || navigationAgent.DestinationReached, Is.True,
                    $"{navigationAgent.name} has neither a path nor a reached first waypoint");
            }
            Assert.That(Object.FindAnyObjectByType<TeamVisionSystem>(), Is.Not.Null);
            Assert.That(Object.FindAnyObjectByType<BattleRoster>(), Is.Not.Null);
            Assert.That(Object.FindAnyObjectByType<BattleHudController>(), Is.Not.Null);
            Assert.That(Object.FindObjectsByType<TeamMember>().Length, Is.EqualTo(BattleScenario.TeamSize * 2));
            Assert.That(Object.FindObjectsByType<TeamMember>().Count(member =>
                member.Definition != null && member.Definition.id == "ms_1"), Is.EqualTo(BattleScenario.TeamSize));
            Assert.That(Object.FindObjectsByType<TeamMember>().Count(member =>
                member.Definition != null && member.Definition.id == "leichttraktor"), Is.EqualTo(BattleScenario.TeamSize));
            Assert.That(Object.FindObjectsByType<TeamMember>().Any(member =>
                member.Definition != null && member.Definition.id == "tiger_ii"), Is.False);
            BattleDirector director = Object.FindAnyObjectByType<BattleDirector>();
            Assert.That(director, Is.Not.Null);
            Assert.That(director.TacticalMap, Is.Not.Null);
            Assert.That(director.TacticalMap.sceneId, Is.EqualTo("Battle_RiverCrossing"));
            Assert.That(director.TacticalMap.lanes.Select(lane => lane.id), Is.EqualTo(new[]
            {
                "lane_north", "lane_center", "lane_south"
            }));
            Assert.That(director.TeamAAlive, Is.EqualTo(BattleScenario.TeamSize));
            Assert.That(director.TeamBAlive, Is.EqualTo(BattleScenario.TeamSize));
            Assert.That(director.StrategicPlanner, Is.Not.Null);
            Assert.That(director.StrategicPlanner.HasUsablePlans, Is.True);
            Assert.That(director.StrategicPlanner.MapAnalysis.lanes.Select(lane => lane.laneId),
                Is.EquivalentTo(director.TacticalMap.lanes.Select(lane => lane.id)));
            Assert.That(director.StrategicPlanner.TeamAPlan.assignments.Length,
                Is.EqualTo(BattleScenario.TeamSize - 1));
            Assert.That(director.StrategicPlanner.TeamBPlan.assignments.Length,
                Is.EqualTo(BattleScenario.TeamSize));
            Assert.That(director.StrategicCoordinator, Is.Not.Null);
            Assert.That(director.DiagnosticsOverlay, Is.Not.Null);
            Assert.That(director.DiagnosticsOverlay.HelpVisible, Is.False);
            Assert.That(director.DiagnosticsOverlay.MapAnalysisVisible, Is.False);
            director.DiagnosticsOverlay.ToggleHelp();
            Assert.That(director.DiagnosticsOverlay.HelpVisible, Is.True);
            Assert.That(BattleDiagnosticsOverlay.AnyModalVisible, Is.True);
            director.DiagnosticsOverlay.ToggleMapAnalysis();
            Assert.That(director.DiagnosticsOverlay.HelpVisible, Is.False);
            Assert.That(director.DiagnosticsOverlay.MapAnalysisVisible, Is.True);
            director.DiagnosticsOverlay.CloseTopWindow();
            Assert.That(BattleDiagnosticsOverlay.AnyModalVisible, Is.False);
            Assert.That(director.StrategicCoordinator.Brains.Count,
                Is.EqualTo(BattleScenario.TeamSize * 2 - 1));
            Assert.That(director.StrategicCoordinator.Brains,
                Has.All.Matches<BotStrategicBrain>(brain =>
                    brain.CurrentSnapshot != null && brain.CurrentDecision != null &&
                    brain.RequestState == BotStrategicRequestState.Fallback));
            Assert.That(Object.FindObjectsByType<CombatTankAI>().Length, Is.EqualTo(BattleScenario.TeamSize * 2 - 1));
            CombatAiDebugOverlay aiOverlay = Object.FindAnyObjectByType<CombatAiDebugOverlay>();
            Assert.That(aiOverlay, Is.Not.Null);
            aiOverlay.SetVisible(true);
            CombatTankAI firstSelected = aiOverlay.Selected;
            Assert.That(firstSelected, Is.Not.Null);
            TankDebugSelectionOutline firstOutline =
                firstSelected.GetComponent<TankDebugSelectionOutline>();
            Assert.That(firstOutline, Is.Not.Null);
            Assert.That(firstOutline.IsVisible, Is.True);
            aiOverlay.SelectRelativeBot(1);
            Assert.That(aiOverlay.Selected, Is.Not.Null.And.Not.SameAs(firstSelected));
            Assert.That(firstOutline.IsVisible, Is.False);
            Assert.That(aiOverlay.Selected.GetComponent<TankDebugSelectionOutline>().IsVisible, Is.True);
            aiOverlay.SelectRelativeBot(-1);
            Assert.That(aiOverlay.Selected, Is.SameAs(firstSelected));
            Assert.That(firstOutline.IsVisible, Is.True);
            aiOverlay.SetVisible(false);
            Assert.That(firstOutline.IsVisible, Is.False);
            BotIdentityComponent[] identities = Object.FindObjectsByType<BotIdentityComponent>();
            Assert.That(identities.Length, Is.EqualTo(BattleScenario.TeamSize * 2 - 1));
            Assert.That(identities.Select(item => item.Identity.nickname)
                .Distinct(System.StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(identities.Length));
            Assert.That(identities, Has.All.Matches<BotIdentityComponent>(item =>
                item.Identity.personality != null && !string.IsNullOrWhiteSpace(item.Identity.personality.DominantTrait)));
            Assert.That(Object.FindObjectsByType<BotIdentityPresenter>().Length,
                Is.EqualTo(BattleScenario.TeamSize * 2 - 1));
            Assert.That(director.PlayerMember.GetComponent<BotIdentityComponent>(), Is.Null);
            Assert.That(Object.FindObjectsByType<ConcealmentZone>().Length, Is.GreaterThan(0));
            Assert.That(GameObject.Find("VisionBlockers").GetComponent<VisionBlocker>(), Is.Not.Null);
            BattleRoster roster = Object.FindAnyObjectByType<BattleRoster>();
            BattleHudController hud = Object.FindAnyObjectByType<BattleHudController>();
            yield return null;
            Assert.That(roster.Allies.Count, Is.EqualTo(BattleScenario.TeamSize));
            Assert.That(roster.Enemies.Count, Is.EqualTo(BattleScenario.TeamSize));
            Assert.That(hud.AllyList.childCount, Is.EqualTo(BattleScenario.TeamSize));
            Assert.That(hud.EnemyList.childCount, Is.EqualTo(BattleScenario.TeamSize));
            Assert.That(roster.Allies, Has.All.Matches<BattleRosterEntry>(entry => entry.ClassIcon != null));
            Assert.That(roster.Enemies, Has.All.Matches<BattleRosterEntry>(entry => entry.ClassIcon != null));
            VisualElement[] rosterRows = hud.AllyList.Children()
                .Concat(hud.EnemyList.Children())
                .ToArray();
            Assert.That(rosterRows, Has.All.Matches<VisualElement>(row =>
                row.Q<Image>(className: "class-icon")?.sprite != null));
            Assert.That(rosterRows, Has.All.Matches<VisualElement>(row =>
                row.Q<Label>(className: "tank-name")?.text.Length <= BattleHudController.MaximumTankNameLength));
            Label shortName = rosterRows
                .Select(row => row.Q<Label>(className: "tank-name"))
                .First(label => label.tooltip == "Leichttraktor");
            Assert.That(shortName.text, Is.EqualTo("Leichttraktor"));
            Assert.That(shortName.tooltip, Is.EqualTo("Leichttraktor"));
            Assert.That(Object.FindObjectsByType<TankClassIconPresenter>().Length, Is.EqualTo(BattleScenario.TeamSize * 2));
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
            Assert.That(follow.MinimumZoom, Is.EqualTo(CameraFollow2D.CloseInspectionZoom));
            TankDefinition playerDefinition = playerMember.GetComponent<TankHealth>().Definition;
            Assert.That(follow.MaximumZoom,
                Is.EqualTo(VisionRangeTable.UnitsFor(playerDefinition.vehicleClass, playerDefinition.tier)));
        }

        [UnityTest]
        public IEnumerator RiverCrossing_FakeLlmCreatesMapAnalysisAndIndependentTeamPlans()
        {
            SceneManager.LoadScene("Battle_RiverCrossing");
            yield return null;

            BattleDirector director = Object.FindAnyObjectByType<BattleDirector>();
            BattleStrategicPlanner planner = director.StrategicPlanner;
            Assert.That(planner.State, Is.EqualTo(StrategicPlanningState.FallbackReady));
            FakeLocalLlmProvider provider = new FakeLocalLlmProvider();
            provider.Enqueue(LocalLlmResponse.Success(
                MapStrategicAnalysisSchema.ToJson(planner.MapAnalysis), 11));
            provider.Enqueue(LocalLlmResponse.Success(
                TeamBattlePlanSchema.ToJson(planner.TeamAPlan), 12));
            provider.Enqueue(LocalLlmResponse.Success(
                TeamBattlePlanSchema.ToJson(planner.TeamBPlan), 13));

            planner.RunWithProvider(provider);
            for (int frame = 0; frame < 30 && !planner.RemotePlanningFinished; frame++)
            {
                yield return null;
            }

            Assert.That(planner.State, Is.EqualTo(StrategicPlanningState.Ready));
            Assert.That(provider.Requests.Select(item => item.purpose), Is.EqualTo(new[]
            {
                $"map-analysis:{director.TacticalMap.mapId}:{director.TacticalMap.sourceHash}",
                "team-plan:river_crossing_battle:TeamA",
                "team-plan:river_crossing_battle:TeamB"
            }));
            Assert.That(planner.Diagnostics.mapAnalysisFromLlm, Is.True);
            Assert.That(planner.Diagnostics.teamAPlanFromLlm, Is.True);
            Assert.That(planner.Diagnostics.teamBPlanFromLlm, Is.True);
            Assert.That(planner.Diagnostics.mapDurationMilliseconds, Is.EqualTo(11));
            Assert.That(planner.Diagnostics.teamADurationMilliseconds, Is.EqualTo(12));
            Assert.That(planner.Diagnostics.teamBDurationMilliseconds, Is.EqualTo(13));
            Assert.That(planner.TeamAPlan.teamId, Is.EqualTo("TeamA"));
            Assert.That(planner.TeamBPlan.teamId, Is.EqualTo("TeamB"));
            Assert.That(planner.TeamAPlan, Is.Not.SameAs(planner.TeamBPlan));
        }

        [UnityTest]
        public IEnumerator RiverCrossing_InvalidLlmResponsesKeepEveryDeterministicFallback()
        {
            SceneManager.LoadScene("Battle_RiverCrossing");
            yield return null;

            BattleStrategicPlanner planner = Object.FindAnyObjectByType<BattleDirector>().StrategicPlanner;
            MapStrategicAnalysis fallbackMap = planner.MapAnalysis;
            TeamBattlePlan fallbackA = planner.TeamAPlan;
            TeamBattlePlan fallbackB = planner.TeamBPlan;
            FakeLocalLlmProvider provider = new FakeLocalLlmProvider();
            provider.Enqueue(LocalLlmResponse.Success("{\"schemaVersion\":1,\"mapId\":\"invented\"}"));
            provider.Enqueue(LocalLlmResponse.Failure("team A timeout"));
            provider.Enqueue(LocalLlmResponse.Failure("team B malformed response"));

            planner.RunWithProvider(provider);
            for (int frame = 0; frame < 30 && !planner.RemotePlanningFinished; frame++)
            {
                yield return null;
            }

            Assert.That(planner.State, Is.EqualTo(StrategicPlanningState.PartialFallback));
            Assert.That(planner.MapAnalysis, Is.SameAs(fallbackMap));
            Assert.That(planner.TeamAPlan, Is.SameAs(fallbackA));
            Assert.That(planner.TeamBPlan, Is.SameAs(fallbackB));
            Assert.That(planner.Diagnostics.mapAnalysisFromLlm, Is.False);
            Assert.That(planner.Diagnostics.teamAPlanFromLlm, Is.False);
            Assert.That(planner.Diagnostics.teamBPlanFromLlm, Is.False);
            Assert.That(planner.Diagnostics.mapError, Is.Not.Empty);
            Assert.That(planner.Diagnostics.teamAError, Does.Contain("timeout"));
            Assert.That(planner.Diagnostics.teamBError, Does.Contain("malformed"));
        }

        [UnityTest]
        public IEnumerator RiverCrossing_StrategicCoordinatorQueuesAndAppliesThirteenValidDecisions()
        {
            SceneManager.LoadScene("Battle_RiverCrossing");
            yield return null;

            BattleDirector director = Object.FindAnyObjectByType<BattleDirector>();
            BotStrategicCoordinator coordinator = director.StrategicCoordinator;
            FakeLocalLlmProvider provider = new FakeLocalLlmProvider();
            foreach (BotStrategicBrain brain in coordinator.Brains)
            {
                BotStrategicDecision decision = new BotStrategicDecision
                {
                    requestId = $"{director.Scenario.id}:{brain.BotId}:0001",
                    botId = brain.BotId,
                    intent = BotStrategicIntentType.Wait,
                    laneId = string.Empty,
                    zoneId = string.Empty,
                    targetId = string.Empty,
                    allyId = string.Empty,
                    desiredDistance = brain.TacticalAi.PreferredDistance,
                    urgency = 0.4f,
                    durationSeconds = 10f,
                    explanation = "Жду развития боя по командному плану.",
                    message = string.Empty,
                    chatChannel = BotChatChannel.None
                };
                provider.Enqueue(LocalLlmResponse.Success(BotStrategicDecisionSchema.ToJson(decision), 5));
            }

            coordinator.RunInitialDecisionsWithProvider(provider);
            for (int frame = 0; frame < 90 && !coordinator.InitialDecisionsFinished; frame++)
            {
                yield return null;
            }

            Assert.That(coordinator.InitialDecisionsFinished, Is.True);
            Assert.That(coordinator.State, Is.EqualTo(BotStrategicCoordinatorState.Complete));
            Assert.That(provider.Requests.Count, Is.EqualTo(BattleScenario.TeamSize * 2 - 1));
            Assert.That(provider.Requests, Has.All.Matches<LocalLlmRequest>(request =>
                request.purpose.StartsWith("bot-decision:river_crossing_battle:")));
            Assert.That(coordinator.Brains, Has.All.Matches<BotStrategicBrain>(brain =>
                brain.DecisionFromLlm && brain.RequestState == BotStrategicRequestState.Applied &&
                brain.CurrentDecision.intent == BotStrategicIntentType.Wait &&
                brain.TacticalAi.StrategicDecision == brain.CurrentDecision));
        }

        [UnityTest]
        public IEnumerator RiverCrossing_RememberedEnemyNeverLeaksCurrentPositionOrHitPoints()
        {
            SceneManager.LoadScene("Battle_RiverCrossing");
            yield return null;

            BattleDirector director = Object.FindAnyObjectByType<BattleDirector>();
            BotStrategicBrain brain = director.StrategicCoordinator.Brains
                .First(item => item.GetComponent<TeamMember>().Team == TeamId.TeamA);
            TeamMember observer = brain.GetComponent<TeamMember>();
            TeamMember enemy = director.SpawnedMembers.First(item => item.Team == TeamId.TeamB && item.IsAlive);
            Vector2 visiblePosition = (Vector2)observer.transform.position + (Vector2)observer.VisionDirection.up * 1.8f;
            enemy.transform.position = visiblePosition;
            Physics2D.SyncTransforms();
            director.VisionSystem.ForceEvaluateAll();
            Assert.That(director.VisionSystem.IsVisibleTo(TeamId.TeamA, enemy), Is.True);

            enemy.transform.position = visiblePosition + new Vector2(100f, 100f);
            Physics2D.SyncTransforms();
            director.VisionSystem.ForceEvaluateAll();
            Assert.That(director.VisionSystem.IsVisibleTo(TeamId.TeamA, enemy), Is.False);
            Assert.That(director.VisionSystem.TryGetContact(TeamId.TeamA, enemy, out VisionContact contact), Is.True);
            Assert.That(contact.HasMemory, Is.True);

            enemy.transform.position = visiblePosition + new Vector2(-80f, 120f);
            Physics2D.SyncTransforms();
            BotKnowledgeSnapshot snapshot = BotKnowledgeSnapshotBuilder.Build(
                "memory-audit", director, brain.TacticalAi, director.StrategicPlanner, Time.time);
            BotEnemyKnowledge remembered = snapshot.enemies.Single(item => item.memberId == director.GetEntityId(enemy));

            Assert.That(remembered.contactState, Is.EqualTo(BotContactState.Remembered));
            Assert.That(remembered.positionIsCurrent, Is.False);
            Assert.That(remembered.currentHitPoints, Is.EqualTo(-1));
            Assert.That(remembered.positionX, Is.EqualTo(visiblePosition.x).Within(0.05f));
            Assert.That(remembered.positionY, Is.EqualTo(visiblePosition.y).Within(0.05f));
            Assert.That(new Vector2(remembered.positionX, remembered.positionY),
                Is.Not.EqualTo((Vector2)enemy.transform.position));
            Assert.That(BotKnowledgeSnapshotValidator.Validate(snapshot), Is.Empty);
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
            Assert.That(moved, Is.GreaterThanOrEqualTo(9), "most AI tanks should leave their initial slots");

            CombatTankAI destroyed = agents.First(item => item != null && item.enabled);
            TankHealth health = destroyed.GetComponent<TankHealth>();
            health.ApplyDamage(health.MaximumHitPoints);
            yield return null;

            Assert.That(destroyed.State, Is.EqualTo(CombatAiState.Destroyed));
            Assert.That(destroyed.GetComponent<NavigationAgent>().enabled, Is.False);
            Assert.That(destroyed.GetComponent<WeaponController>().enabled, Is.False);
            Assert.That(destroyed.GetComponent<BotIdentityPresenter>().ShouldRender, Is.False);
            Assert.That(destroyed.transform.Find("ClassIcon").GetComponent<SpriteRenderer>().enabled, Is.False);
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
                               item.GetComponent<TankHealth>().Definition.id == "ms_1");
            TeamMember target = Object.FindObjectsByType<TeamMember>()
                .First(item => item.Team == TeamId.TeamB && item.Definition.id == "leichttraktor");

            foreach (CombatTankAI ai in Object.FindObjectsByType<CombatTankAI>())
            {
                if (ai != shooter) ai.enabled = false;
            }
            NavigationAgent targetNavigation = target.GetComponent<NavigationAgent>();
            if (targetNavigation != null) targetNavigation.enabled = false;
            TankMotor targetMotor = target.GetComponent<TankMotor>();
            if (targetMotor != null) targetMotor.enabled = false;
            Rigidbody2D targetBody = target.GetComponent<Rigidbody2D>();
            if (targetBody != null) targetBody.linearVelocity = Vector2.zero;
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
            float timeout = Time.time + 4.5f;
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
        public IEnumerator RiverCrossing_CloseEnemyTriggersReverseOrbitBreak()
        {
            SceneManager.LoadScene("Battle_RiverCrossing");
            yield return null;

            NavigationMap map = Object.FindAnyObjectByType<NavigationMap>();
            TeamVisionSystem vision = Object.FindAnyObjectByType<TeamVisionSystem>();
            CombatTankAI shooter = Object.FindObjectsByType<CombatTankAI>()
                .First(item => item.GetComponent<TeamMember>().Team == TeamId.TeamA &&
                               item.GetComponent<TankHealth>().Definition.id == "ms_1");
            TeamMember shooterMember = shooter.GetComponent<TeamMember>();
            TeamMember target = Object.FindObjectsByType<TeamMember>()
                .First(item => item.Team == TeamId.TeamB);
            foreach (TeamMember other in TeamMember.ActiveMembers.ToArray())
            {
                if (other != shooterMember && other != target) other.gameObject.SetActive(false);
            }
            foreach (CombatTankAI ai in Object.FindObjectsByType<CombatTankAI>())
            {
                if (ai != shooter) ai.enabled = false;
            }

            NavigationAgent targetNavigation = target.GetComponent<NavigationAgent>();
            if (targetNavigation != null) targetNavigation.enabled = false;
            TankMotor targetMotor = target.GetComponent<TankMotor>();
            if (targetMotor != null) targetMotor.enabled = false;
            Rigidbody2D targetBody = target.GetComponent<Rigidbody2D>();
            if (targetBody != null)
            {
                targetBody.linearVelocity = Vector2.zero;
                targetBody.constraints = RigidbodyConstraints2D.FreezeAll;
            }

            Vector2 shooterPosition = map.CellToWorld(new Vector2Int(30, 22));
            shooter.transform.SetPositionAndRotation(shooterPosition, Quaternion.identity);
            Vector2 closeOffset = new Vector2(0.866f, 0.5f) * 2.5f;
            target.transform.SetPositionAndRotation(shooterPosition + closeOffset, Quaternion.identity);
            shooterMember.VisionDirection.rotation = Quaternion.identity;
            Physics2D.SyncTransforms();
            vision.ForceEvaluateAll();
            bool initiallyVisible = vision.IsVisibleTo(TeamId.TeamA, target);

            bool observedBreak = false;
            bool observedReverse = false;
            float timeout = Time.time + 2.5f;
            while (Time.time < timeout && !observedBreak)
            {
                observedBreak |= shooter.TacticalMovementPurpose == TacticalMovementPurpose.BreakCloseOrbit;
                observedReverse |= shooter.GetComponent<NavigationAgent>().MovingInReverse;
                yield return null;
            }

            Assert.That(initiallyVisible, Is.True, "Close target must start inside the light tank view arc.");
            Assert.That(observedBreak, Is.True,
                $"AI did not break close orbit; state={shooter.State}, task={shooter.TacticalMovementPurpose}, " +
                $"distance={shooter.CurrentTargetDistance:0.00}, aim={shooter.TurretAimErrorDegrees:0.0}, " +
                $"result={shooter.LastTacticalPositionResult}");
            Assert.That(observedReverse, Is.True, "Close-orbit break must preserve frontal armor by reversing away.");
        }

        [UnityTest]
        public IEnumerator RiverCrossing_CombatAiKeepsTacticalStateDuringBriefContactFlicker()
        {
            SceneManager.LoadScene("Battle_RiverCrossing");
            yield return null;

            NavigationMap map = Object.FindAnyObjectByType<NavigationMap>();
            TeamVisionSystem vision = Object.FindAnyObjectByType<TeamVisionSystem>();
            CombatTankAI shooter = Object.FindObjectsByType<CombatTankAI>()
                .First(item => item.GetComponent<TeamMember>().Team == TeamId.TeamA);
            TeamMember shooterMember = shooter.GetComponent<TeamMember>();
            TeamMember target = Object.FindObjectsByType<TeamMember>()
                .First(item => item.Team == TeamId.TeamB);

            foreach (TeamMember other in TeamMember.ActiveMembers.ToArray())
            {
                if (other != shooterMember && other != target) other.enabled = false;
            }
            foreach (CombatTankAI ai in Object.FindObjectsByType<CombatTankAI>())
            {
                if (ai != shooter) ai.enabled = false;
            }

            shooter.transform.SetPositionAndRotation(
                map.CellToWorld(new Vector2Int(30, 22)), Quaternion.Euler(0f, 0f, -90f));
            target.transform.SetPositionAndRotation(
                map.CellToWorld(new Vector2Int(34, 22)), Quaternion.Euler(0f, 0f, 90f));
            shooterMember.VisionDirection.rotation = Quaternion.Euler(0f, 0f, -90f);
            Physics2D.SyncTransforms();
            vision.ForceEvaluateAll();
            yield return new WaitForSeconds(0.35f);

            Assert.That(vision.IsVisibleTo(TeamId.TeamA, target), Is.True);
            Assert.That(shooter.CurrentTarget, Is.EqualTo(target));

            target.transform.position = new Vector2(100f, 100f);
            Physics2D.SyncTransforms();
            vision.ForceEvaluateAll();
            yield return new WaitForSeconds(0.35f);

            Assert.That(vision.IsVisibleTo(TeamId.TeamA, target), Is.False);
            Assert.That(shooter.CurrentTarget, Is.EqualTo(target));
            Assert.That(shooter.State, Is.Not.EqualTo(CombatAiState.Investigate),
                "A contact flicker shorter than the grace interval must not reset movement into investigation.");
        }

        [UnityTest]
        public IEnumerator RiverCrossing_CombatAiRoutesAroundProjectileBlockingObstacleToFire()
        {
            SceneManager.LoadScene("Battle_RiverCrossing");
            yield return null;

            NavigationMap map = Object.FindAnyObjectByType<NavigationMap>();
            TeamVisionSystem vision = Object.FindAnyObjectByType<TeamVisionSystem>();
            CombatTankAI shooter = Object.FindObjectsByType<CombatTankAI>()
                .First(item => item.GetComponent<TeamMember>().Team == TeamId.TeamA);
            TeamMember shooterMember = shooter.GetComponent<TeamMember>();
            TeamMember target = Object.FindObjectsByType<TeamMember>()
                .First(item => item.Team == TeamId.TeamB);
            foreach (TeamMember other in TeamMember.ActiveMembers.ToArray())
            {
                if (other != shooterMember && other != target) other.enabled = false;
            }
            foreach (CombatTankAI ai in Object.FindObjectsByType<CombatTankAI>())
            {
                if (ai != shooter) ai.enabled = false;
            }

            Vector2Int shooterCell = new Vector2Int(25, 22);
            Vector2Int targetCell = new Vector2Int(33, 22);
            Vector2Int[] blockedCells =
            {
                new Vector2Int(29, 21),
                new Vector2Int(29, 22),
                new Vector2Int(29, 23)
            };
            GameObject obstacle = new GameObject("TestProjectileOnlyObstacle");
            obstacle.transform.position = map.CellToWorld(new Vector2Int(29, 22));
            BoxCollider2D obstacleCollider = obstacle.AddComponent<BoxCollider2D>();
            obstacleCollider.size = new Vector2(map.CellSize, map.CellSize * 3f);
            foreach (Vector2Int cell in blockedCells) map.SetBlocked(cell, true);

            float previousScale = Time.timeScale;
            bool fired = false;
            try
            {
                shooter.transform.SetPositionAndRotation(
                    map.CellToWorld(shooterCell), Quaternion.Euler(0f, 0f, -90f));
                target.transform.SetPositionAndRotation(
                    map.CellToWorld(targetCell), Quaternion.Euler(0f, 0f, 90f));
                shooterMember.VisionDirection.rotation = Quaternion.Euler(0f, 0f, -90f);
                Physics2D.SyncTransforms();
                vision.ForceEvaluateAll();

                WeaponController weapon = shooter.GetComponent<WeaponController>();
                weapon.Fired += _ => fired = true;
                Time.timeScale = 3f;
                float timeout = Time.time + 15f;
                while (!fired && Time.time < timeout)
                {
                    yield return null;
                }

                Assert.That(vision.IsVisibleTo(TeamId.TeamA, target), Is.True,
                    "The test obstacle blocks projectiles, not team vision.");
                Assert.That(fired, Is.True,
                    $"AI did not route around obstacle; state={shooter.State}, " +
                    $"block={shooter.CurrentFireBlockReason}, planner={shooter.LastTacticalPositionResult}");
                Assert.That(shooter.LastTacticalPositionResult,
                    Is.EqualTo("Устойчивый прострел получен").Or.Contains("Объезд"));
            }
            finally
            {
                Time.timeScale = previousScale;
                foreach (Vector2Int cell in blockedCells) map.SetBlocked(cell, false);
                Object.Destroy(obstacle);
            }
        }

        [UnityTest]
        public IEnumerator RiverCrossing_CombatAiTurnsDerivedArmorAngleTowardVisibleThreat()
        {
            SceneManager.LoadScene("Battle_RiverCrossing");
            yield return null;

            NavigationMap map = Object.FindAnyObjectByType<NavigationMap>();
            TeamVisionSystem vision = Object.FindAnyObjectByType<TeamVisionSystem>();
            CombatTankAI shooter = Object.FindObjectsByType<CombatTankAI>()
                .First(item => item.GetComponent<TeamMember>().Team == TeamId.TeamA);
            TeamMember shooterMember = shooter.GetComponent<TeamMember>();
            TeamMember target = Object.FindObjectsByType<TeamMember>()
                .First(item => item.Team == TeamId.TeamB);
            foreach (TeamMember other in TeamMember.ActiveMembers.ToArray())
            {
                if (other != shooterMember && other != target) other.gameObject.SetActive(false);
            }
            foreach (CombatTankAI ai in Object.FindObjectsByType<CombatTankAI>())
            {
                if (ai != shooter) ai.enabled = false;
            }
            NavigationAgent targetNavigation = target.GetComponent<NavigationAgent>();
            if (targetNavigation != null) targetNavigation.enabled = false;
            TankMotor targetMotor = target.GetComponent<TankMotor>();
            if (targetMotor != null) targetMotor.enabled = false;
            Rigidbody2D targetBody = target.GetComponent<Rigidbody2D>();
            if (targetBody != null) targetBody.linearVelocity = Vector2.zero;

            shooter.transform.SetPositionAndRotation(
                map.CellToWorld(new Vector2Int(30, 22)), Quaternion.identity);
            target.transform.SetPositionAndRotation(
                map.CellToWorld(new Vector2Int(34, 22)), Quaternion.Euler(0f, 0f, 90f));
            shooterMember.VisionDirection.rotation = Quaternion.Euler(0f, 0f, -90f);
            Physics2D.SyncTransforms();
            vision.ForceEvaluateAll();

            bool observedArmorFacing = false;
            float timeout = Time.time + 4.2f;
            while (!observedArmorFacing && Time.time < timeout)
            {
                Vector2 direction = ((Vector2)target.transform.position -
                                     (Vector2)shooter.transform.position).normalized;
                float offset = Mathf.Abs(Vector2.SignedAngle(shooter.transform.up, direction));
                observedArmorFacing = shooter.GetComponent<NavigationAgent>().HasFacingDirection &&
                                      Mathf.Abs(offset - shooter.ArmorProfileUsage.SafeAngleDegrees) <= 6f;
                yield return null;
            }

            Assert.That(shooter.ArmorProfileUsage.CanAngle, Is.True);
            Assert.That(observedArmorFacing, Is.True,
                $"state={shooter.State}; target={shooter.CurrentTarget}; task={shooter.TacticalMovementPurpose}; " +
                $"block={shooter.CurrentFireBlockReason}; result={shooter.LastTacticalPositionResult}");
        }

        [UnityTest]
        public IEnumerator RiverCrossing_PenetratedLightTankFindsPhysicalCoverFromThreatDirection()
        {
            SceneManager.LoadScene("Battle_RiverCrossing");
            yield return null;

            NavigationMap map = Object.FindAnyObjectByType<NavigationMap>();
            CombatTankAI bot = Object.FindObjectsByType<CombatTankAI>()
                .First(item => item.GetComponent<TeamMember>().Team == TeamId.TeamA);
            TeamMember botMember = bot.GetComponent<TeamMember>();
            foreach (TeamMember other in TeamMember.ActiveMembers.ToArray())
            {
                if (other != botMember) other.gameObject.SetActive(false);
            }
            foreach (CombatTankAI ai in Object.FindObjectsByType<CombatTankAI>())
            {
                if (ai != bot) ai.enabled = false;
            }
            foreach (NavigationAgent otherNavigation in Object.FindObjectsByType<NavigationAgent>())
            {
                if (otherNavigation.gameObject != bot.gameObject) otherNavigation.enabled = false;
            }
            foreach (TankMotor otherMotor in Object.FindObjectsByType<TankMotor>())
            {
                if (otherMotor.gameObject != bot.gameObject) otherMotor.enabled = false;
            }

            Vector2Int botCell = new Vector2Int(29, 22);
            Vector2Int[] blockedCells =
            {
                new Vector2Int(27, 21),
                new Vector2Int(27, 22),
                new Vector2Int(27, 23)
            };
            GameObject obstacle = new GameObject("TestTacticalCover");
            obstacle.transform.position = map.CellToWorld(new Vector2Int(27, 22));
            obstacle.AddComponent<BoxCollider2D>().size = new Vector2(map.CellSize, map.CellSize * 3f);
            foreach (Vector2Int cell in blockedCells) map.SetBlocked(cell, true);

            float previousScale = Time.timeScale;
            bool reachedCover = false;
            try
            {
                bot.transform.SetPositionAndRotation(map.CellToWorld(botCell), Quaternion.identity);
                Rigidbody2D body = bot.GetComponent<Rigidbody2D>();
                body.linearVelocity = Vector2.zero;
                Physics2D.SyncTransforms();

                TankArmor tankArmor = bot.GetComponent<TankArmor>();
                tankArmor.ResolveImpact(
                    Vector2.right,
                    bot.transform.position,
                    Vector2.left,
                    penetration: 999f,
                    speed: 10f,
                    damage: 1,
                    ricochetAngle: 70f,
                    ricochetSpeedMultiplier: 0.7f,
                    ricochetPenetrationMultiplier: 0.65f,
                    remainingRicochets: 1);

                Time.timeScale = 3f;
                float timeout = Time.time + 30f;
                while (!reachedCover && Time.time < timeout)
                {
                    reachedCover = bot.LastTacticalPositionResult.Contains("Укрытие достигнуто");
                    yield return null;
                }

                Assert.That(bot.HasCoverPosition, Is.True, bot.LastTacticalPositionResult);
                NavigationAgent botNavigation = bot.GetComponent<NavigationAgent>();
                Assert.That(reachedCover, Is.True,
                    $"{bot.LastTacticalPositionResult}; state={bot.State}; task={bot.TacticalMovementPurpose}; " +
                    $"paused={botNavigation.MovementPaused}; hasPath={botNavigation.HasPath}; " +
                    $"destinationReached={botNavigation.DestinationReached}; " +
                    $"position={(Vector2)bot.transform.position}; cover={bot.CoverPosition}");
                LineOfFireAssessment protection = CombatLineOfFire.Evaluate(
                    bot.transform,
                    botMember.Team,
                    bot.CoverPosition + Vector2.left * botMember.Definition.viewRange,
                    bot.CoverPosition,
                    null);
                Assert.That(protection.BlockType,
                    Is.EqualTo(LineOfFireBlockType.HardObstacle));
            }
            finally
            {
                Time.timeScale = previousScale;
                foreach (Vector2Int cell in blockedCells) map.SetBlocked(cell, false);
                Object.Destroy(obstacle);
            }
        }

        [UnityTest]
        public IEnumerator RiverCrossing_CoverUserPeeksFiresAndReturnsInReverse()
        {
            UnityEngine.Random.State previousRandomState = UnityEngine.Random.state;
            UnityEngine.Random.InitState(1702);
            SceneManager.LoadScene("Battle_RiverCrossing");
            yield return null;

            NavigationMap map = Object.FindAnyObjectByType<NavigationMap>();
            TeamVisionSystem vision = Object.FindAnyObjectByType<TeamVisionSystem>();
            BattleDirector director = Object.FindAnyObjectByType<BattleDirector>();
            TeamMember botMember = director.FindMember("a_north_ms1_1");
            CombatTankAI bot = botMember.GetComponent<CombatTankAI>();
            TeamMember target = director.FindMember("b_north_leichttraktor_1");
            foreach (TeamMember other in TeamMember.ActiveMembers.ToArray())
            {
                if (other != botMember && other != target) other.gameObject.SetActive(false);
            }
            foreach (CombatTankAI ai in Object.FindObjectsByType<CombatTankAI>())
            {
                if (ai != bot) ai.enabled = false;
            }
            NavigationAgent targetNavigation = target.GetComponent<NavigationAgent>();
            if (targetNavigation != null) targetNavigation.enabled = false;
            TankMotor targetMotor = target.GetComponent<TankMotor>();
            if (targetMotor != null) targetMotor.enabled = false;
            Rigidbody2D targetBody = target.GetComponent<Rigidbody2D>();
            if (targetBody != null) targetBody.linearVelocity = Vector2.zero;
            TankDefinition durableTargetDefinition = Object.Instantiate(target.Definition);
            durableTargetDefinition.maxHitPoints = 10000;
            target.GetComponent<TankHealth>().Configure(durableTargetDefinition);

            Vector2Int[] blockedCells =
            {
                new Vector2Int(31, 20)
            };
            GameObject obstacle = new GameObject("TestPeekAndReturnCover");
            obstacle.transform.position = map.CellToWorld(new Vector2Int(31, 20));
            obstacle.AddComponent<BoxCollider2D>().size =
                new Vector2(map.CellSize * 0.6f, map.CellSize * 0.6f);
            foreach (Vector2Int cell in blockedCells) map.SetBlocked(cell, true);

            float previousScale = Time.timeScale;
            bool sawPair = false;
            bool sawAnyCover = false;
            bool sawCoverMovement = false;
            bool firedFromPeek = false;
            bool returnedInReverse = false;
            bool returnedToCover = false;
            bool usedForwardReturnFallback = false;
            float minimumReturnDistance = float.PositiveInfinity;
            float shotDistanceFromCover = float.PositiveInfinity;
            try
            {
                bot.transform.SetPositionAndRotation(
                    map.CellToWorld(new Vector2Int(29, 22)), Quaternion.identity);
                target.transform.SetPositionAndRotation(
                    map.CellToWorld(new Vector2Int(34, 22)), Quaternion.Euler(0f, 0f, 90f));
                Vector2 lookDirection =
                    ((Vector2)target.transform.position - (Vector2)bot.transform.position).normalized;
                botMember.VisionDirection.rotation = Quaternion.Euler(
                    0f,
                    0f,
                    Mathf.Atan2(lookDirection.y, lookDirection.x) * Mathf.Rad2Deg - 90f);
                Rigidbody2D botBody = bot.GetComponent<Rigidbody2D>();
                botBody.linearVelocity = Vector2.zero;
                Physics2D.SyncTransforms();
                vision.ForceEvaluateAll();

                int initialCoverCycleShots = bot.CompletedCoverCycleShots;
                bot.GetComponent<TankArmor>().ResolveImpact(
                    Vector2.left,
                    bot.transform.position,
                    Vector2.right,
                    penetration: 999f,
                    speed: 10f,
                    damage: 1,
                    ricochetAngle: 70f,
                    ricochetSpeedMultiplier: 0.7f,
                    ricochetPenetrationMultiplier: 0.65f,
                    remainingRicochets: 1);

                Time.timeScale = 3f;
                float timeout = Time.time + 120f;
                while (!returnedToCover && Time.time < timeout)
                {
                    sawAnyCover |= bot.HasCoverPosition;
                    sawPair |= bot.HasCoverPosition && bot.HasCoverFiringPosition;
                    sawCoverMovement |= bot.TacticalMovementPurpose == TacticalMovementPurpose.MoveToCover ||
                                        bot.TacticalMovementPurpose == TacticalMovementPurpose.DefensiveRetreat;
                    NavigationAgent botNavigation = bot.GetComponent<NavigationAgent>();
                    if (!firedFromPeek && bot.CompletedCoverCycleShots > initialCoverCycleShots)
                    {
                        firedFromPeek = true;
                        returnedInReverse = false;
                        usedForwardReturnFallback = false;
                        minimumReturnDistance = float.PositiveInfinity;
                        shotDistanceFromCover = Vector2.Distance(
                            bot.transform.position,
                            bot.CoverPosition);
                    }
                    returnedInReverse |= firedFromPeek && bot.LastCoverReturnStartedInReverse;
                    if (bot.TacticalMovementPurpose == TacticalMovementPurpose.ReturnToCover)
                    {
                        usedForwardReturnFallback |= returnedInReverse && !botNavigation.MovingInReverse;
                        minimumReturnDistance = Mathf.Min(
                            minimumReturnDistance,
                            Vector2.Distance(bot.transform.position, bot.CoverPosition));
                    }
                    // LastTacticalPositionResult is written only after the defensive movement
                    // has actually reached a protected position. The bot may legitimately leave
                    // Hold on the next decision tick when its target has just been destroyed, so
                    // requiring the transient state here makes the scenario order-dependent.
                    returnedToCover = firedFromPeek && returnedInReverse &&
                                      bot.LastTacticalPositionResult.Contains("Возврат в укрытие");
                    yield return null;
                }

                Assert.That(sawPair, Is.True,
                    $"{bot.LastTacticalPositionResult}; anyCover={sawAnyCover}; moving={sawCoverMovement}; " +
                    $"task={bot.TacticalMovementPurpose}; state={bot.State}");
                Assert.That(firedFromPeek, Is.True,
                    $"{bot.LastTacticalPositionResult}; task={bot.TacticalMovementPurpose}; " +
                    $"state={bot.State}; fire={bot.CurrentFireBlockReason}; " +
                    $"reverse={bot.GetComponent<NavigationAgent>().MovingInReverse}");
                Assert.That(returnedInReverse, Is.True,
                    $"{bot.LastTacticalPositionResult}; shotDistance={shotDistanceFromCover:0.00}; " +
                    $"hasCover={bot.HasCoverPosition}; reverseCommand={bot.LastCoverReturnStartedInReverse}");
                Assert.That(returnedToCover, Is.True,
                    $"{bot.LastTacticalPositionResult}; pos={bot.transform.position}; " +
                    $"cover={bot.CoverPosition}; minReturn={minimumReturnDistance:0.00}; " +
                    $"forwardFallback={usedForwardReturnFallback}; task={bot.TacticalMovementPurpose}");
            }
            finally
            {
                Time.timeScale = previousScale;
                UnityEngine.Random.state = previousRandomState;
                foreach (Vector2Int cell in blockedCells) map.SetBlocked(cell, false);
                Object.Destroy(obstacle);
                Object.Destroy(durableTargetDefinition);
            }
        }

        [UnityTest]
        public IEnumerator RiverCrossing_SixtySecondBehaviorAuditHasNoSevereAnomalies()
        {
            UnityEngine.Random.State previousRandomState = UnityEngine.Random.state;
            float previousCaptureDeltaTime = Time.captureDeltaTime;
            UnityEngine.Random.InitState(1707);
            Time.captureDeltaTime = 1f / 60f;
            SceneManager.LoadScene("Battle_RiverCrossing");
            yield return null;

            BattleDirector director = Object.FindAnyObjectByType<BattleDirector>();
            Assert.That(director, Is.Not.Null);
            CombatAiAuditSession session = new CombatAiAuditSession(director);
            float previousScale = Time.timeScale;
            float startedAt = Time.time;
            float nextSampleAt = startedAt;
            const float duration = 60f;
            try
            {
                Time.timeScale = 3f;
                while (Time.time - startedAt < duration)
                {
                    if (Time.time >= nextSampleAt)
                    {
                        session.Sample(Time.time - startedAt);
                        nextSampleAt += 0.2f;
                    }
                    yield return null;
                }
                session.Sample(duration);
            }
            finally
            {
                Time.timeScale = previousScale;
                UnityEngine.Random.state = previousRandomState;
                Time.captureDeltaTime = previousCaptureDeltaTime;
                session.Dispose();
            }

            string report = session.Auditor.BuildReport(duration);
            Debug.Log(report + "\n" + session.Auditor.BuildTransitionReport());
            CombatAiAuditIssue[] blockingIssues = session.Auditor.Issues.ToArray();
            Assert.That(blockingIssues, Is.Empty, report);
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
            MapPrimitiveVisual wallVisual = wall.GetComponent<MapPrimitiveVisual>();
            Assert.That(wallVisual, Is.Not.Null);
            Assert.That(wallVisual.Kind, Is.EqualTo(MapPrimitiveKind.DestructibleObstacle));
            Assert.That(Vector4.Distance(
                wall.GetComponentInChildren<SpriteRenderer>().color,
                MapPrimitiveVisual.DestructibleObstacleColor), Is.LessThan(0.001f));
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

            GameObject target = GameObject.Find("Leichttraktor_Target");
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
