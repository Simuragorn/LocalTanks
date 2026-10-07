using UnityEngine;

namespace LocalTanks
{
    [RequireComponent(typeof(TankHealth))]
    public sealed class TankHealthBar : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float width = 72f;
        [SerializeField, Min(1f)] private float height = 8f;
        [SerializeField, Min(0f)] private float worldOffset = 1.45f;
        [SerializeField] private bool useTeamColor;
        [SerializeField] private bool allied = true;

        private TankHealth health;

        public float HealthRatio => health != null && health.MaximumHitPoints > 0
            ? Mathf.Clamp01((float)health.CurrentHitPoints / health.MaximumHitPoints)
            : 0f;
        public Color FillColor => useTeamColor
            ? TeamPalette.ForRelation(allied)
            : GetHealthColor(HealthRatio);

        private void Awake()
        {
            health = GetComponent<TankHealth>();
        }

        public void Configure(float offset)
        {
            worldOffset = Mathf.Max(0f, offset);
            useTeamColor = false;
            if (health == null)
            {
                health = GetComponent<TankHealth>();
            }
        }

        public void Configure(float offset, bool isAllied)
        {
            Configure(offset);
            allied = isAllied;
            useTeamColor = true;
        }

        private void OnGUI()
        {
            Camera camera = Camera.main;
            if (camera == null || health == null)
            {
                return;
            }

            Vector3 screenPoint = camera.WorldToScreenPoint(transform.position + Vector3.up * worldOffset);
            if (screenPoint.z <= 0f)
            {
                return;
            }

            Rect background = new Rect(
                screenPoint.x - width * 0.5f,
                Screen.height - screenPoint.y - height * 0.5f,
                width,
                height);
            Rect fill = new Rect(background.x + 1f, background.y + 1f, (width - 2f) * HealthRatio, height - 2f);

            Color previousColor = GUI.color;
            GUI.color = new Color(0.08f, 0.08f, 0.08f, 0.9f);
            GUI.DrawTexture(background, Texture2D.whiteTexture);
            GUI.color = FillColor;
            GUI.DrawTexture(fill, Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private static Color GetHealthColor(float ratio)
        {
            if (ratio > 0.5f)
            {
                return new Color(0.2f, 0.85f, 0.25f, 0.95f);
            }

            if (ratio > 0.25f)
            {
                return new Color(1f, 0.72f, 0.1f, 0.95f);
            }

            return new Color(0.9f, 0.12f, 0.1f, 0.95f);
        }
    }
}
