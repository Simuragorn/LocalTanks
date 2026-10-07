using UnityEngine;

namespace LocalTanks
{
    public sealed class VisionRules : ScriptableObject
    {
        [Header("Detection")]
        [Range(0f, 1f)] public float maximumConcealment = 0.8f;
        [Range(0f, 1f)] public float maximumBushBonus = 0.45f;
        [Range(0f, 1f)] public float minimumVisibilityFactor = 0.2f;
        [Min(0.05f)] public float checkInterval = 0.2f;
        [Min(1)] public int checksPerFrame = 24;
        [Min(0f)] public float contactMemorySeconds = 8f;

        [Header("View arc by vehicle class")]
        [Range(1f, 360f)] public float lightTankViewAngle = 160f;
        [Range(1f, 360f)] public float mediumTankViewAngle = 140f;
        [Range(1f, 360f)] public float heavyTankViewAngle = 120f;
        [Range(1f, 360f)] public float tankDestroyerViewAngle = 100f;
        [Range(1f, 360f)] public float artilleryViewAngle = 80f;

        [Header("Local player presentation")]
        [Min(10f)] public float viewArcVisualRadius = 60f;
        [Range(0f, 0.4f)] public float outsideArcOverlayAlpha = 0.14f;
        [Range(0f, 0.5f)] public float viewBoundaryAlpha = 0.2f;
        [Min(0.01f)] public float viewBoundaryWidth = 0.04f;

        public float GetViewAngle(VehicleClass vehicleClass)
        {
            switch (vehicleClass)
            {
                case VehicleClass.LightTank:
                    return lightTankViewAngle;
                case VehicleClass.MediumTank:
                    return mediumTankViewAngle;
                case VehicleClass.HeavyTank:
                    return heavyTankViewAngle;
                case VehicleClass.TankDestroyer:
                    return tankDestroyerViewAngle;
                case VehicleClass.Artillery:
                    return artilleryViewAngle;
                default:
                    return heavyTankViewAngle;
            }
        }
    }
}
