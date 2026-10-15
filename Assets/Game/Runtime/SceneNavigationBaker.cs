using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LocalTanks
{
    public readonly struct SceneNavigationBakeReport
    {
        public SceneNavigationBakeReport(int colliderCount, int blockedCellCount, long elapsedMilliseconds)
        {
            ColliderCount = colliderCount;
            BlockedCellCount = blockedCellCount;
            ElapsedMilliseconds = elapsedMilliseconds;
        }

        public int ColliderCount { get; }
        public int BlockedCellCount { get; }
        public long ElapsedMilliseconds { get; }
        public bool WasRebuilt => ColliderCount > 0;
    }

    /// <summary>
    /// Rebuilds obstacle occupancy from the colliders that are actually present in a loaded scene. The
    /// imported grid still supplies bounds and ground types, but moving a rock, wall or building no longer
    /// requires changing map data or authored bot routes: the physical scene is authoritative for walls.
    /// </summary>
    public static class SceneNavigationBaker
    {
        private const float SampleOffset = 0.35f;
        private const int RequiredSamples = 5;
        private const int NearestWalkableRadius = 12;

        public static SceneNavigationBakeReport Rebuild(NavigationMap map, Scene scene)
        {
            if (map == null || !scene.IsValid() || !scene.isLoaded)
            {
                return default;
            }

            Stopwatch timer = Stopwatch.StartNew();
            List<Collider2D> colliders = CollectBlockingColliders(scene);
            Physics2D.SyncTransforms();
            HashSet<Vector2Int> blockedCells = new HashSet<Vector2Int>();
            Dictionary<DestructibleObstacle, HashSet<Vector2Int>> destructibleCells =
                new Dictionary<DestructibleObstacle, HashSet<Vector2Int>>();

            foreach (Collider2D collider in colliders)
            {
                Rasterize(collider, map, blockedCells, destructibleCells);
            }

            map.ReplaceSceneObstacleMask(blockedCells);
            foreach (KeyValuePair<DestructibleObstacle, HashSet<Vector2Int>> pair in destructibleCells)
            {
                pair.Key.SetNavigationCells(map, pair.Value.OrderBy(cell => cell.y).ThenBy(cell => cell.x).ToArray());
            }

            timer.Stop();
            UnityEngine.Debug.Log(
                $"Scene navigation rebuilt from {colliders.Count} collider(s): " +
                $"{blockedCells.Count} blocked cell(s) in {timer.ElapsedMilliseconds} ms.");
            return new SceneNavigationBakeReport(colliders.Count, blockedCells.Count, timer.ElapsedMilliseconds);
        }

        /// <summary>
        /// Connects the authored lane hints over the freshly baked grid. Blocked hints move to the nearest
        /// walkable cell, unreachable hints are skipped, and a final path to the opposing base is added.
        /// The result is a real connected route, not a promise that old points still happen to be valid.
        /// </summary>
        public static Vector2[] BuildConnectedRoute(
            NavigationMap map,
            IReadOnlyList<Vector2Int> laneHints,
            Vector2 startWorld,
            Vector2 destinationWorld)
        {
            if (map == null || !TryFindNearestWalkable(map, map.WorldToCell(startWorld), out Vector2Int current))
            {
                return Array.Empty<Vector2>();
            }

            List<Vector2Int> connected = new List<Vector2Int> { current };
            if (laneHints != null)
            {
                foreach (Vector2Int hint in SelectLaneAnchors(laneHints))
                {
                    AppendReachableLeg(map, connected, ref current, hint);
                }
            }

            AppendReachableLeg(map, connected, ref current, map.WorldToCell(destinationWorld));
            if (connected.Count < 2)
            {
                return Array.Empty<Vector2>();
            }

            // These are sparse strategic anchors, not adjacent A* cells. SimplifyCollinear compares only
            // the sign of each delta, so applying it here removes real bends whenever several distant
            // anchors happen to lie in the same broad quadrant. The NavigationAgent already simplifies
            // the detailed A* path of every leg; all connected lane anchors must remain mandatory.
            return connected.Select(map.CellToWorld).ToArray();
        }

        private static IEnumerable<Vector2Int> SelectLaneAnchors(IReadOnlyList<Vector2Int> laneHints)
        {
            if (laneHints == null || laneHints.Count <= 2)
            {
                yield break;
            }

            if (laneHints.Count <= 6)
            {
                // The lane path begins and ends at the two base cells. BuildConnectedRoute already owns
                // the tank's actual spawn and the opposing base destination, so keeping those authored
                // endpoints makes every member of a formation converge on the centre of its own base
                // before departing. Use only interior guidance and let spread-out spawns fan directly into
                // the lane.
                for (int index = 1; index < laneHints.Count - 1; index++)
                {
                    yield return laneHints[index];
                }

                yield break;
            }

            int previous = -1;
            // Endpoints are supplied separately as the real spawn and requested destination. Only the
            // interior quarters describe the tactical shape of the lane.
            float[] fractions = { 0.25f, 0.5f, 0.75f };
            foreach (float fraction in fractions)
            {
                int index = Mathf.RoundToInt((laneHints.Count - 1) * fraction);
                if (index == previous)
                {
                    continue;
                }

                previous = index;
                yield return laneHints[index];
            }
        }

        private static List<Collider2D> CollectBlockingColliders(Scene scene)
        {
            List<Collider2D> result = new List<Collider2D>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Collider2D collider in root.GetComponentsInChildren<Collider2D>(true))
                {
                    if (IsBlockingSceneCollider(collider))
                    {
                        result.Add(collider);
                    }
                }
            }

            return result
                .OrderBy(collider => HierarchyKey(collider.transform), StringComparer.Ordinal)
                .ToList();
        }

        private static bool IsBlockingSceneCollider(Collider2D collider)
        {
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger)
            {
                return false;
            }

            if (collider.GetComponentInParent<TeamMember>() != null ||
                collider.GetComponentInParent<CaptureBase>() != null ||
                collider.GetComponentInParent<ConcealmentZone>() != null)
            {
                return false;
            }

            Rigidbody2D body = collider.attachedRigidbody;
            return body == null || body.bodyType == RigidbodyType2D.Static;
        }

        private static void Rasterize(
            Collider2D collider,
            NavigationMap map,
            HashSet<Vector2Int> blockedCells,
            Dictionary<DestructibleObstacle, HashSet<Vector2Int>> destructibleCells)
        {
            Bounds bounds = collider.bounds;
            Vector2 padding = Vector2.one * (map.CellSize * 0.5f);
            Vector2Int minimum = map.WorldToCell((Vector2)bounds.min - padding);
            Vector2Int maximum = map.WorldToCell((Vector2)bounds.max + padding);
            DestructibleObstacle destructible = collider.GetComponentInParent<DestructibleObstacle>();

            for (int row = Mathf.Max(0, minimum.y); row <= Mathf.Min(map.Height - 1, maximum.y); row++)
            {
                for (int column = Mathf.Max(0, minimum.x); column <= Mathf.Min(map.Width - 1, maximum.x); column++)
                {
                    Vector2Int cell = new Vector2Int(column, row);
                    if (!CoversCell(collider, map.CellToWorld(cell), map.CellSize))
                    {
                        continue;
                    }

                    blockedCells.Add(cell);
                    if (destructible != null)
                    {
                        if (!destructibleCells.TryGetValue(destructible, out HashSet<Vector2Int> cells))
                        {
                            cells = new HashSet<Vector2Int>();
                            destructibleCells.Add(destructible, cells);
                        }

                        cells.Add(cell);
                    }
                }
            }
        }

        private static bool CoversCell(Collider2D collider, Vector2 center, float cellSize)
        {
            if (collider.OverlapPoint(center))
            {
                return true;
            }

            float offset = cellSize * SampleOffset;
            int covered = 0;
            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    if (x == 0 && y == 0)
                    {
                        continue;
                    }

                    if (collider.OverlapPoint(center + new Vector2(x * offset, y * offset)) &&
                        ++covered >= RequiredSamples)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void AppendReachableLeg(
            NavigationMap map,
            List<Vector2Int> connected,
            ref Vector2Int current,
            Vector2Int desired)
        {
            if (!TryFindNearestWalkable(map, desired, out Vector2Int target) || target == current)
            {
                return;
            }

            List<Vector2Int> leg = map.FindCellPath(current, target, 0, 1f);
            if (leg.Count < 2)
            {
                return;
            }

            // The NavigationAgent computes the detailed path for this leg. Keeping only the connected
            // high-level target preserves the three tactical lanes and avoids turning every A* corner
            // into a strategic route assignment.
            connected.Add(target);
            current = target;
        }

        private static bool TryFindNearestWalkable(
            NavigationMap map,
            Vector2Int desired,
            out Vector2Int result)
        {
            if (map.Contains(desired) && !map.IsBlocked(desired))
            {
                result = desired;
                return true;
            }

            for (int radius = 1; radius <= NearestWalkableRadius; radius++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    for (int x = -radius; x <= radius; x++)
                    {
                        if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) != radius)
                        {
                            continue;
                        }

                        Vector2Int candidate = desired + new Vector2Int(x, y);
                        if (map.Contains(candidate) && !map.IsBlocked(candidate))
                        {
                            result = candidate;
                            return true;
                        }
                    }
                }
            }

            result = default;
            return false;
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
