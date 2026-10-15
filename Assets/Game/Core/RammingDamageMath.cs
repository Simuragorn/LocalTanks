using UnityEngine;

namespace LocalTanks
{
    /// <summary>The deterministic result of one tank-to-tank impact.</summary>
    public readonly struct RammingDamageResult
    {
        public RammingDamageResult(int damageToFirst, int damageToSecond, float collisionEnergyKilojoules)
        {
            DamageToFirst = damageToFirst;
            DamageToSecond = damageToSecond;
            CollisionEnergyKilojoules = collisionEnergyKilojoules;
        }

        public int DamageToFirst { get; }
        public int DamageToSecond { get; }
        public float CollisionEnergyKilojoules { get; }
        public bool HasDamage => DamageToFirst > 0 || DamageToSecond > 0;
    }

    /// <summary>
    /// A compact WoT-style collision model. Reduced mass and closing speed give the shared collision
    /// energy; the opposing mass determines how much of it each hull receives; armor at the touching face
    /// absorbs part of that share. It intentionally has no team input: a physical collision hurts allies
    /// and opponents by the same rules.
    /// </summary>
    public static class RammingDamageMath
    {
        public static RammingDamageResult Calculate(
            float firstMassTonnes,
            float secondMassTonnes,
            float impactSpeedMps,
            float firstContactArmorMm,
            float secondContactArmorMm,
            RammingRules rules)
        {
            RammingRules effectiveRules = rules ?? new RammingRules();
            float firstMass = Mathf.Max(0f, firstMassTonnes);
            float secondMass = Mathf.Max(0f, secondMassTonnes);
            float totalMass = firstMass + secondMass;
            float speed = Mathf.Max(0f, impactSpeedMps);
            if (!effectiveRules.enabled || firstMass <= 0f || secondMass <= 0f || totalMass <= 0f ||
                speed < effectiveRules.minimumImpactSpeedMps)
            {
                return default;
            }

            // 0.5 * tonnes * (m/s)^2 is numerically equal to kilojoules.
            float reducedMass = firstMass * secondMass / totalMass;
            float collisionEnergy = 0.5f * reducedMass * speed * speed;
            float scaledEnergy = collisionEnergy * Mathf.Max(0f, effectiveRules.hitPointsPerKilojoule);

            float damageToFirst = scaledEnergy * (secondMass / totalMass) *
                                  ArmorSurvivalMultiplier(firstContactArmorMm, effectiveRules);
            float damageToSecond = scaledEnergy * (firstMass / totalMass) *
                                   ArmorSurvivalMultiplier(secondContactArmorMm, effectiveRules);
            return new RammingDamageResult(
                Mathf.Max(0, Mathf.RoundToInt(damageToFirst)),
                Mathf.Max(0, Mathf.RoundToInt(damageToSecond)),
                collisionEnergy);
        }

        public static float ArmorSurvivalMultiplier(float armorMm, RammingRules rules)
        {
            float reference = Mathf.Max(0.01f, (rules ?? new RammingRules()).armorProtectionReferenceMm);
            return reference / (reference + Mathf.Max(0f, armorMm));
        }
    }
}
