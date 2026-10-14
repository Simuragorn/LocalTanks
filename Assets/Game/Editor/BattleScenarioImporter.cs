using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LocalTanks;
using UnityEditor;
using UnityEngine;

namespace LocalTanks.Editor
{
    [Serializable]
    public sealed class ScenarioPointJson
    {
        public int x;
        public int y;
    }

    [Serializable]
    public sealed class ScenarioRouteJson
    {
        public string team;
        public string lane;
        public ScenarioPointJson[] points;
    }

    [Serializable]
    public sealed class ScenarioEntryJson
    {
        public string id;
        public string tankId;
        public string team;
        public bool playerControlled;
        public string baseId;
        public int spawnSlot;
        public string lane;
        public string role;
    }

    [Serializable]
    public sealed class ScenarioRulesJson
    {
        public float timeLimitSeconds;
        public bool timeLimitIsDraw;
        public bool friendlyFire;
    }

    [Serializable]
    public sealed class BattleScenarioJson
    {
        public int schemaVersion;
        public string id;
        public string sceneId;
        public string kind;
        public ScenarioRulesJson rules;
        public ScenarioRouteJson[] routes;
        public ScenarioEntryJson[] entries;
    }

    public static class BattleScenarioImporter
    {
        public const string SourcePath = "Assets/Game/GameData/Scenarios/river_crossing_battle.json";
        public const string AssetPath = "Assets/Game/GameData/Generated/Scenarios/river_crossing_battle.asset";
        public const string DuelSourcePath = "Assets/Game/GameData/Scenarios/ai_duel_lab_battle.json";
        public const string DuelAssetPath = "Assets/Game/GameData/Generated/Scenarios/ai_duel_lab_battle.asset";

        [MenuItem("Local Tanks/Data/Reimport Battle Scenario")]
        public static BattleScenario Reimport()
        {
            return Reimport(SourcePath, AssetPath);
        }

        [MenuItem("Local Tanks/Data/Reimport AI Duel Lab Scenario")]
        public static BattleScenario ReimportDuelLab()
        {
            return Reimport(DuelSourcePath, DuelAssetPath);
        }

        private static BattleScenario Reimport(string sourcePath, string assetPath)
        {
            BattleScenarioJson source = LoadSource(sourcePath);
            CombatDatabase database = AssetDatabase.LoadAssetAtPath<CombatDatabase>(CombatDefinitionImporter.DatabasePath);
            if (database == null)
            {
                CombatDefinitionImporter.Reimport();
                database = AssetDatabase.LoadAssetAtPath<CombatDatabase>(CombatDefinitionImporter.DatabasePath);
            }

            EnsureFolder("Assets/Game/GameData/Generated/Scenarios");
            BattleScenario asset = AssetDatabase.LoadAssetAtPath<BattleScenario>(assetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<BattleScenario>();
                AssetDatabase.CreateAsset(asset, assetPath);
            }

            Dictionary<string, Vector2Int[]> routes = BuildRoutes(source.routes);
            asset.id = source.id;
            asset.sceneId = source.sceneId;
            asset.kind = ParseKind(source.kind, source.id);
            asset.rules = ConvertRules(source.rules);
            asset.entries = (source.entries ?? Array.Empty<ScenarioEntryJson>())
                .Select(item => ConvertEntry(item, routes))
                .ToArray();
            string[] errors = asset.Validate(database);
            if (errors.Length > 0)
            {
                throw new InvalidDataException("Battle scenario validation failed:\n- " + string.Join("\n- ", errors));
            }

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
        }

        [MenuItem("Local Tanks/Data/Validate Battle Scenario")]
        public static void ValidateMenu()
        {
            BattleScenario scenario = Reimport();
            Debug.Log($"Local Tanks: battle scenario '{scenario.id}' is valid ({scenario.entries.Length} entries).");
        }

        public static BattleScenarioJson LoadSource()
        {
            return LoadSource(SourcePath);
        }

