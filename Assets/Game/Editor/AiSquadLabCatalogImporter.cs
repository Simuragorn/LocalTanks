using System;
using System.IO;
using System.Linq;
using LocalTanks;
using UnityEditor;
using UnityEngine;

namespace LocalTanks.Editor
{
    [Serializable]
    internal sealed class AiSquadLabCaseJson
    {
        public string id;
        public string displayName;
        public string description;
        public int seed;
        public float durationSeconds;
    }

    [Serializable]
    internal sealed class AiSquadLabCatalogJson
    {
        public int schemaVersion;
        public AiSquadLabCaseJson[] cases;
    }

    public static class AiSquadLabCatalogImporter
    {
        public const string SourcePath = "Assets/Game/GameData/Scenarios/ai_squad_lab_cases.json";
        public const string AssetPath = "Assets/Game/GameData/Generated/Scenarios/ai_squad_lab_cases.asset";

        [MenuItem("Local Tanks/Data/Reimport AI Squad Lab Cases")]
        public static AiSquadLabCatalog Reimport()
        {
            string absolute = Path.GetFullPath(SourcePath);
            if (!File.Exists(absolute))
                throw new FileNotFoundException("AI squad lab cases were not found.", SourcePath);
            AiSquadLabCatalogJson source =
                JsonUtility.FromJson<AiSquadLabCatalogJson>(File.ReadAllText(absolute));
            if (source == null || source.schemaVersion != 1)
                throw new InvalidDataException("AI squad lab catalog header is invalid.");

            EnsureFolder("Assets/Game/GameData/Generated/Scenarios");
            AiSquadLabCatalog asset = AssetDatabase.LoadAssetAtPath<AiSquadLabCatalog>(AssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<AiSquadLabCatalog>();
                AssetDatabase.CreateAsset(asset, AssetPath);
            }

            asset.cases = (source.cases ?? Array.Empty<AiSquadLabCaseJson>())
                .Select(item => item == null ? null : new AiSquadLabCase
                {
                    id = item.id,
                    displayName = item.displayName,
                    description = item.description,
                    seed = item.seed,
                    durationSeconds = item.durationSeconds
                })
                .ToArray();
            string[] errors = asset.Validate();
            if (errors.Length > 0)
                throw new InvalidDataException("AI squad lab catalog is invalid:\n- " + string.Join("\n- ", errors));
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
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

    internal sealed class AiSquadLabCatalogPostprocessor : AssetPostprocessor
    {
        private static bool scheduled;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (scheduled || !importedAssets.Concat(deletedAssets).Concat(movedAssets).Concat(movedFromAssetPaths)
                    .Any(path => path == AiSquadLabCatalogImporter.SourcePath)) return;
            scheduled = true;
            EditorApplication.delayCall += () =>
            {
                scheduled = false;
                try { AiSquadLabCatalogImporter.Reimport(); }
                catch (Exception exception) { Debug.LogException(exception); }
            };
        }
    }
}
