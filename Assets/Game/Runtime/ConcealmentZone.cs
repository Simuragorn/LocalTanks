using UnityEngine;

namespace LocalTanks
{
    public sealed class ConcealmentZone : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float concealmentBonus = 0.18f;

        public float ConcealmentBonus => concealmentBonus;

        public void Configure(float bonus)
        {
            concealmentBonus = Mathf.Clamp01(bonus);
        }
    }
}
