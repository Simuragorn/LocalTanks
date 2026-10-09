using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    public enum TacticalFeatureKind
    {
        OpenGround,
        Road,
        Mud,
        ShallowWater,
        DeepWater,
        Boundary,
        HardCover,
        Trees,
        Building,
        Wall,
        Rubble,
        Bush,
        Reeds,
        Crater,
        BaseA,
        BaseB
    }

    [Serializable]
    public sealed class TacticalTerrainSource
    {
        public string symbol;
        public string kind;
        public float navigationCost;
        public float speedMultiplier;
        public float accelerationMultiplier;
        public bool blocked;
        public string tacticalFeature;
    }

    [Serializable]
    public sealed class TacticalMapSource
    {
        public int schemaVersion;
        public string id;
        public string sceneId;
        public string displayName;
        public int width;
        public int height;
        public float cellSize;
        public TacticalTerrainSource[] terrains;
        public string[] rows;

        public static TacticalMapSource Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("Tactical map JSON is empty.", nameof(json));
            TacticalMapSource source = JsonUtility.FromJson<TacticalMapSource>(json);
            if (source == null)
                throw new ArgumentException("Tactical map JSON could not be parsed.", nameof(json));
            return source;
        }

        public string[] Validate()
        {
            List<string> errors = new List<string>();
            if (schemaVersion != 1) errors.Add($"Unsupported map schema version {schemaVersion}.");
            if (string.IsNullOrWhiteSpace(id)) errors.Add("Map id is empty.");
            if (string.IsNullOrWhiteSpace(sceneId)) errors.Add("Map sceneId is empty.");
            if (width <= 0 || height <= 0) errors.Add("Map dimensions must be positive.");
            if (cellSize <= 0f) errors.Add("Map cell size must be positive.");
            if (rows == null || rows.Length != height)
                errors.Add($"Map must contain exactly {height} rows.");

            Dictionary<char, TacticalTerrainSource> definitions = new Dictionary<char, TacticalTerrainSource>();
            foreach (TacticalTerrainSource terrain in terrains ?? Array.Empty<TacticalTerrainSource>())
            {
                if (terrain == null || string.IsNullOrEmpty(terrain.symbol) || terrain.symbol.Length != 1)
                {
                    errors.Add("Every terrain needs a one-character symbol.");
                    continue;
                }

                char symbol = terrain.symbol[0];
                if (definitions.ContainsKey(symbol)) errors.Add($"Terrain symbol '{symbol}' is duplicated.");
                else definitions.Add(symbol, terrain);
                if (!Enum.TryParse(terrain.tacticalFeature, false, out TacticalFeatureKind _))
                    errors.Add($"Terrain '{symbol}' has unknown tactical feature '{terrain.tacticalFeature}'.");
                if (terrain.navigationCost <= 0f) errors.Add($"Terrain '{symbol}' has invalid navigation cost.");
            }

            if (definitions.Count == 0) errors.Add("Map has no terrain definitions.");
            int baseACount = 0;
            int baseBCount = 0;
            if (rows != null && rows.Length == height)
            {
                for (int row = 0; row < rows.Length; row++)
                {
                    if (rows[row] == null || rows[row].Length != width)
                    {
                        errors.Add($"Row {row} must contain exactly {width} characters.");
                        continue;
                    }

                    foreach (char symbol in rows[row])
                    {
                        if (!definitions.TryGetValue(symbol, out TacticalTerrainSource terrain))
                        {
                            errors.Add($"Row {row} contains undefined symbol '{symbol}'.");
                            continue;
                        }

                        if (ParseFeature(terrain) == TacticalFeatureKind.BaseA) baseACount++;
                        if (ParseFeature(terrain) == TacticalFeatureKind.BaseB) baseBCount++;
                    }
                }
            }

            if (baseACount != 1) errors.Add($"Map must contain one Team A base, found {baseACount}.");
            if (baseBCount != 1) errors.Add($"Map must contain one Team B base, found {baseBCount}.");
            return errors.ToArray();
        }

        public char GetSymbol(Vector2Int cell)
        {
            if (cell.x < 0 || cell.y < 0 || cell.x >= width || cell.y >= height)
                throw new ArgumentOutOfRangeException(nameof(cell));
            return rows[height - 1 - cell.y][cell.x];
        }

        public static TacticalFeatureKind ParseFeature(TacticalTerrainSource terrain)
        {
            return terrain != null && Enum.TryParse(terrain.tacticalFeature, false, out TacticalFeatureKind feature)
                ? feature
                : TacticalFeatureKind.OpenGround;
        }
    }

    [Serializable]
    public sealed class TacticalTerrainShare
    {
        public string terrainKind;
        [Range(0f, 1f)] public float share;
    }

    [Serializable]
    public sealed class TacticalClassSuitability
    {
        [Range(0f, 1f)] public float lightTank;
        [Range(0f, 1f)] public float mediumTank;
        [Range(0f, 1f)] public float heavyTank;
        [Range(0f, 1f)] public float tankDestroyer;
        [Range(0f, 1f)] public float artillery;
    }

    [Serializable]
    public sealed class TacticalLaneConnection
    {
        public string targetLaneId;
        public Vector2Int fromCell;
        public Vector2Int targetCell;
        public float transferDistanceWorld;
    }

    [Serializable]
    public sealed class TacticalLaneDescriptor
    {
        public string id;
        public string provisionalName;
        public int order;
        public Vector2Int crossingCell;
        public Vector2Int entryA;
        public Vector2Int entryB;
        public Vector2Int defenseA;
        public Vector2Int defenseB;
        public Vector2Int[] pathCells;
        public float lengthWorld;
        public float averageWidthWorld;
        public float averageNavigationCost;
        [Range(0f, 1f)] public float hardCoverDensity;
        [Range(0f, 1f)] public float vegetationDensity;
        [Range(0f, 1f)] public float concealmentDensity;
        public float averageSightlineWorld;
        public float maximumSightlineWorld;
        public int chokePointCount;
        public bool hasMandatoryCrossing;
        public TacticalTerrainShare[] terrainShares;
        public TacticalClassSuitability suitability;
        public TacticalLaneConnection[] connections;
    }

    [Serializable]
    public sealed class TacticalMapDescriptor
    {
        public int analyzerVersion;
        public string sourceHash;
        public string mapId;
        public string sceneId;
        public string displayName;
        public int width;
        public int height;
        public float cellSize;
        public Vector2Int baseA;
        public Vector2Int baseB;
        public TacticalLaneDescriptor[] lanes;
    }
}
