using UnityEngine;

namespace LocalTanks
{
    /// <summary>
    /// Lets the balance between the ground's shape and its grain be dialled in from the scene, while the
    /// battle plays.
    /// <para>
    /// The ground is one baked texture, so its content cannot be changed without running the bake again. What
    /// can be changed cheaply is how strongly that texture is tinted, and that turns out to be the control
    /// worth having: the arena's surface image carries roads, fields and water at about five pixels per metre
    /// and the material tiles carry grain at about eighty, so raising the tint makes the ground read as
    /// detailed and lowering it makes the map's own shapes clearer. Which of those is right is a judgement
    /// about how the arena should look, so it belongs to whoever is looking at it rather than to a constant
    /// in a script.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GroundAppearanceTuner : MonoBehaviour
    {
        [Tooltip("Multiplier on the ground texture's colour. Above one strengthens the grain, below one lets " +
                 "the surface image's shapes through. It cannot add detail that is not baked in.")]
        [SerializeField, Range(0.5f, 1.6f)] private float detailStrength = 1f;

        [Tooltip("Ground tint, for correcting a colour cast without touching the bake.")]
        [SerializeField] private Color tint = Color.white;

        private SpriteRenderer target;
        private float appliedStrength = -1f;
        private Color appliedTint;

        public float DetailStrength => detailStrength;

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
            detailStrength = Mathf.Clamp(strength, 0.5f, 1.6f);
            Apply();
        }

        private void Apply()
        {
            if (target == null) return;
            appliedStrength = detailStrength;
            appliedTint = tint;
            target.color = new Color(
                Mathf.Clamp01(tint.r * detailStrength),
                Mathf.Clamp01(tint.g * detailStrength),
                Mathf.Clamp01(tint.b * detailStrength),
                1f);
        }

        private void OnGUI()
        {
            if (BattleDiagnosticsOverlay.AnyModalVisible) return;

            // Bottom left, away from the speed control on the right and the help list on the left top.
            Rect row = new Rect(12f, Screen.height - 64f, 260f, 22f);
            GUI.Label(new Rect(row.x, row.y, 116f, 20f), $"Земля ×{detailStrength:0.00}");
            float wanted = GUI.HorizontalSlider(
                new Rect(row.x + 118f, row.y + 4f, row.width - 118f, 20f), detailStrength, 0.5f, 1.6f);
            if (!Mathf.Approximately(wanted, detailStrength))
            {
                SetDetailStrength(wanted);
            }

            Rect tintRow = new Rect(12f, Screen.height - 40f, 260f, 22f);
            if (GUI.Button(tintRow, "Цвет земли: сброс"))
            {
                tint = Color.white;
                Apply();
            }
        }
    }
}
