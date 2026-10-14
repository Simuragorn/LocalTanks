using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LocalTanks.Tests
{
    /// <summary>
    /// A spawn or route point that sits inside a solid obstacle is unreachable, and a tank sent to one
    /// circles it for the whole battle while reporting a valid path. These checks find such points before
    /// a scene is built instead of after an hour of playtesting.
    /// </summary>
    public sealed class WoTMapReachabilityTests
    {
        private const string AssetPath = "Assets/Game/GameData/Generated/Maps/10_hills_map.asset";

        private static WoTMapAsset Load()
        {
            return AssetDatabase.LoadAssetAtPath<WoTMapAsset>(AssetPath);
        }

        /// <summary>
        /// True when a solid obstacle covers the given point. Rotation is respected, because a fence or a
        /// house turned across a spawn point blocks it just as effectively as one aligned with it.
        /// </summary>
        private static bool InsideSolid(WoTMapAsset map, Vector2 point)
        {
            foreach (WoTMapObstacle obstacle in map.obstacles)
            {
                if (!map.IsSolid(obstacle)) continue;
                Vector2 half = obstacle.size * 0.5f;
                if (half.x <= 0f || half.y <= 0f) continue;

                float radians = -obstacle.rotationDegrees * Mathf.Deg2Rad;
                float cos = Mathf.Cos(radians);
                float sin = Mathf.Sin(radians);
                Vector2 delta = point - obstacle.center;
                Vector2 local = new Vector2(
                    delta.x * cos - delta.y * sin,
                    delta.x * sin + delta.y * cos);
                if (obstacle.shape == MapObstacleShape.Circle || obstacle.shape == MapObstacleShape.Ellipse)
                {
                    float radius = Mathf.Min(half.x, half.y);
                    if (local.sqrMagnitude <= radius * radius) return true;
                    continue;
                }

                if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y) return true;
            }

            return false;
        }

        /// <summary>
        /// Whether routing can actually get from a base to the far side of the arena. Per-cell walkability
        /// is not enough: two walkable cells can sit in different connected regions, and a path between
        /// them comes back empty while both ends look perfectly fine. On the converted arena this is the
        /// standing explanation for six bots halting at the same distance from their first route point.
        /// </summary>
        [Test]
        public void BothBases_AreInTheSameWalkableRegion()
        {
            WoTMapAsset map = Load();
            if (map == null) Assert.Ignore("The converted map asset has not been imported.");

            Vector2 origin = map.worldCenter - map.playableSize * 0.5f;
            int startColumn = Mathf.FloorToInt((map.basePositions[0].x - origin.x) / map.navigationCellSize);
            int startRow = Mathf.FloorToInt((map.basePositions[0].y - origin.y) / map.navigationCellSize);
            int goalColumn = Mathf.FloorToInt((map.basePositions[1].x - origin.x) / map.navigationCellSize);
            int goalRow = Mathf.FloorToInt((map.basePositions[1].y - origin.y) / map.navigationCellSize);

            HashSet<Vector2Int> region = FloodFill(map, new Vector2Int(startColumn, startRow));
            Assert.That(region, Does.Contain(new Vector2Int(goalColumn, goalRow)),
                $"Base B at {map.basePositions[1]} is not reachable on foot from base A at {map.basePositions[0]}. " +
                $"Base A's region holds {region.Count} cells; the whole grid has " +
                $"{map.navigationColumns * map.navigationRows}.");
        }

        /// <summary>Connected walkable cells around a starting cell, four-way.</summary>
        private static HashSet<Vector2Int> FloodFill(WoTMapAsset map, Vector2Int start)
        {
            HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
            if (!IsWalkable(map, start)) return visited;

            Queue<Vector2Int> pending = new Queue<Vector2Int>();
            pending.Enqueue(start);
            visited.Add(start);
            Vector2Int[] steps = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            while (pending.Count > 0)
            {
                Vector2Int cell = pending.Dequeue();
                foreach (Vector2Int step in steps)
                {
                    Vector2Int next = cell + step;
                    if (visited.Contains(next) || !IsWalkable(map, next)) continue;
                    visited.Add(next);
                    pending.Enqueue(next);
                }
            }

            return visited;
        }

        private static bool IsWalkable(WoTMapAsset map, Vector2Int cell)
        {
            if (cell.x < 0 || cell.y < 0 || cell.x >= map.navigationColumns || cell.y >= map.navigationRows)
                return false;
            return !map.navigationBlocked[cell.y * map.navigationColumns + cell.x];
        }

        [Test]
        public void BasePositions_AreNotInsideSolidGeometry()
        {
            WoTMapAsset map = Load();
            if (map == null) Assert.Ignore("The converted map asset has not been imported.");

            List<string> trapped = map.basePositions
                .Where(position => InsideSolid(map, position))
                .Select(position => $"base at {position}")
                .ToList();

            Assert.That(trapped, Is.Empty,
                "A base inside solid geometry cannot be reached or spawned from:\n- " +
                string.Join("\n- ", trapped));
        }

        /// <summary>
        /// The base exists to be captured, so a tank must be able to stand on it. This is the check that
        /// would have caught the arena's stalled bots before they were built into a scene.
        /// </summary>
        [Test]
        public void BasePositions_AreOnWalkableNavigationCells()
        {
            WoTMapAsset map = Load();
            if (map == null) Assert.Ignore("The converted map asset has not been imported.");

            foreach (Vector2 position in map.basePositions)
            {
                Vector2 origin = map.worldCenter - map.playableSize * 0.5f;
                int column = Mathf.FloorToInt((position.x - origin.x) / map.navigationCellSize);
                int row = Mathf.FloorToInt((position.y - origin.y) / map.navigationCellSize);
                Assert.That(column, Is.InRange(0, map.navigationColumns - 1), $"base {position} is off the grid");
                Assert.That(row, Is.InRange(0, map.navigationRows - 1), $"base {position} is off the grid");
                int index = row * map.navigationColumns + column;
                Assert.That(map.navigationBlocked[index], Is.False,
                    $"Base at {position} stands on a navigation cell routing cannot cross.");
            }
        }

        [Test]
        public void SolidColliderPolicy_ExactlyMatchesNavigationBlockers()
        {
            WoTMapAsset map = Load();
            if (map == null) Assert.Ignore("The converted map asset has not been imported.");

            List<WoTMapObstacle> missing = map.obstacles
                .Where(obstacle => map.IsSolid(obstacle) && !obstacle.blocksNavigation)
                .ToList();
            List<WoTMapObstacle> phantom = map.obstacles
                .Where(obstacle => !map.IsSolid(obstacle) && obstacle.blocksNavigation)
                .ToList();

            Assert.That(missing, Is.Empty,
                $"{missing.Count} solid colliders are still passable to routing. First: " +
                string.Join(", ", missing.Take(8).Select(item => item.id)));
            Assert.That(phantom, Is.Empty,
                $"{phantom.Count} navigation blockers have no solid collider. First: " +
                string.Join(", ", phantom.Take(8).Select(item => item.id)));
        }

        [Test]
        public void WalkableCellCentres_AreOutsideSolidColliders()
        {
            WoTMapAsset map = Load();
            if (map == null) Assert.Ignore("The converted map asset has not been imported.");

            Vector2 origin = map.worldCenter - map.playableSize * 0.5f;
            List<Vector2Int> conflicts = new List<Vector2Int>();
            for (int row = 0; row < map.navigationRows; row++)
            {
                for (int column = 0; column < map.navigationColumns; column++)
                {
                    int index = row * map.navigationColumns + column;
                    if (map.navigationBlocked[index]) continue;
                    Vector2 centre = origin + new Vector2(
                        (column + 0.5f) * map.navigationCellSize,
                        (row + 0.5f) * map.navigationCellSize);
                    if (InsideSolid(map, centre)) conflicts.Add(new Vector2Int(column, row));
                }
            }

            Assert.That(conflicts, Is.Empty,
                $"{conflicts.Count} walkable navigation nodes sit inside real solid colliders. First: " +
                string.Join(", ", conflicts.Take(12)));
        }
    }
}
