using System.Linq;
using UnityEngine;

namespace LocalTanks
{
    public sealed class TankVisibilityPresenter : MonoBehaviour
    {
        /// <summary>
        /// Keeps an unseen tank rendered as a semi-transparent silhouette. <see cref="BattleDirector"/>
        /// sets this from the scene's <see cref="BattleVisibilitySettings"/>; the default is
        /// disabled, so a tank the team cannot see is invisible.
        /// </summary>
        [SerializeField] private bool showHiddenTanksForTesting;

        [SerializeField, Range(0.05f, 0.9f)] private float hiddenAlpha = 0.28f;

        private SpriteRenderer[] renderers;
        private float[] originalAlphas;
        private TankHealthBar healthBar;

        public bool IsVisible { get; private set; } = true;
        public bool ShowHiddenTanksForTesting => showHiddenTanksForTesting;
        public float HiddenAlpha => hiddenAlpha;

        private void Awake()
        {
            if (renderers == null)
            {
                CacheVisuals();
            }
        }

        /// <summary>Applies the per-scene visibility presentation used for every spawned tank.</summary>
        public void ConfigureHiddenPresentation(bool showHiddenForTesting, float alpha)
        {
            showHiddenTanksForTesting = showHiddenForTesting;
            hiddenAlpha = Mathf.Clamp(alpha, 0.05f, 0.9f);
            SetVisible(IsVisible);
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

                renderer.enabled = visible || showHiddenTanksForTesting;
                Color color = renderer.color;
                color.a = originalAlphas[index] * (visible ? 1f : hiddenAlpha);
                renderer.color = color;
            }

            if (healthBar != null)
            {
                healthBar.enabled = visible;
            }
        }

        public void RefreshVisuals()
        {
            RestoreOriginalAlphas();
            CacheVisuals();
            SetVisible(IsVisible);
        }

        private void RestoreOriginalAlphas()
        {
            if (renderers == null || originalAlphas == null)
            {
                return;
            }

            int count = Mathf.Min(renderers.Length, originalAlphas.Length);
            for (int index = 0; index < count; index++)
            {
                SpriteRenderer renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                Color color = renderer.color;
                color.a = originalAlphas[index];
                renderer.color = color;
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
