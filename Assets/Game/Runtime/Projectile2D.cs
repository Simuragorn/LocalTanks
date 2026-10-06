using UnityEngine;

namespace LocalTanks
{
    public sealed class Projectile2D : MonoBehaviour
    {
        [SerializeField] private LayerMask collisionMask = ~0;

        private Vector2 direction;
        private float speed;
        private float radius;
        private float penetration;
        private int damage;
        private float ricochetAngle;
        private float ricochetSpeedMultiplier;
        private float ricochetPenetrationMultiplier;
        private int remainingRicochets;
        private Transform ownerRoot;
        private Collider2D temporarilyIgnoredCollider;
        private bool initialized;
        private readonly ProjectileTravelBudget travelBudget = new ProjectileTravelBudget();

        public Vector2 Direction => direction;
        public float Speed => speed;
        public float Penetration => penetration;
        public int RemainingRicochets => remainingRicochets;

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

        public void Initialize(Vector2 travelDirection, ShellDefinition shell, Transform newOwnerRoot)
        {
            if (shell == null)
            {
                throw new System.ArgumentNullException(nameof(shell));
            }

            Initialize(
                travelDirection,
                shell.speed,
                shell.radius,
                shell.lifetimeSeconds,
                shell.maximumRange,
                newOwnerRoot);
            penetration = Mathf.Max(0f, shell.penetration);
            damage = Mathf.Max(0, shell.damage);
            ricochetAngle = Mathf.Clamp(shell.ricochetAngle, 0f, 90f);
            ricochetSpeedMultiplier = Mathf.Clamp01(shell.ricochetSpeedMultiplier);
            ricochetPenetrationMultiplier = Mathf.Clamp01(shell.ricochetPenetrationMultiplier);
            remainingRicochets = Mathf.Max(0, shell.maximumRicochets);
        }

        private void FixedUpdate()
        {
            if (!initialized)
            {
                return;
            }

            RefreshIgnoredCollider();

            float stepDistance = travelBudget.Consume(speed * Time.fixedDeltaTime, Time.fixedDeltaTime);
            RaycastHit2D[] hits = Physics2D.CircleCastAll(
                transform.position,
                radius,
                direction,
                stepDistance,
                collisionMask);

            foreach (RaycastHit2D hit in hits)
            {
                if (hit.collider == null ||
                    hit.collider.isTrigger ||
                    hit.collider == temporarilyIgnoredCollider ||
                    IsOwnedCollider(hit.collider.transform))
                {
                    continue;
                }

                transform.position = hit.centroid;
                TankArmor armor = hit.collider.GetComponentInParent<TankArmor>();
                if (armor == null)
                {
                    Destroy(gameObject);
                    return;
                }

                ImpactResult result = armor.ResolveImpact(
                    direction,
                    hit.point,
                    hit.normal,
                    penetration,
                    speed,
                    damage,
                    ricochetAngle,
                    ricochetSpeedMultiplier,
                    ricochetPenetrationMultiplier,
                    remainingRicochets);

                if (result.Outcome != ImpactOutcome.Ricocheted)
                {
                    Destroy(gameObject);
                    return;
                }

                direction = result.OutgoingDirection;
                speed = result.OutgoingSpeed;
                penetration = result.RemainingPenetration;
                remainingRicochets--;
                temporarilyIgnoredCollider = hit.collider;
                transform.position = hit.centroid + hit.normal * (radius + 0.002f);
                transform.up = direction;
                return;
            }

            transform.position += (Vector3)(direction * stepDistance);
            if (travelBudget.IsExpired)
            {
                Destroy(gameObject);
            }
        }

        private void RefreshIgnoredCollider()
        {
            if (temporarilyIgnoredCollider == null)
            {
                return;
            }

            Vector2 position = transform.position;
            Vector2 closest = temporarilyIgnoredCollider.ClosestPoint(position);
            if (Vector2.Distance(position, closest) > radius + 0.002f)
            {
                temporarilyIgnoredCollider = null;
            }
        }

        private bool IsOwnedCollider(Transform candidate)
        {
            return ownerRoot != null && (candidate == ownerRoot || candidate.IsChildOf(ownerRoot));
        }
    }
}
