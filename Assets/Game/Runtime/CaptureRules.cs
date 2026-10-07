using UnityEngine;

namespace LocalTanks
{
    public sealed class CaptureRules : ScriptableObject
    {
        [Min(1f)] public float baseCaptureSeconds = 180f;
        [Min(1)] public int maximumContributingTanks = 3;
        [Min(1f)] public float fullRecoverySeconds = 60f;
    }
}
