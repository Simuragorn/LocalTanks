using UnityEngine;

namespace LocalTanks
{
    public sealed class TankVisibilityPresenter : MonoBehaviour
    {
        private SpriteRenderer[] renderers;
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
            foreach (SpriteRenderer renderer in renderers)
            {
                if (renderer != null)
                {
                    renderer.enabled = visible;
                }
            }

            if (healthBar != null)
            {
                healthBar.enabled = visible;
            }
        }

        private void CacheVisuals()
        {
            renderers = GetComponentsInChildren<SpriteRenderer>(true);
            healthBar = GetComponent<TankHealthBar>();
        }
    }
}
