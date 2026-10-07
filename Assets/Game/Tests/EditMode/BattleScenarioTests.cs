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
        public void RiverCrossingScenario_HasValidSymmetricFifteenTankTeams()
        {
            BattleScenario scenario = BattleScenarioImporter.Reimport();
            CombatDatabase database = AssetDatabase.LoadAssetAtPath<CombatDatabase>(
                CombatDefinitionImporter.DatabasePath);

            Assert.That(scenario.Validate(database), Is.Empty);
            Assert.That(scenario.entries.Count(item => item.team == TeamId.TeamA), Is.EqualTo(15));
            Assert.That(scenario.entries.Count(item => item.team == TeamId.TeamB), Is.EqualTo(15));
            Assert.That(scenario.entries.Count(item => item.playerControlled), Is.EqualTo(1));
            Assert.That(scenario.entries.GroupBy(item => $"{item.baseId}:{item.spawnSlot}")
                .All(group => group.Count() == 1), Is.True);
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
            scenario.entries = Enumerable.Range(0, 30).Select(index => new BattleScenarioEntry
            {
                id = $"tank_{index}",
                tankId = index == 29 ? "missing" : "known",
                team = index < 15 ? TeamId.TeamA : TeamId.TeamB,
                playerControlled = index == 0,
                baseId = index < 15 ? "A" : "B",
                spawnSlot = index == 1 ? 0 : index % 15,
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
