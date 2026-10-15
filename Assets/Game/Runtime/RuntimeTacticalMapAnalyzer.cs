using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LocalTanks
{
    public readonly struct RuntimeTacticalAnalysisReport
    {
        public RuntimeTacticalAnalysisReport(
            int cellCount,
            int concealmentZoneCount,
            int laneCount,
            long elapsedMilliseconds)
        {
            CellCount = cellCount;
            ConcealmentZoneCount = concealmentZoneCount;
            LaneCount = laneCount;
            ElapsedMilliseconds = elapsedMilliseconds;
        }

        public int CellCount { get; }
        public int ConcealmentZoneCount { get; }
        public int LaneCount { get; }
        public long ElapsedMilliseconds { get; }
        public bool WasAnalyzed => CellCount > 0 && LaneCount > 0;
    }

    public readonly struct RuntimeTacticalAnalysisResult
    {
        public RuntimeTacticalAnalysisResult(
            TacticalMapDescriptor descriptor,
            RuntimeTacticalAnalysisReport report)
        {
            Descriptor = descriptor;
            Report = report;
        }

        public TacticalMapDescriptor Descriptor { get; }
        public RuntimeTacticalAnalysisReport Report { get; }
    }

    /// <summary>
    /// Builds the complete tactical input from the navigation grid and scene objects that exist at battle
    /// start. Saved tactical assets may still be useful as editor previews, but bots never need them to
    /// understand a loaded arena.
    /// </summary>
    public static class RuntimeTacticalMapAnalyzer
    {
        private const char Grass = '.';
        private const char Road = '=';
        private const char Mud = 'm';
        private const char Water = 'w';
        private const char BlockedWater = 'W';
        private const char Rough = 'x';
        private const char HardCover = '#';
        private const char Concealment = 'b';
        private const char TeamABase = 'A';
        private const char TeamBBase = 'Z';

        public static RuntimeTacticalAnalysisResult Analyze(
            NavigationMap map,
            Scene scene,
            IReadOnlyList<CaptureBase> bases,
            string sceneId)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (!scene.IsValid() || !scene.isLoaded)
                throw new ArgumentException("Tactical analysis needs a loaded scene.", nameof(scene));

            CaptureBase teamA = FindBase(bases, TeamId.TeamA, "A");
            CaptureBase teamB = FindBase(bases, TeamId.TeamB, "B");
            if (teamA == null || teamB == null)
                throw new InvalidOperationException("Runtime tactical analysis needs one base for each team.");

            Stopwatch timer = Stopwatch.StartNew();
            Physics2D.SyncTransforms();
            HashSet<Vector2Int> concealmentCells = CollectConcealmentCells(map, scene, out int zoneCount);
            char[,] symbols = BuildSymbols(map, concealmentCells);
            Vector2Int baseA = FindNearestWalkable(map, map.WorldToCell(teamA.transform.position));
            Vector2Int baseB = FindNearestWalkable(map, map.WorldToCell(teamB.transform.position));
            if (baseA == baseB)
                throw new InvalidOperationException("Both team bases resolve to the same navigation cell.");

            symbols[baseA.x, baseA.y] = TeamABase;
            symbols[baseB.x, baseB.y] = TeamBBase;
            string resolvedSceneId = string.IsNullOrWhiteSpace(sceneId) ? scene.name : sceneId;
            TacticalMapSource source = new TacticalMapSource
            {
                schemaVersion = 1,
                id = $"{resolvedSceneId}_runtime",
                sceneId = resolvedSceneId,
                displayName = scene.name,
                width = map.Width,
                height = map.Height,
                cellSize = map.CellSize,
                terrains = CreateTerrainDefinitions(),
                rows = BuildRows(symbols, map.Width, map.Height)
            };

            TacticalMapDescriptor descriptor = TacticalMapAnalyzer.Analyze(source);
            timer.Stop();
            RuntimeTacticalAnalysisReport report = new RuntimeTacticalAnalysisReport(
                map.Width * map.Height,
                zoneCount,
                descriptor.lanes.Length,
                timer.ElapsedMilliseconds);
            UnityEngine.Debug.Log(
                $"Runtime tactical analysis mapped {report.CellCount} cell(s), {zoneCount} concealment " +
                $"zone(s) and {report.LaneCount} lane(s) in {report.ElapsedMilliseconds} ms.");
            return new RuntimeTacticalAnalysisResult(descriptor, report);
        }

        public static TacticalLaneDescriptor SelectLane(TacticalMapDescriptor descriptor, BattleLane lane)
        {
            TacticalLaneDescriptor[] ordered = (descriptor?.lanes ?? Array.Empty<TacticalLaneDescriptor>())
                .Where(item => item != null && item.pathCells != null && item.pathCells.Length >= 2)
                .OrderBy(item => item.order)
                .ToArray();
            if (ordered.Length == 0) return null;

            int index = lane switch
            {
                BattleLane.North => 0,
                BattleLane.Center => ordered.Length / 2,
                _ => ordered.Length - 1
            };
            return ordered[index];
        }

        public static Vector2Int[] OrientFrom(TacticalLaneDescriptor lane, Vector2Int start)
        {
            if (lane?.pathCells == null || lane.pathCells.Length == 0)
                return Array.Empty<Vector2Int>();

            Vector2Int[] result = (Vector2Int[])lane.pathCells.Clone();
            if ((result[result.Length - 1] - start).sqrMagnitude < (result[0] - start).sqrMagnitude)
                Array.Reverse(result);
            return result;
        }

        private static CaptureBase FindBase(IReadOnlyList<CaptureBase> bases, TeamId owner, string id)
        {
            CaptureBase[] available = (bases ?? Array.Empty<CaptureBase>())
                .Where(item => item != null)
                .ToArray();
            return available.FirstOrDefault(item => item.Owner == owner) ??
                   available.FirstOrDefault(item => string.Equals(item.BaseId, id, StringComparison.Ordinal));
        }

        private static char[,] BuildSymbols(NavigationMap map, HashSet<Vector2Int> concealmentCells)
        {
            char[,] symbols = new char[map.Width, map.Height];
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                TerrainKind kind = map.GetTerrain(cell);
                if (kind == TerrainKind.ShallowWater)
                    symbols[x, y] = map.IsBlocked(cell) ? BlockedWater : Water;
                else if (map.IsBlocked(cell) || kind == TerrainKind.Blocked)
                    symbols[x, y] = HardCover;
                else if (concealmentCells.Contains(cell))
                    symbols[x, y] = Concealment;
                else
                    symbols[x, y] = kind switch
                    {
                        TerrainKind.Road => Road,
                        TerrainKind.Mud => Mud,
                        TerrainKind.Obstacle => Rough,
                        _ => Grass
                    };
            }

            return symbols;
        }

        private static HashSet<Vector2Int> CollectConcealmentCells(
            NavigationMap map,
            Scene scene,
            out int zoneCount)
        {
            HashSet<Vector2Int> result = new HashSet<Vector2Int>();
            List<ConcealmentZone> zones = new List<ConcealmentZone>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                zones.AddRange(root.GetComponentsInChildren<ConcealmentZone>(true)
                    .Where(zone => zone != null && zone.gameObject.activeInHierarchy));
            }

            foreach (ConcealmentZone zone in zones.OrderBy(item => HierarchyKey(item.transform), StringComparer.Ordinal))
            {
                Collider2D[] colliders = zone.GetComponentsInChildren<Collider2D>(true);
                foreach (Collider2D collider in colliders)
                {
                    if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
                    Bounds bounds = collider.bounds;
                    Vector2Int minimum = map.WorldToCell(bounds.min);
                    Vector2Int maximum = map.WorldToCell(bounds.max);
                    for (int y = Mathf.Max(0, minimum.y); y <= Mathf.Min(map.Height - 1, maximum.y); y++)
                    for (int x = Mathf.Max(0, minimum.x); x <= Mathf.Min(map.Width - 1, maximum.x); x++)
                    {
                        Vector2Int cell = new Vector2Int(x, y);
                        if (collider.OverlapPoint(map.CellToWorld(cell))) result.Add(cell);
                    }
                }
            }

            zoneCount = zones.Count;
            return result;
        }

        private static Vector2Int FindNearestWalkable(NavigationMap map, Vector2Int desired)
        {
            int maximumRadius = Mathf.Max(map.Width, map.Height);
            for (int radius = 0; radius <= maximumRadius; radius++)
            {
                for (int y = -radius; y <= radius; y++)
                for (int x = -radius; x <= radius; x++)
                {
                    if (radius > 0 && Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) != radius) continue;
                    Vector2Int candidate = desired + new Vector2Int(x, y);
                    if (map.Contains(candidate) && !map.IsBlocked(candidate)) return candidate;
                }
            }

            throw new InvalidOperationException($"No walkable navigation cell exists near base cell {desired}.");
        }

        private static string[] BuildRows(char[,] symbols, int width, int height)
        {
            string[] rows = new string[height];
            for (int sourceY = height - 1, row = 0; sourceY >= 0; sourceY--, row++)
            {
                char[] values = new char[width];
                for (int x = 0; x < width; x++) values[x] = symbols[x, sourceY];
                rows[row] = new string(values);
            }

            return rows;
        }

        private static TacticalTerrainSource[] CreateTerrainDefinitions()
        {
            return new[]
            {
                Terrain(Grass, TerrainKind.Grass, 1f, 1f, 1f, false, TacticalFeatureKind.OpenGround),
                Terrain(Road, TerrainKind.Road, 1f, 1f, 1f, false, TacticalFeatureKind.Road),
                Terrain(Mud, TerrainKind.Mud, 2.2f, 0.55f, 0.45f, false, TacticalFeatureKind.Mud),
                Terrain(Water, TerrainKind.ShallowWater, 3.5f, 0.35f, 0.3f, false, TacticalFeatureKind.ShallowWater),
                Terrain(BlockedWater, TerrainKind.ShallowWater, 10f, 0.35f, 0.3f, true, TacticalFeatureKind.DeepWater),
                Terrain(Rough, TerrainKind.Obstacle, 2.6f, 1f, 1f, false, TacticalFeatureKind.Rubble),
                Terrain(HardCover, TerrainKind.Blocked, 10f, 1f, 1f, true, TacticalFeatureKind.HardCover),
                Terrain(Concealment, TerrainKind.Grass, 1.2f, 0.9f, 0.9f, false, TacticalFeatureKind.Bush),
                Terrain(TeamABase, TerrainKind.Grass, 1.2f, 0.9f, 0.9f, false, TacticalFeatureKind.BaseA),
                Terrain(TeamBBase, TerrainKind.Grass, 1.2f, 0.9f, 0.9f, false, TacticalFeatureKind.BaseB)
            };
        }

        private static TacticalTerrainSource Terrain(
            char symbol,
            TerrainKind kind,
            float cost,
            float speed,
            float acceleration,
            bool blocked,
            TacticalFeatureKind feature)
        {
            return new TacticalTerrainSource
            {
                symbol = symbol.ToString(),
                kind = kind.ToString(),
                navigationCost = cost,
                speedMultiplier = speed,
                accelerationMultiplier = acceleration,
                blocked = blocked,
                tacticalFeature = feature.ToString()
            };
        }

        private static string HierarchyKey(Transform transform)
        {
            string key = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                key = transform.name + "/" + key;
            }

            return key;
        }
    }
}
