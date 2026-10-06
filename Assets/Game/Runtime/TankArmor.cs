using UnityEngine;

namespace LocalTanks
{
    [RequireComponent(typeof(TankHealth))]
    public sealed class TankArmor : MonoBehaviour
    {
        [SerializeField] private TankDefinition definition;
        [SerializeField] private TankHealth health;
        [SerializeField] private bool logImpacts = true;

        private void Awake()
        {
            if (health == null)
            {
                health = GetComponent<TankHealth>();
            }
        }

        public void Configure(TankDefinition newDefinition, TankHealth newHealth)
        {
            definition = newDefinition;
            health = newHealth;
        }

        public ImpactResult ResolveImpact(
            Vector2 projectileDirection,
            Vector2 impactPoint,
            Vector2 surfaceNormal,
            float penetration,
            float speed,
            int damage,
            float ricochetAngle,
            float ricochetSpeedMultiplier,
            float ricochetPenetrationMultiplier,
            int remainingRicochets)
        {
            if (definition == null)
            {
                throw new System.InvalidOperationException($"TankArmor on '{name}' has no tank definition.");
            }

            Vector2 localNormal = transform.InverseTransformDirection(surfaceNormal).normalized;
            ImpactResult result = ImpactResolver.Resolve(new ImpactRequest(
                projectileDirection,
                surfaceNormal,
                localNormal,
                definition.armor,
                penetration,
                speed,
                ricochetAngle,
                ricochetSpeedMultiplier,
                ricochetPenetrationMultiplier,
                remainingRicochets));

            if (result.Outcome == ImpactOutcome.Penetrated && health != null)
            {
                health.ApplyDamage(damage);
            }

            if (logImpacts)
            {
                Color debugColor = result.Outcome == ImpactOutcome.Penetrated
                    ? Color.green
                    : result.Outcome == ImpactOutcome.Ricocheted
                        ? Color.yellow
                        : Color.red;
                Debug.DrawRay(impactPoint, surfaceNormal.normalized * 0.75f, debugColor, 2f);
                Debug.Log(
                    $"Impact on {name}: {result.Outcome}, zone={result.Zone}, " +
                    $"angle={result.ImpactAngle:F1}°, armor={result.EffectiveArmor:F1} mm, penetration={penetration:F1} mm.",
                    this);
            }

            return result;
        }
    }
}
