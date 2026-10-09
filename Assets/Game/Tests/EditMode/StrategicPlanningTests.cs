using System;
using System.Collections.Generic;
using System.Linq;
using LocalTanks.Editor;
using NUnit.Framework;
using UnityEditor;

namespace LocalTanks.Tests
{
    public sealed class StrategicPlanningTests
    {
        [Test]
        public void DeterministicFallback_ProducesValidMapAnalysisAndTwoTeamPlans()
        {
            LoadProjectData(out TacticalMapDescriptor descriptor, out BattleScenario scenario, out CombatDatabase database);
            IReadOnlyDictionary<string, BotIdentity> identities = BuildIdentities(scenario, database);

            MapStrategicAnalysis map = StrategicMapFallback.Create(descriptor);
            TeamBattlePlan teamA = StrategicTeamPlanFallback.Create(
                descriptor, scenario, database, identities, TeamId.TeamA);
            TeamBattlePlan teamB = StrategicTeamPlanFallback.Create(
                descriptor, scenario, database, identities, TeamId.TeamB);

            Assert.That(MapStrategicAnalysisValidator.Validate(map, descriptor), Is.Empty);
            Assert.That(TeamBattlePlanValidator.Validate(teamA, Context(descriptor, scenario, TeamId.TeamA)), Is.Empty);
            Assert.That(TeamBattlePlanValidator.Validate(teamB, Context(descriptor, scenario, TeamId.TeamB)), Is.Empty);
            Assert.That(teamA, Is.Not.SameAs(teamB));
            Assert.That(teamA.assignments.Select(item => item.botId),
                Is.EquivalentTo(scenario.entries.Where(item => item.team == TeamId.TeamA && !item.playerControlled)
                    .Select(item => item.id)));
            Assert.That(teamB.assignments.Select(item => item.botId),
                Is.EquivalentTo(scenario.entries.Where(item => item.team == TeamId.TeamB && !item.playerControlled)
                    .Select(item => item.id)));
        }

        [Test]
        public void MapAnalysis_RoundTripsAndRejectsInventedLane()
        {
            LoadProjectData(out TacticalMapDescriptor descriptor, out _, out _);
            MapStrategicAnalysis source = StrategicMapFallback.Create(descriptor);

            bool parsed = MapStrategicAnalysisSchema.TryParse(
                MapStrategicAnalysisSchema.ToJson(source), out MapStrategicAnalysis roundTrip, out string parseError);

            Assert.That(parsed, Is.True, parseError);
            Assert.That(MapStrategicAnalysisValidator.Validate(roundTrip, descriptor), Is.Empty);
            roundTrip.lanes[0].laneId = "invented_secret_tunnel";
            string[] errors = MapStrategicAnalysisValidator.Validate(roundTrip, descriptor);
            Assert.That(errors.Any(item => item.Contains("unknown")), Is.True);
            Assert.That(errors.Any(item => item.Contains("omits")), Is.True);
        }

        [Test]
        public void TeamPlan_StringTasksRoundTripAndRejectOpponentBot()
        {
            LoadProjectData(out TacticalMapDescriptor descriptor, out BattleScenario scenario, out CombatDatabase database);
            IReadOnlyDictionary<string, BotIdentity> identities = BuildIdentities(scenario, database);
            TeamBattlePlan source = StrategicTeamPlanFallback.Create(
                descriptor, scenario, database, identities, TeamId.TeamA);

            bool parsed = TeamBattlePlanSchema.TryParse(
                TeamBattlePlanSchema.ToJson(source), out TeamBattlePlan roundTrip, out string parseError);

            Assert.That(parsed, Is.True, parseError);
            Assert.That(TeamBattlePlanValidator.Validate(roundTrip, Context(descriptor, scenario, TeamId.TeamA)), Is.Empty);
            roundTrip.assignments[0].botId = scenario.entries.First(item => item.team == TeamId.TeamB).id;
            Assert.That(TeamBattlePlanValidator.Validate(roundTrip, Context(descriptor, scenario, TeamId.TeamA)),
                Has.Some.Contains("unknown bot"));
        }

