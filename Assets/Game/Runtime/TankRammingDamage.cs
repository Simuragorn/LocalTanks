using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    public readonly struct RammingImpactObservation
    {
        public RammingImpactObservation(
            TankRammingDamage other,
            float speedMps,
            ArmorZone ownZone,
            ArmorZone otherZone,
            RammingDamageResult result)
        {
            Other = other;
            SpeedMps = speedMps;
            OwnZone = ownZone;
            OtherZone = otherZone;
            Result = result;
        }

        public TankRammingDamage Other { get; }
        public float SpeedMps { get; }
        public ArmorZone OwnZone { get; }
        public ArmorZone OtherZone { get; }
        public RammingDamageResult Result { get; }
    }

    /// <summary>
    /// Converts one physical tank contact into one bilateral damage event. Only one of the two components
    /// resolves a pair, preventing Unity's mirrored collision callbacks from applying damage twice.
    /// Team membership is deliberately absent: ramming remains physical friendly damage even when shell
    /// friendly fire is disabled.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TankHealth), typeof(Rigidbody2D))]
    public sealed class TankRammingDamage : MonoBehaviour
    {
        [SerializeField] private TankHealth health;

        private readonly Dictionary<EntityId, float> nextAllowedImpactByOther = new Dictionary<EntityId, float>();
        private CombatRules configuredRules;

        public event Action<TankRammingDamage, RammingImpactObservation> ImpactResolved;

        public RammingDamageResult LastResult { get; private set; }
        public float LastImpactSpeedMps { get; private set; }
        public TankHealth Health => health;

        private void Awake()
        {
            ResolveReferences();
        }

        public void Configure(CombatRules rules)
        {
            configuredRules = rules ?? new CombatRules();
            ResolveReferences();
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            TankRammingDamage other = collision.gameObject.GetComponentInParent<TankRammingDamage>();
            if (other == null || other == this ||
                string.CompareOrdinal(GetEntityId().ToString(), other.GetEntityId().ToString()) > 0)
            {
                return;
            }

            Vector2 directionToOther = other.transform.position - transform.position;
            Vector2 contactNormal = collision.contactCount > 0
                ? collision.GetContact(0).normal
                : directionToOther.normalized;
            float closingSpeedUnits = Mathf.Abs(Vector2.Dot(collision.relativeVelocity, contactNormal.normalized));
            ResolvePair(other, GameScale.UnitsToMetres(closingSpeedUnits), directionToOther, Time.time);
        }

        private void ResolvePair(
            TankRammingDamage other,
            float impactSpeedMps,
            Vector2 directionToOther,
            float now)
        {
            ResolveReferences();
            other.ResolveReferences();
            if (health == null || other.health == null || health.IsDestroyed || other.health.IsDestroyed ||
                health.Definition == null || other.health.Definition == null)
            {
                return;
            }

            RammingRules rules = EffectiveRules().ramming ?? new RammingRules();
            EntityId otherId = other.GetEntityId();
            if (nextAllowedImpactByOther.TryGetValue(otherId, out float nextAllowed) && now < nextAllowed)
            {
                return;
            }

            Vector2 outwardToOther = directionToOther.sqrMagnitude > 0.0001f
                ? directionToOther.normalized
                : Vector2.up;
            ArmorZone ownZone = ContactZone(transform, outwardToOther);
            ArmorZone otherZone = ContactZone(other.transform, -outwardToOther);
            TankDefinition ownDefinition = health.Definition;
            TankDefinition otherDefinition = other.health.Definition;
            RammingDamageResult result = RammingDamageMath.Calculate(
                ownDefinition.massTonnes,
                otherDefinition.massTonnes,
                impactSpeedMps,
                ownDefinition.armor.ThicknessFor(ownZone),
                otherDefinition.armor.ThicknessFor(otherZone),
                rules);

            LastImpactSpeedMps = impactSpeedMps;
            LastResult = result;
            if (!result.HasDamage)
            {
                return;
            }

            nextAllowedImpactByOther[otherId] = now + Mathf.Max(0f, rules.repeatContactCooldownSeconds);

            // Both results are calculated before either health state changes, so a lethal head-on ram can
            // destroy both vehicles in the same physics step and correctly produce a draw.
            health.ApplyDamage(result.DamageToFirst);
            other.health.ApplyDamage(result.DamageToSecond);
            ImpactResolved?.Invoke(this, new RammingImpactObservation(
                other,
                impactSpeedMps,
                ownZone,
                otherZone,
                result));
        }

        private CombatRules EffectiveRules()
        {
            if (configuredRules != null)
            {
                return configuredRules;
            }

            BattleDirector director = GetComponentInParent<BattleDirector>();
            return director != null && director.Rules != null && director.Rules.combat != null
                ? director.Rules.combat
                : configuredRules = new CombatRules();
        }

        private void ResolveReferences()
        {
            if (health == null) health = GetComponent<TankHealth>();
        }

        private static ArmorZone ContactZone(Transform tank, Vector2 outwardNormal)
        {
            Vector2 localNormal = tank.InverseTransformDirection(outwardNormal).normalized;
            return ArmorMath.SelectZone(localNormal);
        }
    }
}
