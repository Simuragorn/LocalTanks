using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LocalTanks
{
    public sealed class CombatAiDebugOverlay : MonoBehaviour
    {
        [SerializeField] private bool visible;
        private CombatTankAI selected;

        public bool Visible => visible;

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f4Key.wasPressedThisFrame)
            {
                visible = !visible;
            }

            if (!visible)
            {
                return;
            }

            TeamMember player = TeamMember.ActiveMembers.FirstOrDefault(item =>
                item != null && item.IsPlayerControlled && item.IsAlive);
            Vector2 origin = player != null ? player.transform.position : Vector2.zero;
            selected = FindObjectsByType<CombatTankAI>()
                .Where(item => item != null && item.isActiveAndEnabled)
                .OrderBy(item => Vector2.SqrMagnitude((Vector2)item.transform.position - origin))
                .FirstOrDefault();

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

        private void OnGUI()
        {
            Rect button = new Rect(Screen.width - 262f, 48f, 250f, 28f);
            if (GUI.Button(button, visible ? "Скрыть боевой ИИ (F4)" : "Показать боевой ИИ (F4)"))
            {
                visible = !visible;
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
                $"ИИ: {botName}\n" +
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
