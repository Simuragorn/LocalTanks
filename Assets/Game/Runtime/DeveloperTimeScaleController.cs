using UnityEngine;
using UnityEngine.InputSystem;

namespace LocalTanks
{
    /// <summary>
    /// Speeds a played battle up for observation, cycling through fixed multipliers and offering a slider.
    /// <para>
    /// This is the batch sweep clock's idea, for a person rather than a script, and it takes the same
    /// care: the physics step is never changed. Raising <see cref="Time.timeScale"/> alone advances the
    /// simulation faster while frames still arrive at the display's pace, which is why the earlier version
    /// stopped at three times - beyond that a battle looks like a slideshow. Raising
    /// <see cref="Time.captureDeltaTime"/> as well makes the engine run more fixed steps per frame instead
    /// of waiting for a monitor, so a higher multiplier stays smooth and the trajectories stay the ones the
    /// real game produces.
    /// </para>
    /// </summary>
    public sealed class DeveloperTimeScaleController : MonoBehaviour
    {
        private const float NormalScale = 1f;

        /// <summary>Multipliers the key cycles through. Ten is fast enough to watch an approach finish.</summary>
        private static readonly float[] Steps = { 1f, 3f, 10f };

        /// <summary>
        /// Ceiling on captured delta. Past roughly a tenth of a second Unity stops trying to catch up and
        /// runs no fixed steps, so a larger request would silently slow the battle down instead.
        /// </summary>
        private const float MaximumCaptureDelta = 0.1f;

        [SerializeField, Range(1f, 10f)] private float sliderScale = 1f;

        private int stepIndex;

        public bool IsAccelerated { get; private set; }
        public float CurrentScale => Time.timeScale;

        private void OnEnable()
        {
            ApplyScale(NormalScale);
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f5Key.wasPressedThisFrame)
            {
                Toggle();
            }
        }

        private void OnDisable()
        {
            IsAccelerated = false;
            ApplyScale(NormalScale);
        }

        /// <summary>Advances to the next multiplier in the cycle.</summary>
        public void Toggle()
        {
            stepIndex = (stepIndex + 1) % Steps.Length;
            SetScale(Steps[stepIndex]);
        }

        public void SetAccelerated(bool accelerated)
        {
            SetScale(accelerated ? Steps[1] : NormalScale);
        }

        /// <summary>Applies an arbitrary multiplier, which is what the slider uses.</summary>
        public void SetScale(float multiplier)
        {
            float clamped = Mathf.Clamp(multiplier, NormalScale, Steps[Steps.Length - 1]);
            sliderScale = clamped;
            IsAccelerated = clamped > NormalScale;
            ApplyScale(clamped);
        }

        private void OnGUI()
        {
            if (BattleDiagnosticsOverlay.AnyModalVisible)
            {
                ModalUiState.ClearPointerRegion(this);
                return;
            }

            Rect button = new Rect(Screen.width - 262f, Screen.height - 112f, 250f, 28f);
            Rect sliderRow = new Rect(Screen.width - 262f, Screen.height - 140f, 250f, 24f);
            ModalUiState.SetPointerRegion(this, LocalTanksGuiTheme.Union(button, sliderRow));
            string label = IsAccelerated
                ? $"Скорость ×{CurrentScale:0.#} — вернуть ×1 (F5)"
                : "Ускорить время (F5)";
            if (LocalTanksGuiTheme.Button(button, label))
            {
                Toggle();
            }

            GUI.Label(new Rect(sliderRow.x, sliderRow.y, 58f, 20f), $"×{sliderScale:0.#}",
                LocalTanksGuiTheme.Caption);
            float wanted = LocalTanksGuiTheme.HorizontalSlider(
                new Rect(sliderRow.x + 60f, sliderRow.y + 4f, sliderRow.width - 60f, 20f),
                sliderScale, NormalScale, Steps[Steps.Length - 1]);
            if (!Mathf.Approximately(wanted, sliderScale))
            {
                SetScale(wanted);
            }
        }

        private void OnDestroy()
        {
            ModalUiState.ClearPointerRegion(this);
        }

        /// <summary>
        /// Applies a multiplier without touching the physics step.
        /// <para>
        /// At normal speed the captured delta is switched off rather than set to the frame time. Asking the
        /// engine to capture a fixed delta makes every frame simulate exactly that much, regardless of how
        /// long the frame really took, so on anything other than 50 frames per second the battle runs at the
        /// wrong speed - that is what made tanks appear faster after this control was added. Above normal
        /// speed the delta is raised deliberately, and capped, with the rest of the speedup coming from the
        /// time scale, which never changes step size.
        /// </para>
        /// </summary>
        private static void ApplyScale(float scale)
        {
            float multiplier = Mathf.Max(NormalScale, scale);
            Time.timeScale = multiplier;
            Time.captureDeltaTime = multiplier <= NormalScale
                ? 0f
                : Mathf.Min(Time.fixedDeltaTime * multiplier, MaximumCaptureDelta);
        }
    }
}
