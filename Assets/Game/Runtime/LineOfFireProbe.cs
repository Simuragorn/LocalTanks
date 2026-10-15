using System;
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
            RaycastHit2D[] hits = Physics2D.RaycastAll(origin, delta / distance, distance);
            Array.Sort(hits, PhysicsQueryOrder.CompareHits);
            foreach (RaycastHit2D hit in hits)
            {
                if (hit.collider == null || hit.collider.isTrigger ||
                    hit.collider.GetComponentInParent<FirePassThroughObstacle>() != null ||
                    BelongsTo(hit.collider.transform, ownerRoot))
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

        public static Transform FindLivingTankAt(Vector2 point)
        {
            Collider2D[] colliders = Physics2D.OverlapPointAll(point);
            Array.Sort(colliders, PhysicsQueryOrder.CompareColliders);
            foreach (Collider2D collider in colliders)
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

    /// <summary>
    /// Unity does not guarantee the order returned by *All physics queries. Gameplay always resolves
    /// nearest contact first and uses a hierarchy-derived tie breaker rather than transient instance ids.
    /// </summary>
    internal static class PhysicsQueryOrder
    {
        public static int CompareHits(RaycastHit2D left, RaycastHit2D right)
        {
            int distance = left.distance.CompareTo(right.distance);
            return distance != 0 ? distance : CompareColliders(left.collider, right.collider);
        }

        public static int CompareColliders(Collider2D left, Collider2D right)
        {
            return string.CompareOrdinal(StableKey(left), StableKey(right));
        }

        private static string StableKey(Collider2D collider)
        {
            if (collider == null) return string.Empty;
            string key = collider.GetType().Name;
            for (Transform current = collider.transform; current != null; current = current.parent)
            {
                key = current.GetSiblingIndex().ToString("D4") + ":" + current.name + "/" + key;
            }

            return key;
        }
    }
}
