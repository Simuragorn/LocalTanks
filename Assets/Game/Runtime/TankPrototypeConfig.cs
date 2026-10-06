using UnityEngine;

namespace LocalTanks
{
    [CreateAssetMenu(fileName = "TankPrototypeConfig", menuName = "Local Tanks/Tank Prototype Config")]
    public sealed class TankPrototypeConfig : ScriptableObject
    {
        [Header("Hull movement")]
        [Min(0f)] public float maxForwardSpeed = 5f;
        [Min(0f)] public float maxReverseSpeed = 2.25f;
        [Min(0f)] public float acceleration = 3.5f;
        [Min(0f)] public float braking = 6f;
        [Min(0f)] public float hullTurnSpeed = 75f;

        [Header("Turret")]
        [Min(0f)] public float turretTurnSpeed = 110f;

        [Header("Cannon")]
        [Min(0.01f)] public float reloadSeconds = 0.8f;
        [Min(0.01f)] public float projectileSpeed = 18f;
        [Min(0.001f)] public float projectileRadius = 0.06f;
        [Min(0.01f)] public float projectileLifetime = 3f;
        [Min(0.01f)] public float projectileRange = 45f;
    }
}
