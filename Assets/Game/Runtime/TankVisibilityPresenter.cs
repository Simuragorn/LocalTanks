using System.Linq;
using UnityEngine;

namespace LocalTanks
{
    public sealed class TankVisibilityPresenter : MonoBehaviour
    {
        [SerializeField] private bool showHiddenForTesting = true;
        [SerializeField, Range(0.05f, 0.9f)] private float hiddenAlpha = 0.28f;

        private SpriteRenderer[] renderers;
        private float[] originalAlphas;
        private TankHealthBar healthBar;

        public bool IsVisible { get; private set; } = true;

        private void Awake()
        {
            CacheVisuals();
        }

        public void SetVisible(bool visible)
        {
            if (renderers == null)
            {
                CacheVisuals();
            }

            IsVisible = visible;
            for (int index = 0; index < renderers.Length; index++)
            {
                SpriteRenderer renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                renderer.enabled = visible || showHiddenForTesting;
                Color color = renderer.color;
                color.a = originalAlphas[index] * (visible ? 1f : hiddenAlpha);
                renderer.color = color;
            }

            if (healthBar != null)
            {
                healthBar.enabled = visible;
            }
        }

        private void CacheVisuals()
        {
            renderers = GetComponentsInChildren<SpriteRenderer>(true);
            originalAlphas = renderers.Select(renderer => renderer.color.a).ToArray();
            healthBar = GetComponent<TankHealthBar>();
        }
    }
}
