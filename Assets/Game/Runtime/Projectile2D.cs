using UnityEngine;

namespace LocalTanks
{
    public sealed class Projectile2D : MonoBehaviour
    {
        [SerializeField] private LayerMask collisionMask = ~0;

        private Vector2 direction;
        private float speed;
        private float radius;
        private Transform ownerRoot;
        private bool initialized;
        private readonly ProjectileTravelBudget travelBudget = new ProjectileTravelBudget();

        public void Initialize(
            Vector2 travelDirection,
            float newSpeed,
            float newRadius,
            float lifetime,
            float range,
            Transform newOwnerRoot)
        {
            direction = travelDirection.normalized;
            speed = Mathf.Max(0f, newSpeed);
            radius = Mathf.Max(0.001f, newRadius);
            travelBudget.Reset(lifetime, range);
            ownerRoot = newOwnerRoot;
            initialized = true;
        }

        private void FixedUpdate()
        {
            if (!initialized)
            {
                return;
            }

            float stepDistance = travelBudget.Consume(speed * Time.fixedDeltaTime, Time.fixedDeltaTime);
            RaycastHit2D[] hits = Physics2D.CircleCastAll(
                transform.position,
                radius,
                direction,
                stepDistance,
                collisionMask);

            foreach (RaycastHit2D hit in hits)
            {
                if (hit.collider == null || hit.collider.isTrigger || IsOwnedCollider(hit.collider.transform))
                {
                    continue;
                }

                transform.position = hit.centroid;
                Destroy(gameObject);
                return;
            }

            transform.position += (Vector3)(direction * stepDistance);
            if (travelBudget.IsExpired)
            {
                Destroy(gameObject);
            }
        }

        private bool IsOwnedCollider(Transform candidate)
        {
            return ownerRoot != null && (candidate == ownerRoot || candidate.IsChildOf(ownerRoot));
        }
    }
}
