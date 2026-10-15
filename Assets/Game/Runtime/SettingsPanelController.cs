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
        private VisualElement settingsPanel;
        private VisualElement graphicsPlaceholder;
        private Slider masterSlider;
        private Slider tankSlider;
        private Slider effectsSlider;
        private Slider musicSlider;
        private Slider voiceSlider;
        private Label masterValue;
        private Label tankValue;
        private Label effectsValue;
        private Label musicValue;
        private Label voiceValue;
        private DropdownField crewLanguage;
        private DropdownField crewGender;
        private Button graphicsButton;
        private Button graphicsBackButton;
        private Button resetButton;
        private Button closeButton;

        public bool IsOpen => overlay != null && !overlay.ClassListContains("hidden");
        public bool IsGraphicsPlaceholderOpen => graphicsPlaceholder != null &&
                                                 !graphicsPlaceholder.ClassListContains("hidden");

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
            // Closing the scene with the panel open must not leave the game believing an overlay still
            // owns the pointer.
            ModalUiState.Set(this, false);
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
                if (graphicsPlaceholder != null && !graphicsPlaceholder.ClassListContains("hidden"))
                {
                    ShowGraphics(false);
                    return;
                }

                SetOpen(false);
            }
        }

        public void Configure(StyleSheet styles, bool openAtStart = false)
        {
            styleSheet = styles;
            openOnStart = openAtStart;
            // A caller configures the panel right after adding the component, when the document may already
            // have bound itself and skipped the stylesheet: that silent miss left the panel unstyled.
            ApplyStyleSheet();
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
            // The panel is the one overlay that takes the mouse for dragging, so the tank has to know it
            // is open; otherwise every slider drag fires the gun behind the panel.
            ModalUiState.Set(this, open);
            if (open)
            {
                ShowGraphics(false);
                RefreshLabels();
            }
        }

        public void OpenGraphicsPlaceholder()
        {
            if (IsOpen)
            {
                ShowGraphics(true);
            }
        }

        private void Bind()
        {
            UIDocument source = document != null ? document : GetComponent<UIDocument>();
            if (source == null || source.rootVisualElement == null)
            {
                return;
            }

            ApplyStyleSheet();

            overlay = source.rootVisualElement.Q<VisualElement>("settings-overlay");
            settingsPanel = source.rootVisualElement.Q<VisualElement>("settings-panel");
            graphicsPlaceholder = source.rootVisualElement.Q<VisualElement>("graphics-placeholder");
            masterSlider = source.rootVisualElement.Q<Slider>("slider-master");
            tankSlider = source.rootVisualElement.Q<Slider>("slider-tank");
            effectsSlider = source.rootVisualElement.Q<Slider>("slider-effects");
            musicSlider = source.rootVisualElement.Q<Slider>("slider-music");
            voiceSlider = source.rootVisualElement.Q<Slider>("slider-voice");
            masterValue = source.rootVisualElement.Q<Label>("value-master");
            tankValue = source.rootVisualElement.Q<Label>("value-tank");
            effectsValue = source.rootVisualElement.Q<Label>("value-effects");
            musicValue = source.rootVisualElement.Q<Label>("value-music");
            voiceValue = source.rootVisualElement.Q<Label>("value-voice");
            crewLanguage = source.rootVisualElement.Q<DropdownField>("crew-language");
            crewGender = source.rootVisualElement.Q<DropdownField>("crew-gender");
            graphicsButton = source.rootVisualElement.Q<Button>("button-graphics");
            graphicsBackButton = source.rootVisualElement.Q<Button>("button-graphics-back");
            resetButton = source.rootVisualElement.Q<Button>("button-reset");
            closeButton = source.rootVisualElement.Q<Button>("button-close");

            if (overlay == null || masterSlider == null || tankSlider == null || effectsSlider == null ||
                musicSlider == null || voiceSlider == null || crewLanguage == null || crewGender == null)
            {
                Debug.LogError("Settings panel markup is incomplete; audio and crew controls are required.");
                return;
            }

            masterSlider.value = PlayerSettingsStore.MasterVolume * 100f;
            tankSlider.value = PlayerSettingsStore.TankVolume * 100f;
            effectsSlider.value = PlayerSettingsStore.EffectsVolume * 100f;
            musicSlider.value = PlayerSettingsStore.MusicVolume * 100f;
            voiceSlider.value = PlayerSettingsStore.VoiceVolume * 100f;
            crewLanguage.choices = new System.Collections.Generic.List<string> { "Национальная", "Русская" };
            crewGender.choices = new System.Collections.Generic.List<string> { "Мужская", "Женская" };
            RefreshCrewChoices();
            masterSlider.RegisterValueChangedCallback(OnMasterChanged);
            tankSlider.RegisterValueChangedCallback(OnTankChanged);
            effectsSlider.RegisterValueChangedCallback(OnEffectsChanged);
            musicSlider.RegisterValueChangedCallback(OnMusicChanged);
            voiceSlider.RegisterValueChangedCallback(OnVoiceChanged);
            crewLanguage.RegisterValueChangedCallback(OnCrewLanguageChanged);
            crewGender.RegisterValueChangedCallback(OnCrewGenderChanged);
            if (graphicsButton != null)
            {
                graphicsButton.clicked += OnGraphicsClicked;
            }

            if (graphicsBackButton != null)
            {
                graphicsBackButton.clicked += OnGraphicsBackClicked;
            }
            if (resetButton != null)
            {
                resetButton.clicked += OnResetClicked;
            }

            if (closeButton != null)
            {
                closeButton.clicked += OnCloseClicked;
            }

            overlay.EnableInClassList("hidden", !openOnStart);
            ShowGraphics(false);
            RefreshLabels();
        }

        /// <summary>
        /// Adds the panel's stylesheet to the document root, whichever order the caller used. A stylesheet
        /// that arrives after the document bound itself must still apply: silently missing it left the panel
        /// with the default look, where rows overlap because nothing reserves their height.
        /// </summary>
        private void ApplyStyleSheet()
        {
            UIDocument source = document != null ? document : GetComponent<UIDocument>();
            if (styleSheet == null || source == null || source.rootVisualElement == null)
            {
                return;
            }

            if (!source.rootVisualElement.styleSheets.Contains(styleSheet))
            {
                source.rootVisualElement.styleSheets.Add(styleSheet);
            }
        }

        private void Unbind()
        {
            if (masterSlider != null) masterSlider.UnregisterValueChangedCallback(OnMasterChanged);
            if (tankSlider != null) tankSlider.UnregisterValueChangedCallback(OnTankChanged);
            if (effectsSlider != null) effectsSlider.UnregisterValueChangedCallback(OnEffectsChanged);
            if (musicSlider != null) musicSlider.UnregisterValueChangedCallback(OnMusicChanged);
            if (voiceSlider != null) voiceSlider.UnregisterValueChangedCallback(OnVoiceChanged);
            if (crewLanguage != null) crewLanguage.UnregisterValueChangedCallback(OnCrewLanguageChanged);
            if (crewGender != null) crewGender.UnregisterValueChangedCallback(OnCrewGenderChanged);
            if (graphicsButton != null) graphicsButton.clicked -= OnGraphicsClicked;
            if (graphicsBackButton != null) graphicsBackButton.clicked -= OnGraphicsBackClicked;
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

        private void OnMusicChanged(ChangeEvent<float> change)
        {
            PlayerSettingsStore.MusicVolume = change.newValue / 100f;
            RefreshLabels();
        }

        private void OnVoiceChanged(ChangeEvent<float> change)
        {
            PlayerSettingsStore.VoiceVolume = change.newValue / 100f;
            RefreshLabels();
        }

        private void OnCrewLanguageChanged(ChangeEvent<string> change)
        {
            PlayerSettingsStore.CrewLanguage = change.newValue == "Русская"
                ? CrewLanguageMode.Russian
                : CrewLanguageMode.National;
        }

        private void OnCrewGenderChanged(ChangeEvent<string> change)
        {
            PlayerSettingsStore.CrewGender = change.newValue == "Женская"
                ? CrewVoiceGender.Female
                : CrewVoiceGender.Male;
        }

        private void OnResetClicked()
        {
            PlayerSettingsStore.ResetToDefaults();
            if (masterSlider != null) masterSlider.value = PlayerSettingsStore.MasterVolume * 100f;
            if (tankSlider != null) tankSlider.value = PlayerSettingsStore.TankVolume * 100f;
            if (effectsSlider != null) effectsSlider.value = PlayerSettingsStore.EffectsVolume * 100f;
            if (musicSlider != null) musicSlider.value = PlayerSettingsStore.MusicVolume * 100f;
            if (voiceSlider != null) voiceSlider.value = PlayerSettingsStore.VoiceVolume * 100f;
            RefreshCrewChoices();
            RefreshLabels();
        }

        private void OnGraphicsClicked()
        {
            OpenGraphicsPlaceholder();
        }

        private void OnGraphicsBackClicked()
        {
            ShowGraphics(false);
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
            if (musicValue != null) musicValue.text = $"{Mathf.RoundToInt(PlayerSettingsStore.MusicVolume * 100f)}%";
            if (voiceValue != null) voiceValue.text = $"{Mathf.RoundToInt(PlayerSettingsStore.VoiceVolume * 100f)}%";
        }

        private void RefreshCrewChoices()
        {
            if (crewLanguage != null)
            {
                crewLanguage.SetValueWithoutNotify(PlayerSettingsStore.CrewLanguage == CrewLanguageMode.Russian
                    ? "Русская"
                    : "Национальная");
            }

            if (crewGender != null)
            {
                crewGender.SetValueWithoutNotify(PlayerSettingsStore.CrewGender == CrewVoiceGender.Female
                    ? "Женская"
                    : "Мужская");
            }
        }

        private void ShowGraphics(bool show)
        {
            settingsPanel?.EnableInClassList("hidden", show);
            graphicsPlaceholder?.EnableInClassList("hidden", !show);
        }
    }
}
