using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace LocalTanks
{
    public static class TacticalMapAnalyzer
    {
        public const int AnalyzerVersion = 1;

        private static readonly Vector2Int[] SightDirections =
        {
            Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down,
            new Vector2Int(1, 1), new Vector2Int(1, -1),
            new Vector2Int(-1, 1), new Vector2Int(-1, -1)
        };

        public static TacticalMapDescriptor Analyze(TacticalMapSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            string[] errors = source.Validate();
            if (errors.Length > 0)
                throw new ArgumentException("Tactical map is invalid:\n- " + string.Join("\n- ", errors), nameof(source));

            Dictionary<char, TacticalTerrainSource> terrain = source.terrains
                .ToDictionary(item => item.symbol[0]);
            NavigationGridModel originalGrid = BuildGrid(source, terrain);
            Vector2Int baseA = FindFeature(source, terrain, TacticalFeatureKind.BaseA);
            Vector2Int baseB = FindFeature(source, terrain, TacticalFeatureKind.BaseB);
            bool horizontal = Mathf.Abs(baseB.x - baseA.x) >= Mathf.Abs(baseB.y - baseA.y);
            int scanCoordinate = horizontal ? (baseA.x + baseB.x) / 2 : (baseA.y + baseB.y) / 2;
            List<List<int>> crossingGroups = FindCrossingGroups(source, originalGrid, horizontal, scanCoordinate);
            if (crossingGroups.Count == 0)
                throw new InvalidOperationException("No passable strategic direction crosses the middle of the map.");

            crossingGroups.Sort((first, second) => horizontal
                ? second.Average().CompareTo(first.Average())
                : first.Average().CompareTo(second.Average()));

            List<TacticalLaneDescriptor> lanes = new List<TacticalLaneDescriptor>();
            for (int index = 0; index < crossingGroups.Count; index++)
            {
                HashSet<int> allowedCrossing = new HashSet<int>(crossingGroups[index]);
                NavigationGridModel constrained = BuildGrid(source, terrain);
                int perpendicularSize = horizontal ? source.height : source.width;
                for (int perpendicular = 0; perpendicular < perpendicularSize; perpendicular++)
                {
                    if (allowedCrossing.Contains(perpendicular)) continue;
                    Vector2Int cell = horizontal
                        ? new Vector2Int(scanCoordinate, perpendicular)
                        : new Vector2Int(perpendicular, scanCoordinate);
                    if (constrained.Contains(cell)) constrained.SetBlocked(cell, true);
                }

                List<Vector2Int> path = WeightedAStar.FindPath(constrained, baseA, baseB, 0, 1f);
                if (path.Count == 0) continue;
                Vector2Int crossing = path.First(cell => horizontal
                    ? cell.x == scanCoordinate
                    : cell.y == scanCoordinate);
                lanes.Add(BuildLane(source, terrain, originalGrid, path, crossing, lanes.Count, crossingGroups.Count, horizontal));
            }

            if (lanes.Count == 0)
                throw new InvalidOperationException("Strategic crossings exist, but neither base can reach them.");
            AddConnections(lanes, source.cellSize, source.width, source.height);
            return new TacticalMapDescriptor
            {
                analyzerVersion = AnalyzerVersion,
                sourceHash = ComputeSourceHash(source),
                mapId = source.id,
                sceneId = source.sceneId,
                displayName = source.displayName,
                width = source.width,
                height = source.height,
                cellSize = source.cellSize,
                baseA = baseA,
                baseB = baseB,
                lanes = lanes.ToArray()
            };
        }

        public static string ComputeSourceHash(TacticalMapSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            StringBuilder canonical = new StringBuilder();
            canonical.Append(AnalyzerVersion).Append('|').Append(source.schemaVersion).Append('|')
                .Append(source.id).Append('|').Append(source.sceneId).Append('|')
                .Append(source.width).Append('x').Append(source.height).Append('|')
                .Append(source.cellSize.ToString("R", CultureInfo.InvariantCulture));
            foreach (TacticalTerrainSource item in (source.terrains ?? Array.Empty<TacticalTerrainSource>())
                         .OrderBy(value => value.symbol, StringComparer.Ordinal))
            {
                canonical.Append('|').Append(item.symbol).Append(':').Append(item.kind).Append(':')
                    .Append(item.navigationCost.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                    .Append(item.speedMultiplier.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                    .Append(item.accelerationMultiplier.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                    .Append(item.blocked ? '1' : '0').Append(':').Append(item.tacticalFeature);
            }

            foreach (string row in source.rows ?? Array.Empty<string>()) canonical.Append('|').Append(row);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString()));
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static TacticalLaneDescriptor BuildLane(
            TacticalMapSource source,
            IReadOnlyDictionary<char, TacticalTerrainSource> terrain,
            NavigationGridModel grid,
            List<Vector2Int> path,
            Vector2Int crossing,
            int order,
            int laneCount,
            bool horizontal)
        {
            List<float> widths = new List<float>();
            List<float> sightlines = new List<float>();
            List<float> hardCoverProximity = new List<float>();
            List<float> vegetationProximity = new List<float>();
            List<float> concealmentProximity = new List<float>();
            int chokePoints = 0;
            float navigationCost = 0f;
            float length = 0f;
            Dictionary<string, int> terrainCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < path.Count; index++)
            {
                Vector2Int cell = path[index];
                TacticalTerrainSource definition = terrain[source.GetSymbol(cell)];
                navigationCost += definition.navigationCost;
                if (!terrainCounts.TryAdd(definition.kind, 1)) terrainCounts[definition.kind]++;
                if (index > 0)
                {
                    Vector2Int delta = cell - path[index - 1];
                    length += source.cellSize * (delta.x != 0 && delta.y != 0 ? 1.41421356f : 1f);
                }

                if (index % 3 != 0 && index != path.Count - 1) continue;
                float width = MeasureWidth(grid, cell, horizontal, source.cellSize);
                widths.Add(width);
                if (width <= source.cellSize * 3.1f) chokePoints++;
                sightlines.Add(MeasureSightline(grid, cell, source.cellSize));
                hardCoverProximity.Add(MeasureFeatureProximity(source, terrain, cell, 7,
                    feature => feature == TacticalFeatureKind.HardCover ||
                               feature == TacticalFeatureKind.Building ||
                               feature == TacticalFeatureKind.Wall ||
                               feature == TacticalFeatureKind.Rubble));
                vegetationProximity.Add(MeasureFeatureProximity(source, terrain, cell, 7,
                    feature => feature == TacticalFeatureKind.Trees ||
                               feature == TacticalFeatureKind.Bush ||
                               feature == TacticalFeatureKind.Reeds));
                concealmentProximity.Add(MeasureFeatureProximity(source, terrain, cell, 6,
                    feature => feature == TacticalFeatureKind.Bush ||
                               feature == TacticalFeatureKind.Reeds));
            }
            float averageWidth = widths.Count > 0 ? widths.Average() : source.cellSize;
            float averageSightline = sightlines.Count > 0 ? sightlines.Average() : 0f;
            float maximumSightline = sightlines.Count > 0 ? sightlines.Max() : 0f;
            float averageCost = navigationCost / Mathf.Max(1, path.Count);
            float coverDensity = hardCoverProximity.Count > 0 ? hardCoverProximity.Average() : 0f;
            float vegetationDensity = vegetationProximity.Count > 0 ? vegetationProximity.Average() : 0f;
            float concealmentDensity = concealmentProximity.Count > 0 ? concealmentProximity.Average() : 0f;
            return new TacticalLaneDescriptor
            {
                id = LaneId(order, laneCount),
                provisionalName = LaneName(order, laneCount),
                order = order,
                crossingCell = crossing,
                entryA = path[Mathf.Clamp(Mathf.RoundToInt((path.Count - 1) * 0.1f), 0, path.Count - 1)],
                entryB = path[Mathf.Clamp(Mathf.RoundToInt((path.Count - 1) * 0.9f), 0, path.Count - 1)],
                defenseA = path[Mathf.Clamp(Mathf.RoundToInt((path.Count - 1) * 0.25f), 0, path.Count - 1)],
                defenseB = path[Mathf.Clamp(Mathf.RoundToInt((path.Count - 1) * 0.75f), 0, path.Count - 1)],
                pathCells = path.ToArray(),
                lengthWorld = length,
                averageWidthWorld = averageWidth,
                averageNavigationCost = averageCost,
                hardCoverDensity = coverDensity,
                vegetationDensity = vegetationDensity,
                concealmentDensity = concealmentDensity,
                averageSightlineWorld = averageSightline,
                maximumSightlineWorld = maximumSightline,
                chokePointCount = chokePoints,
                hasMandatoryCrossing = HasWaterBesideCrossing(source, terrain, crossing, horizontal),
                terrainShares = terrainCounts.OrderBy(item => item.Key, StringComparer.Ordinal)
                    .Select(item => new TacticalTerrainShare
                    {
                        terrainKind = item.Key,
                        share = item.Value / (float)Mathf.Max(1, path.Count)
                    }).ToArray(),
                suitability = ScoreClasses(source, averageWidth, averageCost, averageSightline,
                    coverDensity, concealmentDensity, chokePoints),
                connections = Array.Empty<TacticalLaneConnection>()
            };
        }

        private static TacticalClassSuitability ScoreClasses(
            TacticalMapSource source,
            float width,
            float navigationCost,
            float sightline,
            float cover,
            float concealment,
            int chokePoints)
        {
            float mobility = Mathf.Clamp01(1.6f / Mathf.Max(1f, navigationCost));
            float openness = Mathf.Clamp01(width / (source.cellSize * 28f));
            float range = Mathf.Clamp01(sightline / (source.cellSize * 40f));
            float coverScore = Mathf.Clamp01(cover * 2.5f);
            float concealmentScore = Mathf.Clamp01(concealment * 2.5f);
            float choke = Mathf.Clamp01(chokePoints / 3f);
            return new TacticalClassSuitability
            {
                lightTank = Weighted(mobility, 0.35f, openness, 0.25f, concealmentScore, 0.2f, range, 0.2f),
                mediumTank = Weighted(mobility, 0.3f, openness, 0.25f, coverScore, 0.25f, range, 0.2f),
                heavyTank = Weighted(coverScore, 0.45f, mobility, 0.2f, choke, 0.25f, 1f - range, 0.1f),
                tankDestroyer = Weighted(concealmentScore, 0.4f, range, 0.35f, coverScore, 0.15f, openness, 0.1f),
                artillery = Weighted(range, 0.5f, openness, 0.3f, 1f - coverScore, 0.2f, mobility, 0f)
            };
        }

        private static float Weighted(float a, float aw, float b, float bw, float c, float cw, float d, float dw)
        {
            return Mathf.Clamp01(a * aw + b * bw + c * cw + d * dw);
        }

        private static void AddConnections(List<TacticalLaneDescriptor> lanes, float cellSize, int width, int height)
        {
            float maximumTransfer = Mathf.Max(width, height) * cellSize * 0.22f;
            foreach (TacticalLaneDescriptor lane in lanes)
            {
                List<TacticalLaneConnection> connections = new List<TacticalLaneConnection>();
                foreach (TacticalLaneDescriptor other in lanes)
                {
                    if (ReferenceEquals(lane, other)) continue;
                    TacticalLaneConnection connection = FindConnection(lane, other, cellSize);
                    if (connection.transferDistanceWorld <= maximumTransfer) connections.Add(connection);
                }

                lane.connections = connections.OrderBy(item => item.transferDistanceWorld).ToArray();
            }
        }

        private static TacticalLaneConnection FindConnection(
            TacticalLaneDescriptor from,
            TacticalLaneDescriptor target,
            float cellSize)
        {
            float best = float.PositiveInfinity;
            Vector2Int fromCell = default;
            Vector2Int targetCell = default;
            int fromStart = Mathf.FloorToInt(from.pathCells.Length * 0.2f);
            int fromEnd = Mathf.CeilToInt(from.pathCells.Length * 0.8f);
            int targetStart = Mathf.FloorToInt(target.pathCells.Length * 0.2f);
            int targetEnd = Mathf.CeilToInt(target.pathCells.Length * 0.8f);
            for (int first = fromStart; first < fromEnd; first += 2)
            for (int second = targetStart; second < targetEnd; second += 2)
            {
                float distance = Vector2Int.Distance(from.pathCells[first], target.pathCells[second]);
                if (distance >= best) continue;
                best = distance;
                fromCell = from.pathCells[first];
                targetCell = target.pathCells[second];
            }

            return new TacticalLaneConnection
            {
                targetLaneId = target.id,
                fromCell = fromCell,
                targetCell = targetCell,
                transferDistanceWorld = best * cellSize
            };
        }

        private static NavigationGridModel BuildGrid(
            TacticalMapSource source,
            IReadOnlyDictionary<char, TacticalTerrainSource> terrain)
        {
            NavigationGridModel grid = new NavigationGridModel(source.width, source.height);
            for (int y = 0; y < source.height; y++)
            for (int x = 0; x < source.width; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                TacticalTerrainSource definition = terrain[source.GetSymbol(cell)];
                grid.SetCell(cell, definition.navigationCost, definition.blocked);
            }

            return grid;
        }

        private static Vector2Int FindFeature(
            TacticalMapSource source,
            IReadOnlyDictionary<char, TacticalTerrainSource> terrain,
            TacticalFeatureKind expected)
        {
            for (int y = 0; y < source.height; y++)
            for (int x = 0; x < source.width; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (TacticalMapSource.ParseFeature(terrain[source.GetSymbol(cell)]) == expected) return cell;
            }

            throw new InvalidOperationException($"Map does not contain feature {expected}.");
        }

        private static List<List<int>> FindCrossingGroups(
            TacticalMapSource source,
            NavigationGridModel grid,
            bool horizontal,
            int scanCoordinate)
        {
            int perpendicularSize = horizontal ? source.height : source.width;
            List<List<int>> groups = new List<List<int>>();
            List<int> current = null;
            for (int perpendicular = 0; perpendicular < perpendicularSize; perpendicular++)
            {
                Vector2Int cell = horizontal
                    ? new Vector2Int(scanCoordinate, perpendicular)
                    : new Vector2Int(perpendicular, scanCoordinate);
                if (!grid.IsBlocked(cell))
                {
                    current ??= new List<int>();
                    current.Add(perpendicular);
                }
                else if (current != null)
                {
                    groups.Add(current);
                    current = null;
                }
            }

            if (current != null) groups.Add(current);
            return groups;
        }

        private static float MeasureWidth(NavigationGridModel grid, Vector2Int cell, bool horizontal, float cellSize)
        {
            Vector2Int perpendicular = horizontal ? Vector2Int.up : Vector2Int.right;
            int count = 1;
            for (int sign = -1; sign <= 1; sign += 2)
            for (int step = 1; step <= 12; step++)
            {
                Vector2Int candidate = cell + perpendicular * (step * sign);
                if (!grid.Contains(candidate) || grid.IsBlocked(candidate)) break;
                count++;
            }

            return count * cellSize;
        }

        private static float MeasureSightline(NavigationGridModel grid, Vector2Int cell, float cellSize)
        {
            float maximum = 0f;
            foreach (Vector2Int direction in SightDirections)
            {
                int steps = 0;
                for (int step = 1; step <= 30; step++)
                {
                    Vector2Int candidate = cell + direction * step;
                    if (!grid.Contains(candidate) || grid.IsBlocked(candidate)) break;
                    steps++;
                }

                float multiplier = direction.x != 0 && direction.y != 0 ? 1.41421356f : 1f;
                maximum = Mathf.Max(maximum, steps * cellSize * multiplier);
            }

            return maximum;
        }

        private static float MeasureFeatureProximity(
            TacticalMapSource source,
            IReadOnlyDictionary<char, TacticalTerrainSource> terrain,
            Vector2Int center,
            int radius,
            Func<TacticalFeatureKind, bool> matches)
        {
            float nearest = float.PositiveInfinity;
            for (int y = center.y - radius; y <= center.y + radius; y++)
            for (int x = center.x - radius; x <= center.x + radius; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (cell.x < 0 || cell.y < 0 || cell.x >= source.width || cell.y >= source.height)
                    continue;
                TacticalFeatureKind feature = TacticalMapSource.ParseFeature(terrain[source.GetSymbol(cell)]);
                if (!matches(feature)) continue;
                nearest = Mathf.Min(nearest, Vector2Int.Distance(center, cell));
            }

            return float.IsPositiveInfinity(nearest)
                ? 0f
                : Mathf.Clamp01(1f - nearest / (radius + 0.5f));
        }

        private static bool HasWaterBesideCrossing(
            TacticalMapSource source,
            IReadOnlyDictionary<char, TacticalTerrainSource> terrain,
            Vector2Int crossing,
            bool horizontal)
        {
            Vector2Int perpendicular = horizontal ? Vector2Int.up : Vector2Int.right;
            for (int distance = 1; distance <= 3; distance++)
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Vector2Int candidate = crossing + perpendicular * (distance * sign);
                if (candidate.x < 0 || candidate.y < 0 || candidate.x >= source.width || candidate.y >= source.height)
                    continue;
                TacticalFeatureKind feature = TacticalMapSource.ParseFeature(terrain[source.GetSymbol(candidate)]);
                if (feature == TacticalFeatureKind.DeepWater || feature == TacticalFeatureKind.ShallowWater) return true;
            }

            return false;
        }

        private static string LaneId(int order, int count)
        {
            if (count == 3) return order == 0 ? "lane_north" : order == 1 ? "lane_center" : "lane_south";
            return $"lane_{order + 1:00}";
        }

        private static string LaneName(int order, int count)
        {
            if (count == 3) return order == 0 ? "Северное направление" : order == 1 ? "Центральное направление" : "Южное направление";
            return $"Направление {order + 1}";
        }
    }
}
