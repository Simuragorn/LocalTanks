using UnityEngine;

namespace LocalTanks
{
    /// <summary>
    /// Lets the balance between the ground's macro surface and its world-scaled material grain be dialled
    /// in while the battle plays.
    /// <para>
    /// The arena surface carries roads, fields and water; its atlas carries only neutralised grain. The
    /// slider changes the shader blend rather than brightening the whole sprite, so it can reveal detail
    /// without washing the map pale. MaterialPropertyBlock keeps the generated material shared.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GroundAppearanceTuner : MonoBehaviour
    {
        private static readonly int DetailStrengthId = Shader.PropertyToID("_DetailStrength");
        private static readonly int GroundTintId = Shader.PropertyToID("_GroundTint");

        [Tooltip("Share of the world-scaled material grain. Zero is the macro surface alone; one is the " +
                 "strongest detail while roads and fields still come from the macro surface.")]
        [SerializeField, Range(0f, 1f)] private float detailStrength = 0.55f;

        [Tooltip("Ground tint, for correcting a colour cast without touching the bake.")]
        [SerializeField] private Color tint = Color.white;

        private SpriteRenderer target;
        private MaterialPropertyBlock properties;
        private float appliedStrength = -1f;
        private Color appliedTint;

        public float DetailStrength => detailStrength;

        public void Configure(float strength)
        {
            detailStrength = Mathf.Clamp01(strength);
            Apply();
        }

        private void Awake()
        {
            target = GetComponent<SpriteRenderer>();
            Apply();
        }

        private void Update()
        {
            if (!Mathf.Approximately(appliedStrength, detailStrength) || appliedTint != tint)
            {
                Apply();
            }
        }

        /// <summary>Sets the multiplier, as the scene's own inspector or a test would.</summary>
        public void SetDetailStrength(float strength)
        {
            detailStrength = Mathf.Clamp01(strength);
            Apply();
        }

        private void Apply()
        {
            if (target == null) target = GetComponent<SpriteRenderer>();
            if (target == null) return;
            properties ??= new MaterialPropertyBlock();
            appliedStrength = detailStrength;
            appliedTint = tint;
            target.GetPropertyBlock(properties);
            properties.SetFloat(DetailStrengthId, detailStrength);
            properties.SetColor(GroundTintId, tint);
            target.SetPropertyBlock(properties);
        }

        private void OnGUI()
        {
            if (BattleDiagnosticsOverlay.AnyModalVisible) return;

            // Bottom left, away from the speed control on the right and the help list on the left top.
            Rect row = new Rect(12f, Screen.height - 64f, 300f, 22f);
            GUI.Label(new Rect(row.x, row.y, 150f, 20f), $"Детали земли {detailStrength:0.00}");
            float wanted = GUI.HorizontalSlider(
                new Rect(row.x + 152f, row.y + 4f, row.width - 152f, 20f), detailStrength, 0f, 1f);
            if (!Mathf.Approximately(wanted, detailStrength))
            {
                SetDetailStrength(wanted);
            }

            Rect tintRow = new Rect(12f, Screen.height - 40f, 300f, 22f);
            if (GUI.Button(tintRow, "Цвет земли: сброс"))
            {
                tint = Color.white;
                Apply();
            }
        }
    }
}
