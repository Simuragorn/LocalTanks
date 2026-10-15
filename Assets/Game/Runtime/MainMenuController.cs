using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace LocalTanks
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class MainMenuController : MonoBehaviour
    {
        [SerializeField] private CombatDatabase combatDatabase;
        [SerializeField] private StyleSheet[] styleSheets;

        private readonly Dictionary<string, string> tankIdsByLabel = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> tankLabelsById = new Dictionary<string, string>(StringComparer.Ordinal);
        private UIDocument document;
        private DropdownField mapChoice;
        private TeamEditor teamA;
        private TeamEditor teamB;
        private Button playButton;
        private Button settingsButton;
        private Label validationLabel;

        public IReadOnlyList<string> SelectedTeamATanks => teamA?.SelectedTankIds ?? Array.Empty<string>();
        public IReadOnlyList<string> SelectedTeamBTanks => teamB?.SelectedTankIds ?? Array.Empty<string>();

        private void Awake()
        {
            document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            Invoke(nameof(Bind), 0f);
        }

        private void OnDisable()
        {
            if (playButton != null) playButton.clicked -= StartBattle;
            if (settingsButton != null) settingsButton.clicked -= OpenSettings;
            if (mapChoice != null) mapChoice.UnregisterValueChangedCallback(OnMapChanged);
        }

        public void Configure(CombatDatabase database, StyleSheet[] styles)
        {
            combatDatabase = database;
            styleSheets = styles;
        }

        private void Bind()
        {
            VisualElement root = document != null ? document.rootVisualElement : null;
            if (root == null) return;
            // The settings template container fills the screen even while its child overlay is hidden.
            // Ignoring the container itself lets controls below it receive pointer events; visible modal
            // children remain pickable and continue to own input while the settings panel is open.
            VisualElement settingsInstance = root.Q<VisualElement>("settings-instance");
            if (settingsInstance != null) settingsInstance.pickingMode = PickingMode.Ignore;
            foreach (StyleSheet sheet in styleSheets ?? Array.Empty<StyleSheet>())
            {
                if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            }

            TankDefinition[] tanks = (combatDatabase?.tanks ?? Array.Empty<TankDefinition>())
                .Where(item => item != null && item.availableInGame)
                .OrderBy(item => item.tier)
                .ThenBy(item => item.displayName, StringComparer.Ordinal)
                .ToArray();
            tankIdsByLabel.Clear();
            tankLabelsById.Clear();
            foreach (TankDefinition tank in tanks)
            {
                string label = $"{tank.displayName} · {NationName(tank.nation)} · {tank.tier} ур.";
                tankIdsByLabel[label] = tank.id;
                tankLabelsById[tank.id] = label;
            }

            List<string> tankLabels = tankIdsByLabel.Keys.ToList();
            mapChoice = root.Q<DropdownField>("map-choice");
            mapChoice.choices = BattleMapCatalog.Playable.Select(item => item.DisplayName).ToList();
            mapChoice.SetValueWithoutNotify(mapChoice.choices.FirstOrDefault() ?? string.Empty);
            teamA = new TeamEditor(root, "team-a", tankLabels, tankIdsByLabel, true, SaveCurrentSetup);
            teamB = new TeamEditor(root, "team-b", tankLabels, tankIdsByLabel, false, SaveCurrentSetup);
            RestoreSavedSetup();
            UiDropdownPresenter.Apply(mapChoice);
            playButton = root.Q<Button>("button-play");
            settingsButton = root.Q<Button>("button-settings");
            validationLabel = root.Q<Label>("launch-validation");
            if (playButton != null) playButton.clicked += StartBattle;
            if (settingsButton != null) settingsButton.clicked += OpenSettings;
            if (mapChoice != null) mapChoice.RegisterValueChangedCallback(OnMapChanged);
            SetValidation(tankLabels.Count == 0 ? "Нет доступной техники." : string.Empty);
            SaveCurrentSetup();
        }

        public void StartBattle()
        {
            BattleMapOption selectedMap = BattleMapCatalog.FindByDisplayName(mapChoice?.value);
            if (string.IsNullOrWhiteSpace(selectedMap.SceneName))
            {
                SetValidation("Выберите карту.");
                return;
            }

            try
            {
                SaveCurrentSetup();
                BattleLaunchConfiguration.Set(selectedMap.SceneName, SelectedTeamATanks, SelectedTeamBTanks);
                SceneManager.LoadScene(selectedMap.SceneName);
            }
            catch (Exception exception)
            {
                SetValidation(exception.Message);
            }
        }

        private void OpenSettings()
        {
            GetComponent<SettingsPanelController>()?.SetOpen(true);
        }

        private void RestoreSavedSetup()
        {
            BattleLaunchSelection saved = BattleSetupPreferencesStore.Load();
            if (saved == null) return;

            BattleMapOption savedMap = Array.Find(
                BattleMapCatalog.Playable,
                item => string.Equals(item.SceneName, saved.sceneName, StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(savedMap.SceneName))
                mapChoice?.SetValueWithoutNotify(savedMap.DisplayName);

            teamA?.Restore(saved.teamATanks, tankLabelsById);
            teamB?.Restore(saved.teamBTanks, tankLabelsById);
        }

        private void OnMapChanged(ChangeEvent<string> change)
        {
            SaveCurrentSetup();
        }

        private void SaveCurrentSetup()
        {
            BattleMapOption selectedMap = BattleMapCatalog.FindByDisplayName(mapChoice?.value);
            IReadOnlyList<string> selectedTeamA = SelectedTeamATanks;
            IReadOnlyList<string> selectedTeamB = SelectedTeamBTanks;
            if (string.IsNullOrWhiteSpace(selectedMap.SceneName) ||
                selectedTeamA.Count == 0 || selectedTeamB.Count == 0 ||
                selectedTeamA.Any(id => string.IsNullOrWhiteSpace(id)) ||
                selectedTeamB.Any(id => string.IsNullOrWhiteSpace(id)))
            {
                return;
            }

            BattleSetupPreferencesStore.Save(selectedMap.SceneName, selectedTeamA, selectedTeamB);
        }

        private void SetValidation(string message)
        {
            if (validationLabel == null) return;
            validationLabel.text = message ?? string.Empty;
            validationLabel.EnableInClassList("hidden", string.IsNullOrEmpty(message));
        }

        private static string NationName(TankNation nation) => nation switch
        {
            TankNation.Germany => "Германия",
            TankNation.USSR => "СССР",
            TankNation.USA => "США",
            _ => nation.ToString()
        };

        private sealed class TeamEditor
        {
            private readonly VisualElement rows;
            private readonly List<string> tankLabels;
            private readonly IReadOnlyDictionary<string, string> tankIdsByLabel;
            private readonly bool playerTeam;
            private readonly List<DropdownField> slots = new List<DropdownField>();
            private readonly SliderInt count;
            private readonly DropdownField wholeTeam;
            private readonly Action selectionChanged;

            public TeamEditor(
                VisualElement root,
                string prefix,
                List<string> labels,
                IReadOnlyDictionary<string, string> idsByLabel,
                bool isPlayerTeam,
                Action onSelectionChanged)
            {
                tankLabels = labels;
                tankIdsByLabel = idsByLabel;
                playerTeam = isPlayerTeam;
                selectionChanged = onSelectionChanged;
                ScrollView scrollView = root.Q<ScrollView>($"{prefix}-rows");
                rows = scrollView?.contentContainer;
                count = root.Q<SliderInt>($"{prefix}-count");
                wholeTeam = root.Q<DropdownField>($"{prefix}-whole");
                if (count != null)
                {
                    count.lowValue = 1;
                    count.highValue = BattleScenario.TeamSize;
                    count.SetValueWithoutNotify(BattleScenario.TeamSize);
                    count.RegisterValueChangedCallback(change =>
                    {
                        Rebuild(change.newValue);
                        selectionChanged?.Invoke();
                    });
                }

                if (wholeTeam != null)
                {
                    wholeTeam.choices = new[] { "Выбрать для всей команды" }.Concat(tankLabels).ToList();
                    wholeTeam.SetValueWithoutNotify(wholeTeam.choices[0]);
                    UiDropdownPresenter.Apply(wholeTeam);
                    wholeTeam.RegisterValueChangedCallback(change => ApplyToWholeTeam(change.newValue));
                }

                Rebuild(BattleScenario.TeamSize);
            }

            public IReadOnlyList<string> SelectedTankIds => slots
                .Select(slot => tankIdsByLabel.TryGetValue(slot.value, out string id) ? id : string.Empty)
                .ToArray();

            public void Restore(
                IReadOnlyList<string> tankIds,
                IReadOnlyDictionary<string, string> labelsById)
            {
                if (tankIds == null || tankIds.Count == 0) return;
                int desiredCount = Mathf.Clamp(tankIds.Count, 1, BattleScenario.TeamSize);
                string[] restoredLabels = tankIds
                    .Take(desiredCount)
                    .Select(id => id != null && labelsById.TryGetValue(id, out string label) ? label : null)
                    .ToArray();
                count?.SetValueWithoutNotify(desiredCount);
                Rebuild(desiredCount, restoredLabels);
            }

            private void Rebuild(int desiredCount, IReadOnlyList<string> requested = null)
            {
                if (rows == null) return;
                string[] previous = slots.Select(item => item.value).ToArray();
                rows.Clear();
                slots.Clear();
                for (int index = 0; index < desiredCount; index++)
                {
                    VisualElement row = new VisualElement();
                    row.AddToClassList("team-slot");
                    Label label = new Label(playerTeam && index == 0 ? "Игрок" : $"Бот {index + (playerTeam ? 0 : 1)}");
                    label.AddToClassList("team-slot-label");
                    DropdownField dropdown = new DropdownField { choices = new List<string>(tankLabels) };
                    dropdown.AddToClassList("team-slot-dropdown");
                    dropdown.AddToClassList("lt-dropdown");
                    string selected = index < (requested?.Count ?? 0) ? requested[index] : null;
                    if (string.IsNullOrEmpty(selected) || !tankLabels.Contains(selected))
                        selected = index < previous.Length ? previous[index] : tankLabels.FirstOrDefault();
                    if (string.IsNullOrEmpty(selected) || !tankLabels.Contains(selected))
                        selected = tankLabels.FirstOrDefault();
                    dropdown.SetValueWithoutNotify(selected ?? string.Empty);
                    dropdown.RegisterValueChangedCallback(_ => selectionChanged?.Invoke());
                    row.Add(label);
                    row.Add(dropdown);
                    UiDropdownPresenter.Apply(dropdown);
                    rows.Add(row);
                    slots.Add(dropdown);
                }
            }

            private void ApplyToWholeTeam(string label)
            {
                if (!tankIdsByLabel.ContainsKey(label)) return;
                foreach (DropdownField slot in slots) slot.SetValueWithoutNotify(label);
                selectionChanged?.Invoke();
            }
        }
    }
}
