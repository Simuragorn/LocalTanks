using UnityEngine;

namespace LocalTanks
{
    public readonly struct LineOfFireResult
    {
        public LineOfFireResult(bool isBlocked, Vector2 blockPoint)
        {
            IsBlocked = isBlocked;
            BlockPoint = blockPoint;
        }

        public bool IsBlocked { get; }
        public Vector2 BlockPoint { get; }
    }

    public static class LineOfFireProbe
    {
        private const float MinimumDistance = 0.001f;

        public static bool IsBlocked(Transform ownerRoot, Vector2 origin, Vector2 destination)
        {
            return Evaluate(ownerRoot, origin, destination).IsBlocked;
        }

        public static LineOfFireResult Evaluate(Transform ownerRoot, Vector2 origin, Vector2 destination)
        {
            Vector2 delta = destination - origin;
            float distance = delta.magnitude;
            if (distance <= MinimumDistance)
            {
                return new LineOfFireResult(false, destination);
            }

            Transform intendedTank = FindLivingTankAt(destination);
            RaycastHit2D nearest = default;
            float nearestDistance = float.PositiveInfinity;
            foreach (RaycastHit2D hit in Physics2D.RaycastAll(origin, delta / distance, distance))
            {
                if (hit.collider == null || hit.collider.isTrigger || BelongsTo(hit.collider.transform, ownerRoot))
                {
                    continue;
                }

                if (hit.distance < nearestDistance)
                {
                    nearest = hit;
                    nearestDistance = hit.distance;
                }
            }

            if (nearest.collider == null)
            {
                return new LineOfFireResult(false, destination);
            }

            bool blocked = intendedTank == null || !BelongsTo(nearest.collider.transform, intendedTank);
            return new LineOfFireResult(blocked, blocked ? nearest.point : destination);
        }

        private static Transform FindLivingTankAt(Vector2 point)
        {
            foreach (Collider2D collider in Physics2D.OverlapPointAll(point))
            {
                if (collider == null || collider.isTrigger)
                {
                    continue;
                }

                TankHealth health = collider.GetComponentInParent<TankHealth>();
                if (health != null && !health.IsDestroyed)
                {
                    return health.transform;
                }
            }

            return null;
        }

        private static bool BelongsTo(Transform candidate, Transform root)
        {
            return root != null && (candidate == root || candidate.IsChildOf(root));
        }
    }
}
