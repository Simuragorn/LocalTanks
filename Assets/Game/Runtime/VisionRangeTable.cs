using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    /// <summary>
    /// View range by vehicle class and tier, written in World of Tanks metres (ADR-064).
    ///
    /// The class decides the range and the tier adds to it: light tanks see furthest, artillery least. The
    /// table is authored in metres and converted with <see cref="GameScale"/>, so the distance a player is
    /// shown and the distance the game uses cannot drift apart. A tank does not carry its own view range on
    /// purpose: the hand-written value per machine is what made every vehicle see roughly the same distance,
    /// with the class affecting only the view angle.
    /// </summary>
    public static class VisionRangeTable
    {
        public const int MinimumTier = 1;
        public const int MaximumTier = 10;

        /// <summary>
        /// Share of the view range at which a target is detected regardless of its concealment. Preserved
        /// from the authored data, where the guaranteed range was 5.408 units against a 33.8 unit view range,
        /// so the guarantee keeps its meaning when the view range grows with the class table.
        /// </summary>
        public const float GuaranteedDetectionShare = 0.16f;

        private static readonly float[] LightTankMetres = { 240f, 255f, 270f, 290f, 305f, 320f, 335f, 350f, 365f, 380f };
        private static readonly float[] MediumTankMetres = { 220f, 235f, 250f, 265f, 280f, 295f, 310f, 325f, 340f, 360f };
        private static readonly float[] HeavyTankMetres = { 200f, 215f, 230f, 250f, 265f, 280f, 295f, 310f, 330f, 350f };
        private static readonly float[] TankDestroyerMetres = { 190f, 205f, 220f, 235f, 250f, 265f, 280f, 295f, 310f, 330f };
        private static readonly float[] ArtilleryMetres = { 170f, 180f, 190f, 200f, 215f, 230f, 245f, 260f, 275f, 290f };

        /// <summary>Every class the table covers. Used by the test that forbids a hole in it.</summary>
        public static IEnumerable<VehicleClass> Classes
        {
            get
            {
                yield return VehicleClass.LightTank;
                yield return VehicleClass.MediumTank;
                yield return VehicleClass.HeavyTank;
                yield return VehicleClass.TankDestroyer;
                yield return VehicleClass.Artillery;
            }
        }

        /// <summary>View range of a class at a tier, in client metres.</summary>
        public static float MetresFor(VehicleClass vehicleClass, int tier)
        {
            if (tier < MinimumTier || tier > MaximumTier)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tier), tier, $"Tier must be between {MinimumTier} and {MaximumTier}.");
            }

            float[] row = RowFor(vehicleClass);
            return row[tier - MinimumTier];
        }

        /// <summary>
        /// View range in game units, rounded to two decimals the way the data pipeline rounds the values it
        /// writes, so a regenerated file and a computed value agree exactly.
        /// </summary>
        public static float UnitsFor(VehicleClass vehicleClass, int tier)
        {
            return Mathf.Round(GameScale.MetresToUnits(MetresFor(vehicleClass, tier)) * 100f) / 100f;
        }

        /// <summary>Distance at which concealment stops mattering, in game units.</summary>
        public static float GuaranteedDetectionUnits(float viewRange)
        {
            return Mathf.Round(viewRange * GuaranteedDetectionShare * 100f) / 100f;
        }

        private static float[] RowFor(VehicleClass vehicleClass)
        {
            switch (vehicleClass)
            {
                case VehicleClass.LightTank:
                    return LightTankMetres;
                case VehicleClass.MediumTank:
                    return MediumTankMetres;
                case VehicleClass.HeavyTank:
                    return HeavyTankMetres;
                case VehicleClass.TankDestroyer:
                    return TankDestroyerMetres;
                case VehicleClass.Artillery:
                    return ArtilleryMetres;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(vehicleClass), vehicleClass, "The view range table does not cover this class.");
            }
        }
    }
}
