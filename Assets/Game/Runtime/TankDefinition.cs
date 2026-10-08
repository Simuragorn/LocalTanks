using UnityEngine;

namespace LocalTanks
{
    public enum VehicleClass
    {
        HeavyTank,
        MediumTank,
        LightTank,
        TankDestroyer,
        Artillery
    }

    public enum TankNation
    {
        Germany,
        USSR
    }

    public sealed class TankDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        public VehicleClass vehicleClass;
        public TankNation nation;
        public bool availableInGame = true;
        [Min(1)] public int maxHitPoints = 1;
        public WeaponDefinition weapon;

        [Header("Hull movement")]
        [Min(0f)] public float maxForwardSpeed;
        [Min(0f)] public float maxReverseSpeed;
        [Min(0f)] public float acceleration;
        [Min(0f)] public float groundResistance;
        [Min(0f)] public float braking;
        [Min(0f)] public float hullTurnSpeed;

        [Header("Turret")]
        [Min(0f)] public float turretTurnSpeed;

        [Header("Vision")]
        [Min(0f)] public float viewRange;
        [Range(0f, 1f)] public float stationaryConcealment;
        [Range(0f, 1f)] public float movementRevealPenalty;
        [Range(0f, 1f)] public float firingRevealPenalty;
        [Min(0f)] public float firingRevealDuration;
        [Min(0f)] public float guaranteedDetectionRange;

        [Header("Hull armor")]
        public ArmorProfile armor;
    }
}
