using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LocalTanks
{
    [Serializable]
    public sealed class StrategicLaneAnalysis
    {
        public string laneId;
        public string name;
        public string description;
        public string advantages;
        public string risks;
        public string[] preferredClasses;
    }

    [Serializable]
    public sealed class MapStrategicAnalysis
    {
        public int schemaVersion = MapStrategicAnalysisSchema.SchemaVersion;
        public string mapId;
        public string sourceHash;
        public string summary;
        public StrategicLaneAnalysis[] lanes;
    }

    public static class MapStrategicAnalysisSchema
    {
        public const int SchemaVersion = 1;

        public static string BuildJsonSchema(int laneCount)
        {
            int count = Math.Max(1, laneCount);
            return "{" +
                   "\"type\":\"object\"," +
                   "\"additionalProperties\":false," +
                   "\"properties\":{" +
                   "\"schemaVersion\":{\"type\":\"integer\",\"const\":1}," +
                   "\"mapId\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":80}," +
                   "\"sourceHash\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":128}," +
                   "\"summary\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":400}," +
                   "\"lanes\":{\"type\":\"array\",\"minItems\":" + count + ",\"maxItems\":" + count + ",\"items\":{" +
                   "\"type\":\"object\",\"additionalProperties\":false,\"properties\":{" +
                   "\"laneId\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":80}," +
                   "\"name\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":40}," +
                   "\"description\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":300}," +
                   "\"advantages\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":240}," +
                   "\"risks\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":240}," +
                   "\"preferredClasses\":{\"type\":\"array\",\"minItems\":1,\"maxItems\":5,\"uniqueItems\":true,\"items\":{" +
                   "\"type\":\"string\",\"enum\":[\"LightTank\",\"MediumTank\",\"HeavyTank\",\"TankDestroyer\",\"Artillery\"]}}" +
                   "},\"required\":[\"laneId\",\"name\",\"description\",\"advantages\",\"risks\",\"preferredClasses\"]}}" +
                   "},\"required\":[\"schemaVersion\",\"mapId\",\"sourceHash\",\"summary\",\"lanes\"]}";
        }

        public static bool TryParse(string json, out MapStrategicAnalysis analysis, out string error)
        {
            analysis = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Map analysis JSON is empty.";
                return false;
            }

            try
            {
                analysis = JsonUtility.FromJson<MapStrategicAnalysis>(json);
            }
            catch (Exception exception)
            {
                error = $"Map analysis JSON is invalid: {exception.Message}";
                return false;
            }

            if (analysis == null)
            {
                error = "Map analysis JSON produced no object.";
                return false;
            }

            return true;
        }

        public static string ToJson(MapStrategicAnalysis analysis)
        {
            return JsonUtility.ToJson(analysis ?? throw new ArgumentNullException(nameof(analysis)));
        }
    }

    public static class MapStrategicAnalysisValidator
    {
        private static readonly HashSet<string> AllowedClasses = new HashSet<string>(
            new[] { "LightTank", "MediumTank", "HeavyTank", "TankDestroyer", "Artillery" },
            StringComparer.Ordinal);

        public static string[] Validate(MapStrategicAnalysis analysis, TacticalMapDescriptor descriptor)
        {
            List<string> errors = new List<string>();
            if (analysis == null)
            {
                errors.Add("Map strategic analysis is null.");
                return errors.ToArray();
            }

            if (descriptor == null)
            {
                errors.Add("Tactical map descriptor is null.");
                return errors.ToArray();
            }

            if (analysis.schemaVersion != MapStrategicAnalysisSchema.SchemaVersion)
                errors.Add($"Unsupported map analysis schema version {analysis.schemaVersion}.");
            if (!string.Equals(analysis.mapId, descriptor.mapId, StringComparison.Ordinal))
                errors.Add("Map analysis mapId does not match the tactical descriptor.");
            if (!string.Equals(analysis.sourceHash, descriptor.sourceHash, StringComparison.Ordinal))
                errors.Add("Map analysis sourceHash does not match the tactical descriptor.");
            ValidateText(errors, "summary", analysis.summary, 1, 400);

            TacticalLaneDescriptor[] sourceLanes = descriptor.lanes ?? Array.Empty<TacticalLaneDescriptor>();
            StrategicLaneAnalysis[] lanes = analysis.lanes ?? Array.Empty<StrategicLaneAnalysis>();
            if (lanes.Length != sourceLanes.Length)
                errors.Add($"Map analysis must contain exactly {sourceLanes.Length} lanes.");

            HashSet<string> knownIds = new HashSet<string>(
                sourceLanes.Where(item => item != null).Select(item => item.id), StringComparer.Ordinal);
            HashSet<string> seenIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (StrategicLaneAnalysis lane in lanes)
            {
                if (lane == null)
                {
                    errors.Add("Map analysis contains a null lane.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(lane.laneId) || !knownIds.Contains(lane.laneId))
                    errors.Add($"Map analysis lane '{lane.laneId}' is unknown.");
                else if (!seenIds.Add(lane.laneId))
                    errors.Add($"Map analysis lane '{lane.laneId}' is duplicated.");
                ValidateText(errors, $"lane '{lane.laneId}' name", lane.name, 1, 40);
                if (!string.IsNullOrWhiteSpace(lane.name) && !seenNames.Add(lane.name.Trim()))
                    errors.Add($"Map analysis lane name '{lane.name}' is duplicated.");
                ValidateText(errors, $"lane '{lane.laneId}' description", lane.description, 1, 300);
                ValidateText(errors, $"lane '{lane.laneId}' advantages", lane.advantages, 1, 240);
                ValidateText(errors, $"lane '{lane.laneId}' risks", lane.risks, 1, 240);
                string[] classes = lane.preferredClasses ?? Array.Empty<string>();
                if (classes.Length < 1 || classes.Length > 5)
                    errors.Add($"Map analysis lane '{lane.laneId}' needs 1..5 preferred classes.");
                if (classes.Distinct(StringComparer.Ordinal).Count() != classes.Length)
                    errors.Add($"Map analysis lane '{lane.laneId}' repeats preferred classes.");
                foreach (string vehicleClass in classes)
                {
                    if (!AllowedClasses.Contains(vehicleClass ?? string.Empty))
                        errors.Add($"Map analysis lane '{lane.laneId}' uses unknown class '{vehicleClass}'.");
                }
            }

            foreach (string missing in knownIds.Where(id => !seenIds.Contains(id)))
                errors.Add($"Map analysis omits lane '{missing}'.");
            return errors.ToArray();
        }

        private static void ValidateText(ICollection<string> errors, string label, string value, int minimum, int maximum)
        {
            int length = value?.Trim().Length ?? 0;
            if (length < minimum || length > maximum)
                errors.Add($"Map analysis {label} length must be {minimum}..{maximum} characters.");
        }
    }

    public enum TeamPlanTask
    {
        Scout,
        Assault,
        Support,
        Hold,
        Reserve
    }

    [Serializable]
    public sealed class TeamPlanAssignment
    {
        public string botId;
        public string laneId;
        public TeamPlanTask task;
        [Range(0f, 1f)] public float priority;
        public string rationale;
    }

    [Serializable]
    public sealed class TeamBattlePlan
    {
        public int schemaVersion = TeamBattlePlanSchema.SchemaVersion;
        public string scenarioId;
        public string mapSourceHash;
        public string teamId;
        public string summary;
        public string primaryLaneId;
        public string secondaryLaneId;
        public string reserveBotId;
        public TeamPlanAssignment[] assignments;
        public string[] redeployConditions;
        public string[] retreatConditions;
    }

    [Serializable]
    public sealed class TeamPlanAssignmentJson
    {
        public string botId;
        public string laneId;
        public string task;
        public float priority;
        public string rationale;
    }

    [Serializable]
    public sealed class TeamBattlePlanJson
    {
        public int schemaVersion;
        public string scenarioId;
        public string mapSourceHash;
        public string teamId;
        public string summary;
        public string primaryLaneId;
        public string secondaryLaneId;
        public string reserveBotId;
        public TeamPlanAssignmentJson[] assignments;
        public string[] redeployConditions;
        public string[] retreatConditions;
    }

    [Serializable]
    public sealed class TeamPlanValidationContext
    {
        public string scenarioId;
        public string mapSourceHash;
        public string teamId;
        public string[] knownLaneIds;
        public string[] expectedBotIds;
    }

    public static class TeamBattlePlanSchema
    {
        public const int SchemaVersion = 1;

        public static string BuildJsonSchema(int botCount)
        {
            int count = Math.Max(1, botCount);
            return "{" +
                   "\"type\":\"object\",\"additionalProperties\":false,\"properties\":{" +
                   "\"schemaVersion\":{\"type\":\"integer\",\"const\":1}," +
                   "\"scenarioId\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":80}," +
                   "\"mapSourceHash\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":128}," +
                   "\"teamId\":{\"type\":\"string\",\"enum\":[\"TeamA\",\"TeamB\"]}," +
                   "\"summary\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":400}," +
                   "\"primaryLaneId\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":80}," +
                   "\"secondaryLaneId\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":80}," +
                   "\"reserveBotId\":{\"type\":\"string\",\"maxLength\":80}," +
                   "\"assignments\":{\"type\":\"array\",\"minItems\":" + count + ",\"maxItems\":" + count + ",\"items\":{" +
                   "\"type\":\"object\",\"additionalProperties\":false,\"properties\":{" +
                   "\"botId\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":80}," +
                   "\"laneId\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":80}," +
                   "\"task\":{\"type\":\"string\",\"enum\":[\"Scout\",\"Assault\",\"Support\",\"Hold\",\"Reserve\"]}," +
                   "\"priority\":{\"type\":\"number\",\"minimum\":0,\"maximum\":1}," +
                   "\"rationale\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":180}" +
                   "},\"required\":[\"botId\",\"laneId\",\"task\",\"priority\",\"rationale\"]}}," +
                   "\"redeployConditions\":{\"type\":\"array\",\"minItems\":1,\"maxItems\":4,\"items\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":180}}," +
                   "\"retreatConditions\":{\"type\":\"array\",\"minItems\":1,\"maxItems\":4,\"items\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":180}}" +
                   "},\"required\":[\"schemaVersion\",\"scenarioId\",\"mapSourceHash\",\"teamId\",\"summary\",\"primaryLaneId\",\"secondaryLaneId\",\"reserveBotId\",\"assignments\",\"redeployConditions\",\"retreatConditions\"]}";
        }

        public static bool TryParse(string json, out TeamBattlePlan plan, out string error)
        {
            plan = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Team plan JSON is empty.";
                return false;
            }

            TeamBattlePlanJson raw;
            try
            {
                raw = JsonUtility.FromJson<TeamBattlePlanJson>(json);
            }
            catch (Exception exception)
            {
                error = $"Team plan JSON is invalid: {exception.Message}";
                return false;
            }

            if (raw == null)
            {
                error = "Team plan JSON produced no object.";
                return false;
            }

            List<TeamPlanAssignment> assignments = new List<TeamPlanAssignment>();
            foreach (TeamPlanAssignmentJson assignment in raw.assignments ?? Array.Empty<TeamPlanAssignmentJson>())
            {
                if (assignment == null || !Enum.TryParse(assignment.task, false, out TeamPlanTask task))
                {
                    error = "Team plan contains an unknown assignment task.";
                    return false;
                }

                assignments.Add(new TeamPlanAssignment
                {
                    botId = assignment.botId,
                    laneId = assignment.laneId,
                    task = task,
                    priority = assignment.priority,
                    rationale = assignment.rationale
                });
            }

            plan = new TeamBattlePlan
            {
                schemaVersion = raw.schemaVersion,
                scenarioId = raw.scenarioId,
                mapSourceHash = raw.mapSourceHash,
                teamId = raw.teamId,
                summary = raw.summary,
                primaryLaneId = raw.primaryLaneId,
                secondaryLaneId = raw.secondaryLaneId,
                reserveBotId = raw.reserveBotId,
                assignments = assignments.ToArray(),
                redeployConditions = raw.redeployConditions,
                retreatConditions = raw.retreatConditions
            };
            return true;
        }

        public static string ToJson(TeamBattlePlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            TeamBattlePlanJson raw = new TeamBattlePlanJson
            {
                schemaVersion = plan.schemaVersion,
                scenarioId = plan.scenarioId,
                mapSourceHash = plan.mapSourceHash,
                teamId = plan.teamId,
                summary = plan.summary,
                primaryLaneId = plan.primaryLaneId,
                secondaryLaneId = plan.secondaryLaneId,
                reserveBotId = plan.reserveBotId,
                assignments = (plan.assignments ?? Array.Empty<TeamPlanAssignment>()).Select(item =>
                    new TeamPlanAssignmentJson
                    {
                        botId = item.botId,
                        laneId = item.laneId,
                        task = item.task.ToString(),
                        priority = item.priority,
                        rationale = item.rationale
                    }).ToArray(),
                redeployConditions = plan.redeployConditions,
                retreatConditions = plan.retreatConditions
            };
            return JsonUtility.ToJson(raw);
        }
    }

    public static class TeamBattlePlanValidator
    {
        public static string[] Validate(TeamBattlePlan plan, TeamPlanValidationContext context)
        {
            List<string> errors = new List<string>();
            if (plan == null)
            {
                errors.Add("Team battle plan is null.");
                return errors.ToArray();
            }

            if (context == null)
            {
                errors.Add("Team plan validation context is null.");
                return errors.ToArray();
            }

            if (plan.schemaVersion != TeamBattlePlanSchema.SchemaVersion)
                errors.Add($"Unsupported team plan schema version {plan.schemaVersion}.");
            if (!string.Equals(plan.scenarioId, context.scenarioId, StringComparison.Ordinal))
                errors.Add("Team plan scenarioId does not match the active scenario.");
            if (!string.Equals(plan.mapSourceHash, context.mapSourceHash, StringComparison.Ordinal))
                errors.Add("Team plan mapSourceHash does not match the active map.");
            if (!string.Equals(plan.teamId, context.teamId, StringComparison.Ordinal))
                errors.Add("Team plan teamId does not match the requested team.");
            ValidateText(errors, "summary", plan.summary, 1, 400);

            HashSet<string> lanes = new HashSet<string>(context.knownLaneIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            ValidateKnownReference(errors, "primary lane", plan.primaryLaneId, lanes, true);
            ValidateKnownReference(errors, "secondary lane", plan.secondaryLaneId, lanes, true);
            if (lanes.Count > 1 && !string.IsNullOrWhiteSpace(plan.primaryLaneId) &&
                string.Equals(plan.primaryLaneId, plan.secondaryLaneId, StringComparison.Ordinal))
                errors.Add("Team plan primary and secondary lanes must differ.");

            HashSet<string> expectedBots = new HashSet<string>(context.expectedBotIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            HashSet<string> assignedBots = new HashSet<string>(StringComparer.Ordinal);
            TeamPlanAssignment[] assignments = plan.assignments ?? Array.Empty<TeamPlanAssignment>();
            if (assignments.Length != expectedBots.Count)
                errors.Add($"Team plan must assign exactly {expectedBots.Count} bots.");
            foreach (TeamPlanAssignment assignment in assignments)
            {
                if (assignment == null)
                {
                    errors.Add("Team plan contains a null assignment.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(assignment.botId) || !expectedBots.Contains(assignment.botId))
                    errors.Add($"Team plan assignment references unknown bot '{assignment.botId}'.");
                else if (!assignedBots.Add(assignment.botId))
                    errors.Add($"Team plan assigns bot '{assignment.botId}' more than once.");
                ValidateKnownReference(errors, $"lane for bot '{assignment.botId}'", assignment.laneId, lanes, true);
                if (!Enum.IsDefined(typeof(TeamPlanTask), assignment.task))
                    errors.Add($"Team plan bot '{assignment.botId}' has an unknown task.");
                if (assignment.priority < 0f || assignment.priority > 1f)
                    errors.Add($"Team plan bot '{assignment.botId}' has priority outside 0..1.");
                ValidateText(errors, $"rationale for bot '{assignment.botId}'", assignment.rationale, 1, 180);
            }

            foreach (string missing in expectedBots.Where(id => !assignedBots.Contains(id)))
                errors.Add($"Team plan omits bot '{missing}'.");

            if (!string.IsNullOrWhiteSpace(plan.reserveBotId))
            {
                if (!expectedBots.Contains(plan.reserveBotId))
                    errors.Add("Team plan reserveBotId is not a member of the team.");
                else if (!assignments.Any(item => item != null && item.botId == plan.reserveBotId && item.task == TeamPlanTask.Reserve))
                    errors.Add("Team plan reserve bot must have the Reserve task.");
            }

            ValidateConditions(errors, "redeploy", plan.redeployConditions);
            ValidateConditions(errors, "retreat", plan.retreatConditions);
            return errors.ToArray();
        }

        private static void ValidateKnownReference(
            ICollection<string> errors,
            string label,
            string value,
            ISet<string> known,
            bool required)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (required) errors.Add($"Team plan {label} is required.");
                return;
            }

            if (!known.Contains(value)) errors.Add($"Team plan {label} '{value}' is unknown.");
        }

        private static void ValidateText(ICollection<string> errors, string label, string value, int minimum, int maximum)
        {
            int length = value?.Trim().Length ?? 0;
            if (length < minimum || length > maximum)
                errors.Add($"Team plan {label} length must be {minimum}..{maximum} characters.");
        }

        private static void ValidateConditions(ICollection<string> errors, string label, string[] conditions)
        {
            string[] values = conditions ?? Array.Empty<string>();
            if (values.Length < 1 || values.Length > 4)
                errors.Add($"Team plan needs 1..4 {label} conditions.");
            foreach (string value in values) ValidateText(errors, $"{label} condition", value, 1, 180);
        }
    }

    public static class StrategicMapFallback
    {
        public static MapStrategicAnalysis Create(TacticalMapDescriptor descriptor)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            TacticalLaneDescriptor[] sourceLanes = descriptor.lanes ?? Array.Empty<TacticalLaneDescriptor>();
            StrategicLaneAnalysis[] lanes = sourceLanes
                .OrderBy(item => item.order)
                .Select((lane, index) => CreateLane(lane, index, sourceLanes.Length))
                .ToArray();
            return new MapStrategicAnalysis
            {
                mapId = descriptor.mapId,
                sourceHash = descriptor.sourceHash,
                summary = $"Карта «{descriptor.displayName}» содержит {lanes.Length} связанных направления. " +
                          "Названия и выводы получены детерминированно из геометрии и свойств местности.",
                lanes = lanes
            };
        }

        private static StrategicLaneAnalysis CreateLane(TacticalLaneDescriptor lane, int index, int count)
        {
            string name = lane?.provisionalName;
            if (count == 3)
            {
                if (index == 0) name = "Северная переправа";
                else if (index == 1) name = "Центральный проход";
                else if (index == 2) name = "Южный обход";
            }
            if (string.IsNullOrWhiteSpace(name)) name = $"Направление {index + 1}";

            string advantages = lane.hardCoverDensity >= 0.18f
                ? "Много твёрдых укрытий для безопасного сближения и удержания позиции."
                : lane.concealmentDensity >= 0.18f
                    ? "Растительность помогает разведке, маскировке и засадной стрельбе."
                    : "Открытая местность позволяет быстро менять позицию и поддерживать союзников огнём.";
            string risks = lane.chokePointCount > 0
                ? $"Есть узкие места ({lane.chokePointCount}), где возможны заторы и сосредоточенный огонь."
                : lane.maximumSightlineWorld >= 25f
                    ? "Длинные прострелы повышают риск раннего обнаружения и перекрёстного огня."
                    : "Недостаток выраженных укрытий требует осторожного продвижения и разведки.";
            return new StrategicLaneAnalysis
            {
                laneId = lane.id,
                name = name,
                description = $"Длина около {lane.lengthWorld:0} м, средний простор {lane.averageWidthWorld:0.0} м; " +
                              $"укрытия {lane.hardCoverDensity:P0}, растительность {lane.vegetationDensity:P0}.",
                advantages = advantages,
                risks = risks,
                preferredClasses = PreferredClasses(lane.suitability)
            };
        }

        private static string[] PreferredClasses(TacticalClassSuitability suitability)
        {
            if (suitability == null) return new[] { "MediumTank" };
            return new[]
                {
                    new KeyValuePair<string, float>("LightTank", suitability.lightTank),
                    new KeyValuePair<string, float>("MediumTank", suitability.mediumTank),
                    new KeyValuePair<string, float>("HeavyTank", suitability.heavyTank),
                    new KeyValuePair<string, float>("TankDestroyer", suitability.tankDestroyer),
                    new KeyValuePair<string, float>("Artillery", suitability.artillery)
                }
                .OrderByDescending(item => item.Value)
                .ThenBy(item => item.Key, StringComparer.Ordinal)
                .Take(2)
                .Select(item => item.Key)
                .ToArray();
        }
    }
}
