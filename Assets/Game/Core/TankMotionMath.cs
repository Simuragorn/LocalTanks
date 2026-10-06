using UnityEngine;

namespace LocalTanks
{
    public static class TankMotionMath
    {
        public static float TargetSpeed(float driveInput, float maxForwardSpeed, float maxReverseSpeed)
        {
            float input = Mathf.Clamp(driveInput, -1f, 1f);
            return input >= 0f ? input * maxForwardSpeed : input * maxReverseSpeed;
        }

        public static float ApproachSpeed(
            float currentSpeed,
            float targetSpeed,
            float acceleration,
            float braking,
            float deltaTime)
        {
            float rate = Mathf.Approximately(targetSpeed, 0f) ? braking : acceleration;
            return Mathf.MoveTowards(currentSpeed, targetSpeed, Mathf.Max(0f, rate) * deltaTime);
        }

        public static float StepAngle(float currentAngle, float targetAngle, float degreesPerSecond, float deltaTime)
        {
            return Mathf.MoveTowardsAngle(
                currentAngle,
                targetAngle,
                Mathf.Max(0f, degreesPerSecond) * deltaTime);
        }
    }
}
