using UnityEngine;

namespace LocalTanks
{
    public static class GunDispersionMath
    {
        public static float CalculateTarget(
            float minimum,
            float maximum,
            float movement01,
            float hullTraverse01,
            float turretTraverse01,
            float movementPenalty,
            float hullTraversePenalty,
            float turretTraversePenalty)
        {
            float target = Mathf.Max(0f, minimum) +
                           Mathf.Clamp01(movement01) * Mathf.Max(0f, movementPenalty) +
                           Mathf.Clamp01(hullTraverse01) * Mathf.Max(0f, hullTraversePenalty) +
                           Mathf.Clamp01(turretTraverse01) * Mathf.Max(0f, turretTraversePenalty);
            return Mathf.Clamp(target, Mathf.Max(0f, minimum), Mathf.Max(minimum, maximum));
        }

        public static float Step(
            float current,
            float target,
            float minimum,
            float maximum,
            float aimingTimeSeconds,
            float deltaTime)
        {
            float safeMinimum = Mathf.Max(0f, minimum);
            float safeMaximum = Mathf.Max(safeMinimum, maximum);
            float clampedCurrent = Mathf.Clamp(current, safeMinimum, safeMaximum);
            float clampedTarget = Mathf.Clamp(target, safeMinimum, safeMaximum);
            float range = safeMaximum - safeMinimum;
            float recoveryRate = range / Mathf.Max(0.01f, aimingTimeSeconds);
            float expansionRate = Mathf.Max(recoveryRate * 3f, range * 4f);
            float rate = clampedTarget > clampedCurrent ? expansionRate : recoveryRate;
            return Mathf.MoveTowards(clampedCurrent, clampedTarget, rate * Mathf.Max(0f, deltaTime));
        }

        public static float AddShotPenalty(float current, float penalty, float minimum, float maximum)
        {
            return Mathf.Clamp(current + Mathf.Max(0f, penalty), Mathf.Max(0f, minimum), Mathf.Max(minimum, maximum));
        }

        public static float SampleOffsetDegrees(float dispersionDegrees, int shotSequence)
        {
            if (dispersionDegrees <= 0f)
            {
                return 0f;
            }

            uint value = unchecked((uint)(shotSequence + 1) * 747796405u + 2891336453u);
            value = unchecked(((value >> ((int)(value >> 28) + 4)) ^ value) * 277803737u);
            value = (value >> 22) ^ value;
            float normalized = (value & 0x00FFFFFFu) / 16777215f;
            return Mathf.Lerp(-dispersionDegrees, dispersionDegrees, normalized);
        }
    }
}