        private static BattleScenarioJson LoadSource(string sourcePath)
        {
            string path = Path.GetFullPath(sourcePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Battle scenario JSON was not found.", sourcePath);
            BattleScenarioJson source = JsonUtility.FromJson<BattleScenarioJson>(File.ReadAllText(path));
            if (source == null || source.schemaVersion != 1 || string.IsNullOrWhiteSpace(source.id) ||
                string.IsNullOrWhiteSpace(source.sceneId))
                throw new InvalidDataException("Battle scenario header is invalid.");
            return source;
        }

        private static BattleScenarioKind ParseKind(string value, string scenarioId)
        {
            if (string.IsNullOrWhiteSpace(value)) return BattleScenarioKind.StandardBattle;
            if (Enum.TryParse(value, false, out BattleScenarioKind kind)) return kind;
            throw new InvalidDataException($"Scenario '{scenarioId}' has unknown kind '{value}'.");
        }

        /// <summary>
        /// Match rules from the scenario file. A file without the block keeps the authored defaults rather
        /// than a set of zeroes: an omitted <c>friendlyFire</c> would otherwise read as "disabled", and the
        /// rule would change because a key was forgotten instead of because someone decided it.
        /// </summary>
        private static MatchRules ConvertRules(ScenarioRulesJson source)
        {
            if (source == null)
            {
                return new MatchRules();
            }

            return new MatchRules
            {
                timeLimitSeconds = source.timeLimitSeconds,
                timeLimitIsDraw = source.timeLimitIsDraw,
                friendlyFire = source.friendlyFire
            };
        }

        private static BattleScenarioEntry ConvertEntry(
            ScenarioEntryJson source,
            IReadOnlyDictionary<string, Vector2Int[]> routes)
        {
            if (!Enum.TryParse(source.team, false, out TeamId team))
                throw new InvalidDataException($"Scenario entry '{source.id}' has unknown team '{source.team}'.");
            if (!Enum.TryParse(source.lane, false, out BattleLane lane))
                throw new InvalidDataException($"Scenario entry '{source.id}' has unknown lane '{source.lane}'.");
            if (!Enum.TryParse(source.role, false, out CombatAiRole role))
                throw new InvalidDataException($"Scenario entry '{source.id}' has unknown role '{source.role}'.");
            string routeKey = RouteKey(team, lane);
            if (!routes.TryGetValue(routeKey, out Vector2Int[] route))
                throw new InvalidDataException($"Scenario entry '{source.id}' has no route '{routeKey}'.");
            return new BattleScenarioEntry
            {
                id = source.id,
                tankId = source.tankId,
                team = team,
                playerControlled = source.playerControlled,
                baseId = source.baseId,
                spawnSlot = source.spawnSlot,
                lane = lane,
                role = role,
                routeCells = route.ToArray()
            };
        }

        private static Dictionary<string, Vector2Int[]> BuildRoutes(ScenarioRouteJson[] sourceRoutes)
        {
            Dictionary<string, Vector2Int[]> routes = new Dictionary<string, Vector2Int[]>(StringComparer.Ordinal);
            foreach (ScenarioRouteJson source in sourceRoutes ?? Array.Empty<ScenarioRouteJson>())
            {
                if (!Enum.TryParse(source.team, false, out TeamId team) || team == TeamId.Neutral)
                    throw new InvalidDataException($"Scenario route has unknown team '{source.team}'.");
                if (!Enum.TryParse(source.lane, false, out BattleLane lane))
                    throw new InvalidDataException($"Scenario route has unknown lane '{source.lane}'.");
                Vector2Int[] points = (source.points ?? Array.Empty<ScenarioPointJson>())
                    .Select(point => new Vector2Int(point.x, point.y))
                    .ToArray();
                if (points.Length < 2 || !routes.TryAdd(RouteKey(team, lane), points))
                    throw new InvalidDataException($"Scenario route '{team}:{lane}' is missing, short or duplicated.");
            }

            return routes;
        }

        private static string RouteKey(TeamId team, BattleLane lane)
        {
            return $"{team}:{lane}";
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            string parent = assetPath.Substring(0, assetPath.LastIndexOf('/'));
            string name = assetPath.Substring(assetPath.LastIndexOf('/') + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }

    internal sealed class BattleScenarioAssetPostprocessor : AssetPostprocessor
    {
        private static bool scheduled;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (scheduled || !importedAssets.Concat(deletedAssets).Concat(movedAssets).Concat(movedFromAssetPaths)
                    .Any(path => path == BattleScenarioImporter.SourcePath ||
                                 path == BattleScenarioImporter.DuelSourcePath))
                return;
            scheduled = true;
            EditorApplication.delayCall += () =>
            {
                scheduled = false;
                try
                {
                    BattleScenarioImporter.Reimport();
                    BattleScenarioImporter.ReimportDuelLab();
                }
                catch (Exception exception) { Debug.LogException(exception); }
            };
        }
    }
}
