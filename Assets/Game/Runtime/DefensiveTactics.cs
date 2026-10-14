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
        HoldCover,
        PeekToFire,
        ReturnToCover,
        DefensiveRetreat,
        BreakCloseOrbit
    }

    public enum TacticalDecisionPriority
    {
        None = 0,
        StrategicMovement = 10,
        GainFiringLine = 20,
        UseCover = 30,
        SurviveCrossfire = 40,
        EscapeCloseCombat = 50
    }

    public static class TacticalDecisionArbiter
    {
        public static bool ShouldReplace(
            TacticalMovementPurpose currentPurpose,
            TacticalDecisionPriority currentPriority,
            float currentExpiresAt,
            TacticalMovementPurpose requestedPurpose,
            TacticalDecisionPriority requestedPriority,
            float time)
        {
            if (requestedPurpose == TacticalMovementPurpose.None) return true;
            if (currentPurpose == TacticalMovementPurpose.None || time >= currentExpiresAt) return true;
            if (requestedPurpose == currentPurpose) return true;
            return requestedPriority >= currentPriority;
        }

        public static bool ShouldFireBeforeCover(
            bool weaponReady,
            bool targetVisible,
            bool lineClear,
            bool criticalSurvival,
            float healthRatio)
        {
            return weaponReady && targetVisible && lineClear && !criticalSurvival && healthRatio > 0.25f;
        }
    }

    public readonly struct TacticalBehaviorProfile
    {
        public TacticalBehaviorProfile(
            float coverPreference,
            float peekReloadThreshold,
            float minimumCoverHoldSeconds,
            float maximumCoverHoldSeconds,
            float coverSearchDistance)
        {
            CoverPreference = Mathf.Clamp01(coverPreference);
            PeekReloadThreshold = Mathf.Clamp01(peekReloadThreshold);
            MinimumCoverHoldSeconds = Mathf.Max(0.2f, minimumCoverHoldSeconds);
            MaximumCoverHoldSeconds = Mathf.Max(MinimumCoverHoldSeconds, maximumCoverHoldSeconds);
            CoverSearchDistance = Mathf.Max(2f, coverSearchDistance);
        }

        public float CoverPreference { get; }
        public float PeekReloadThreshold { get; }
        public float MinimumCoverHoldSeconds { get; }
        public float MaximumCoverHoldSeconds { get; }
        public float CoverSearchDistance { get; }

        public static TacticalBehaviorProfile From(
            TankDefinition definition,
            BotPersonalityProfile personality)
        {
            float classCover = 0.55f;
            float classReload = 0.82f;
            float searchDistance = 5.5f;
            if (definition != null)
            {
                switch (definition.vehicleClass)
                {
                    case VehicleClass.LightTank:
                        classCover = 0.72f;
                        classReload = 0.9f;
                        searchDistance = 7f;
                        break;
                    case VehicleClass.MediumTank:
                        classCover = 0.62f;
                        classReload = 0.86f;
                        searchDistance = 6.5f;
                        break;
                    case VehicleClass.HeavyTank:
                        classCover = 0.4f;
                        classReload = 0.72f;
                        searchDistance = 5.5f;
                        break;
                    case VehicleClass.TankDestroyer:
                        classCover = 0.8f;
                        classReload = 0.94f;
                        searchDistance = 7.5f;
                        break;
                    case VehicleClass.Artillery:
                        classCover = 0.9f;
                        classReload = 0.97f;
                        searchDistance = 8f;
                        break;
                }
            }

            float caution = personality?.caution ?? 0.5f;
            float aggression = personality?.aggression ?? 0.5f;
            float patience = personality?.patience ?? 0.5f;
            float coverPreference = classCover + (caution - aggression) * 0.22f;
            float reloadThreshold = classReload + caution * 0.05f - aggression * 0.04f;
            float minimumHold = Mathf.Lerp(0.45f, 1.2f, patience);
            float maximumHold = Mathf.Lerp(2.5f, 5f, Mathf.Max(caution, patience));
            return new TacticalBehaviorProfile(
                coverPreference,
                reloadThreshold,
                minimumHold,
                maximumHold,
                searchDistance);
        }

        public bool ShouldSeekCoverWhileReloading(
            float reloadProgress,
            bool exposed,
            bool crossfire,
            bool recentlyDamaged,
            float healthRatio)
        {
            if (!exposed) return false;
            if (crossfire) return true;
            float lowHealthThreshold = Mathf.Lerp(0.35f, 0.58f, CoverPreference);
            if (!recentlyDamaged && healthRatio > lowHealthThreshold) return false;
            float triggerProgress = Mathf.Lerp(0.28f, 0.7f, CoverPreference);
            return reloadProgress < triggerProgress;
        }
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
            float time,
            TeamId attackerTeam = TeamId.Neutral)
        {
            ThreatDirection = threatDirection.sqrMagnitude > 0.0001f
                ? threatDirection.normalized
                : Vector2.up;
            Outcome = outcome;
            Zone = zone;
            Damage = Mathf.Max(0, damage);
            Time = time;
            AttackerTeam = attackerTeam;
        }

        public Vector2 ThreatDirection { get; }
        public ImpactOutcome Outcome { get; }
        public ArmorZone Zone { get; }
        public int Damage { get; }
        public float Time { get; }
        public TeamId AttackerTeam { get; }
    }

    public readonly struct ThreatSector
    {
        public ThreatSector(Vector2 direction, float weight, float mostRecentTime)
        {
            Direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.up;
            Weight = Mathf.Max(0f, weight);
            MostRecentTime = mostRecentTime;
        }

        public Vector2 Direction { get; }
        public float Weight { get; }
        public float MostRecentTime { get; }
    }

    public readonly struct ThreatAnalysis
    {
        public ThreatAnalysis(ThreatSector[] sectors, float crossfireAngle)
        {
            Sectors = sectors ?? Array.Empty<ThreatSector>();
            CrossfireAngle = Mathf.Max(0f, crossfireAngle);
        }

        public ThreatSector[] Sectors { get; }
        public int SectorCount => Sectors?.Length ?? 0;
        public bool HasThreat => SectorCount > 0;
        public Vector2 PrimaryDirection => HasThreat ? Sectors[0].Direction : Vector2.zero;
        public Vector2 SecondaryDirection => SectorCount > 1 ? Sectors[1].Direction : Vector2.zero;
        public float CrossfireAngle { get; }
        public bool IsCrossfire => SectorCount > 1 && CrossfireAngle >= 55f;
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
            ThreatAnalysis analysis = Analyze(time, null);
            direction = analysis.PrimaryDirection;
            return analysis.HasThreat;
        }

        public ThreatAnalysis Analyze(float time, IReadOnlyList<Vector2> visibleThreatDirections)
        {
            RemoveExpired(time);
            List<ThreatSector> sectors = new List<ThreatSector>();
            for (int index = 0; index < observations.Count; index++)
            {
                ArmorImpactObservation observation = observations[index];
                float age = Mathf.Max(0f, time - observation.Time);
                float severity = observation.Outcome == ImpactOutcome.Penetrated ? 1.5f : 1f;
                float damageWeight = 1f + Mathf.Min(1f, observation.Damage / 100f);
                float weight = severity * damageWeight * Mathf.Clamp01(1f - age / ObservationLifetime);
                AddToSector(sectors, observation.ThreatDirection, weight, observation.Time);
            }

            if (visibleThreatDirections != null)
            {
                for (int index = 0; index < visibleThreatDirections.Count; index++)
                {
                    Vector2 direction = visibleThreatDirections[index];
                    if (direction.sqrMagnitude > 0.0001f)
                    {
                        AddToSector(sectors, direction, 1.1f, time);
                    }
                }
            }

            sectors.Sort((left, right) =>
            {
                int byWeight = right.Weight.CompareTo(left.Weight);
                if (byWeight != 0) return byWeight;
                return right.MostRecentTime.CompareTo(left.MostRecentTime);
            });
            float crossfireAngle = 0f;
            for (int left = 0; left < sectors.Count; left++)
            {
                for (int right = left + 1; right < sectors.Count; right++)
                {
                    crossfireAngle = Mathf.Max(
                        crossfireAngle,
                        Mathf.Abs(Vector2.SignedAngle(sectors[left].Direction, sectors[right].Direction)));
                }
            }

            return new ThreatAnalysis(sectors.ToArray(), crossfireAngle);
        }

        public void Clear()
        {
            observations.Clear();
        }

        private void RemoveExpired(float time)
        {
            observations.RemoveAll(item => time - item.Time > ObservationLifetime);
        }

        private static void AddToSector(
            List<ThreatSector> sectors,
            Vector2 direction,
            float weight,
            float time)
        {
            Vector2 normalized = direction.normalized;
            for (int index = 0; index < sectors.Count; index++)
            {
                ThreatSector current = sectors[index];
                if (Mathf.Abs(Vector2.SignedAngle(current.Direction, normalized)) > 32f) continue;
                float combinedWeight = current.Weight + weight;
                Vector2 combined = current.Direction * current.Weight + normalized * weight;
                sectors[index] = new ThreatSector(
                    combined.sqrMagnitude > 0.0001f ? combined.normalized : current.Direction,
                    combinedWeight,
                    Mathf.Max(current.MostRecentTime, time));
                return;
            }

            sectors.Add(new ThreatSector(normalized, weight, time));
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
            : this(
                cell,
                position,
                score,
                pathLength,
                evaluatedCandidates,
                default,
                position,
                0f,
                false,
                Vector2.zero)
        {
        }

        public TacticalCoverSolution(
            Vector2Int cell,
            Vector2 position,
            float score,
            float pathLength,
            int evaluatedCandidates,
            Vector2Int firingCell,
            Vector2 firingPosition,
            float firingPathLength,
            bool hasFiringPosition,
            Vector2 initialPathDirection)
        {
            Cell = cell;
            Position = position;
            Score = score;
            PathLength = pathLength;
            EvaluatedCandidates = evaluatedCandidates;
            FiringCell = firingCell;
            FiringPosition = firingPosition;
            FiringPathLength = firingPathLength;
            HasFiringPosition = hasFiringPosition;
            InitialPathDirection = initialPathDirection.sqrMagnitude > 0.0001f
                ? initialPathDirection.normalized
                : Vector2.zero;
        }

        public Vector2Int Cell { get; }
        public Vector2 Position { get; }
        public float Score { get; }
        public float PathLength { get; }
        public int EvaluatedCandidates { get; }
        public Vector2Int FiringCell { get; }
        public Vector2 FiringPosition { get; }
        public float FiringPathLength { get; }
        public bool HasFiringPosition { get; }
        public Vector2 InitialPathDirection { get; }
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
            return TryFindPair(
                map,
                origin,
                clearanceCells,
                maximumSearchDistance,
                map != null ? map.CellSize * 3f : 3f,
                isProtected,
                isAvailable,
                null,
                out solution);
        }

        public static bool TryFindPair(
            NavigationMap map,
            Vector2 origin,
            int clearanceCells,
            float maximumSearchDistance,
            float maximumPeekDistance,
            Func<Vector2, bool> isProtected,
            Func<Vector2, bool> isAvailable,
            Func<Vector2, bool> canFireFrom,
            out TacticalCoverSolution solution)
        {
            return TryFindPair(
                map,
                origin,
                clearanceCells,
                maximumSearchDistance,
                maximumPeekDistance,
                isProtected,
                isAvailable,
                canFireFrom,
                null,
                out solution);
        }

        public static bool TryFindPair(
            NavigationMap map,
            Vector2 origin,
            int clearanceCells,
            float maximumSearchDistance,
            float maximumPeekDistance,
            Func<Vector2, bool> isProtected,
            Func<Vector2, bool> isAvailable,
            Func<Vector2, bool> canFireFrom,
            Func<IReadOnlyList<Vector2>, float> pathScoreAdjustment,
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
            Vector2Int bestFiringCell = default;
            Vector2 bestFiringPosition = default;
            float bestFiringPathLength = 0f;
            bool bestHasFiringPosition = false;
            Vector2 bestInitialPathDirection = Vector2.zero;
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
                    bool hasFiringPosition = TryFindFiringPoint(
                        map,
                        candidate,
                        clearanceCells,
                        maximumPeekDistance,
                        isAvailable,
                        canFireFrom,
                        out Vector2Int firingCell,
                        out Vector2 firingPosition,
                        out float firingPathLength);
                    if (canFireFrom != null && !hasFiringPosition) continue;
                    float pathAdjustment = pathScoreAdjustment != null
                        ? Mathf.Max(0f, pathScoreAdjustment(path))
                        : 0f;
                    float score = pathLength * 1.4f + detour + firingPathLength * 0.75f + pathAdjustment;
                    if (!found || score < bestScore - 0.001f ||
                        Mathf.Approximately(score, bestScore) && IsEarlier(cell, bestCell))
                    {
                        found = true;
                        bestScore = score;
                        bestCell = cell;
                        bestPosition = candidate;
                        bestPathLength = pathLength;
                        bestFiringCell = firingCell;
                        bestFiringPosition = firingPosition;
                        bestFiringPathLength = firingPathLength;
                        bestHasFiringPosition = hasFiringPosition;
                        bestInitialPathDirection = FirstPathDirection(path);
                    }
                }
            }

            if (!found) return false;
            solution = new TacticalCoverSolution(
                bestCell,
                bestPosition,
                bestScore,
                bestPathLength,
                evaluated,
                bestFiringCell,
                bestFiringPosition,
                bestFiringPathLength,
                bestHasFiringPosition,
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
            TacticalCoverSolution solution,
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

        private static bool TryFindFiringPoint(
            NavigationMap map,
            Vector2 shelter,
            int clearanceCells,
            float maximumPeekDistance,
            Func<Vector2, bool> isAvailable,
            Func<Vector2, bool> canFireFrom,
            out Vector2Int firingCell,
            out Vector2 firingPosition,
            out float firingPathLength)
        {
            firingCell = default;
            firingPosition = shelter;
            firingPathLength = 0f;
            if (canFireFrom == null) return false;

            int radius = Mathf.Max(1, Mathf.CeilToInt(maximumPeekDistance / map.CellSize));
            Vector2Int shelterCell = map.WorldToCell(shelter);
            bool found = false;
            float bestScore = float.PositiveInfinity;
            for (int y = shelterCell.y - radius; y <= shelterCell.y + radius; y++)
            {
                for (int x = shelterCell.x - radius; x <= shelterCell.x + radius; x++)
                {
                    Vector2Int cell = new Vector2Int(x, y);
                    if (!map.Contains(cell) || map.IsBlocked(cell) || cell == shelterCell) continue;
                    Vector2 candidate = map.CellToWorld(cell);
                    float directDistance = Vector2.Distance(shelter, candidate);
                    if (directDistance < map.CellSize * 1.5f ||
                        directDistance > maximumPeekDistance ||
                        isAvailable != null && !isAvailable(candidate) ||
                        !canFireFrom(candidate))
                    {
                        continue;
                    }

                    List<Vector2> path = map.FindWorldPath(shelter, candidate, clearanceCells);
                    if (path == null || path.Count < 2) continue;
                    float pathLength = CalculatePathLength(path);
                    float score = pathLength + Mathf.Max(0f, pathLength - directDistance) * 2f;
                    if (!found || score < bestScore - 0.001f ||
                        Mathf.Approximately(score, bestScore) && IsEarlier(cell, firingCell))
                    {
                        found = true;
                        bestScore = score;
                        firingCell = cell;
                        firingPosition = candidate;
                        firingPathLength = pathLength;
                    }
                }
            }

            return found;
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

    public static class TacticalCoverReservations
    {
        private sealed class Reservation
        {
            public string OwnerId;
            public float ExpiresAt;
        }

        private static readonly Dictionary<Vector2Int, Reservation> Reservations =
            new Dictionary<Vector2Int, Reservation>();

        public static bool IsAvailable(Vector2Int cell, string ownerId, float time)
        {
            RemoveExpired(time);
            return !Reservations.TryGetValue(cell, out Reservation reservation) ||
                   string.Equals(reservation.OwnerId, ownerId, StringComparison.Ordinal);
        }

        public static bool TryReserve(Vector2Int cell, string ownerId, float time, float duration)
        {
            if (string.IsNullOrWhiteSpace(ownerId) || !IsAvailable(cell, ownerId, time)) return false;
            Reservations[cell] = new Reservation
            {
                OwnerId = ownerId,
                ExpiresAt = time + Mathf.Max(1f, duration)
            };
            return true;
        }

        public static void Release(string ownerId)
        {
            if (string.IsNullOrWhiteSpace(ownerId)) return;
            List<Vector2Int> owned = new List<Vector2Int>();
            foreach (KeyValuePair<Vector2Int, Reservation> pair in Reservations)
            {
                if (string.Equals(pair.Value.OwnerId, ownerId, StringComparison.Ordinal)) owned.Add(pair.Key);
            }
            foreach (Vector2Int cell in owned) Reservations.Remove(cell);
        }

        public static void Clear()
        {
            Reservations.Clear();
        }

        private static void RemoveExpired(float time)
        {
            List<Vector2Int> expired = new List<Vector2Int>();
            foreach (KeyValuePair<Vector2Int, Reservation> pair in Reservations)
            {
                if (pair.Value.ExpiresAt <= time) expired.Add(pair.Key);
            }
            foreach (Vector2Int cell in expired) Reservations.Remove(cell);
        }
    }
}
