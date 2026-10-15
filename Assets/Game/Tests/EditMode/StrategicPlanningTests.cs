using System;
using System.Collections.Generic;
using System.IO;
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
            Assert.That(StrategicLaneDistributionPolicy.Validate(
                teamA, descriptor, scenario, database, TeamId.TeamA), Is.Empty);
            Assert.That(StrategicLaneDistributionPolicy.Validate(
                teamB, descriptor, scenario, database, TeamId.TeamB), Is.Empty);
            AssertBalanced(teamA, descriptor);
            AssertBalanced(teamB, descriptor);
        }

        [Test]
        public void TeamPlan_RejectsPuttingEveryCompatibleBotOnOneDirection()
        {
            LoadProjectData(out TacticalMapDescriptor descriptor, out BattleScenario scenario, out CombatDatabase database);
            IReadOnlyDictionary<string, BotIdentity> identities = BuildIdentities(scenario, database);
            TeamBattlePlan plan = StrategicTeamPlanFallback.Create(
                descriptor, scenario, database, identities, TeamId.TeamB);
            string oneLane = RuntimeTacticalMapAnalyzer.SelectOperationalLanes(descriptor)[0].id;
            plan.primaryLaneId = oneLane;
            foreach (TeamPlanAssignment assignment in plan.assignments) assignment.laneId = oneLane;

            string[] errors = StrategicLaneDistributionPolicy.Validate(
                plan, descriptor, scenario, database, TeamId.TeamB);

            Assert.That(errors, Has.Some.Contains("concentrates bots"));
        }

        [Test]
        public void RuntimeScenario_BalancesBotsSeparatelyFromThePlayer()
        {
            LoadProjectData(out TacticalMapDescriptor descriptor, out BattleScenario source, out CombatDatabase database);
            BattleScenario authored = UnityEngine.ScriptableObject.CreateInstance<BattleScenario>();
            authored.id = source.id;
            authored.sceneId = source.sceneId;
            authored.kind = source.kind;
            authored.rules = source.rules;
            authored.entries = source.entries.Select(item => new BattleScenarioEntry
            {
                id = item.id,
                tankId = item.tankId,
                team = item.team,
                playerControlled = item.playerControlled,
                baseId = item.baseId,
                spawnSlot = item.spawnSlot,
                lane = item.lane,
                role = item.role
            }).ToArray();
            BattleScenarioEntry player = authored.entries.Single(item => item.playerControlled);
            BattleScenarioEntry northBot = authored.entries.First(item =>
                item.team == TeamId.TeamA && !item.playerControlled && item.lane == BattleLane.North);
            player.lane = BattleLane.North;
            northBot.lane = BattleLane.Center;

            BattleScenario runtime = StrategicLaneDistributionPolicy.CreateBalancedRuntimeScenario(
                authored, descriptor, database);
            int[] counts = Enum.GetValues(typeof(BattleLane)).Cast<BattleLane>()
                .Select(lane => runtime.entries.Count(item =>
                    item.team == TeamId.TeamA && !item.playerControlled && item.lane == lane))
                .ToArray();

            Assert.That(counts, Is.EqualTo(new[] { 2, 2, 2 }));
            Assert.That(runtime.entries.Single(item => item.playerControlled).lane, Is.EqualTo(BattleLane.North));
            UnityEngine.Object.DestroyImmediate(runtime);
            UnityEngine.Object.DestroyImmediate(authored);
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
            Assert.That(planRequest.userPrompt, Does.Contain("operationalLaneIds"));
            Assert.That(planRequest.systemPrompt, Does.Contain("максимально равномерно"));
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
            const string sourcePath = "Assets/Game/GameData/Maps/river_crossing.json";
            TacticalMapSource source = TacticalMapSource.Parse(File.ReadAllText(Path.GetFullPath(sourcePath)));
            descriptor = TacticalMapAnalyzer.Analyze(source);
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

        private static void AssertBalanced(TeamBattlePlan plan, TacticalMapDescriptor descriptor)
        {
            string[] laneIds = RuntimeTacticalMapAnalyzer.SelectOperationalLanes(descriptor)
                .Select(item => item.id)
                .ToArray();
            int[] counts = laneIds
                .Select(laneId => plan.assignments.Count(item => item.laneId == laneId))
                .ToArray();
            Assert.That(counts, Has.All.GreaterThan(0));
            Assert.That(counts.Max() - counts.Min(), Is.LessThanOrEqualTo(1),
                $"Expected an even operational split, got {string.Join("/", counts)}.");
        }
    }
}
