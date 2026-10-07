using UnityEngine;

namespace LocalTanks
{
    public sealed class VisionRules : ScriptableObject
    {
        [Range(0f, 1f)] public float maximumConcealment = 0.8f;
        [Range(0f, 1f)] public float maximumBushBonus = 0.45f;
        [Range(0f, 1f)] public float minimumVisibilityFactor = 0.2f;
        [Min(0.05f)] public float checkInterval = 0.2f;
        [Min(1)] public int checksPerFrame = 24;
        [Min(0f)] public float contactMemorySeconds = 8f;
    }
}
