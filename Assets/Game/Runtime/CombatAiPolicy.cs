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
