using UnityEngine;

namespace LocalTanks
{
    public sealed class ProjectileTravelBudget
    {
        public float RemainingLifetime { get; private set; }
        public float RemainingRange { get; private set; }
        public bool IsExpired => RemainingLifetime <= 0f || RemainingRange <= 0f;

        public void Reset(float lifetime, float range)
        {
            RemainingLifetime = Mathf.Max(0f, lifetime);
            RemainingRange = Mathf.Max(0f, range);
        }

        public float Consume(float requestedDistance, float deltaTime)
        {
            float travelledDistance = Mathf.Min(Mathf.Max(0f, requestedDistance), RemainingRange);
            RemainingRange -= travelledDistance;
            RemainingLifetime = Mathf.Max(0f, RemainingLifetime - Mathf.Max(0f, deltaTime));
            return travelledDistance;
        }
    }
}
