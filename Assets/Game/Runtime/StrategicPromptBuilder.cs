using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LocalTanks
{
    public static class StrategicPromptBuilder
    {
        [Serializable]
        private sealed class MapPromptPayload
        {
            public string mapId;
            public string displayName;
            public string sourceHash;
            public int width;
            public int height;
            public float cellSize;
            public LanePromptPayload[] lanes;
        }

        [Serializable]
        private sealed class LanePromptPayload
        {
            public string laneId;
            public string provisionalName;
            public int order;
            public float lengthWorld;
            public float averageWidthWorld;
            public float averageNavigationCost;
            public float hardCoverDensity;
            public float vegetationDensity;
            public float concealmentDensity;
            public float averageSightlineWorld;
            public float maximumSightlineWorld;
            public int chokePointCount;
            public bool hasMandatoryCrossing;
            public string[] terrainKinds;
            public TacticalClassSuitability suitability;
            public string[] connectedLaneIds;
        }

        [Serializable]
        private sealed class TeamPromptPayload
        {
            public string scenarioId;
            public string teamId;
            public string mapSourceHash;
            public MapStrategicAnalysis mapAnalysis;
            public TeamMemberPromptPayload[] members;
            public TeamMemberPromptPayload humanPlayer;
            public OpponentPromptPayload[] opponents;
        }

        [Serializable]
        private sealed class TeamMemberPromptPayload
        {
            public string botId;
            public string nickname;
            public string tankId;
            public string tankName;
            public string vehicleClass;
            public int tier;
            public string currentLaneId;
            public string role;
            public BotPersonalityProfile personality;
        }

        [Serializable]
        private sealed class OpponentPromptPayload
        {
            public string memberId;
            public string tankId;
            public string tankName;
            public string vehicleClass;
            public int tier;
        }

        public static LocalLlmRequest BuildMapAnalysisRequest(TacticalMapDescriptor descriptor)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            TacticalLaneDescriptor[] lanes = descriptor.lanes ?? Array.Empty<TacticalLaneDescriptor>();
            MapPromptPayload payload = new MapPromptPayload
            {
                mapId = descriptor.mapId,
                displayName = descriptor.displayName,
                sourceHash = descriptor.sourceHash,
                width = descriptor.width,
                height = descriptor.height,
                cellSize = descriptor.cellSize,
                lanes = lanes.OrderBy(item => item.order).Select(item => new LanePromptPayload
                {
                    laneId = item.id,
                    provisionalName = item.provisionalName,
                    order = item.order,
                    lengthWorld = item.lengthWorld,
                    averageWidthWorld = item.averageWidthWorld,
                    averageNavigationCost = item.averageNavigationCost,
                    hardCoverDensity = item.hardCoverDensity,
                    vegetationDensity = item.vegetationDensity,
                    concealmentDensity = item.concealmentDensity,
                    averageSightlineWorld = item.averageSightlineWorld,
                    maximumSightlineWorld = item.maximumSightlineWorld,
                    chokePointCount = item.chokePointCount,
                    hasMandatoryCrossing = item.hasMandatoryCrossing,
                    terrainKinds = (item.terrainShares ?? Array.Empty<TacticalTerrainShare>())
                        .Where(share => share != null && share.share >= 0.05f)
                        .OrderByDescending(share => share.share)
                        .Select(share => share.terrainKind)
                        .ToArray(),
                    suitability = item.suitability,
                    connectedLaneIds = (item.connections ?? Array.Empty<TacticalLaneConnection>())
                        .Where(connection => connection != null)
                        .Select(connection => connection.targetLaneId)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(id => id, StringComparer.Ordinal)
                        .ToArray()
                }).ToArray()
            };
            return new LocalLlmRequest
            {
                purpose = $"map-analysis:{descriptor.mapId}:{descriptor.sourceHash}",
                systemPrompt =
                    "Ты тактический аналитик одиночной танковой игры с видом сверху. " +
                    "Анализируй только переданный структурированный дескриптор. Не придумывай проходы, координаты, объекты или направления. " +
                    "Сохраняй laneId, mapId и sourceHash без изменений. Дай каждому направлению короткое уникальное русское название, " +
                    "практичное описание, преимущества, риски и подходящие классы. Ответ должен строго соответствовать JSON-схеме.",
                userPrompt = "Структурированное описание карты:\n" + JsonUtility.ToJson(payload) +
                             "\nВерни ровно один объект анализа без Markdown и дополнительного текста.",
                jsonSchema = MapStrategicAnalysisSchema.BuildJsonSchema(lanes.Length),
                temperature = 0.25f
            };
        }

        public static LocalLlmRequest BuildTeamPlanRequest(
            TacticalMapDescriptor descriptor,
            MapStrategicAnalysis analysis,
            BattleScenario scenario,
            CombatDatabase database,
            IReadOnlyDictionary<string, BotIdentity> identities,
            TeamId team)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            if (analysis == null) throw new ArgumentNullException(nameof(analysis));
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (team == TeamId.Neutral) throw new ArgumentException("Neutral team cannot receive a battle plan.", nameof(team));

            BattleScenarioEntry[] entries = (scenario.entries ?? Array.Empty<BattleScenarioEntry>())
                .Where(item => item != null && item.team == team)
                .OrderBy(item => item.id, StringComparer.Ordinal)
                .ToArray();
            TeamMemberPromptPayload[] bots = entries
                .Where(item => !item.playerControlled)
                .Select(item => BuildOwnMember(descriptor, database, identities, item))
                .ToArray();
            BattleScenarioEntry humanEntry = entries.FirstOrDefault(item => item.playerControlled);
            TeamMemberPromptPayload human = humanEntry != null
                ? BuildOwnMember(descriptor, database, identities, humanEntry)
                : null;
            OpponentPromptPayload[] opponents = (scenario.entries ?? Array.Empty<BattleScenarioEntry>())
                .Where(item => item != null && item.team != team && item.team != TeamId.Neutral)
                .OrderBy(item => item.id, StringComparer.Ordinal)
                .Select(item =>
                {
                    TankDefinition tank = database.FindTank(item.tankId);
                    return new OpponentPromptPayload
                    {
                        memberId = item.id,
                        tankId = item.tankId,
                        tankName = tank != null ? tank.displayName : item.tankId,
                        vehicleClass = tank != null ? tank.vehicleClass.ToString() : string.Empty,
                        tier = tank != null ? tank.tier : 0
                    };
                })
                .ToArray();
            TeamPromptPayload payload = new TeamPromptPayload
            {
                scenarioId = scenario.id,
                teamId = team.ToString(),
                mapSourceHash = descriptor.sourceHash,
                mapAnalysis = analysis,
                members = bots,
                humanPlayer = human,
                opponents = opponents
            };
            return new LocalLlmRequest
            {
                purpose = $"team-plan:{scenario.id}:{team}",
                systemPrompt =
                    "Ты командир команды в одиночной танковой игре. Составь стартовый план только для перечисленных ботов своей команды. " +
                    "Используй исключительно существующие botId и laneId. Учитывай класс, текущую роль и числовой темперамент, " +
                    "публичный состав противника, но не назначай и не контролируй человека. Каждый бот должен встретиться ровно один раз. " +
                    "Выбери разные главное и вспомогательное направления, не раскрывай план противнику и не придумывай координаты. " +
                    "Если указан reserveBotId, этот бот обязан получить задачу Reserve. Ответ строго по JSON-схеме.",
                userPrompt = "Данные команды и общий анализ карты:\n" + JsonUtility.ToJson(payload) +
                             "\nВерни ровно один командный план без Markdown и дополнительного текста.",
                jsonSchema = TeamBattlePlanSchema.BuildJsonSchema(bots.Length),
                temperature = 0.4f
            };
        }

        private static TeamMemberPromptPayload BuildOwnMember(
            TacticalMapDescriptor descriptor,
            CombatDatabase database,
            IReadOnlyDictionary<string, BotIdentity> identities,
            BattleScenarioEntry item)
        {
            TankDefinition tank = database.FindTank(item.tankId);
            BotIdentity identity = null;
            identities?.TryGetValue(item.id, out identity);
            return new TeamMemberPromptPayload
            {
                botId = item.id,
                nickname = identity?.nickname ?? item.id,
                tankId = item.tankId,
                tankName = tank != null ? tank.displayName : item.tankId,
                vehicleClass = tank != null ? tank.vehicleClass.ToString() : string.Empty,
                tier = tank != null ? tank.tier : 0,
                currentLaneId = ResolveLaneId(descriptor, item.lane),
                role = item.role.ToString(),
                personality = identity?.personality
            };
        }

        public static string ResolveLaneId(TacticalMapDescriptor descriptor, BattleLane lane)
        {
            return RuntimeTacticalMapAnalyzer.SelectLane(descriptor, lane)?.id ?? string.Empty;
        }
    }

    public static class StrategicTeamPlanFallback
    {
        public static TeamBattlePlan Create(
            TacticalMapDescriptor descriptor,
            BattleScenario scenario,
            CombatDatabase database,
            IReadOnlyDictionary<string, BotIdentity> identities,
            TeamId team)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            TacticalLaneDescriptor[] lanes = (descriptor.lanes ?? Array.Empty<TacticalLaneDescriptor>())
                .Where(item => item != null)
                .OrderBy(item => item.order)
                .ToArray();
            BattleScenarioEntry[] bots = (scenario.entries ?? Array.Empty<BattleScenarioEntry>())
                .Where(item => item != null && item.team == team && !item.playerControlled)
                .OrderBy(item => item.id, StringComparer.Ordinal)
                .ToArray();
            string[] rankedLanes = lanes
                .OrderByDescending(lane => TeamSuitability(lane, bots, database))
                .ThenBy(lane => lane.order)
                .Select(lane => lane.id)
                .ToArray();
            string primary = rankedLanes.FirstOrDefault() ?? string.Empty;
            string secondary = rankedLanes.Skip(1).FirstOrDefault() ?? primary;
            BattleScenarioEntry reserve = bots
                .OrderByDescending(item => ReserveScore(item, identities))
                .ThenBy(item => item.id, StringComparer.Ordinal)
                .FirstOrDefault();
            TeamPlanAssignment[] assignments = bots.Select(item =>
            {
                bool isReserve = reserve != null && item.id == reserve.id;
                TeamPlanTask task = isReserve ? TeamPlanTask.Reserve : TaskForRole(item.role);
                return new TeamPlanAssignment
                {
                    botId = item.id,
                    laneId = StrategicPromptBuilder.ResolveLaneId(descriptor, item.lane),
                    task = task,
                    priority = isReserve ? 0.55f : item.role == CombatAiRole.Assault ? 0.9f : 0.75f,
                    rationale = isReserve
                        ? "Сохраняет возможность усилить ослабленное направление или защитить базу."
                        : $"Продолжает исходную роль {item.role} на назначенном направлении без ожидания модели."
                };
            }).ToArray();
            return new TeamBattlePlan
            {
                scenarioId = scenario.id,
                mapSourceHash = descriptor.sourceHash,
                teamId = team.ToString(),
                summary = $"Детерминированный план {team}: основное направление {primary}, резерв реагирует на изменение счёта и захват базы.",
                primaryLaneId = primary,
                secondaryLaneId = secondary,
                reserveBotId = reserve?.id ?? string.Empty,
                assignments = assignments,
                redeployConditions = new[]
                {
                    "Основное направление потеряло большинство назначенных машин.",
                    "Противник начал бесспорный захват союзной базы."
                },
                retreatConditions = new[]
                {
                    "Машина осталась без поддержки против превосходящих видимых сил.",
                    "Продолжение движения открывает несколько подтверждённых направлений огня."
                }
            };
        }

        private static float TeamSuitability(
            TacticalLaneDescriptor lane,
            IEnumerable<BattleScenarioEntry> bots,
            CombatDatabase database)
        {
            float sum = 0f;
            int count = 0;
            foreach (BattleScenarioEntry bot in bots)
            {
                TankDefinition tank = database?.FindTank(bot.tankId);
                if (tank == null || lane.suitability == null) continue;
                switch (tank.vehicleClass)
                {
                    case VehicleClass.LightTank: sum += lane.suitability.lightTank; break;
                    case VehicleClass.MediumTank: sum += lane.suitability.mediumTank; break;
                    case VehicleClass.HeavyTank: sum += lane.suitability.heavyTank; break;
                    case VehicleClass.TankDestroyer: sum += lane.suitability.tankDestroyer; break;
                    case VehicleClass.Artillery: sum += lane.suitability.artillery; break;
                }
                count++;
            }
            return count > 0 ? sum / count : 0f;
        }

        private static float ReserveScore(BattleScenarioEntry entry, IReadOnlyDictionary<string, BotIdentity> identities)
        {
            if (entry == null || identities == null || !identities.TryGetValue(entry.id, out BotIdentity identity) ||
                identity?.personality == null) return entry?.role == CombatAiRole.Support ? 1f : 0f;
            BotPersonalityProfile profile = identity.personality;
            return profile.teamwork + profile.helpfulness + profile.caution - profile.aggression * 0.25f +
                   (entry.role == CombatAiRole.Support ? 0.5f : 0f);
        }

        private static TeamPlanTask TaskForRole(CombatAiRole role)
        {
            switch (role)
            {
                case CombatAiRole.Assault: return TeamPlanTask.Assault;
                case CombatAiRole.Support: return TeamPlanTask.Support;
                default: return TeamPlanTask.Scout;
            }
        }
    }
}
