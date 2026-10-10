using System;
using System.IO;
using System.Linq;
using LocalTanks;
using UnityEditor;
using UnityEngine;

namespace LocalTanks.Editor
{
    [Serializable]
    internal sealed class AiDuelLabCaseJson
    {
        public string id;
        public string displayName;
        public string description;
        public int seed;
        public float durationSeconds;
        public ScenarioPointJson teamAStart;
        public float teamAHeading;
        public ScenarioPointJson[] teamARoute;
        public ScenarioPointJson teamBStart;
        public float teamBHeading;
        public ScenarioPointJson[] teamBRoute;
    }

    [Serializable]
    internal sealed class AiDuelLabCatalogJson
    {
        public int schemaVersion;
        public AiDuelLabCaseJson[] cases;
    }

    public static class AiDuelLabCatalogImporter
    {
        public const string SourcePath = "Assets/Game/GameData/Scenarios/ai_duel_lab_cases.json";
        public const string AssetPath = "Assets/Game/GameData/Generated/Scenarios/ai_duel_lab_cases.asset";

        [MenuItem("Local Tanks/Data/Reimport AI Duel Lab Cases")]
        public static AiDuelLabCatalog Reimport()
        {
            string absolute = Path.GetFullPath(SourcePath);
            if (!File.Exists(absolute)) throw new FileNotFoundException("AI duel lab cases were not found.", SourcePath);
            AiDuelLabCatalogJson source = JsonUtility.FromJson<AiDuelLabCatalogJson>(File.ReadAllText(absolute));
            if (source == null || source.schemaVersion != 1)
                throw new InvalidDataException("AI duel lab catalog header is invalid.");

            EnsureFolder("Assets/Game/GameData/Generated/Scenarios");
            AiDuelLabCatalog asset = AssetDatabase.LoadAssetAtPath<AiDuelLabCatalog>(AssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<AiDuelLabCatalog>();
                AssetDatabase.CreateAsset(asset, AssetPath);
            }
            asset.cases = (source.cases ?? Array.Empty<AiDuelLabCaseJson>()).Select(Convert).ToArray();
            string[] errors = asset.Validate();
            if (errors.Length > 0)
                throw new InvalidDataException("AI duel lab catalog is invalid:\n- " + string.Join("\n- ", errors));
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
        }

        private static AiDuelLabCase Convert(AiDuelLabCaseJson source)
        {
            if (source == null) return null;
            return new AiDuelLabCase
            {
                id = source.id,
                displayName = source.displayName,
                description = source.description,
                seed = source.seed,
                durationSeconds = source.durationSeconds,
                teamAStart = Convert(source.teamAStart),
                teamAHeading = source.teamAHeading,
                teamARoute = (source.teamARoute ?? Array.Empty<ScenarioPointJson>()).Select(Convert).ToArray(),
                teamBStart = Convert(source.teamBStart),
                teamBHeading = source.teamBHeading,
                teamBRoute = (source.teamBRoute ?? Array.Empty<ScenarioPointJson>()).Select(Convert).ToArray()
            };
        }

        private static Vector2Int Convert(ScenarioPointJson point)
        {
            return point != null ? new Vector2Int(point.x, point.y) : default;
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            int separator = assetPath.LastIndexOf('/');
            string parent = assetPath.Substring(0, separator);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, assetPath.Substring(separator + 1));
        }
    }

    internal sealed class AiDuelLabCatalogPostprocessor : AssetPostprocessor
    {
        private static bool scheduled;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (scheduled || !importedAssets.Concat(deletedAssets).Concat(movedAssets).Concat(movedFromAssetPaths)
                    .Any(path => path == AiDuelLabCatalogImporter.SourcePath)) return;
            scheduled = true;
            EditorApplication.delayCall += () =>
            {
                scheduled = false;
                try { AiDuelLabCatalogImporter.Reimport(); }
                catch (Exception exception) { Debug.LogException(exception); }
            };
        }
    }
}
