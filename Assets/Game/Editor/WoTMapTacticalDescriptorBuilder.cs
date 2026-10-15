using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LocalTanks;
using UnityEditor;
using UnityEngine;

namespace LocalTanks.Editor
{
    /// <summary>
    /// Produces the tactical analysis asset for a converted arena. The shared analysis asset describes a
    /// different map, and the battle director rejects a mismatch rather than letting the AI reason about
    /// the wrong battlefield, so each arena needs its own descriptor built from its own navigation grid.
    /// </summary>
    public static class WoTMapTacticalDescriptorBuilder
    {
        private const string TacticalRoot = "Assets/Game/GameData/Generated/Tactical";
        private const string SourceRoot = "Assets/Game/GameData/Maps";

        private static readonly TerrainSymbol[] Symbols =
        {
            new TerrainSymbol('=', TerrainKind.Road, TacticalFeatureKind.Road, 1.0f, 1.0f, 1.0f, false),
            new TerrainSymbol('m', TerrainKind.Mud, TacticalFeatureKind.Mud, 2.2f, 0.55f, 0.45f, false),
            new TerrainSymbol('w', TerrainKind.ShallowWater, TacticalFeatureKind.ShallowWater, 3.5f, 0.35f, 0.3f, false),
            new TerrainSymbol('x', TerrainKind.Obstacle, TacticalFeatureKind.Rubble, 2.6f, 0.5f, 0.5f, false),
            new TerrainSymbol('#', TerrainKind.Blocked, TacticalFeatureKind.HardCover, 10f, 1f, 1f, true),
            new TerrainSymbol('.', TerrainKind.Grass, TacticalFeatureKind.OpenGround, 1.25f, 0.86f, 0.86f, false)
        };

        public static string SourcePath(string mapId) => $"{SourceRoot}/{mapId}_tactical.json";
        public static string AssetPath(string mapId) => $"{TacticalRoot}/{mapId}_tactical.asset";

        [MenuItem("Local Tanks/Build WoT Map Tactical Analysis (Mines)")]
        public static void BuildDefault() => Build("10_hills");

        /// <summary>
        /// Writes the tactical source JSON the project already uses as the editable input, then runs the
        /// existing analyzer over it. Reusing the analyzer keeps one definition of a lane, a sightline and
        /// a choke point instead of a second implementation for generated arenas.
        /// </summary>
        public static TacticalMapDescriptorAsset Build(string mapId)
        {
            WoTMapAsset map = WoTMapImporter.ImportAsset(mapId);
            string sceneId = BattleSceneLayout.MapSceneName(mapId);
            TacticalMapSource source = CreateSource(map, mapId, sceneId);
            string[] errors = source.Validate();
            if (errors.Length > 0)
            {
                throw new InvalidDataException(
                    $"Generated tactical source for '{mapId}' is invalid:{Environment.NewLine}- " +
                    string.Join($"{Environment.NewLine}- ", errors));
            }

            Directory.CreateDirectory(SourceRoot);
            File.WriteAllText(SourcePath(mapId), JsonUtility.ToJson(source, true));

            TacticalMapDescriptor descriptor = TacticalMapAnalyzer.Analyze(source);
            Directory.CreateDirectory(TacticalRoot);
            string assetPath = AssetPath(mapId);
            TacticalMapDescriptorAsset asset = AssetDatabase.LoadAssetAtPath<TacticalMapDescriptorAsset>(assetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<TacticalMapDescriptorAsset>();
                AssetDatabase.CreateAsset(asset, assetPath);
            }

            asset.descriptor = descriptor;
            string[] assetErrors = asset.Validate();
            if (assetErrors.Length > 0)
            {
                throw new InvalidDataException(
                    $"Tactical analysis for '{mapId}' failed validation:{Environment.NewLine}- " +
                    string.Join($"{Environment.NewLine}- ", assetErrors));
            }

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"Tactical analysis for '{mapId}': {descriptor.lanes.Length} lane(s) between " +
                      $"base {descriptor.baseA} and base {descriptor.baseB}.");
            return asset;
        }

