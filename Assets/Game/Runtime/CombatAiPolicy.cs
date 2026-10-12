using UnityEngine;

namespace LocalTanks
{
    public enum CombatAiState
    {
        Deploy,
        Advance,
        Engage,
        Investigate,
        Reposition,
        Hold,
        Destroyed
    }

    public enum FireBlockReason
    {
        None,
        NoVisibleTarget,
        OutOfRange,
        Aiming,
        Reloading,
        AllyInLine,
        ObstacleInLine
    }

    public static class CombatAiPolicy
    {
        public const float LostContactGraceSeconds = 0.9f;
        public const float TargetLockSeconds = 3f;
        public const float TargetSwitchScoreAdvantage = 10f;
        public const float FiringLineClearConfirmationSeconds = 0.75f;
        public const float FiringLineDestinationConfirmationSeconds = 0.35f;
        public const float FiringLineRetryCooldownSeconds = 2.5f;
        public const float MaximumAdvanceRearAngle = 90f;
        public const float MinimumCloseCombatDistance = 3f;
        public const float CloseCombatAimErrorDegrees = 10f;
        public const float CloseCombatBearingRateFactor = 0.7f;

        /// <summary>
        /// Reports whether a fire block reason names a blocking object, and otherwise clears the
        /// assessment it is paired with. <see cref="FireBlockReason.AllyInLine"/> and
        /// <see cref="FireBlockReason.ObstacleInLine"/> come from a line-of-fire assessment and
        /// must keep it; reasons such as no visible target or out of range describe nothing about
        /// a blocker, so a leftover assessment would misreport a destroyed tank as a living ally.
        /// </summary>
        public static bool DescribesBlocker(FireBlockReason reason, ref LineOfFireAssessment assessment)
        {
            bool describesBlocker = reason == FireBlockReason.AllyInLine ||
                                    reason == FireBlockReason.ObstacleInLine;
            if (!describesBlocker)
            {
                assessment = default;
            }

            return describesBlocker;
        }

        public static float ScoreTarget(
            float distance,
            float viewRange,
            float healthRatio,
            float aimErrorDegrees,
            bool hasClearLine,
            bool isCurrentTarget)
        {
            float normalizedDistance = Mathf.Clamp01(distance / Mathf.Max(0.1f, viewRange));
            float score = (1f - normalizedDistance) * 40f;
            score += (1f - Mathf.Clamp01(healthRatio)) * 20f;
            score += (1f - Mathf.Clamp01(aimErrorDegrees / 180f)) * 10f;
            if (hasClearLine) score += 20f;
            if (isCurrentTarget) score += 15f;
            return score;
        }

        public static CombatAiState ResolveState(
            bool alive,
            bool visibleTarget,
            bool rememberedTarget,
            bool routeStarted)
        {
            if (!alive) return CombatAiState.Destroyed;
            if (visibleTarget) return CombatAiState.Engage;
            if (rememberedTarget) return CombatAiState.Investigate;
            return routeStarted ? CombatAiState.Advance : CombatAiState.Deploy;
        }

        public static bool ShouldDelayInvestigation(float currentTime, float lastVisibleTime)
        {
            return lastVisibleTime > float.NegativeInfinity &&
                   currentTime - lastVisibleTime < LostContactGraceSeconds;
        }

        public static bool ShouldKeepRepositioning(
            bool hasRememberedTarget,
            bool destinationReached,
            float currentTime,
            float repositionExpiresAt)
        {
            return hasRememberedTarget && !destinationReached && currentTime < repositionExpiresAt;
        }

        public static bool ShouldCompleteFiringLineManeuver(
            bool lineClear,
            bool destinationReached,
            float clearSince,
            float currentTime,
            float confirmationSeconds = FiringLineClearConfirmationSeconds)
        {
            if (!lineClear) return false;
            float requiredConfirmation = destinationReached
                ? Mathf.Min(confirmationSeconds, FiringLineDestinationConfirmationSeconds)
                : confirmationSeconds;
            return clearSince >= 0f && currentTime - clearSince >= Mathf.Max(0f, requiredConfirmation);
        }

        public static bool ShouldAlignHullBeforeAdvancing(float targetRelativeAngleDegrees)
        {
            return Mathf.Abs(targetRelativeAngleDegrees) > MaximumAdvanceRearAngle;
        }

        public static bool ShouldBreakCloseOrbit(
            float targetDistance,
            float triggerDistance,
            float turretAimErrorDegrees,
            float targetBearingRateDegrees,
            float turretTurnSpeed)
        {
            if (targetDistance > Mathf.Max(MinimumCloseCombatDistance, triggerDistance)) return false;
            return turretAimErrorDegrees >= CloseCombatAimErrorDegrees ||
                   targetBearingRateDegrees >= Mathf.Max(8f, turretTurnSpeed * CloseCombatBearingRateFactor);
        }

        public static bool ShouldSwitchVisibleTarget(
            float currentTime,
            float targetLockedUntil,
            float currentScore,
            float candidateScore)
        {
            return currentTime >= targetLockedUntil &&
                   candidateScore >= currentScore + TargetSwitchScoreAdvantage;
        }

        public static float PreferredDistance(TankDefinition definition, CombatAiRole role)
        {
            if (definition == null) return 5f;
            float factor = definition.vehicleClass == VehicleClass.HeavyTank ? 0.58f : 0.72f;
            if (role == CombatAiRole.Assault) factor -= 0.08f;
            if (role == CombatAiRole.Support) factor += 0.08f;
            return Mathf.Max(definition.guaranteedDetectionRange * 1.5f, definition.viewRange * factor);
        }
    }
}
