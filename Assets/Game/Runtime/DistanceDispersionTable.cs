using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    /// <summary>
    /// Class-based accuracy loss over distance for the top-down battle (ADR-084).
    ///
    /// The multiplier is added smoothly for every 20 metres. A small reference angle keeps an authored
    /// zero-degree gun from bypassing the rule, while leaving its point-blank accuracy unchanged.
    /// </summary>
    public static class DistanceDispersionTable
    {
        public const float StepMetres = 20f;
        public const float ReferenceDispersionDegrees = 0.5f;

        public const float ArtilleryMultiplierPerStep = 0f;
        public const float TankDestroyerMultiplierPerStep = 0.05f;
        public const float MediumTankMultiplierPerStep = 0.15f;
        public const float LightTankMultiplierPerStep = 0.20f;
        public const float HeavyTankMultiplierPerStep = 0.40f;

        public static IEnumerable<VehicleClass> Classes
        {
            get
            {
                yield return VehicleClass.Artillery;
                yield return VehicleClass.TankDestroyer;
                yield return VehicleClass.MediumTank;
                yield return VehicleClass.LightTank;
                yield return VehicleClass.HeavyTank;
            }
        }

        public static float MultiplierPerStep(VehicleClass vehicleClass)
        {
            switch (vehicleClass)
            {
                case VehicleClass.Artillery:
                    return ArtilleryMultiplierPerStep;
                case VehicleClass.TankDestroyer:
                    return TankDestroyerMultiplierPerStep;
                case VehicleClass.MediumTank:
                    return MediumTankMultiplierPerStep;
                case VehicleClass.LightTank:
                    return LightTankMultiplierPerStep;
                case VehicleClass.HeavyTank:
                    return HeavyTankMultiplierPerStep;
                default:
                    throw new System.ArgumentOutOfRangeException(
                        nameof(vehicleClass), vehicleClass, "The distance-dispersion table does not cover this class.");
            }
        }

        public static float PenaltyDegrees(
            VehicleClass vehicleClass,
            float distanceUnits,
            float currentDispersionDegrees)
        {
            float multiplier = MultiplierPerStep(vehicleClass);
            if (multiplier <= 0f || distanceUnits <= 0f)
            {
                return 0f;
            }

            float distanceSteps = GameScale.UnitsToMetres(distanceUnits) / StepMetres;
            float reference = Mathf.Max(ReferenceDispersionDegrees, Mathf.Max(0f, currentDispersionDegrees));
            return reference * multiplier * distanceSteps;
        }

        public static float EffectiveDegrees(
            VehicleClass vehicleClass,
            float distanceUnits,
            float currentDispersionDegrees)
        {
            float current = Mathf.Max(0f, currentDispersionDegrees);
            return current + PenaltyDegrees(vehicleClass, distanceUnits, current);
        }
    }
}
