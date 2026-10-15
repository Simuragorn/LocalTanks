using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    /// <summary>
    /// Numeric limits shared by the aiming preview and the real impact calculation. They belong to match
    /// data so a balance change cannot silently make the reticle disagree with the shot.
    /// </summary>
    [Serializable]
    public sealed class CombatRules
    {
        [Range(0.0001f, 1f)] public float minimumArmorCosine = ArmorMath.DefaultMinimumCosine;
        [Min(0.01f)] public float penetrationRandomMinimum = 0.75f;
        [Min(0.01f)] public float penetrationRandomMaximum = 1.25f;
        [Min(0.01f)] public float damageRandomMinimum = 0.75f;
        [Min(0.01f)] public float damageRandomMaximum = 1.25f;
        public RammingRules ramming = new RammingRules();

        public string[] Validate(string owner)
        {
            List<string> errors = new List<string>();
            if (minimumArmorCosine < 0.0001f || minimumArmorCosine > 1f)
                errors.Add($"{owner} minimum armor cosine must be in 0.0001..1, found {minimumArmorCosine}.");
            ValidateRange(penetrationRandomMinimum, penetrationRandomMaximum, owner + " penetration", errors);
            ValidateRange(damageRandomMinimum, damageRandomMaximum, owner + " damage", errors);
            if (ramming == null)
                errors.Add($"{owner} ramming rules are missing.");
            else
                errors.AddRange(ramming.Validate(owner + " ramming"));
            return errors.ToArray();
        }

        private static void ValidateRange(float minimum, float maximum, string owner, ICollection<string> errors)
        {
            if (minimum <= 0f || maximum <= 0f || maximum <= minimum)
                errors.Add($"{owner} random range must be positive and increasing, found {minimum}..{maximum}.");
        }
    }

    /// <summary>
    /// Ramming balance lives beside shell balance because both are ways to remove hit points. The defaults
    /// are deliberately expressed in physical inputs: impact speed in metres per second, vehicle mass in
    /// tonnes and collision energy in kilojoules. One authored profile therefore works at every map scale.
    /// </summary>
    [Serializable]
    public sealed class RammingRules
    {
        public bool enabled = true;
        [Min(0f)] public float minimumImpactSpeedMps = 1.5f;
        [Min(0.0001f)] public float hitPointsPerKilojoule = 1f;
        [Min(0.01f)] public float armorProtectionReferenceMm = 50f;
        [Min(0f)] public float repeatContactCooldownSeconds = 0.35f;

        public string[] Validate(string owner)
        {
            List<string> errors = new List<string>();
            if (minimumImpactSpeedMps < 0f)
                errors.Add($"{owner} minimum impact speed must not be negative, found {minimumImpactSpeedMps}.");
            if (hitPointsPerKilojoule <= 0f)
                errors.Add($"{owner} damage scale must be positive, found {hitPointsPerKilojoule}.");
            if (armorProtectionReferenceMm <= 0f)
                errors.Add($"{owner} armor reference must be positive, found {armorProtectionReferenceMm}.");
            if (repeatContactCooldownSeconds < 0f)
                errors.Add($"{owner} repeat-contact cooldown must not be negative, found {repeatContactCooldownSeconds}.");
            return errors.ToArray();
        }
    }

    /// <summary>Both independent rolls made once when a shell leaves the gun.</summary>
    public readonly struct ShotRandomization
    {
        public static readonly ShotRandomization Identity = new ShotRandomization(-1, 1f, 1f);

        public ShotRandomization(int sequence, float penetrationMultiplier, float damageMultiplier)
        {
            Sequence = sequence;
            PenetrationMultiplier = penetrationMultiplier;
            DamageMultiplier = damageMultiplier;
        }

        public int Sequence { get; }
        public float PenetrationMultiplier { get; }
        public float DamageMultiplier { get; }
    }

    /// <summary>
    /// One deterministic stream per match. It has no reset method by design: recreating it mid-battle
    /// would repeat rolls and make two later shots indistinguishable from the opening shots.
    /// </summary>
    public sealed class ShotRandomStream
    {
        private readonly int seed;
        private int nextSequence;

        public ShotRandomStream(int seed)
        {
            this.seed = seed;
        }

        public int Seed => seed;
        public int NextSequence => nextSequence;

        public ShotRandomization Next(CombatRules rules)
        {
            CombatRules effectiveRules = rules ?? new CombatRules();
            int sequence = nextSequence++;
            float penetration = CombatRandomMath.ScaleNormalized(
                CombatRandomMath.Sample01(seed, sequence, 0xA511E9B3u),
                effectiveRules.penetrationRandomMinimum,
                effectiveRules.penetrationRandomMaximum);
            float damage = CombatRandomMath.ScaleNormalized(
                CombatRandomMath.Sample01(seed, sequence, 0x63D83595u),
                effectiveRules.damageRandomMinimum,
                effectiveRules.damageRandomMaximum);
            return new ShotRandomization(sequence, penetration, damage);
        }
    }

    public static class CombatRandomMath
    {
        public static float ScaleNormalized(float normalized, float minimum, float maximum)
        {
            return Mathf.Lerp(minimum, maximum, Mathf.Clamp01(normalized));
        }

        public static float ApplyPenetration(float basePenetration, float multiplier)
        {
            return Mathf.Max(0f, basePenetration) * Mathf.Max(0f, multiplier);
        }

        public static int ApplyDamage(int baseDamage, float multiplier)
        {
            if (baseDamage <= 0) return 0;
            return Mathf.Max(1, Mathf.RoundToInt(baseDamage * Mathf.Max(0f, multiplier)));
        }

        public static float Sample01(int seed, int sequence, uint channel)
        {
            uint value = unchecked((uint)seed);
            value ^= unchecked((uint)(sequence + 1) * 0x9E3779B9u);
            value ^= channel;
            value ^= value >> 16;
            value = unchecked(value * 0x7FEB352Du);
            value ^= value >> 15;
            value = unchecked(value * 0x846CA68Bu);
            value ^= value >> 16;
            return (value & 0x00FFFFFFu) / 16777215f;
        }
    }
}
