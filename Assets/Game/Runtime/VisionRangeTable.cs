using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    /// <summary>
    /// View range by vehicle class, written in metres (ADR-064).
    ///
    /// Five deliberately separated bands fit the top-down game: light tanks scout, medium tanks can find
    /// their own fights, and heavy tanks, tank destroyers and artillery increasingly rely on team contacts.
    /// Tier does not alter the range. The table is converted with <see cref="GameScale"/>, so the distance a
    /// player is shown and the distance the game uses cannot drift apart.
    /// </summary>
    public static class VisionRangeTable
    {
        /// <summary>
        /// Share of the view range at which a target is detected regardless of its concealment. Preserved
        /// from the authored data, where the guaranteed range was 5.408 units against a 33.8 unit view range,
        /// so the guarantee keeps its meaning when the view range grows with the class table.
        /// </summary>
        public const float GuaranteedDetectionShare = 0.16f;

        public const float LightTankMetres = 200f;
        public const float MediumTankMetres = 160f;
        public const float HeavyTankMetres = 120f;
        public const float TankDestroyerMetres = 100f;
        public const float ArtilleryMetres = 60f;

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

        /// <summary>View range of a class in game metres. Tier deliberately does not participate.</summary>
        public static float MetresFor(VehicleClass vehicleClass)
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
                    throw new System.ArgumentOutOfRangeException(
                        nameof(vehicleClass), vehicleClass, "The view range table does not cover this class.");
            }
        }

        /// <summary>
        /// View range in game units, rounded to two decimals the way the data pipeline rounds the values it
        /// writes, so a regenerated file and a computed value agree exactly.
        /// </summary>
        public static float UnitsFor(VehicleClass vehicleClass)
        {
            return Mathf.Round(GameScale.MetresToUnits(MetresFor(vehicleClass)) * 100f) / 100f;
        }

        /// <summary>Distance at which concealment stops mattering, in game units.</summary>
        public static float GuaranteedDetectionUnits(float viewRange)
        {
            return Mathf.Round(viewRange * GuaranteedDetectionShare * 100f) / 100f;
        }

    }
}
