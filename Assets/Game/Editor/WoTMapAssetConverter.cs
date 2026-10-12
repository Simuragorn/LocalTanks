using System;
using System.Collections.Generic;
using System.IO;
using LocalTanks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LocalTanks.Editor
{
    /// <summary>
    /// Reads the authoritative JSON produced by <c>Tools/WoTMapPrimitivePipeline</c> and builds the
    /// single map asset the scene builder and the runtime read. The source JSON stays the only
    /// maintained description of the arena; this class is a projection of it into Unity types.
    /// </summary>
    /// <remarks>
    /// The document is loaded as an object model rather than walked token by token. Manual token
    /// positioning is easy to get subtly wrong, and a mistake there does not fail: it silently
    /// produces an empty map. Nesting, arrays and numeric coercion are the library's job.
    /// </remarks>
    public static class WoTMapAssetConverter
    {
        public static WoTMapAsset Convert(string jsonPath, float navigationCellMetres = 4f)
        {
            if (!File.Exists(jsonPath))
            {
                throw new FileNotFoundException($"Primitive map JSON was not found: {jsonPath}", jsonPath);
            }

            JObject root = JObject.Parse(File.ReadAllText(jsonPath));
            WoTMapAsset asset = ScriptableObject.CreateInstance<WoTMapAsset>();
            asset.navigationCellSize = navigationCellMetres;
            asset.mapId = Text(root, "mapId");
            asset.displayName = Text(root, "displayName");
            asset.englishName = Text(root, "englishName");

            JObject source = root["source"] as JObject;
            asset.clientVersion = Text(source, "clientVersion");
            asset.spaceSha256 = Text(source, "spaceSha256");

            ReadPlayableBounds(root["playableBoundsMeters"] as JObject, asset);
            asset.basePositions = ReadPointList(root["bases"] as JArray, asset);
            asset.baseSpawnPositions = ReadPointList(root["spawns"] as JArray, asset);
            ReadTerrain(root["terrain"] as JObject, asset);
            ReadWater(root["water"] as JObject, asset);
            asset.obstacles = ReadObstacles(root["staticPrimitives"] as JArray, asset);
            asset.vegetation = ReadVegetation(root["vegetationPlacements"] as JArray, asset);
            BuildNavigation(asset);
            return asset;
        }

        private static string Text(JObject source, string name) => source?[name]?.Value<string>();

        private static float Number(JToken token, float fallback = 0f)
        {
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<float>();
        }

        /// <summary>
        /// Reads the playable bounds and derives the uniform world placement. The map keeps its true
        /// size: compressing it would make buildings smaller than a tank hull.
        /// </summary>
        private static void ReadPlayableBounds(JObject bounds, WoTMapAsset asset)
        {
            if (bounds == null)
            {
                throw new InvalidDataException("The primitive map has no playableBoundsMeters section.");
            }

            Vector2 min = ToVector2(bounds["min"]);
            Vector2 max = ToVector2(bounds["max"]);
            asset.sourceCenterMetres = (min + max) * 0.5f;
            asset.playableSize = new Vector2(
                (max.x - min.x) / asset.metresPerUnit,
                (max.y - min.y) / asset.metresPerUnit);
            asset.worldCenter = Vector2.zero;
        }

        private static Vector2 ToVector2(JToken token)
        {
            if (token is not JArray array || array.Count < 2)
            {
                throw new InvalidDataException("A coordinate pair is missing or incomplete.");
            }

            return new Vector2(array[0].Value<float>(), array[1].Value<float>());
        }

        private static Vector2[] ReadPointList(JArray entries, WoTMapAsset asset)
        {
            List<Vector2> points = new List<Vector2>();
            if (entries == null) return points.ToArray();
            foreach (JToken entry in entries)
            {
                JToken position = entry["positionMeters"];
                if (position == null) continue;
                points.Add(asset.FromSourceMetres(ToVector2(position)));
            }

            return points.ToArray();
        }

        private static void ReadTerrain(JObject terrain, WoTMapAsset asset)
        {
            if (terrain == null) throw new InvalidDataException("The primitive map has no terrain section.");
            asset.terrainCellSize = Number(terrain["stepMeters"]) / asset.metresPerUnit;
            asset.terrainColumns = terrain["columns"]?.Value<int>() ?? 0;
            asset.terrainRows = terrain["rows"]?.Value<int>() ?? 0;
            asset.terrainMinimumMetres = Number(terrain["minimumMeters"]);
            asset.terrainMaximumMetres = Number(terrain["maximumMeters"]);

            List<float> heights = new List<float>(asset.terrainColumns * asset.terrainRows);
            if (terrain["heightsMeters"] is JArray rows)
            {
                foreach (JToken row in rows)
                {
                    if (row is not JArray values) continue;
                    foreach (JToken value in values) heights.Add(value.Value<float>());
                }
            }

            asset.terrainHeightsMetres = heights.ToArray();
        }

        private static void ReadWater(JObject water, WoTMapAsset asset)
        {
            if (water == null) throw new InvalidDataException("The primitive map has no water section.");
            asset.waterCellSize = Number(water["stepMeters"]) / asset.metresPerUnit;
            asset.waterColumns = water["columns"]?.Value<int>() ?? 0;
            asset.waterRows = water["rows"]?.Value<int>() ?? 0;

            // Cells arrive as [column, row, kind]. Only the position matters for gameplay, so the grid
            // is stored as a flat presence array indexed by row * columns + column.
            List<int> indices = new List<int>();
            if (water["cells"] is JArray cells)
            {
                foreach (JToken cell in cells)
                {
                    if (cell is not JArray parts || parts.Count < 2) continue;
                    int column = parts[0].Value<int>();
                    int row = parts[1].Value<int>();
                    if (column < 0 || row < 0 || column >= asset.waterColumns || row >= asset.waterRows)
                    {
                        continue;
                    }

                    indices.Add(row * asset.waterColumns + column);
                }
            }

            asset.waterCellIndices = indices.ToArray();
        }

        private static WoTMapObstacle[] ReadObstacles(JArray entries, WoTMapAsset asset)
        {
            List<WoTMapObstacle> obstacles = new List<WoTMapObstacle>();
            if (entries == null) return obstacles.ToArray();
            foreach (JToken entry in entries)
            {
                WoTMapObstacle obstacle = new WoTMapObstacle
                {
                    id = Text(entry as JObject, "id"),
                    category = Text(entry as JObject, "category"),
                    shape = ParseShape(Text(entry as JObject, "shape")),
                    // Centres and sizes are converted once, so the scene builder never rescales twice.
                    center = asset.FromSourceMetres(ToVector2(entry["centerMeters"])),
                    size = ToVector2(entry["sizeMeters"]) / asset.metresPerUnit,
                    rotationDegrees = Number(entry["rotationDegrees"]),
                    destructible = entry["destructible"]?.Value<bool>() ?? false,
                    blocksMovement = entry["blocksMovement"]?.Value<bool>() ?? false,
                    blocksProjectiles = entry["blocksProjectiles"]?.Value<bool>() ?? false,
                    blocksVision = entry["blocksVision"]?.Value<bool>() ?? false,
                    sourceResource = Text(entry as JObject, "sourceResource")
                };
                obstacles.Add(obstacle);
            }

            return obstacles.ToArray();
        }

        /// <summary>
        /// Plant placements arrive as a compact array of [x, z, radius, kind, resource]. Reading them
        /// into named fields once keeps the rest of the importer free of positional indexing.
        /// </summary>
        private static WoTMapVegetation[] ReadVegetation(JArray entries, WoTMapAsset asset)
        {
            List<WoTMapVegetation> placements = new List<WoTMapVegetation>();
            if (entries == null) return placements.ToArray();
            foreach (JToken entry in entries)
            {
                if (entry is not JArray values || values.Count < 5) continue;
                bool isTree = string.Equals(values[3].Value<string>(), "tree", StringComparison.OrdinalIgnoreCase);
                placements.Add(new WoTMapVegetation
                {
                    center = asset.FromSourceMetres(new Vector2(
                        values[0].Value<float>(),
                        values[1].Value<float>())),
                    radius = values[2].Value<float>() / asset.metresPerUnit,
                    kind = isTree ? MapCoverKind.Tree : MapCoverKind.Bush,
                    // Standing trees are destructible obstacles; bushes only conceal.
                    destructible = isTree,
                    sourceResource = values[4].Value<string>()
                });
            }

            return placements.ToArray();
        }

        /// <summary>
        /// Builds the coarse navigation grid. Every cell is tested against the map content instead of
        /// stamping each obstacle into the grid: stamping depends on getting every footprint's bounds
        /// and rotation right, and a mistake there silently leaves the map walkable.
        /// <para>
        /// The grid answers routing, cost and terrain kind; it is deliberately coarser than the
        /// colliders, which answer hits and collisions.
        /// </para>
        /// </summary>
        private static void BuildNavigation(WoTMapAsset asset)
        {
            float cellMetres = asset.navigationCellSize * asset.metresPerUnit;
            asset.navigationColumns = Mathf.Max(1, Mathf.RoundToInt(asset.playableSize.x * asset.metresPerUnit / cellMetres));
            asset.navigationRows = Mathf.Max(1, Mathf.RoundToInt(asset.playableSize.y * asset.metresPerUnit / cellMetres));
            int cells = asset.navigationColumns * asset.navigationRows;
            asset.navigationTerrain = new int[cells];
            asset.navigationBlocked = new bool[cells];
            asset.navigationCosts = new float[cells];
            asset.navigationSpeedMultipliers = new float[cells];

            ApplyNavigationPolicy(asset);
            bool[] water = BuildWaterMask(asset);
            float half = asset.navigationCellSize * 0.5f;
            Vector2 origin = asset.worldCenter - asset.playableSize * 0.5f;

            for (int row = 0; row < asset.navigationRows; row++)
            {
                for (int column = 0; column < asset.navigationColumns; column++)
                {
                    int index = row * asset.navigationColumns + column;
                    asset.navigationCosts[index] = 1f;
                    asset.navigationSpeedMultipliers[index] = 1f;
                    Vector2 center = origin + new Vector2(
                        (column + 0.5f) * asset.navigationCellSize,
                        (row + 0.5f) * asset.navigationCellSize);

                    if (water != null && water[index])
                    {
                        asset.navigationTerrain[index] = (int)TerrainKind.ShallowWater;
                        asset.navigationBlocked[index] = true;
                        continue;
                    }

                    ObstacleOverlap overlap = MeasureObstacleOverlap(asset, center, half);
                    if (overlap.Blocked)
                    {
                        asset.navigationTerrain[index] = (int)TerrainKind.Blocked;
                        asset.navigationBlocked[index] = true;
                    }
                    else if (overlap.Rough)
                    {
                        // Passable but slow: routing prefers open ground without refusing to cross cover.
                        asset.navigationTerrain[index] = (int)TerrainKind.Obstacle;
                        asset.navigationCosts[index] = WoTMapAsset.ObstacleNavigationCost;
                        asset.navigationSpeedMultipliers[index] = WoTMapAsset.ObstacleSpeedMultiplier;
                    }
                }
            }
        }

        private readonly struct ObstacleOverlap
        {
            public ObstacleOverlap(bool blocked, bool rough)
            {
                Blocked = blocked;
                Rough = rough;
            }

            /// <summary>Ground a route may not cross at all.</summary>
            public bool Blocked { get; }

            /// <summary>Ground a route may cross at a cost.</summary>
            public bool Rough { get; }
        }

        /// <summary>
        /// Fraction of a cell that a hard blocker must cover before the cell is closed to routing.
        /// Below it the cell is passable-but-slow, which is what keeps doors, alleys and the gaps
        /// between scattered rocks open. Blocking any cell a footprint merely touches is what once
        /// split the converted Mines into dozens of pockets.
        /// </summary>
        private const float BlockingCoverage = 0.6f;

        /// <summary>
        /// Classifies one navigation cell against the obstacles covering it. A cell that is only
        /// partially covered is passable-but-slow; only a hard blocker covering most of the cell
        /// closes it.
        /// </summary>
        private static ObstacleOverlap MeasureObstacleOverlap(WoTMapAsset asset, Vector2 center, float half)
        {
            bool rough = false;
            foreach (WoTMapObstacle obstacle in asset.obstacles)
            {
                if (!obstacle.blocksMovement) continue;
                if (!OverlapsCell(obstacle, center, half)) continue;

                if (!obstacle.blocksNavigation) rough = true;
                else if (Coverage(obstacle, center, half) >= BlockingCoverage) return new ObstacleOverlap(true, true);
                else rough = true;
            }

            return new ObstacleOverlap(false, rough);
        }

        /// <summary>
        /// Fraction of the cell's axis-aligned extent that the rotated footprint covers along each axis,
        /// taking the smaller of the two as the covered share. This is a cheap, monotone stand-in for a
        /// true area intersection and is only used to decide whether a cell is more obstacle than ground.
        /// </summary>
        private static float Coverage(WoTMapObstacle obstacle, Vector2 center, float half)
        {
            Vector2 halfSize = obstacle.size * 0.5f;
            float radians = -obstacle.rotationDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            Vector2 delta = center - obstacle.center;
            Vector2 local = new Vector2(
                delta.x * cos - delta.y * sin,
                delta.x * sin + delta.y * cos);

            // Extent of the rotated footprint along the cell axes, which is its bounding box in the cell
            // frame. Using the bounding box is deliberately conservative: it can only make a cell look
            // more covered, never less.
            float extentX = Mathf.Abs(halfSize.x * cos) + Mathf.Abs(halfSize.y * sin);
            float extentY = Mathf.Abs(halfSize.x * sin) + Mathf.Abs(halfSize.y * cos);
            float shareX = Mathf.Clamp01((extentX - Mathf.Abs(local.x) + half) / (2f * half));
            float shareY = Mathf.Clamp01((extentY - Mathf.Abs(local.y) + half) / (2f * half));
            return Mathf.Min(shareX, shareY);
        }

        /// <summary>
        /// Decides which obstacles close ground. Routing a tank through a dense arena cannot treat every
        /// fence and boulder as a wall: on the converted Mines that blocked about 68% of the grid and
        /// split the field into more than a hundred pockets.
        /// </summary>
        private static void ApplyNavigationPolicy(WoTMapAsset asset)
        {
            int blockers = 0;
            float threshold = WoTMapAsset.NavigationBlockerMetres / asset.metresPerUnit;
            foreach (WoTMapObstacle obstacle in asset.obstacles)
            {
                obstacle.blocksNavigation = asset.navigationPolicy switch
                {
                    MapNavigationPolicy.Never => false,
                    MapNavigationPolicy.AllMovementBlockers => obstacle.blocksMovement,
                    MapNavigationPolicy.DestructiblePassable => obstacle.blocksMovement &&
                                                                !obstacle.destructible &&
                                                                Mathf.Max(obstacle.size.x, obstacle.size.y) >= threshold,
                    MapNavigationPolicy.ObstacleCost => obstacle.blocksMovement && !obstacle.destructible,
                    _ => obstacle.blocksMovement &&
                         !obstacle.destructible &&
                         Mathf.Max(obstacle.size.x, obstacle.size.y) >= threshold
                };

                if (obstacle.blocksNavigation) blockers++;
            }

            asset.navigationBlockerCount = blockers;
        }

        /// <summary>
        /// Water presence per navigation cell. The water grid has a different step, so each water cell
        /// is projected onto the navigation cells whose centres it covers.
        /// </summary>
        private static bool[] BuildWaterMask(WoTMapAsset asset)
        {
            if (asset.waterCellIndices == null || asset.waterCellIndices.Length == 0) return null;
            bool[] water = new bool[asset.navigationColumns * asset.navigationRows];
            float waterStep = asset.waterCellSize;
            Vector2 origin = asset.worldCenter - asset.playableSize * 0.5f;

            foreach (int index in asset.waterCellIndices)
            {
                int column = index % asset.waterColumns;
                int row = index / asset.waterColumns;
                Vector2 corner = origin + new Vector2(column * waterStep, row * waterStep);
                int firstColumn = Mathf.FloorToInt((corner.x - origin.x) / asset.navigationCellSize);
                int firstRow = Mathf.FloorToInt((corner.y - origin.y) / asset.navigationCellSize);
                int lastColumn = Mathf.FloorToInt((corner.x - origin.x + waterStep - 0.001f) / asset.navigationCellSize);
                int lastRow = Mathf.FloorToInt((corner.y - origin.y + waterStep - 0.001f) / asset.navigationCellSize);

                for (int r = Mathf.Max(0, firstRow); r <= Mathf.Min(asset.navigationRows - 1, lastRow); r++)
                {
                    for (int c = Mathf.Max(0, firstColumn); c <= Mathf.Min(asset.navigationColumns - 1, lastColumn); c++)
                    {
                        water[r * asset.navigationColumns + c] = true;
                    }
                }
            }

            return water;
        }

        /// <summary>
        /// True when a square of half-extent <paramref name="half"/> centred on
        /// <paramref name="center"/> touches the obstacle's rotated footprint, which is the shape the
        /// pipeline emits.
        /// </summary>
        private static bool OverlapsCell(WoTMapObstacle obstacle, Vector2 center, float half)
        {
            Vector2 halfSize = obstacle.size * 0.5f;
            if (halfSize.x <= 0f || halfSize.y <= 0f) return false;

            // Work in the obstacle's own frame so the rotation is a change of basis, not a test.
            float radians = -obstacle.rotationDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            Vector2 delta = center - obstacle.center;
            Vector2 local = new Vector2(
                delta.x * cos - delta.y * sin,
                delta.x * sin + delta.y * cos);

            if (Mathf.Abs(local.x) > halfSize.x + half) return false;
            if (Mathf.Abs(local.y) > halfSize.y + half) return false;
            return true;
        }

        /// <summary>
        /// Counts how the walkable ground is split up. A grid can look acceptable by blocked-cell count
        /// and still be useless if the open cells form a hundred pockets, so this is the number that
        /// decides whether a converted arena is playable. Returns the largest region and the count.
        /// </summary>
        public static (int Largest, int Regions, int Walkable) MeasureWalkableRegions(WoTMapAsset asset)
        {
            int columns = asset.navigationColumns;
            int rows = asset.navigationRows;
            bool[] seen = new bool[columns * rows];
            List<int> sizes = new List<int>();
            int walkable = 0;

            for (int start = 0; start < seen.Length; start++)
            {
                if (asset.navigationBlocked[start] || seen[start]) continue;
                int size = 0;
                Stack<int> pending = new Stack<int>();
                pending.Push(start);
                seen[start] = true;
                while (pending.Count > 0)
                {
                    int index = pending.Pop();
                    size++;
                    walkable++;
                    int column = index % columns;
                    int row = index / columns;
                    if (column > 0) Visit(index - 1);
                    if (column < columns - 1) Visit(index + 1);
                    if (row > 0) Visit(index - columns);
                    if (row < rows - 1) Visit(index + columns);
                }

                sizes.Add(size);

                void Visit(int neighbour)
                {
                    if (asset.navigationBlocked[neighbour] || seen[neighbour]) return;
                    seen[neighbour] = true;
                    pending.Push(neighbour);
                }
            }

            sizes.Sort();
            return (sizes.Count > 0 ? sizes[sizes.Count - 1] : 0, sizes.Count, walkable);
        }

        private static MapObstacleShape ParseShape(string shape)
        {
            return shape switch
            {
                "ellipse" => MapObstacleShape.Ellipse,
                "circle" => MapObstacleShape.Circle,
                _ => MapObstacleShape.Rectangle
            };
        }
    }
}
