using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace LocalTanks.Tests
{
    public sealed class BattleLaunchConfigurationTests
    {
        [TearDown]
        public void ClearPending()
        {
            BattleLaunchConfiguration.ClearPending();
        }

        [Test]
        public void RuntimeRoster_AllowsIndependentTeamSizesAndVehicles()
        {
            CombatDatabase database = ScriptableObject.CreateInstance<CombatDatabase>();
            TankDefinition first = Tank("first", TankNation.Germany);
            TankDefinition second = Tank("second", TankNation.USSR);
            database.tanks = new[] { first, second };
            BattleScenario source = ScriptableObject.CreateInstance<BattleScenario>();
            source.id = "source";
            source.sceneId = "Battle_Test";
            source.kind = BattleScenarioKind.StandardBattle;
            source.entries = new BattleScenarioEntry[0];
            try
            {
                BattleLaunchConfiguration.Set("Battle_Test", new[] { "first", "second" }, new[] { "second" });
                BattleScenario runtime = BattleLaunchConfiguration.CreateRuntimeScenario(source, "Battle_Test", database);

                Assert.That(runtime, Is.Not.SameAs(source));
                Assert.That(runtime.allowsCustomRoster, Is.True);
                Assert.That(runtime.entries.Count(item => item.team == TeamId.TeamA), Is.EqualTo(2));
                Assert.That(runtime.entries.Count(item => item.team == TeamId.TeamB), Is.EqualTo(1));
                Assert.That(runtime.entries.Single(item => item.playerControlled).tankId, Is.EqualTo("first"));
                Assert.That(runtime.Validate(database), Is.Empty,
                    "A menu roster may mix nations and does not need the authored 2/3/2 lane split.");
                Object.DestroyImmediate(runtime);
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(database);
            }
        }

        [Test]
        public void Request_IsOnlyConsumedByItsSelectedScene()
        {
            BattleScenario source = ScriptableObject.CreateInstance<BattleScenario>();
            try
            {
                BattleLaunchConfiguration.Set("Battle_Mines", new[] { "tank" }, new[] { "tank" });
                Assert.That(BattleLaunchConfiguration.CreateRuntimeScenario(source, "Battle_RiverCrossing", null),
                    Is.SameAs(source));
                Assert.That(BattleLaunchConfiguration.Pending.sceneName, Is.EqualTo("Battle_Mines"));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        private static TankDefinition Tank(string id, TankNation nation)
        {
            TankDefinition tank = ScriptableObject.CreateInstance<TankDefinition>();
            tank.id = id;
            tank.displayName = id;
            tank.nation = nation;
            tank.availableInGame = true;
            return tank;
        }
    }
}
