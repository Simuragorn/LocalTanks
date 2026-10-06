using UnityEngine;

namespace LocalTanks
{
    public sealed class ShellDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        [Min(1)] public int damage = 1;
        [Min(0.01f)] public float penetration = 1f;
        [Min(0.01f)] public float speed = 1f;
        [Min(0.001f)] public float radius = 0.01f;
        [Min(0.01f)] public float lifetimeSeconds = 1f;
        [Min(0.01f)] public float maximumRange = 1f;
        [Range(0f, 90f)] public float ricochetAngle = 70f;
        [Range(0.01f, 1f)] public float ricochetSpeedMultiplier = 0.7f;
        [Range(0.01f, 1f)] public float ricochetPenetrationMultiplier = 0.65f;
        [Range(0, 8)] public int maximumRicochets = 2;
    }
}
