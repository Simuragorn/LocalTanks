using UnityEngine;

namespace LocalTanks
{
    [RequireComponent(typeof(TankHealth))]
    public sealed class TankHealthBar : MonoBehaviour
    {
        public const float DefaultWidth = 94f;
        public const float DefaultHeight = 18f;

        [SerializeField, Min(1f)] private float width = DefaultWidth;
        [SerializeField, Min(1f)] private float height = DefaultHeight;
        [SerializeField, Min(0f)] private float worldOffset = 1.45f;
        [SerializeField] private bool useTeamColor;
        [SerializeField] private bool allied = true;

        private TankHealth health;
        private GUIStyle healthTextStyle;
        private bool externalPresentation;

        public float HealthRatio => health != null && health.MaximumHitPoints > 0
            ? Mathf.Clamp01((float)health.CurrentHitPoints / health.MaximumHitPoints)
            : 0f;
        public Color FillColor => useTeamColor
            ? TeamPalette.ForRelation(allied)
            : GetHealthColor(HealthRatio);
        public string HealthText => health != null
            ? FormatHitPoints(health.CurrentHitPoints, health.MaximumHitPoints)
            : string.Empty;
        public float WorldOffset => worldOffset;
        public float Width => width;
        public float Height => height;

        private void Awake()
        {
            health = GetComponent<TankHealth>();
        }

        public void Configure(float offset)
        {
            worldOffset = Mathf.Max(0f, offset);
            width = DefaultWidth;
            height = DefaultHeight;
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

        public void SetExternalPresentation(bool enabled)
        {
            externalPresentation = enabled;
        }

        private void OnGUI()
        {
            Camera camera = Camera.main;
            if (externalPresentation || camera == null || health == null || health.IsDestroyed)
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

            GUIStyle style = HealthTextStyle();
            Color textColor = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.82f);
            GUI.Label(new Rect(background.x - 1f, background.y, background.width, background.height), HealthText, style);
            GUI.Label(new Rect(background.x + 1f, background.y, background.width, background.height), HealthText, style);
            GUI.Label(new Rect(background.x, background.y - 1f, background.width, background.height), HealthText, style);
            GUI.Label(new Rect(background.x, background.y + 1f, background.width, background.height), HealthText, style);
            style.normal.textColor = textColor;
            GUI.Label(background, HealthText, style);
        }

        private GUIStyle HealthTextStyle()
        {
            if (healthTextStyle == null)
            {
                healthTextStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    clipping = TextClipping.Clip
                };
                healthTextStyle.normal.textColor = new Color(0.96f, 0.96f, 0.91f, 1f);
            }

            return healthTextStyle;
        }

        public static string FormatHitPoints(int current, int maximum)
        {
            return $"{Mathf.Max(0, current)}/{Mathf.Max(0, maximum)}";
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
