using System.Linq;
using UnityEngine;

namespace LocalTanks
{
    public sealed class ConcealmentZone : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float concealmentBonus = 0.18f;
        [SerializeField, Min(0.1f)] private float fadeDistance = 2f;
        [SerializeField, Range(0.05f, 1f)] private float fadedAlpha = 0.3f;
        [SerializeField, Min(0.1f)] private float fadeSpeed = 4f;

        private SpriteRenderer[] renderers;
        private float[] originalAlphas;
        private TeamMember player;
        private float nextPlayerSearchTime;

        public float ConcealmentBonus => concealmentBonus;
        public float CurrentAlpha { get; private set; } = 1f;

        private void Awake()
        {
            renderers = GetComponentsInChildren<SpriteRenderer>(true);
            originalAlphas = renderers.Select(renderer => renderer.color.a).ToArray();
        }

        public void Configure(float bonus)
        {
            concealmentBonus = Mathf.Clamp01(bonus);
        }

        private void Update()
        {
            if (player == null && Time.time >= nextPlayerSearchTime)
            {
                player = TeamMember.ActiveMembers.FirstOrDefault(member =>
                    member != null && member.IsPlayerControlled && member.IsAlive);
                nextPlayerSearchTime = Time.time + 0.5f;
            }

            float targetAlpha = player != null &&
                                Vector2.Distance(transform.position, player.transform.position) <= fadeDistance
                ? fadedAlpha
                : 1f;
            CurrentAlpha = Mathf.MoveTowards(CurrentAlpha, targetAlpha, fadeSpeed * Time.deltaTime);
            if (renderers == null)
            {
                return;
            }

            for (int index = 0; index < renderers.Length; index++)
            {
                SpriteRenderer renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                Color color = renderer.color;
                color.a = originalAlphas[index] * CurrentAlpha;
                renderer.color = color;
            }
        }
    }
}
