using UnityEngine;

namespace LocalTanks
{
    public static class TeamPalette
    {
        public static readonly Color Ally = new Color32(120, 169, 123, 255);
        public static readonly Color Enemy = new Color32(185, 104, 104, 255);
        public static readonly Color Neutral = new Color32(175, 175, 168, 255);

        public static Color ForRelation(bool allied)
        {
            return allied ? Ally : Enemy;
        }
    }

    public sealed class TankClassIconPresenter : MonoBehaviour
    {
        [SerializeField] private Sprite icon;
        [SerializeField] private bool allied;
        [SerializeField, Min(0f)] private float worldOffset = 1.65f;
        [SerializeField] private SpriteRenderer iconRenderer;

        public Sprite Icon => icon;
        public Color TeamColor => TeamPalette.ForRelation(allied);

        private void Awake()
        {
            EnsureIconRenderer();
        }

        private void LateUpdate()
        {
            if (iconRenderer == null)
            {
                return;
            }

            iconRenderer.transform.SetPositionAndRotation(
                transform.position + Vector3.up * worldOffset,
                Quaternion.identity);
        }

        public void Configure(Sprite classIcon, bool isAllied, float offset)
        {
            icon = classIcon;
            allied = isAllied;
            worldOffset = Mathf.Max(0f, offset);
            EnsureIconRenderer();
        }

        private void EnsureIconRenderer()
        {
            if (iconRenderer == null)
            {
                Transform existing = transform.Find("ClassIcon");
                GameObject iconObject = existing != null ? existing.gameObject : new GameObject("ClassIcon");
                iconObject.transform.SetParent(transform, true);
                iconRenderer = iconObject.GetComponent<SpriteRenderer>();
                if (iconRenderer == null)
                {
                    iconRenderer = iconObject.AddComponent<SpriteRenderer>();
                }
            }

            iconRenderer.sprite = icon;
            iconRenderer.color = TeamColor;
            iconRenderer.sortingOrder = 100;
            iconRenderer.transform.localScale = Vector3.one * 0.52f;
            iconRenderer.transform.SetPositionAndRotation(
                transform.position + Vector3.up * worldOffset,
                Quaternion.identity);
        }
    }
}
