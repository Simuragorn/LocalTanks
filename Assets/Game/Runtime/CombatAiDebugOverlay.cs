using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LocalTanks
{
    public sealed class CombatAiDebugOverlay : MonoBehaviour
    {
        [SerializeField] private bool visible;
        private CombatTankAI selected;
        private int selectedIndex = -1;

        public bool Visible => visible;
        public CombatTankAI Selected => selected;

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f4Key.wasPressedThisFrame)
            {
                visible = !visible;
                if (visible) SelectNearestBot();
            }

            if (!visible)
            {
                return;
            }

            if (selected == null || !selected.isActiveAndEnabled || !IsAlive(selected))
            {
                SelectNearestBot();
            }

            if (keyboard != null && keyboard.tabKey.wasPressedThisFrame)
            {
                int direction = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed ? -1 : 1;
                SelectRelativeBot(direction);
            }

            NavigationAgent navigation = selected != null ? selected.GetComponent<NavigationAgent>() : null;
            if (navigation != null)
            {
                var path = navigation.CurrentPath;
                for (int index = 1; index < path.Count; index++)
                {
                    Debug.DrawLine(path[index - 1], path[index], Color.yellow);
                }

                if (selected.HasRememberedTarget)
                {
                    Debug.DrawLine(selected.transform.position, selected.LastKnownTargetPosition, Color.magenta);
                }
            }
        }

        public void SetVisible(bool value)
        {
            visible = value;
            if (visible) SelectNearestBot();
        }

        public void SelectRelativeBot(int direction)
        {
            List<CombatTankAI> bots = GetLivingBots();
            if (bots.Count == 0)
            {
                selected = null;
                selectedIndex = -1;
                return;
            }

            int current = bots.IndexOf(selected);
            if (current < 0) current = selectedIndex >= 0 ? Mathf.Min(selectedIndex, bots.Count - 1) : 0;
            int step = direction < 0 ? -1 : 1;
            selectedIndex = (current + step + bots.Count) % bots.Count;
            selected = bots[selectedIndex];
        }

        private void SelectNearestBot()
        {
            List<CombatTankAI> bots = GetLivingBots();
            if (bots.Count == 0)
            {
                selected = null;
                selectedIndex = -1;
                return;
            }

            TeamMember player = TeamMember.ActiveMembers.FirstOrDefault(item =>
                item != null && item.IsPlayerControlled && item.IsAlive);
            Vector2 origin = player != null ? player.transform.position : Vector2.zero;
            selected = bots.OrderBy(item => Vector2.SqrMagnitude((Vector2)item.transform.position - origin)).First();
            selectedIndex = bots.IndexOf(selected);
        }

        private static List<CombatTankAI> GetLivingBots()
        {
            return FindObjectsByType<CombatTankAI>(FindObjectsSortMode.None)
                .Where(item => item != null && item.isActiveAndEnabled && IsAlive(item))
                .OrderBy(item => item.GetComponent<TeamMember>()?.Team ?? TeamId.Neutral)
                .ThenBy(item => item.Identity?.nickname ?? item.name, System.StringComparer.Ordinal)
                .ThenBy(item => item.name, System.StringComparer.Ordinal)
                .ToList();
        }

        private static bool IsAlive(CombatTankAI ai)
        {
            TeamMember member = ai != null ? ai.GetComponent<TeamMember>() : null;
            return member != null && member.IsAlive;
        }

        private void OnGUI()
        {
            Rect button = new Rect(Screen.width - 262f, 48f, 250f, 28f);
            if (GUI.Button(button, visible ? "Скрыть боевой ИИ (F4)" : "Показать боевой ИИ (F4)"))
            {
                SetVisible(!visible);
            }

            if (!visible || selected == null)
            {
                return;
            }

            string targetName = selected.CurrentTarget != null ? selected.CurrentTarget.name : "—";
            string memory = selected.HasRememberedTarget
                ? $"{selected.LastKnownTargetPosition.x:0.0}, {selected.LastKnownTargetPosition.y:0.0}"
                : "—";
            string reposition = selected.State == CombatAiState.Reposition
                ? $"{selected.RepositionDestination.x:0.0}, {selected.RepositionDestination.y:0.0}"
                : "—";
            BotIdentity identity = selected.Identity;
            BotPersonalityProfile personality = identity?.personality;
            string botName = identity != null ? identity.nickname : selected.name;
            string temperament = personality != null
                ? $"{personality.DominantTrait}\n" +
                  $"Агр/ост/дисц: {personality.aggression:0.00} / {personality.caution:0.00} / {personality.discipline:0.00}\n" +
                  $"Иниц/ком/терп: {personality.initiative:0.00} / {personality.teamwork:0.00} / {personality.patience:0.00}\n" +
                  $"Общ/юмор/пом: {personality.sociability:0.00} / {personality.humor:0.00} / {personality.helpfulness:0.00}"
                : "Профиль не назначен";
            BotStrategicBrain brain = selected.GetComponent<BotStrategicBrain>();
            BotKnowledgeSnapshot snapshot = brain?.CurrentSnapshot;
            BotStrategicDecision decision = brain?.CurrentDecision;
            string strategic = decision != null
                ? $"{decision.intent} ({(brain.DecisionFromLlm ? "LLM" : "fallback")})\n" +
                  $"Причина: {decision.explanation}\n" +
                  $"Запрос: {brain.RequestState}, {brain.LastTrigger}\n" +
                  $"Контакты: {snapshot?.knownTargetIds?.Length ?? 0}, союзники: {snapshot?.allyIds?.Length ?? 0}\n" +
                  $"Снимок: {(snapshot?.snapshotHash?.Length >= 8 ? snapshot.snapshotHash.Substring(0, 8) : "—")}"
                : "Стратегическое решение не назначено";
            GUI.Box(
                new Rect(Screen.width - 372f, 118f, 360f, 350f),
                $"ИИ: {botName}  [Tab / Shift+Tab]\n" +
                $"Темперамент: {temperament}\n" +
                $"Состояние: {selected.State}\n" +
                $"Линия / роль: {selected.Lane} / {selected.Role}\n" +
                $"Цель: {targetName}\n" +
                $"Последняя позиция: {memory}\n" +
                $"Перестроение: {reposition}\n" +
                $"Дистанция: {selected.PreferredDistance:0.0}\n" +
                $"Огонь: {selected.CurrentFireBlockReason}\n" +
                $"Стратегия: {strategic}");
        }
    }
}
