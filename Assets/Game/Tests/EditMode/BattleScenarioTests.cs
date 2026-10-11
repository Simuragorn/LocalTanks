using System.Linq;
using LocalTanks.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

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
            Assert.That(scenario.entries.Select(item => item.tankId).Distinct(),
                Is.EquivalentTo(database.tanks.Where(item => item.availableInGame).Select(item => item.id)));
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
                role = CombatAiRole.Support,
                routeCells = new[] { Vector2Int.zero, Vector2Int.one }
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
                role = CombatAiRole.Support,
                routeCells = new[] { Vector2Int.zero, Vector2Int.one }
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
        public void CombatAiPolicy_SwitchesVisibleTargetOnlyAfterLockAndMeaningfulAdvantage()
        {
            Assert.That(CombatAiPolicy.ShouldSwitchVisibleTarget(2f, 3f, 50f, 100f), Is.False);
            Assert.That(CombatAiPolicy.ShouldSwitchVisibleTarget(4f, 3f, 50f, 59f), Is.False);
            Assert.That(CombatAiPolicy.ShouldSwitchVisibleTarget(4f, 3f, 50f, 60f), Is.True);
        }
    }
}