        [Test]
        public void Prompts_UseCompactDescriptorAndStrictSchemas()
        {
            LoadProjectData(out TacticalMapDescriptor descriptor, out BattleScenario scenario, out CombatDatabase database);
            IReadOnlyDictionary<string, BotIdentity> identities = BuildIdentities(scenario, database);
            MapStrategicAnalysis map = StrategicMapFallback.Create(descriptor);

            LocalLlmRequest mapRequest = StrategicPromptBuilder.BuildMapAnalysisRequest(descriptor);
            LocalLlmRequest planRequest = StrategicPromptBuilder.BuildTeamPlanRequest(
                descriptor, map, scenario, database, identities, TeamId.TeamA);

            Assert.That(mapRequest.purpose, Does.StartWith("map-analysis:"));
            Assert.That(mapRequest.userPrompt, Does.Contain(descriptor.sourceHash));
            Assert.That(mapRequest.userPrompt, Does.Not.Contain("pathCells"));
            Assert.That(mapRequest.jsonSchema, Does.Contain("\"additionalProperties\":false"));
            Assert.That(mapRequest.jsonSchema, Does.Contain($"\"minItems\":{descriptor.lanes.Length}"));
            Assert.That(planRequest.purpose, Is.EqualTo($"team-plan:{scenario.id}:TeamA"));
            Assert.That(planRequest.userPrompt, Does.Contain("humanPlayer"));
            Assert.That(planRequest.userPrompt, Does.Contain("opponents"));
            Assert.That(planRequest.userPrompt, Does.Contain("personality"));
            Assert.That(planRequest.jsonSchema, Does.Contain("\"Reserve\""));
        }

        [Test]
        public void Schemas_RequireClosedObjectsAndEveryKnownIdentifierIsValidated()
        {
            LoadProjectData(out TacticalMapDescriptor descriptor, out BattleScenario scenario, out CombatDatabase database);
            IReadOnlyDictionary<string, BotIdentity> identities = BuildIdentities(scenario, database);
            TeamBattlePlan plan = StrategicTeamPlanFallback.Create(
                descriptor, scenario, database, identities, TeamId.TeamB);
            plan.primaryLaneId = "imaginary_lane";
            plan.reserveBotId = "enemy_or_missing";

            string[] errors = TeamBattlePlanValidator.Validate(plan, Context(descriptor, scenario, TeamId.TeamB));

            Assert.That(MapStrategicAnalysisSchema.BuildJsonSchema(descriptor.lanes.Length),
                Does.Contain("\"additionalProperties\":false"));
            Assert.That(TeamBattlePlanSchema.BuildJsonSchema(plan.assignments.Length),
                Does.Contain("\"additionalProperties\":false"));
            Assert.That(errors.Any(item => item.Contains("primary lane")), Is.True);
            Assert.That(errors.Any(item => item.Contains("reserveBotId")), Is.True);
        }

        private static void LoadProjectData(
            out TacticalMapDescriptor descriptor,
            out BattleScenario scenario,
            out CombatDatabase database)
        {
            TacticalMapDescriptorAsset mapAsset = AssetDatabase.LoadAssetAtPath<TacticalMapDescriptorAsset>(
                TacticalMapDescriptorImporter.AssetPath);
            descriptor = mapAsset.descriptor;
            scenario = AssetDatabase.LoadAssetAtPath<BattleScenario>(BattleScenarioImporter.AssetPath);
            database = AssetDatabase.LoadAssetAtPath<CombatDatabase>(CombatDefinitionImporter.DatabasePath);
            Assert.That(descriptor, Is.Not.Null);
            Assert.That(scenario, Is.Not.Null);
            Assert.That(database, Is.Not.Null);
        }

        private static IReadOnlyDictionary<string, BotIdentity> BuildIdentities(
            BattleScenario scenario,
            CombatDatabase database)
        {
            BotProfileContext[] contexts = scenario.entries
                .Where(item => item != null && !item.playerControlled)
                .Select(item =>
                {
                    TankDefinition tank = database.FindTank(item.tankId);
                    return new BotProfileContext
                    {
                        entityId = item.id,
                        team = item.team.ToString(),
                        tankId = item.tankId,
                        vehicleClass = tank.vehicleClass.ToString(),
                        nation = tank.nation.ToString(),
                        lane = item.lane.ToString(),
                        role = item.role.ToString()
                    };
                })
                .ToArray();
            return BotPersonalityGenerator.GenerateRoster(scenario.id, contexts)
                .ToDictionary(item => item.entityId, StringComparer.Ordinal);
        }

        private static TeamPlanValidationContext Context(
            TacticalMapDescriptor descriptor,
            BattleScenario scenario,
            TeamId team)
        {
            return new TeamPlanValidationContext
            {
                scenarioId = scenario.id,
                mapSourceHash = descriptor.sourceHash,
                teamId = team.ToString(),
                knownLaneIds = descriptor.lanes.Select(item => item.id).ToArray(),
                expectedBotIds = scenario.entries
                    .Where(item => item.team == team && !item.playerControlled)
                    .Select(item => item.id)
                    .ToArray()
            };
        }
    }
}
