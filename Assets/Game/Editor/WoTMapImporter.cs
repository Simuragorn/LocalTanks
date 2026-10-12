using System;
using System.IO;
using System.Text;
using LocalTanks;
using UnityEditor;
using UnityEngine;

namespace LocalTanks.Editor
{
    /// <summary>
    /// Imports a converted World of Tanks arena into Unity. The pipeline under
    /// <c>Tools/WoTMapPrimitivePipeline</c> owns the conversion; this class owns everything Unity:
    /// the map asset, and later the battle scene built from it.
    /// </summary>
    public static class WoTMapImporter
    {
        public const string PrimitivesRoot = "Assets/LocalOnly/WoTMapPrimitives";
        public const string AssetRoot = "Assets/Game/GameData/Generated/Maps";

        /// <summary>Navigation step in source metres. See ADR-053 for why it is not the collider step.</summary>
        public const float NavigationCellMetres = 4f;

        /// <summary>
        /// Entry point for batch runs and for an eventual build pipeline. It imports the one arena the
        /// catalog currently converts, so a change to the source JSON can be re-imported with a single
        /// command.
        /// </summary>
        [MenuItem("Local Tanks/Import WoT Map (Mines)")]
        public static void ImportDefaultMap()
        {
            ImportAsset("10_hills");
        }

        public static string PrimitivesPath(string mapId) => $"{PrimitivesRoot}/{mapId}/{mapId}_primitives.json";
        public static string AssetPath(string mapId) => $"{AssetRoot}/{mapId}_map.asset";
        public static string ScenePath(string mapId) => $"Assets/Game/Scenes/Battle_{mapId}.unity";

        /// <summary>
        /// Converts the source JSON into the map asset. Returns the loaded asset so callers can report
        /// on it or continue into scene building.
        /// </summary>
        public static WoTMapAsset ImportAsset(string mapId)
        {
            string source = PrimitivesPath(mapId);
            WoTMapAsset asset = WoTMapAssetConverter.Convert(source, NavigationCellMetres);
            asset.obstacles ??= Array.Empty<WoTMapObstacle>();
            asset.vegetation ??= Array.Empty<WoTMapVegetation>();
            asset.waterCellIndices ??= Array.Empty<int>();
            asset.basePositions ??= Array.Empty<Vector2>();
            asset.baseSpawnPositions ??= Array.Empty<Vector2>();

            string[] errors = asset.Validate().ToArray();
            if (errors.Length > 0)
            {
                UnityEngine.Object.DestroyImmediate(asset);
                throw new InvalidDataException(
                    $"Imported map '{mapId}' is not usable:{Environment.NewLine}- {string.Join($"{Environment.NewLine}- ", errors)}");
            }

            Directory.CreateDirectory(AssetRoot);
            string path = AssetPath(mapId);
            WoTMapAsset existing = AssetDatabase.LoadAssetAtPath<WoTMapAsset>(path);
            if (existing != null)
            {
                // Keep the asset identity so scenes that already reference it stay valid.
                EditorUtility.CopySerialized(asset, existing);
                UnityEngine.Object.DestroyImmediate(asset);
                EditorUtility.SetDirty(existing);
                asset = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(asset, path);
            }

            AssetDatabase.SaveAssets();
            Debug.Log(Describe(asset));
            return asset;
        }

        /// <summary>Human-readable summary, so an import can be checked from the log alone.</summary>
        public static string Describe(WoTMapAsset asset)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine($"WoT map '{asset.mapId}' ({asset.displayName}) imported from {asset.clientVersion}.");
            text.AppendLine($"  field: {asset.playableSize.x:0.#} x {asset.playableSize.y:0.#} units " +
                            $"({asset.playableSize.x * asset.metresPerUnit:0} x {asset.playableSize.y * asset.metresPerUnit:0} m) " +
                            $"at {asset.metresPerUnit:0.###} m per unit");
            if (asset.ObstacleCount > 0)
            {
                Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
                Vector2 max = new Vector2(float.MinValue, float.MinValue);
                float largest = 0f;
                foreach (WoTMapObstacle obstacle in asset.obstacles)
                {
                    min = Vector2.Min(min, obstacle.center);
                    max = Vector2.Max(max, obstacle.center);
                    largest = Mathf.Max(largest, Mathf.Max(obstacle.size.x, obstacle.size.y));
                }

                text.AppendLine($"  obstacle extent: x {min.x:0.#}..{max.x:0.#}, y {min.y:0.#}..{max.y:0.#}, " +
                                $"largest footprint {largest:0.#} units");
                Vector2 example = asset.obstacles[0].center;
                text.AppendLine($"  first obstacle '{asset.obstacles[0].category}' at {example.x:0.#},{example.y:0.#} " +
                                $"size {asset.obstacles[0].size.x:0.#}x{asset.obstacles[0].size.y:0.#}");
            }
            text.AppendLine($"  terrain: {asset.terrainColumns}x{asset.terrainRows} at {asset.terrainCellSize:0.##} units, " +
                            $"elevation {asset.terrainMinimumMetres:0.#}..{asset.terrainMaximumMetres:0.#} m");
            text.AppendLine($"  water: {asset.WaterCellCount} cells of {asset.waterColumns}x{asset.waterRows}");
            text.AppendLine($"  obstacles: {asset.ObstacleCount}, vegetation: {asset.VegetationCount}");
            text.AppendLine($"  navigation: {asset.navigationColumns}x{asset.navigationRows} at " +
                            $"{asset.navigationCellSize:0.##} units, " +
                            $"{System.Array.FindAll(asset.navigationBlocked, blocked => blocked).Length} blocked cells, " +
                            $"{asset.navigationBlockerCount} of {asset.ObstacleCount} obstacles close ground " +
                            $"({asset.navigationPolicy})");

            // A blocked-cell percentage says nothing about whether a tank can get anywhere, so the
            // walkable ground is measured as regions: a field split into pockets is not playable even at
            // a comfortable blocked share.
            (int largestRegion, int regionCount, int walkable) = WoTMapAssetConverter.MeasureWalkableRegions(asset);
            int total = asset.navigationColumns * asset.navigationRows;
            float share = total > 0 ? largestRegion * 100f / total : 0f;
            text.AppendLine($"  walkable: {walkable} of {total} cells in {regionCount} region(s); " +
                            $"largest region {largestRegion} cells ({share:0.#}% of the field)");
            return text.ToString();
        }
    }
}
