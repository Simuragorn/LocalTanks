using System;
using System.IO;
using System.Linq;
using LocalTanks.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LocalTanks.Tests
{
    public sealed class BattleScenarioTests
    {
        [Test]
        public void RiverCrossingScenario_HasValidSymmetricSevenTankTeams()
        {
            BattleScenario scenario = BattleScenarioImporter.Reimport();
            CombatDatabase database = AssetDatabase.LoadAssetAtPath<CombatDatabase>(
                CombatDefinitionImporter.DatabasePath);

            Assert.That(scenario.Validate(database), Is.Empty);
            Assert.That(scenario.entries.Count(item => item.team == TeamId.TeamA), Is.EqualTo(BattleScenario.TeamSize));
            Assert.That(scenario.entries.Count(item => item.team == TeamId.TeamB), Is.EqualTo(BattleScenario.TeamSize));
            Assert.That(scenario.entries.Count(item => item.playerControlled), Is.EqualTo(1));
            Assert.That(scenario.entries.GroupBy(item => $"{item.baseId}:{item.spawnSlot}")
                .All(group => group.Count() == 1), Is.True);
            // The fixed roster names its own vehicles; what matters is that every named vehicle is
            // actually playable. A tank can be available for the manual test laboratory without
            // joining this scenario, which has to stay a hand-authored seven versus seven.
            Assert.That(scenario.entries
                    .Select(item => database.FindTank(item.tankId))
                    .All(tank => tank != null && tank.availableInGame),
                Is.True);
            Assert.That(scenario.entries.Count(item => item.tankId == "ms_1"), Is.EqualTo(BattleScenario.TeamSize));
            Assert.That(scenario.entries.Count(item => item.tankId == "leichttraktor"), Is.EqualTo(BattleScenario.TeamSize));
            Assert.That(scenario.entries.Any(item => item.tankId == "tiger_ii"), Is.False);
            Assert.That(scenario.entries.Where(item => item.team == TeamId.TeamA)
                .Select(item => database.FindTank(item.tankId).nation).Distinct(), Is.EqualTo(new[] { TankNation.USSR }));
            Assert.That(scenario.entries.Where(item => item.team == TeamId.TeamB)
                .Select(item => database.FindTank(item.tankId).nation).Distinct(), Is.EqualTo(new[] { TankNation.Germany }));
        }

        [Test]
        public void AiDuelLabScenarioAndCases_AreValidAndContainOnlyTwoBots()
        {
            BattleScenario scenario = BattleScenarioImporter.ReimportDuelLab();
            AiDuelLabCatalog catalog = AiDuelLabCatalogImporter.Reimport();
            CombatDatabase database = AssetDatabase.LoadAssetAtPath<CombatDatabase>(
                CombatDefinitionImporter.DatabasePath);

            Assert.That(scenario.kind, Is.EqualTo(BattleScenarioKind.AiDuelLab));
            Assert.That(scenario.Validate(database), Is.Empty);
            Assert.That(scenario.entries, Has.Length.EqualTo(2));
            Assert.That(scenario.entries.Count(item => item.team == TeamId.TeamA), Is.EqualTo(1));
            Assert.That(scenario.entries.Count(item => item.team == TeamId.TeamB), Is.EqualTo(1));
            Assert.That(scenario.entries.Any(item => item.playerControlled), Is.False);
            Assert.That(catalog.Validate(), Is.Empty);
            Assert.That(catalog.cases.Select(item => item.id),
                Is.EquivalentTo(new[] { "open_center", "north_cover", "south_cover" }));
            Assert.That(catalog.cases.Select(item => item.seed).Distinct().Count(), Is.EqualTo(3));
        }

        [Test]
        public void AiSquadLabScenarioAndCases_AreValidThreeVersusThreeInputs()
        {
            BattleScenario scenario = BattleScenarioImporter.ReimportSquadLab();
            AiSquadLabCatalog catalog = AiSquadLabCatalogImporter.Reimport();
            CombatDatabase database = AssetDatabase.LoadAssetAtPath<CombatDatabase>(
                CombatDefinitionImporter.DatabasePath);

            Assert.That(scenario.kind, Is.EqualTo(BattleScenarioKind.AiSquadLab));
            Assert.That(scenario.Validate(database), Is.Empty);
            Assert.That(scenario.entries, Has.Length.EqualTo(6));
            Assert.That(scenario.entries.Count(item => item.team == TeamId.TeamA), Is.EqualTo(3));
            Assert.That(scenario.entries.Count(item => item.team == TeamId.TeamB), Is.EqualTo(3));
            Assert.That(scenario.entries.Any(item => item.playerControlled), Is.False);
            Assert.That(scenario.entries.Select(item => item.lane).Distinct(),
                Is.EquivalentTo(new[] { BattleLane.North, BattleLane.Center, BattleLane.South }));
            Assert.That(catalog.Validate(), Is.Empty);
            Assert.That(catalog.cases, Has.Length.EqualTo(3));
            Assert.That(catalog.cases.Select(item => item.seed).Distinct().Count(), Is.EqualTo(3));
        }

        [Test]
        public void ScenarioValidator_RejectsDuplicatedSpawnAndUnknownTank()
        {
            CombatDatabase database = ScriptableObject.CreateInstance<CombatDatabase>();
            TankDefinition knownTank = ScriptableObject.CreateInstance<TankDefinition>();
            knownTank.id = "known";
            database.tanks = new[] { knownTank };
            BattleScenario scenario = ScriptableObject.CreateInstance<BattleScenario>();
            scenario.id = "test";
            scenario.sceneId = "test_scene";
            int totalTankCount = BattleScenario.TeamSize * 2;
            scenario.entries = Enumerable.Range(0, totalTankCount).Select(index => new BattleScenarioEntry
            {
                id = $"tank_{index}",
                tankId = index == totalTankCount - 1 ? "missing" : "known",
                team = index < BattleScenario.TeamSize ? TeamId.TeamA : TeamId.TeamB,
                playerControlled = index == 0,
                baseId = index < BattleScenario.TeamSize ? "A" : "B",
                spawnSlot = index == 1 ? 0 : index % BattleScenario.SpawnSlotCount,
                lane = (BattleLane)(index % 3),
                role = CombatAiRole.Support
            }).ToArray();

            string[] errors = scenario.Validate(database);

            Assert.That(errors.Any(error => error.Contains("assigned more than once")), Is.True);
            Assert.That(errors.Any(error => error.Contains("missing tank")), Is.True);
            Object.DestroyImmediate(scenario);
            Object.DestroyImmediate(database);
            Object.DestroyImmediate(knownTank);
        }

        [Test]
        public void ScenarioValidator_RejectsMixedNationAndUnavailableTank()
        {
            TankDefinition german = ScriptableObject.CreateInstance<TankDefinition>();
            german.id = "german";
            german.nation = TankNation.Germany;
            german.availableInGame = true;
            TankDefinition soviet = ScriptableObject.CreateInstance<TankDefinition>();
            soviet.id = "soviet";
            soviet.nation = TankNation.USSR;
            soviet.availableInGame = true;
            TankDefinition hidden = ScriptableObject.CreateInstance<TankDefinition>();
            hidden.id = "hidden";
            hidden.nation = TankNation.Germany;
            hidden.availableInGame = false;
            CombatDatabase database = ScriptableObject.CreateInstance<CombatDatabase>();
            database.tanks = new[] { german, soviet, hidden };
            BattleScenario scenario = ScriptableObject.CreateInstance<BattleScenario>();
            scenario.id = "test";
            scenario.sceneId = "test_scene";
            scenario.entries = Enumerable.Range(0, BattleScenario.TeamSize * 2).Select(index => new BattleScenarioEntry
            {
                id = $"tank_{index}",
                tankId = index == 1 ? "soviet" : index == BattleScenario.TeamSize ? "hidden" : "german",
                team = index < BattleScenario.TeamSize ? TeamId.TeamA : TeamId.TeamB,
                playerControlled = index == 0,
                baseId = index < BattleScenario.TeamSize ? "A" : "B",
                spawnSlot = index % BattleScenario.TeamSize,
                lane = index % BattleScenario.TeamSize < 2 ? BattleLane.North :
                    index % BattleScenario.TeamSize < 5 ? BattleLane.Center : BattleLane.South,
                role = CombatAiRole.Support
            }).ToArray();

            string[] errors = scenario.Validate(database);

            Assert.That(errors.Any(error => error.Contains("exactly one nation")), Is.True);
            Assert.That(errors.Any(error => error.Contains("unavailable tank")), Is.True);
            Object.DestroyImmediate(scenario);
            Object.DestroyImmediate(database);
            Object.DestroyImmediate(german);
            Object.DestroyImmediate(soviet);
            Object.DestroyImmediate(hidden);
        }

        [Test]
        public void PlayerAiTestLabScenarioFactory_CreatesDeterministicTierFilteredNationalTeams()
        {
            TankDefinition playerTank = CreateTank("player_tank", TankNation.USSR, 5);
            TankDefinition alliedTierOne = CreateTank("ally_tier_one", TankNation.USSR, 1);
            TankDefinition enemyTierOneA = CreateTank("enemy_tier_one_a", TankNation.Germany, 1);
            TankDefinition enemyTierOneB = CreateTank("enemy_tier_one_b", TankNation.Germany, 1);
            TankDefinition wrongTier = CreateTank("wrong_tier", TankNation.Germany, 2);
            CombatDatabase database = ScriptableObject.CreateInstance<CombatDatabase>();
            database.tanks = new[] { playerTank, alliedTierOne, enemyTierOneA, enemyTierOneB, wrongTier };
            GameObject directorObject = new GameObject("Director");
            BattleDirector director = directorObject.AddComponent<BattleDirector>();
            GameObject[] prefabs = database.tanks.Select(item => new GameObject(item.id + "_Prefab")).ToArray();
            director.Configure(
                null,
                database,
                null,
                null,
                null,
                null,
                database.tanks.Select((item, index) => new TankPrefabBinding
                {
                    tankId = item.id,
                    prefab = prefabs[index]
                }).ToArray(),
                null);
            BattleScenario template = CreateFullTemplate();
            PlayerAiTestLabSettings settings = new PlayerAiTestLabSettings
            {
                playerTankId = playerTank.id,
                alliedBotCount = 2,
                enemyBotCount = 3,
                botTier = 1,
                seed = 4127
            };

            BattleScenario first = PlayerAiTestLabScenarioFactory.Create(director, template, settings);
            BattleScenario second = PlayerAiTestLabScenarioFactory.Create(director, template, settings);

            Assert.That(first.Validate(database), Is.Empty);
            Assert.That(first.kind, Is.EqualTo(BattleScenarioKind.PlayerAiTestLab));
            Assert.That(first.entries.Count(item => item.team == TeamId.TeamA), Is.EqualTo(3));
            Assert.That(first.entries.Count(item => item.team == TeamId.TeamB), Is.EqualTo(3));
            Assert.That(first.entries.Single(item => item.playerControlled).tankId, Is.EqualTo(playerTank.id));
            Assert.That(first.entries.Where(item => !item.playerControlled)
                .All(item => database.FindTank(item.tankId).tier == 1), Is.True);
            Assert.That(first.entries.Where(item => item.team == TeamId.TeamA)
                .Select(item => database.FindTank(item.tankId).nation).Distinct(),
                Is.EqualTo(new[] { TankNation.USSR }));
            Assert.That(first.entries.Where(item => item.team == TeamId.TeamB)
                .Select(item => database.FindTank(item.tankId).nation).Distinct(),
                Is.EqualTo(new[] { TankNation.Germany }));
            Assert.That(first.entries.Select(item => item.tankId),
                Is.EqualTo(second.entries.Select(item => item.tankId)));

            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
            Object.DestroyImmediate(template);
            Object.DestroyImmediate(directorObject);
            foreach (GameObject prefab in prefabs) Object.DestroyImmediate(prefab);
            Object.DestroyImmediate(database);
            foreach (TankDefinition definition in new[]
                     { playerTank, alliedTierOne, enemyTierOneA, enemyTierOneB, wrongTier })
                Object.DestroyImmediate(definition);
        }

        [Test]
        public void PlayerAiTestLabScenarioFactory_ExplainsMissingAlliedTier()
        {
            TankDefinition playerTank = CreateTank("player_tank", TankNation.USSR, 5);
            TankDefinition germanTierOne = CreateTank("german_tier_one", TankNation.Germany, 1);
            CombatDatabase database = ScriptableObject.CreateInstance<CombatDatabase>();
            database.tanks = new[] { playerTank, germanTierOne };
            GameObject directorObject = new GameObject("Director");
            BattleDirector director = directorObject.AddComponent<BattleDirector>();
            GameObject playerPrefab = new GameObject("PlayerPrefab");
            GameObject germanPrefab = new GameObject("GermanPrefab");
            director.Configure(null, database, null, null, null, null, new[]
            {
                new TankPrefabBinding { tankId = playerTank.id, prefab = playerPrefab },
                new TankPrefabBinding { tankId = germanTierOne.id, prefab = germanPrefab }
            }, null);
            BattleScenario template = CreateFullTemplate();
            PlayerAiTestLabSettings settings = new PlayerAiTestLabSettings
            {
                playerTankId = playerTank.id,
                alliedBotCount = 1,
                enemyBotCount = 1,
                botTier = 1,
                seed = 1
            };

            string[] errors = PlayerAiTestLabScenarioFactory.Validate(director, template, settings);

            Assert.That(errors.Any(item => item.Contains("Для союзников нет доступной техники")), Is.True);
            Object.DestroyImmediate(template);
            Object.DestroyImmediate(directorObject);
            Object.DestroyImmediate(playerPrefab);
            Object.DestroyImmediate(germanPrefab);
            Object.DestroyImmediate(database);
            Object.DestroyImmediate(playerTank);
            Object.DestroyImmediate(germanTierOne);
        }

        [Test]
        public void PlayerAiTestBattleReportStore_WritesAnalysisBundleAndLatestPointer()
        {
            string root = Path.Combine(Path.GetTempPath(), "LocalTanks_ReportStore_" + Guid.NewGuid().ToString("N"));
            try
            {
                PlayerAiTestBattleReport report = new PlayerAiTestBattleReport
                {
                    sessionId = "test:session",
                    outcome = "PlayerTeamVictory",
                    durationSeconds = 42f,
                    seed = 17,
                    playerTankId = "ms_1",
                    botTier = 1,
                    alliedBotCount = 1,
                    enemyBotCount = 2,
                    playerComment = "Бот долго показывал корму."
                };
                PlayerAiTestTelemetrySample sample = new PlayerAiTestTelemetrySample
                {
                    time = 1f,
                    entityId = "enemy_bot_01",
                    aiState = "Engage"
                };

                string directory = PlayerAiTestBattleReportStore.Save(
                    report,
                    new[] { sample },
                    "# audit",
                    "# transitions",
                    root);

                Assert.That(File.Exists(Path.Combine(directory, "report.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(directory, "telemetry.jsonl")), Is.True);
                Assert.That(File.Exists(Path.Combine(directory, "ai_audit.md")), Is.True);
                Assert.That(File.Exists(Path.Combine(directory, "transitions.md")), Is.True);
                Assert.That(File.ReadAllText(Path.Combine(directory, "analysis_brief.md")),
                    Does.Contain(report.playerComment));
                Assert.That(File.ReadAllText(Path.Combine(root, "latest_report_path.txt")), Is.EqualTo(directory));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void CombatAiPolicy_PrefersClearCurrentDamagedTarget()
        {
            float preferred = CombatAiPolicy.ScoreTarget(3f, 10f, 0.2f, 10f, true, true);
            float alternative = CombatAiPolicy.ScoreTarget(3f, 10f, 1f, 10f, false, false);

            Assert.That(preferred, Is.GreaterThan(alternative));
        }

        [TestCase(false, false, false, false, CombatAiState.Destroyed)]
        [TestCase(true, true, false, false, CombatAiState.Engage)]
        [TestCase(true, false, true, true, CombatAiState.Investigate)]
        [TestCase(true, false, false, false, CombatAiState.Deploy)]
        [TestCase(true, false, false, true, CombatAiState.Advance)]
        public void CombatAiPolicy_ResolvesState(
            bool alive,
            bool visible,
            bool remembered,
            bool routeStarted,
            CombatAiState expected)
        {
            Assert.That(CombatAiPolicy.ResolveState(alive, visible, remembered, routeStarted), Is.EqualTo(expected));
        }

        [Test]
        public void CombatAiPolicy_DelaysInvestigationAfterBriefContactLoss()
        {
            Assert.That(CombatAiPolicy.ShouldDelayInvestigation(10.5f, 10f), Is.True);
            Assert.That(CombatAiPolicy.ShouldDelayInvestigation(11f, 10f), Is.False);
            Assert.That(CombatAiPolicy.ShouldDelayInvestigation(10f, float.NegativeInfinity), Is.False);
        }

        [Test]
        public void CombatAiPolicy_CommitsToReachableRepositionUntilArrivalOrTimeout()
        {
            Assert.That(CombatAiPolicy.ShouldKeepRepositioning(true, false, 2f, 4f), Is.True);
            Assert.That(CombatAiPolicy.ShouldKeepRepositioning(false, false, 2f, 4f), Is.False);
            Assert.That(CombatAiPolicy.ShouldKeepRepositioning(true, true, 2f, 4f), Is.False);
            Assert.That(CombatAiPolicy.ShouldKeepRepositioning(true, false, 4f, 4f), Is.False);
        }

        [Test]
        public void CombatAiPolicy_RequiresStableClearLineEvenAtDestinationBeforeFinishingReposition()
        {
            Assert.That(CombatAiPolicy.ShouldCompleteFiringLineManeuver(true, false, 10f, 10f), Is.False);
            Assert.That(CombatAiPolicy.ShouldCompleteFiringLineManeuver(true, false, 10f, 10.74f), Is.False);
            Assert.That(CombatAiPolicy.ShouldCompleteFiringLineManeuver(true, false, 10f, 10.75f), Is.True);
            Assert.That(CombatAiPolicy.ShouldCompleteFiringLineManeuver(true, true, 10f, 10.34f), Is.False);
            Assert.That(CombatAiPolicy.ShouldCompleteFiringLineManeuver(true, true, 10f, 10.35f), Is.True);
            Assert.That(CombatAiPolicy.ShouldCompleteFiringLineManeuver(false, true, 9f, 10f), Is.False);
        }

        [Test]
        public void CombatAiPolicy_AlignsHullBeforeAdvancingTowardRearThreat()
        {
            Assert.That(CombatAiPolicy.ShouldAlignHullBeforeAdvancing(120f), Is.True);
            Assert.That(CombatAiPolicy.ShouldAlignHullBeforeAdvancing(-120f), Is.True);
            Assert.That(CombatAiPolicy.ShouldAlignHullBeforeAdvancing(90f), Is.False);
            Assert.That(CombatAiPolicy.ShouldAlignHullBeforeAdvancing(45f), Is.False);
        }

        [Test]
        public void CombatAiPolicy_BreaksCloseOrbitWhenTurretCannotTrackTarget()
        {
            Assert.That(CombatAiPolicy.ShouldBreakCloseOrbit(2.5f, 3f, 16f, 5f, 20f), Is.True);
            Assert.That(CombatAiPolicy.ShouldBreakCloseOrbit(2.5f, 3f, 4f, 18f, 20f), Is.True);
            Assert.That(CombatAiPolicy.ShouldBreakCloseOrbit(2.5f, 3f, 4f, 5f, 20f), Is.False);
            Assert.That(CombatAiPolicy.ShouldBreakCloseOrbit(4f, 3f, 30f, 30f, 20f), Is.False);
        }

        [Test]
        public void CombatAiPolicy_SwitchesVisibleTargetOnlyAfterLockAndMeaningfulAdvantage()
        {
            Assert.That(CombatAiPolicy.ShouldSwitchVisibleTarget(2f, 3f, 50f, 100f), Is.False);
            Assert.That(CombatAiPolicy.ShouldSwitchVisibleTarget(4f, 3f, 50f, 59f), Is.False);
            Assert.That(CombatAiPolicy.ShouldSwitchVisibleTarget(4f, 3f, 50f, 60f), Is.True);
        }

        [Test]
        public void CombatAiPolicy_KeepsDeployingAgainstADistantContactOnly()
        {
            // While the route is unfinished a distant enemy does not stop the march, and a close one does.
            Assert.That(CombatAiPolicy.ShouldDeploymentOutrankContact(false, 40f, 15f), Is.True);
            Assert.That(CombatAiPolicy.ShouldDeploymentOutrankContact(false, 15f, 15f), Is.False);
            Assert.That(CombatAiPolicy.ShouldDeploymentOutrankContact(false, 8f, 15f), Is.False);
            // Once the position is taken the route no longer outranks anything.
            Assert.That(CombatAiPolicy.ShouldDeploymentOutrankContact(true, 40f, 15f), Is.False);
        }

        [Test]
        public void CombatAiPolicy_CloseThreatDistanceComesFromTheGuaranteedRadius()
        {
            TankDefinition light = CreateTank("light", TankNation.USSR, 1);
            light.vehicleClass = VehicleClass.LightTank;
            light.viewRange = 62.58f;
            light.guaranteedDetectionRange = 10.01f;
            Assert.That(CombatAiPolicy.CloseThreatDistance(light), Is.EqualTo(15.015f).Within(0.001f));

            // A missing definition must not produce a zero radius that would stop the deployment on any contact.
            Assert.That(CombatAiPolicy.CloseThreatDistance(null), Is.GreaterThan(0f));
        }

        private static TankDefinition CreateTank(string id, TankNation nation, int tier)
        {
            TankDefinition tank = ScriptableObject.CreateInstance<TankDefinition>();
            tank.id = id;
            tank.displayName = id;
            tank.nation = nation;
            tank.tier = tier;
            tank.availableInGame = true;
            return tank;
        }

        private static BattleScenario CreateFullTemplate()
        {
            BattleScenario scenario = ScriptableObject.CreateInstance<BattleScenario>();
            scenario.id = "template";
            scenario.sceneId = "river_crossing";
            scenario.kind = BattleScenarioKind.StandardBattle;
            scenario.entries = Enumerable.Range(0, BattleScenario.TeamSize * 2).Select(index =>
            {
                bool teamA = index < BattleScenario.TeamSize;
                int teamIndex = index % BattleScenario.TeamSize;
                return new BattleScenarioEntry
                {
                    id = $"template_{index}",
                    tankId = "unused",
                    team = teamA ? TeamId.TeamA : TeamId.TeamB,
                    playerControlled = teamA && teamIndex == 0,
                    baseId = teamA ? "A" : "B",
                    spawnSlot = new[] { 7, 5, 9, 3, 11, 1, 13 }[teamIndex],
                    lane = teamIndex < 2 ? BattleLane.North : teamIndex < 5 ? BattleLane.Center : BattleLane.South,
                    role = CombatAiRole.Support
                };
            }).ToArray();
            return scenario;
        }
    }
}
