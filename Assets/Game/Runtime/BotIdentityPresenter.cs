using UnityEngine;

namespace LocalTanks
{
    [RequireComponent(typeof(BotIdentityComponent), typeof(TeamMember), typeof(TankHealth))]
    public sealed class BotIdentityPresenter : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float worldOffset = 2.08f;
        [SerializeField, Min(80f)] private float labelWidth = 180f;

        private BotIdentityComponent identityComponent;
        private TeamMember member;
        private TankHealth health;
        private GUIStyle nicknameStyle;
        private GUIStyle vehicleStyle;

        public string Nickname => identityComponent?.Identity?.nickname ?? string.Empty;
        public string VehicleName => member?.Definition != null ? member.Definition.displayName : string.Empty;
        public bool ShouldRender => identityComponent?.Identity != null && health != null && !health.IsDestroyed &&
                                    (member?.VisibilityPresenter == null || member.VisibilityPresenter.IsVisible);

        private void Awake()
        {
            ResolveReferences();
        }

        public void Configure(float offset = 2.08f)
        {
            worldOffset = Mathf.Max(0f, offset);
            ResolveReferences();
        }

        private void OnGUI()
        {
            if (!ShouldRender || Camera.main == null) return;
            EnsureStyles();
            Vector3 screen = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * worldOffset);
            if (screen.z <= 0f) return;
            float x = screen.x - labelWidth * 0.5f;
            float y = Screen.height - screen.y - 15f;
            Color teamColor = TeamPalette.ForRelation(member.Team == TeamId.TeamA);
            nicknameStyle.normal.textColor = teamColor;
            vehicleStyle.normal.textColor = Color.Lerp(teamColor, TeamPalette.Neutral, 0.45f);
            GUI.Label(new Rect(x, y, labelWidth, 18f), Nickname, nicknameStyle);
            GUI.Label(new Rect(x, y + 15f, labelWidth, 16f), VehicleName, vehicleStyle);
        }

        private void ResolveReferences()
        {
            if (identityComponent == null) identityComponent = GetComponent<BotIdentityComponent>();
            if (member == null) member = GetComponent<TeamMember>();
            if (health == null) health = GetComponent<TankHealth>();
        }

        private void EnsureStyles()
        {
            if (nicknameStyle != null) return;
            nicknameStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Clip
            };
            vehicleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                clipping = TextClipping.Clip
            };
        }
    }
}
