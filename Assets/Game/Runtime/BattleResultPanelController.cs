using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace LocalTanks
{
    public enum BattleResultCommand
    {
        None,
        Repeat,
        MainMenu
    }

    public sealed class BattleResultPanelController : MonoBehaviour
    {
        private BattleDirector director;
        private BattleRoster roster;
        private VisualElement overlay;
        private Label title;
        private Label reason;
        private Button repeatButton;
        private Button menuButton;

        public bool IsVisible => overlay != null && !overlay.ClassListContains("hidden");
        public string Title => title?.text ?? string.Empty;

        private void Update()
        {
            if (!IsVisible)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            BattleResultCommand command = ResolveShortcut(
                keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame,
                keyboard.escapeKey.wasPressedThisFrame);
            if (command == BattleResultCommand.MainMenu)
            {
                MainMenu();
            }
            else if (command == BattleResultCommand.Repeat)
            {
                Repeat();
            }
        }

        public static BattleResultCommand ResolveShortcut(bool enterPressed, bool escapePressed)
        {
            if (escapePressed) return BattleResultCommand.MainMenu;
            return enterPressed ? BattleResultCommand.Repeat : BattleResultCommand.None;
        }

        public void Configure(BattleDirector battleDirector, BattleRoster battleRoster, VisualElement root)
        {
            Unsubscribe();
            director = battleDirector;
            roster = battleRoster;
            overlay = root?.Q<VisualElement>("result-overlay");
            title = root?.Q<Label>("result-title");
            reason = root?.Q<Label>("result-reason");
            repeatButton = root?.Q<Button>("result-repeat");
            menuButton = root?.Q<Button>("result-menu");
            if (repeatButton != null) repeatButton.clicked += Repeat;
            if (menuButton != null) menuButton.clicked += MainMenu;
            if (director != null)
            {
                director.MatchEnded += OnMatchEnded;
                if (director.HasEnded) OnMatchEnded(director.Result);
            }
        }

        public void Show(MatchResult result, TeamId playerTeam)
        {
            if (overlay == null || !result.HasEnded) return;
            bool draw = result.IsDraw;
            bool victory = !draw && result.Winner == playerTeam;
            title.text = draw ? "НИЧЬЯ" : victory ? "ПОБЕДА" : "ПОРАЖЕНИЕ";
            title.EnableInClassList("defeat", !draw && !victory);
            title.EnableInClassList("draw", draw);
            reason.text = result.Describe(playerTeam);
            overlay.RemoveFromClassList("hidden");
            overlay.BringToFront();
            ModalUiState.Set(this, true);
            overlay.schedule.Execute(() => repeatButton?.Focus());
        }

        private void OnMatchEnded(MatchResult result)
        {
            Show(result, roster != null ? roster.LocalPlayerTeam : TeamId.TeamA);
        }

        private static void Repeat()
        {
            BattleLaunchConfiguration.RepeatLast();
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        private static void MainMenu()
        {
            BattleLaunchConfiguration.ClearPending();
            SceneManager.LoadScene("MainMenu");
        }

        private void OnDisable()
        {
            ModalUiState.Set(this, false);
            Unsubscribe();
        }

        private void Unsubscribe()
        {
            if (director != null) director.MatchEnded -= OnMatchEnded;
            if (repeatButton != null) repeatButton.clicked -= Repeat;
            if (menuButton != null) menuButton.clicked -= MainMenu;
        }
    }
}
