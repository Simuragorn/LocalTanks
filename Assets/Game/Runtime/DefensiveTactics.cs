using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    public enum TacticalMovementPurpose
    {
        None,
        GainFiringLine,
        MoveToCover,
        ReturnToCover
    }

    public readonly struct ArmorUseProfile
    {
        public ArmorUseProfile(
            float frontArmor,
            float weakestSideArmor,
            float rearArmor,
            float safeAngleDegrees,
            bool canAngle)
        {
            FrontArmor = frontArmor;
            WeakestSideArmor = weakestSideArmor;
            RearArmor = rearArmor;
            SafeAngleDegrees = safeAngleDegrees;
            CanAngle = canAngle;
        }

        public float FrontArmor { get; }
        public float WeakestSideArmor { get; }
        public float RearArmor { get; }
        public float SafeAngleDegrees { get; }
        public bool CanAngle { get; }

        public static ArmorUseProfile From(TankDefinition definition)
        {
            if (definition == null)
            {
                return new ArmorUseProfile(0f, 0f, 0f, 0f, false);
            }

            ArmorProfile armor = definition.armor;
            float front = Mathf.Max(0f, armor.front);
            float side = Mathf.Max(0f, Mathf.Min(armor.left, armor.right));
            bool canAngle = front > 0f && side >= front * 0.35f;
            float optimal = canAngle ? Mathf.Atan2(side, front) * Mathf.Rad2Deg : 0f;
            float classLimit = definition.vehicleClass == VehicleClass.HeavyTank ? 35f : 30f;
            float safeAngle = canAngle ? Mathf.Clamp(optimal, 0f, classLimit) : 0f;
            return new ArmorUseProfile(front, side, Mathf.Max(0f, armor.rear), safeAngle, canAngle);
        }

        public float EffectiveProtection(float angleDegrees)
        {
            float radians = Mathf.Abs(angleDegrees) * Mathf.Deg2Rad;
            float frontEffective = ArmorMath.EffectiveArmor(
                FrontArmor,
                Mathf.Cos(radians),
                ArmorMath.DefaultMinimumCosine);
            if (Mathf.Abs(angleDegrees) < 0.01f || WeakestSideArmor <= 0f)
            {
                return frontEffective;
            }

            float sideEffective = ArmorMath.EffectiveArmor(
                WeakestSideArmor,
                Mathf.Sin(radians),
                ArmorMath.DefaultMinimumCosine);
            return Mathf.Min(frontEffective, sideEffective);
        }

        public Vector2 DesiredForward(Vector2 towardThreat, int sideSign)
        {
            Vector2 normalized = towardThreat.sqrMagnitude > 0.0001f
                ? towardThreat.normalized
                : Vector2.up;
            if (!CanAngle || SafeAngleDegrees <= 0.01f)
            {
                return normalized;
            }

            float signedAngle = SafeAngleDegrees * (sideSign < 0 ? -1f : 1f);
            float radians = signedAngle * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            return new Vector2(
                normalized.x * cosine - normalized.y * sine,
                normalized.x * sine + normalized.y * cosine).normalized;
        }
    }

    public readonly struct ArmorImpactObservation
    {
        public ArmorImpactObservation(
            Vector2 threatDirection,
            ImpactOutcome outcome,
            ArmorZone zone,
            int damage,
            float time)
        {
            ThreatDirection = threatDirection.sqrMagnitude > 0.0001f
                ? threatDirection.normalized
                : Vector2.up;
            Outcome = outcome;
            Zone = zone;
            Damage = Mathf.Max(0, damage);
            Time = time;
        }

        public Vector2 ThreatDirection { get; }
        public ImpactOutcome Outcome { get; }
        public ArmorZone Zone { get; }
        public int Damage { get; }
        public float Time { get; }
    }

    public sealed class ThreatMemory
    {
        public const float ObservationLifetime = 10f;
        private readonly List<ArmorImpactObservation> observations = new List<ArmorImpactObservation>();

        public int ActiveCount(float time)
        {
            RemoveExpired(time);
            return observations.Count;
        }

        public void Record(ArmorImpactObservation observation)
        {
            observations.Add(observation);
            if (observations.Count > 8) observations.RemoveAt(0);
        }

        public bool TryGetPrimaryDirection(float time, out Vector2 direction)
        {
            RemoveExpired(time);
            direction = Vector2.zero;
            float totalWeight = 0f;
            for (int index = 0; index < observations.Count; index++)
            {
                ArmorImpactObservation observation = observations[index];
                float age = Mathf.Max(0f, time - observation.Time);
                float severity = observation.Outcome == ImpactOutcome.Penetrated ? 1.5f : 1f;
                float weight = severity * Mathf.Clamp01(1f - age / ObservationLifetime);
                direction += observation.ThreatDirection * weight;
                totalWeight += weight;
            }

            if (totalWeight <= 0.001f || direction.sqrMagnitude <= 0.01f)
            {
                if (observations.Count == 0) return false;
                direction = observations[observations.Count - 1].ThreatDirection;
                return true;
            }

            direction.Normalize();
            return true;
        }

        public void Clear()
        {
            observations.Clear();
        }

        private void RemoveExpired(float time)
        {
            observations.RemoveAll(item => time - item.Time > ObservationLifetime);
        }
    }

    public readonly struct TacticalCoverSolution
    {
        public TacticalCoverSolution(
            Vector2Int cell,
            Vector2 position,
            float score,
            float pathLength,
            int evaluatedCandidates)
        {
            Cell = cell;
            Position = position;
            Score = score;
            PathLength = pathLength;
            EvaluatedCandidates = evaluatedCandidates;
        }

        public Vector2Int Cell { get; }
        public Vector2 Position { get; }
        public float Score { get; }
        public float PathLength { get; }
        public int EvaluatedCandidates { get; }
    }

    public static class TacticalCoverPlanner
    {
        public static bool TryFind(
            NavigationMap map,
            Vector2 origin,
            int clearanceCells,
            float maximumSearchDistance,
            Func<Vector2, bool> isProtected,
            Func<Vector2, bool> isAvailable,
            out TacticalCoverSolution solution)
        {
            solution = default;
            if (map == null || isProtected == null) return false;

            float searchDistance = Mathf.Max(map.CellSize, maximumSearchDistance);
            int radius = Mathf.CeilToInt(searchDistance / map.CellSize);
            Vector2Int originCell = map.WorldToCell(origin);
            bool found = false;
            float bestScore = float.PositiveInfinity;
            Vector2Int bestCell = default;
            Vector2 bestPosition = default;
            float bestPathLength = 0f;
            int evaluated = 0;

            for (int y = originCell.y - radius; y <= originCell.y + radius; y++)
            {
                for (int x = originCell.x - radius; x <= originCell.x + radius; x++)
                {
                    Vector2Int cell = new Vector2Int(x, y);
                    if (!map.Contains(cell) || map.IsBlocked(cell)) continue;
                    Vector2 candidate = map.CellToWorld(cell);
                    float directDistance = Vector2.Distance(origin, candidate);
                    if (directDistance < map.CellSize * 0.75f || directDistance > searchDistance) continue;
                    evaluated++;
                    if (!isProtected(candidate) || isAvailable != null && !isAvailable(candidate)) continue;

                    List<Vector2> path = map.FindWorldPath(origin, candidate, clearanceCells);
                    if (path == null || path.Count < 2) continue;
                    float pathLength = CalculatePathLength(path);
                    float detour = Mathf.Max(0f, pathLength - directDistance);
                    float score = pathLength * 1.4f + detour;
                    if (!found || score < bestScore - 0.001f ||
                        Mathf.Approximately(score, bestScore) && IsEarlier(cell, bestCell))
                    {
                        found = true;
                        bestScore = score;
                        bestCell = cell;
                        bestPosition = candidate;
                        bestPathLength = pathLength;
                    }
                }
            }

            if (!found) return false;
            solution = new TacticalCoverSolution(
                bestCell,
                bestPosition,
                bestScore,
                bestPathLength,
                evaluated);
            return true;
        }

        private static float CalculatePathLength(IReadOnlyList<Vector2> path)
        {
            float result = 0f;
            for (int index = 1; index < path.Count; index++)
            {
                result += Vector2.Distance(path[index - 1], path[index]);
            }

            return result;
        }

        private static bool IsEarlier(Vector2Int candidate, Vector2Int current)
        {
            return candidate.y < current.y || candidate.y == current.y && candidate.x < current.x;
        }
    }
}