        /// <summary>
        /// Turns the navigation grid into the tactical source format. Base cells are marked with their
        /// own symbols because the analyzer locates both bases by looking for those features, and the
        /// markers are placed on the empty ground nearest each base rather than on the base centre, which
        /// may sit under a building.
        /// </summary>
        private static TacticalMapSource CreateSource(WoTMapAsset map, string mapId, string sceneId)
        {
            int width = map.navigationColumns;
            int height = map.navigationRows;
            char[,] symbols = new char[width, height];
            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    TerrainKind kind = (TerrainKind)map.navigationTerrain[row * width + column];
                    symbols[column, row] = SymbolFor(kind).symbol;
                }
            }

            MarkBase(symbols, map, map.basePositions.ElementAtOrDefault(0), 'A', width, height);
            MarkBase(symbols, map, map.basePositions.ElementAtOrDefault(1), 'Z', width, height);

            // The source format lists rows from the north edge down, while the navigation grid starts at
            // the south, so the rows are emitted in reverse.
            string[] rows = new string[height];
            for (int row = 0; row < height; row++)
            {
                char[] line = new char[width];
                for (int column = 0; column < width; column++)
                {
                    line[column] = symbols[column, height - 1 - row];
                }

                rows[row] = new string(line);
            }

            List<TacticalTerrainSource> terrains = Symbols
                .Select(item => new TacticalTerrainSource
                {
                    symbol = item.symbol.ToString(),
                    kind = item.kind.ToString(),
                    navigationCost = item.cost,
                    speedMultiplier = item.speed,
                    accelerationMultiplier = item.acceleration,
                    blocked = item.blocked,
                    tacticalFeature = item.feature.ToString()
                })
                .ToList();
            terrains.Add(new TacticalTerrainSource
            {
                symbol = "A",
                kind = TerrainKind.Grass.ToString(),
                navigationCost = 1.2f,
                speedMultiplier = 0.9f,
                accelerationMultiplier = 0.9f,
                blocked = false,
                tacticalFeature = TacticalFeatureKind.BaseA.ToString()
            });
            terrains.Add(new TacticalTerrainSource
            {
                symbol = "Z",
                kind = TerrainKind.Grass.ToString(),
                navigationCost = 1.2f,
                speedMultiplier = 0.9f,
                accelerationMultiplier = 0.9f,
                blocked = false,
                tacticalFeature = TacticalFeatureKind.BaseB.ToString()
            });

            return new TacticalMapSource
            {
                schemaVersion = 1,
                id = $"{mapId}_tactical",
                sceneId = sceneId,
                displayName = map.displayName,
                width = width,
                height = height,
                cellSize = map.navigationCellSize,
                terrains = terrains.ToArray(),
                rows = rows
            };
        }

        /// <summary>
        /// Marks the nearest open cell to a base with its own symbol, and enough of its neighbours that
        /// the analyzer sees a feature rather than a single stray cell.
        /// </summary>
        private static void MarkBase(
            char[,] symbols,
            WoTMapAsset map,
            Vector2 basePosition,
            char symbol,
            int width,
            int height)
        {
            Vector2 origin = map.worldCenter - map.playableSize * 0.5f;
            int centreColumn = Mathf.RoundToInt((basePosition.x - origin.x) / map.navigationCellSize);
            int centreRow = Mathf.RoundToInt((basePosition.y - origin.y) / map.navigationCellSize);

            for (int radius = 0; radius <= 8; radius++)
            {
                for (int row = centreRow - radius; row <= centreRow + radius; row++)
                {
                    for (int column = centreColumn - radius; column <= centreColumn + radius; column++)
                    {
                        if (column < 0 || row < 0 || column >= width || row >= height) continue;
                        TerrainKind kind = (TerrainKind)map.navigationTerrain[row * width + column];
                        if (kind != TerrainKind.Grass && kind != TerrainKind.Road) continue;
                        if (symbols[column, row] == 'A' || symbols[column, row] == 'Z') continue;
                        // Exactly one cell per base: the format allows one Team A and one Team B marker,
                        // and a block of them fails validation.
                        symbols[column, row] = symbol;
                        return;
                    }
                }
            }

            throw new InvalidOperationException("No open ground was found near a base to mark it with.");
        }

        private static TerrainSymbol SymbolFor(TerrainKind kind)
        {
            foreach (TerrainSymbol symbol in Symbols)
            {
                if (symbol.kind == kind) return symbol;
            }

            return Symbols[Symbols.Length - 1];
        }

        private readonly struct TerrainSymbol
        {
            public TerrainSymbol(
                char symbol,
                TerrainKind kind,
                TacticalFeatureKind feature,
                float cost,
                float speed,
                float acceleration,
                bool blocked)
            {
                this.symbol = symbol;
                this.kind = kind;
                this.feature = feature;
                this.cost = cost;
                this.speed = speed;
                this.acceleration = acceleration;
                this.blocked = blocked;
            }

            public readonly char symbol;
            public readonly TerrainKind kind;
            public readonly TacticalFeatureKind feature;
            public readonly float cost;
            public readonly float speed;
            public readonly float acceleration;
            public readonly bool blocked;
        }
    }
}
