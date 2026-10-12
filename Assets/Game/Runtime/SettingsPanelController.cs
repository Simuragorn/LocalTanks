using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace LocalTanks
{
    /// <summary>
    /// Settings overlay shared by every battle scene. It binds one slider per audio channel to
    /// <see cref="PlayerSettingsStore"/>, so a change is stored immediately and applies to the sounds
    /// that are already playing. `Esc` toggles the panel; the battle HUD can call
    /// <see cref="Toggle"/> as well.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class SettingsPanelController : MonoBehaviour
    {
        [SerializeField] private bool openOnStart;
        [SerializeField] private StyleSheet styleSheet;

        private UIDocument document;
        private VisualElement overlay;
        private Slider masterSlider;
        private Slider tankSlider;
        private Slider effectsSlider;
        private Label masterValue;
        private Label tankValue;
        private Label effectsValue;
        private Button resetButton;
        private Button closeButton;

        public bool IsOpen => overlay != null && !overlay.ClassListContains("hidden");

        private void Awake()
        {
            document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            Bind();
        }

        private void OnDisable()
        {
            Unbind();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            // F7 opens and closes the panel. Escape only closes it, and only while it is open, so the
            // key keeps its existing meaning of closing the top diagnostic window the rest of the time.
            if (keyboard.f7Key.wasPressedThisFrame)
            {
                Toggle();
                return;
            }

            if (IsOpen && keyboard.escapeKey.wasPressedThisFrame)
            {
                SetOpen(false);
            }
        }

        public void Configure(StyleSheet styles, bool openAtStart = false)
        {
            styleSheet = styles;
            openOnStart = openAtStart;
        }

        public void Toggle()
        {
            if (overlay == null)
            {
                Bind();
                if (overlay == null)
                {
                    return;
                }
            }

            SetOpen(!IsOpen);
        }

        public void SetOpen(bool open)
        {
            if (overlay == null)
            {
                return;
            }

            overlay.EnableInClassList("hidden", !open);
            if (open)
            {
                RefreshLabels();
            }
        }

        private void Bind()
        {
            UIDocument source = document != null ? document : GetComponent<UIDocument>();
            if (source == null || source.rootVisualElement == null)
            {
                return;
            }

            if (styleSheet != null && !source.rootVisualElement.styleSheets.Contains(styleSheet))
            {
                source.rootVisualElement.styleSheets.Add(styleSheet);
            }

            overlay = source.rootVisualElement.Q<VisualElement>("settings-overlay");
            masterSlider = source.rootVisualElement.Q<Slider>("slider-master");
            tankSlider = source.rootVisualElement.Q<Slider>("slider-tank");
            effectsSlider = source.rootVisualElement.Q<Slider>("slider-effects");
            masterValue = source.rootVisualElement.Q<Label>("value-master");
            tankValue = source.rootVisualElement.Q<Label>("value-tank");
            effectsValue = source.rootVisualElement.Q<Label>("value-effects");
            resetButton = source.rootVisualElement.Q<Button>("button-reset");
            closeButton = source.rootVisualElement.Q<Button>("button-close");

            if (overlay == null || masterSlider == null || tankSlider == null || effectsSlider == null)
            {
                Debug.LogError("Settings panel markup is incomplete; the overlay needs three named sliders.");
                return;
            }

            masterSlider.value = PlayerSettingsStore.MasterVolume * 100f;
            tankSlider.value = PlayerSettingsStore.TankVolume * 100f;
            effectsSlider.value = PlayerSettingsStore.EffectsVolume * 100f;
            masterSlider.RegisterValueChangedCallback(OnMasterChanged);
            tankSlider.RegisterValueChangedCallback(OnTankChanged);
            effectsSlider.RegisterValueChangedCallback(OnEffectsChanged);
            if (resetButton != null)
            {
                resetButton.clicked += OnResetClicked;
            }

            if (closeButton != null)
            {
                closeButton.clicked += OnCloseClicked;
            }

            overlay.EnableInClassList("hidden", !openOnStart);
            RefreshLabels();
        }

        private void Unbind()
        {
            if (masterSlider != null) masterSlider.UnregisterValueChangedCallback(OnMasterChanged);
            if (tankSlider != null) tankSlider.UnregisterValueChangedCallback(OnTankChanged);
            if (effectsSlider != null) effectsSlider.UnregisterValueChangedCallback(OnEffectsChanged);
            if (resetButton != null) resetButton.clicked -= OnResetClicked;
            if (closeButton != null) closeButton.clicked -= OnCloseClicked;
        }

        private void OnMasterChanged(ChangeEvent<float> change)
        {
            PlayerSettingsStore.MasterVolume = change.newValue / 100f;
            RefreshLabels();
        }

        private void OnTankChanged(ChangeEvent<float> change)
        {
            PlayerSettingsStore.TankVolume = change.newValue / 100f;
            RefreshLabels();
        }

        private void OnEffectsChanged(ChangeEvent<float> change)
        {
            PlayerSettingsStore.EffectsVolume = change.newValue / 100f;
            RefreshLabels();
        }

        private void OnResetClicked()
        {
            PlayerSettingsStore.ResetToDefaults();
            if (masterSlider != null) masterSlider.value = PlayerSettingsStore.MasterVolume * 100f;
            if (tankSlider != null) tankSlider.value = PlayerSettingsStore.TankVolume * 100f;
            if (effectsSlider != null) effectsSlider.value = PlayerSettingsStore.EffectsVolume * 100f;
            RefreshLabels();
        }

        private void OnCloseClicked()
        {
            SetOpen(false);
        }

        private void RefreshLabels()
        {
            if (masterValue != null) masterValue.text = $"{Mathf.RoundToInt(PlayerSettingsStore.MasterVolume * 100f)}%";
            if (tankValue != null) tankValue.text = $"{Mathf.RoundToInt(PlayerSettingsStore.TankVolume * 100f)}%";
            if (effectsValue != null) effectsValue.text = $"{Mathf.RoundToInt(PlayerSettingsStore.EffectsVolume * 100f)}%";
        }
    }
}
