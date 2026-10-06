using UnityEngine;

namespace LocalTanks
{
    public sealed class ReloadTimer
    {
        public float Remaining { get; private set; }
        public bool IsReady => Remaining <= 0f;

        public void Start(float duration)
        {
            Remaining = Mathf.Max(0f, duration);
        }

        public void Tick(float deltaTime)
        {
            Remaining = Mathf.Max(0f, Remaining - Mathf.Max(0f, deltaTime));
        }
    }
}
