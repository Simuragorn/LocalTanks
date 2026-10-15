using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    public enum LineOfFireBlockType
    {
        None,
        Ally,
        Enemy,
        Wreck,
        DestructibleObstacle,
        HardObstacle
    }

    public readonly struct LineOfFireAssessment
    {
        public LineOfFireAssessment(
            FireBlockReason reason,
            LineOfFireBlockType blockType,
            Collider2D blocker,
            Vector2 blockPoint,
            Vector2 blockNormal,
            float distance,
            bool canBeDestroyed)
        {
            Reason = reason;
            BlockType = blockType;
            Blocker = blocker;
            BlockPoint = blockPoint;
            BlockNormal = blockNormal;
            Distance = distance;
            CanBeDestroyed = canBeDestroyed;
        }

        public FireBlockReason Reason { get; }
        public LineOfFireBlockType BlockType { get; }
        public Collider2D Blocker { get; }
        public Vector2 BlockPoint { get; }
        public Vector2 BlockNormal { get; }
        public float Distance { get; }
        public bool CanBeDestroyed { get; }
        public bool IsClear => Reason == FireBlockReason.None;
        public string BlockerId => Blocker != null ? Blocker.GetEntityId().ToString() : string.Empty;
        public string BlockerName => Blocker != null ? Blocker.name : "—";

        public static LineOfFireAssessment Clear(Vector2 destination, float distance)
        {
            return new LineOfFireAssessment(
                FireBlockReason.None,
                LineOfFireBlockType.None,
                null,
                destination,
                Vector2.zero,
                distance,
                false);
        }
    }

    public static class CombatLineOfFire
    {
        private static readonly List<RaycastHit2D> Hits = new List<RaycastHit2D>(128);
        private static readonly Comparison<RaycastHit2D> HitComparison = PhysicsQueryOrder.CompareHits;
        private static ContactFilter2D hitFilter = new ContactFilter2D().NoFilter();

        public static bool IsProtectiveCover(LineOfFireBlockType blockType)
        {
            return blockType == LineOfFireBlockType.Wreck ||
                   blockType == LineOfFireBlockType.DestructibleObstacle ||
                   blockType == LineOfFireBlockType.HardObstacle;
        }

        public static LineOfFireAssessment Evaluate(
            Transform ownerRoot,
            TeamId ownerTeam,
            Vector2 origin,
            Vector2 destination,
            Transform intendedTarget)
        {
            Vector2 delta = destination - origin;
            float distance = delta.magnitude;
            if (distance <= 0.001f)
            {
                return LineOfFireAssessment.Clear(destination, distance);
            }

            Hits.Clear();
            Physics2D.Raycast(origin, delta / distance, hitFilter, Hits, distance + 0.2f);
            Hits.Sort(HitComparison);
            foreach (RaycastHit2D hit in Hits)
            {
                if (hit.collider == null || hit.collider.isTrigger ||
                    hit.collider.GetComponentInParent<FirePassThroughObstacle>() != null ||
                    BelongsTo(hit.collider.transform, ownerRoot))
                {
                    continue;
                }

                if (intendedTarget != null && BelongsTo(hit.collider.transform, intendedTarget))
                {
                    return LineOfFireAssessment.Clear(destination, distance);
                }

                TeamMember hitMember = hit.collider.GetComponentInParent<TeamMember>();
                if (hitMember != null)
                {
                    if (!hitMember.IsAlive)
                    {
                        return new LineOfFireAssessment(
                            FireBlockReason.ObstacleInLine,
                            LineOfFireBlockType.Wreck,
                            hit.collider,
                            hit.point,
                            hit.normal,
                            hit.distance,
                            false);
                    }

                    bool ally = hitMember.Team == ownerTeam;
                    return new LineOfFireAssessment(
                        ally ? FireBlockReason.AllyInLine : FireBlockReason.ObstacleInLine,
                        ally ? LineOfFireBlockType.Ally : LineOfFireBlockType.Enemy,
                        hit.collider,
                        hit.point,
                        hit.normal,
                        hit.distance,
                        false);
                }

                DestructibleObstacle destructible = hit.collider.GetComponentInParent<DestructibleObstacle>();
                return new LineOfFireAssessment(
                    FireBlockReason.ObstacleInLine,
                    destructible != null
                        ? LineOfFireBlockType.DestructibleObstacle
                        : LineOfFireBlockType.HardObstacle,
                    hit.collider,
                    hit.point,
                    hit.normal,
                    hit.distance,
                    destructible != null && !destructible.IsDestroyed);
            }

            return LineOfFireAssessment.Clear(destination, distance);
        }

        private static bool BelongsTo(Transform candidate, Transform root)
        {
            return root != null && (candidate == root || candidate.IsChildOf(root));
        }
    }

    public readonly struct TacticalFiringPositionSolution
    {
        public TacticalFiringPositionSolution(
            Vector2Int cell,
            Vector2 position,
            float score,
            float pathLength,
            float targetDistance,
            int evaluatedCandidates,
            Vector2 initialPathDirection)
        {
            Cell = cell;
            Position = position;
            Score = score;
            PathLength = pathLength;
            TargetDistance = targetDistance;
            EvaluatedCandidates = evaluatedCandidates;
            InitialPathDirection = initialPathDirection;
        }

        public Vector2Int Cell { get; }
        public Vector2 Position { get; }
        public float Score { get; }
        public float PathLength { get; }
        public float TargetDistance { get; }
        public int EvaluatedCandidates { get; }
        public Vector2 InitialPathDirection { get; }
    }

    public static class TacticalFiringPositionPlanner
    {
        public const float DefaultSearchDistance = 9f;

        public static bool TryFind(
            NavigationMap map,
            Vector2 origin,
            Vector2 target,
            float preferredDistance,
            float maximumFiringDistance,
            int clearanceCells,
            float maximumSearchDistance,
            Func<Vector2, bool> hasClearLine,
            Func<Vector2Int, bool> isExcluded,
            out TacticalFiringPositionSolution solution)
        {
            return TryFind(
                map,
                origin,
                target,
                preferredDistance,
                maximumFiringDistance,
                clearanceCells,
                maximumSearchDistance,
                hasClearLine,
                isExcluded,
                null,
                out solution);
        }

        public static bool TryFind(
            NavigationMap map,
            Vector2 origin,
            Vector2 target,
            float preferredDistance,
            float maximumFiringDistance,
            int clearanceCells,
            float maximumSearchDistance,
            Func<Vector2, bool> hasClearLine,
            Func<Vector2Int, bool> isExcluded,
            Func<IReadOnlyList<Vector2>, float> pathScoreAdjustment,
            out TacticalFiringPositionSolution solution)
        {
            solution = default;
            if (map == null || hasClearLine == null || maximumFiringDistance <= 0f)
            {
                return false;
            }

            float searchDistance = Mathf.Max(map.CellSize, maximumSearchDistance);
            int searchCells = Mathf.CeilToInt(searchDistance / map.CellSize);
            Vector2Int originCell = map.WorldToCell(origin);
            bool found = false;
            float bestScore = float.PositiveInfinity;
            int evaluated = 0;
            Vector2Int bestCell = default;
            Vector2 bestPosition = default;
            float bestPathLength = 0f;
            float bestTargetDistance = 0f;
            Vector2 bestInitialPathDirection = Vector2.zero;

            for (int y = originCell.y - searchCells; y <= originCell.y + searchCells; y++)
            {
                for (int x = originCell.x - searchCells; x <= originCell.x + searchCells; x++)
                {
                    Vector2Int cell = new Vector2Int(x, y);
                    if (!map.Contains(cell) || map.IsBlocked(cell) ||
                        (isExcluded != null && isExcluded(cell)))
                    {
                        continue;
                    }

                    Vector2 candidate = map.CellToWorld(cell);
                    float movementDistance = Vector2.Distance(origin, candidate);
                    if (movementDistance < map.CellSize * 0.75f || movementDistance > searchDistance)
                    {
                        continue;
                    }

                    float targetDistance = Vector2.Distance(candidate, target);
                    if (targetDistance > maximumFiringDistance || targetDistance < map.CellSize)
                    {
                        continue;
                    }

                    evaluated++;
                    if (!hasClearLine(candidate))
                    {
                        continue;
                    }

                    List<Vector2> path = map.FindWorldPath(origin, candidate, clearanceCells);
                    if (path == null || path.Count < 2)
                    {
                        continue;
                    }

                    float pathLength = CalculatePathLength(path);
                    float distanceError = Mathf.Abs(targetDistance - Mathf.Max(map.CellSize, preferredDistance));
                    float detour = Mathf.Max(0f, pathLength - movementDistance);
                    float pathAdjustment = pathScoreAdjustment != null
                        ? Mathf.Max(0f, pathScoreAdjustment(path))
                        : 0f;
                    float score = pathLength * 1.6f + distanceError * 0.7f + detour * 0.9f + pathAdjustment;
                    if (!found || score < bestScore - 0.001f ||
                        Mathf.Approximately(score, bestScore) && IsEarlier(cell, bestCell))
                    {
                        found = true;
                        bestScore = score;
                        bestCell = cell;
                        bestPosition = candidate;
                        bestPathLength = pathLength;
                        bestTargetDistance = targetDistance;
                        bestInitialPathDirection = FirstPathDirection(path);
                    }
                }
            }

            if (!found)
            {
                return false;
            }

            solution = new TacticalFiringPositionSolution(
                bestCell,
                bestPosition,
                bestScore,
                bestPathLength,
                bestTargetDistance,
                evaluated,
                bestInitialPathDirection);
            return true;
        }

        public static float ThreatExposurePenalty(
            IReadOnlyList<Vector2> path,
            Vector2 threatDirection,
            float maximumReversePathLength)
        {
            if (path == null || path.Count < 2 || threatDirection.sqrMagnitude <= 0.0001f) return 0f;
            float pathLength = CalculatePathLength(path);
            if (pathLength <= Mathf.Max(0f, maximumReversePathLength)) return 0f;
            Vector2 initialDirection = FirstPathDirection(path);
            if (initialDirection.sqrMagnitude <= 0.0001f) return 0f;
            float rearAngle = Mathf.Abs(Vector2.SignedAngle(initialDirection, threatDirection.normalized));
            return Mathf.InverseLerp(100f, 180f, rearAngle) * 18f;
        }

        public static bool ShouldApproachInReverse(
            TacticalFiringPositionSolution solution,
            Vector2 threatDirection,
            float maximumReverseSpeed,
            float maximumReversePathLength)
        {
            if (maximumReverseSpeed <= 0.05f || solution.PathLength > maximumReversePathLength ||
                solution.InitialPathDirection.sqrMagnitude <= 0.0001f ||
                threatDirection.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            float rearAngle = Mathf.Abs(Vector2.SignedAngle(
                solution.InitialPathDirection,
                threatDirection.normalized));
            return rearAngle >= 100f;
        }

        private static float CalculatePathLength(IReadOnlyList<Vector2> path)
        {
            float length = 0f;
            for (int index = 1; index < path.Count; index++)
            {
                length += Vector2.Distance(path[index - 1], path[index]);
            }

            return length;
        }

        private static Vector2 FirstPathDirection(IReadOnlyList<Vector2> path)
        {
            if (path == null || path.Count < 2) return Vector2.zero;
            Vector2 origin = path[0];
            for (int index = 1; index < path.Count; index++)
            {
                Vector2 direction = path[index] - origin;
                if (direction.sqrMagnitude > 0.0001f) return direction.normalized;
            }

            return Vector2.zero;
        }

        private static bool IsEarlier(Vector2Int candidate, Vector2Int current)
        {
            return candidate.y < current.y || candidate.y == current.y && candidate.x < current.x;
        }
    }
}
