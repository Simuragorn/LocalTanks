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
                if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y) return true;
            }

            return false;
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
    }
}
