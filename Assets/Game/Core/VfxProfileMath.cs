using UnityEngine;

namespace LocalTanks
{
    /// <summary>
    /// The arithmetic that turns a machine's own numbers into a visual effect size.
    /// </summary>
    /// <remarks>
    /// No machine stores its own effect parameters. A 45 mm gun and an 88 mm gun differ in the world
    /// because their caliber differs, a 5 t and a 70 t machine differ because their mass differs, and a
    /// burning wreck differs from a healthy tank because its remaining health fraction differs. The band
    /// tables live in one committed JSON file; this class only selects a band and scales a number, so
    /// adding a machine never means adding a second copy of visual data.
    /// <para>
    /// Pure math, covered by Edit Mode tests: band edges are the kind of mistake that produces a
    /// plausible but wrong effect and is invisible in code review.
    /// </para>
    /// </remarks>
    public static class VfxProfileMath
    {
        /// <summary>
        /// Index of the band whose <paramref name="upperBounds"/> entry first covers
        /// <paramref name="value"/>. Bounds are read as an ascending table, so the last entry is the
        /// open-ended band and every value above the table still resolves instead of failing.
        /// </summary>
        /// <returns>-1 when the table is empty.</returns>
        public static int SelectBandIndex(float value, float[] upperBounds)
        {
            if (upperBounds == null || upperBounds.Length == 0)
            {
                return -1;
            }

            for (int index = 0; index < upperBounds.Length; index++)
            {
                if (value <= upperBounds[index])
                {
                    return index;
                }
            }

            return upperBounds.Length - 1;
        }

        /// <summary>
        /// Index of the band whose <paramref name="lowerBounds"/> entry first contains
        /// <paramref name="value"/> while the table is read from the largest bound to the smallest.
        /// Used by the damage bands, which are described by "at most this much health left".
        /// </summary>
        /// <returns>-1 when the table is empty.</returns>
        public static int SelectDescendingBandIndex(float value, float[] lowerBounds)
        {
            if (lowerBounds == null || lowerBounds.Length == 0)
            {
                return -1;
            }

            for (int index = 0; index < lowerBounds.Length; index++)
            {
                if (value >= lowerBounds[index])
                {
                    return index;
                }
            }

            return lowerBounds.Length - 1;
        }

        /// <summary>Remaining health as a fraction of the maximum, clamped to 0..1.</summary>
        public static float HealthFraction(int currentHitPoints, int maximumHitPoints)
        {
            if (maximumHitPoints <= 0)
            {
                return 0f;
            }

            return Mathf.Clamp01((float)currentHitPoints / maximumHitPoints);
        }

        /// <summary>
        /// Maps a value onto 0..1 between two reference points. Used for speed and engine load, where
        /// zero speed must mean zero effect and the limit of the machine must mean exactly one.
        /// </summary>
        public static float Intensity01(float value, float atZero, float atOne)
        {
            if (Mathf.Approximately(atZero, atOne))
            {
                return value > atZero ? 1f : 0f;
            }

            return Mathf.Clamp01((value - atZero) / (atOne - atZero));
        }

        /// <summary>
        /// Applies the quality scale of the effect configuration to a particle count. The count never
        /// becomes negative and never silently loses a whole effect that was asked for.
        /// </summary>
        public static int ScaleCount(int baseCount, float qualityScale)
        {
            if (baseCount <= 0)
            {
                return 0;
            }

            return Mathf.Max(1, Mathf.RoundToInt(baseCount * Mathf.Max(0.05f, qualityScale)));
        }

        /// <summary>
        /// Size of the dust a shell kicks up, derived from the shell radius the projectile already carries.
        /// </summary>
        /// <remarks>
        /// The radius is not an independent number: the World of Tanks pipeline stores a caliber-driven
        /// collision radius of <c>caliberMm / 1200</c>, so a radius of 0.0375 is a 45 mm shell, the smallest
        /// gun in the fleet. Expressing the effect as a multiple of that keeps a heavy shell visibly
        /// heavier without a second table of per-shell visual data.
        /// </remarks>
        public static float ImpactScaleForRadius(float shellRadius)
        {
            const float ReferenceRadius = 45f / 1200f;
            const float SmallestScale = 0.65f;
            const float LargestScale = 2.2f;
            return Mathf.Clamp(Mathf.Max(0f, shellRadius) / ReferenceRadius, SmallestScale, LargestScale);
        }
    }
}
