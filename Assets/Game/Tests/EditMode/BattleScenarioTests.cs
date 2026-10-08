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
                Is.EquivalentTo(database.tanks.Select(item => item.id)));
            Assert.That(scenario.entries.Count(item => item.tankId == "bt_2"), Is.EqualTo(2));
            Assert.That(scenario.entries.Count(item => item.tankId == "ms_1"), Is.EqualTo(2));
            Assert.That(scenario.entries.Count(item => item.tankId == "leichttraktor"), Is.EqualTo(2));
            Assert.That(scenario.entries.Count(item => item.tankId == "tiger_ii"), Is.EqualTo(2));
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
    }
}
