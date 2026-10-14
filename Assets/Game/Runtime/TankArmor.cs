using System;
using UnityEngine;

namespace LocalTanks
{
    [RequireComponent(typeof(TankHealth))]
    public sealed class TankArmor : MonoBehaviour
    {
        [SerializeField] private TankDefinition definition;
        [SerializeField] private TankHealth health;
        [SerializeField] private bool logImpacts = true;

        public event Action<TankArmor, ArmorImpactObservation> ImpactResolved;

        /// <summary>
        /// Contact point of the newest impact, in world space. The point and the normal are facts of one
        /// collision, not of the threat a machine should remember: <see cref="ArmorImpactObservation"/>
        /// stays the AI's threat record and carries no coordinates, so visual effects read the geometry
        /// from here while an event handler runs.
        /// </summary>
        public Vector2 LastImpactPoint { get; private set; }

        /// <summary>Surface normal of the newest impact, in world space.</summary>
        public Vector2 LastImpactNormal { get; private set; } = Vector2.up;
        public int LastAppliedDamage { get; private set; }
        public TankDefinition Definition => definition;

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
            int remainingRicochets,
            float basePenetration = -1f,
            CombatRules combatRules = null,
            TeamId attackerTeam = TeamId.Neutral)
        {
            if (definition == null)
            {
                throw new System.InvalidOperationException($"TankArmor on '{name}' has no tank definition.");
            }

            Vector2 localNormal = transform.InverseTransformDirection(surfaceNormal).normalized;
            CombatRules rules = combatRules ?? new CombatRules
            {
                minimumArmorCosine = ArmorMath.DefaultMinimumCosine,
                penetrationRandomMinimum = 1f,
                penetrationRandomMaximum = 1f,
                damageRandomMinimum = 1f,
                damageRandomMaximum = 1f
            };
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
                remainingRicochets,
                rules.minimumArmorCosine,
                basePenetration >= 0f ? basePenetration : penetration,
                rules.penetrationRandomMinimum,
                rules.penetrationRandomMaximum));

            int appliedDamage = 0;
            if (result.Outcome == ImpactOutcome.Penetrated && health != null)
            {
                appliedDamage = health.ApplyDamage(damage).AppliedDamage;
            }
            LastAppliedDamage = appliedDamage;

            LastImpactPoint = impactPoint;
            LastImpactNormal = surfaceNormal.sqrMagnitude > 0f ? surfaceNormal.normalized : Vector2.up;

            ImpactResolved?.Invoke(this, new ArmorImpactObservation(
                -projectileDirection,
                result.Outcome,
                result.Zone,
                appliedDamage,
                Time.time,
                attackerTeam));

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
