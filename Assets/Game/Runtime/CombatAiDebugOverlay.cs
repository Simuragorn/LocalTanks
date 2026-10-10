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
        private Vector2 detailsScroll;
        private GUIStyle headerStyle;
        private GUIStyle detailsStyle;
        private TankDebugSelectionOutline activeOutline;

        public bool Visible => visible;
        public CombatTankAI Selected => selected;

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f4Key.wasPressedThisFrame)
            {
                SetVisible(!visible);
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

                if (selected.HasFiringPositionSolution)
                {
                    Vector2 firingPosition = selected.FiringPositionSolution.Position;
                    Debug.DrawLine(selected.transform.position, firingPosition, Color.cyan);
                    Debug.DrawLine(firingPosition + Vector2.left * 0.3f,
                        firingPosition + Vector2.right * 0.3f, Color.cyan);
                    Debug.DrawLine(firingPosition + Vector2.down * 0.3f,
                        firingPosition + Vector2.up * 0.3f, Color.cyan);
                }

                LineOfFireAssessment lineOfFire = selected.CurrentLineOfFire;
                if (!lineOfFire.IsClear && lineOfFire.Blocker != null)
                {
                    Debug.DrawLine(selected.transform.position, lineOfFire.BlockPoint, Color.red);
                }

                if (selected.HasCoverPosition)
                {
                    Debug.DrawLine(selected.transform.position, selected.CoverPosition,
                        new Color(0.35f, 0.75f, 1f));
                }

                if (selected.HasCoverFiringPosition)
                {
                    Debug.DrawLine(selected.CoverPosition, selected.CoverFiringPosition,
                        new Color(0.35f, 1f, 0.65f));
                    Debug.DrawLine(selected.CoverFiringPosition + Vector2.left * 0.25f,
                        selected.CoverFiringPosition + Vector2.right * 0.25f,
                        new Color(0.35f, 1f, 0.65f));
                    Debug.DrawLine(selected.CoverFiringPosition + Vector2.down * 0.25f,
                        selected.CoverFiringPosition + Vector2.up * 0.25f,
                        new Color(0.35f, 1f, 0.65f));
                }

                if (selected.PrimaryThreatDirection.sqrMagnitude > 0.001f)
                {
                    Debug.DrawRay(selected.transform.position,
                        selected.PrimaryThreatDirection.normalized * 2f, new Color(1f, 0.45f, 0.2f));
                }

                if (selected.SecondaryThreatDirection.sqrMagnitude > 0.001f)
                {
                    Debug.DrawRay(selected.transform.position,
                        selected.SecondaryThreatDirection.normalized * 1.7f,
                        new Color(1f, 0.75f, 0.2f));
                }
            }
        }

        public void SetVisible(bool value)
        {
            visible = value;
            if (visible) SelectNearestBot();
            else ApplySelectionOutline(null);
        }

        public void SelectRelativeBot(int direction)
        {
            List<CombatTankAI> bots = GetLivingBots();
            if (bots.Count == 0)
            {
                SetSelected(null);
                selectedIndex = -1;
                return;
            }

            int current = bots.IndexOf(selected);
            if (current < 0) current = selectedIndex >= 0 ? Mathf.Min(selectedIndex, bots.Count - 1) : 0;
            int step = direction < 0 ? -1 : 1;
            selectedIndex = (current + step + bots.Count) % bots.Count;
            SetSelected(bots[selectedIndex]);
        }

        private void SelectNearestBot()
        {
            List<CombatTankAI> bots = GetLivingBots();
            if (bots.Count == 0)
            {
                SetSelected(null);
                selectedIndex = -1;
                return;
            }

            TeamMember player = TeamMember.ActiveMembers.FirstOrDefault(item =>
                item != null && item.IsPlayerControlled && item.IsAlive);
            Vector2 origin = player != null ? player.transform.position : Vector2.zero;
            SetSelected(bots.OrderBy(item =>
                Vector2.SqrMagnitude((Vector2)item.transform.position - origin)).First());
            selectedIndex = bots.IndexOf(selected);
        }

        private void SetSelected(CombatTankAI value)
        {
            if (selected == value)
            {
                ApplySelectionOutline(visible ? selected : null);
                return;
            }

            selected = value;
            ApplySelectionOutline(visible ? selected : null);
        }

        private void ApplySelectionOutline(CombatTankAI value)
        {
            if (activeOutline != null) activeOutline.SetVisible(false);
            activeOutline = null;
            if (value == null) return;
            activeOutline = value.GetComponent<TankDebugSelectionOutline>();
            if (activeOutline == null) activeOutline = value.gameObject.AddComponent<TankDebugSelectionOutline>();
            activeOutline.SetVisible(true);
        }

        private void OnDisable()
        {
            ApplySelectionOutline(null);
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
            if (BattleDiagnosticsOverlay.AnyModalVisible) return;

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
            BotStrategicBrain brain = selected.GetComponent<BotStrategicBrain>();
            BotKnowledgeSnapshot snapshot = brain?.CurrentSnapshot;
            BotStrategicDecision decision = brain?.CurrentDecision;
            EnsureStyles();
            float width = Mathf.Min(430f, Screen.width - 24f);
            float top = 118f;
            float height = Mathf.Clamp(Screen.height - top - 12f, 220f, 620f);
            Rect panel = new Rect(Screen.width - width - 12f, top, width, height);
            GUI.Box(panel, GUIContent.none);

            List<CombatTankAI> livingBots = GetLivingBots();
            int number = Mathf.Max(0, livingBots.IndexOf(selected)) + 1;
            GUI.Label(new Rect(panel.x + 16f, panel.y + 12f, panel.width - 32f, 30f),
                $"{botName}   {number}/{livingBots.Count}", headerStyle);
            GUI.Label(new Rect(panel.x + 16f, panel.y + 40f, panel.width - 32f, 22f),
                "Tab — следующий   Shift+Tab — предыдущий", detailsStyle);

            string details = BuildDetailsText(selected, personality, brain, snapshot, decision,
                targetName, memory, reposition);
            Rect viewport = new Rect(panel.x + 10f, panel.y + 68f, panel.width - 20f, panel.height - 78f);
            float contentWidth = viewport.width - 22f;
            float contentHeight = Mathf.Max(viewport.height,
                detailsStyle.CalcHeight(new GUIContent(details), contentWidth) + 16f);
            detailsScroll = GUI.BeginScrollView(viewport, detailsScroll,
                new Rect(0f, 0f, contentWidth, contentHeight));
            GUI.Label(new Rect(8f, 4f, contentWidth - 10f, contentHeight - 8f), details, detailsStyle);
            GUI.EndScrollView();
        }

        private static string BuildDetailsText(
            CombatTankAI ai,
            BotPersonalityProfile personality,
            BotStrategicBrain brain,
            BotKnowledgeSnapshot snapshot,
            BotStrategicDecision decision,
            string targetName,
            string memory,
            string reposition)
        {
            string temperament = personality != null
                ? $"{personality.DominantTrait}\n" +
                  $"Агрессия / осторожность / дисциплина: {personality.aggression:0.00} / {personality.caution:0.00} / {personality.discipline:0.00}\n" +
                  $"Инициатива / командность / терпение: {personality.initiative:0.00} / {personality.teamwork:0.00} / {personality.patience:0.00}\n" +
                  $"Общительность / юмор / помощь: {personality.sociability:0.00} / {personality.humor:0.00} / {personality.helpfulness:0.00}"
                : "Профиль не назначен";
            string strategic = decision != null
                ? $"{decision.intent} ({(brain.DecisionFromLlm ? "LLM" : "fallback")})\n" +
                  $"Причина: {decision.explanation}\n" +
                  $"Запрос: {brain.RequestState}, {brain.LastTrigger}\n" +
                  $"Контакты: {snapshot?.knownTargetIds?.Length ?? 0}; союзники: {snapshot?.allyIds?.Length ?? 0}\n" +
                  $"Снимок: {(snapshot?.snapshotHash?.Length >= 8 ? snapshot.snapshotHash.Substring(0, 8) : "—")}"
                : "Стратегическое решение не назначено";
            LineOfFireAssessment lineOfFire = ai.CurrentLineOfFire;
            string blocker = lineOfFire.IsClear
                ? "нет"
                : $"{lineOfFire.BlockType}: {lineOfFire.BlockerName} ({lineOfFire.Distance:0.0} м)";
            string firingPosition = ai.HasFiringPositionSolution
                ? $"{ai.FiringPositionSolution.Position.x:0.0}, {ai.FiringPositionSolution.Position.y:0.0}; " +
                  $"путь {ai.FiringPositionSolution.PathLength:0.0} м; оценка {ai.FiringPositionSolution.Score:0.0}"
                : "—";
            ArmorUseProfile armor = ai.ArmorProfileUsage;
            string cover = ai.HasCoverPosition
                ? $"{ai.CoverPosition.x:0.0}, {ai.CoverPosition.y:0.0}"
                : "—";
            string coverFire = ai.HasCoverFiringPosition
                ? $"{ai.CoverFiringPosition.x:0.0}, {ai.CoverFiringPosition.y:0.0}"
                : "—";
            TacticalBehaviorProfile behavior = ai.TacticalBehavior;

            return "<b>ЛИЧНОСТЬ</b>\n" + temperament + "\n\n" +
                   "<b>ТЕКУЩАЯ ТАКТИКА</b>\n" +
                   $"Состояние: {ai.State}\n" +
                   $"Линия / роль: {ai.Lane} / {ai.Role}\n" +
                   $"Цель: {targetName}\n" +
                   $"Последняя позиция: {memory}\n" +
                   $"Перестроение: {reposition}\n" +
                   $"Желаемая дистанция: {ai.PreferredDistance:0.0}\n" +
                   $"Огонь: {ai.CurrentFireBlockReason}\n" +
                   $"Препятствие: {blocker}\n" +
                   $"Огневая позиция: {firingPosition}\n" +
                   $"Тактическая задача: {ai.TacticalMovementPurpose} / {ai.TacticalDecisionPriority}\n" +
                   $"Причина задачи: {ai.TacticalDecisionReason}\n" +
                   $"Укрытие: {cover}\n" +
                   $"Огневая точка укрытия: {coverFire}\n" +
                   $"Угрозы: {ai.ActiveThreatCount} наблюдений / {ai.ActiveThreatSectorCount} секторов; направление " +
                   $"{ai.PrimaryThreatDirection.x:0.00}, {ai.PrimaryThreatDirection.y:0.00}\n" +
                   $"Перекрёстный огонь: {(ai.IsUnderCrossfire ? $"да, {ai.CrossfireAngle:0}°" : "нет")}\n" +
                   $"Броня лоб/борт/корма: {armor.FrontArmor:0}/{armor.WeakestSideArmor:0}/{armor.RearArmor:0} мм\n" +
                   $"Безопасный доворот: {armor.SafeAngleDegrees:0.0}°\n" +
                   $"Профиль укрытия / готовность к выезду: {behavior.CoverPreference:0.00} / {behavior.PeekReloadThreshold:0.00}\n" +
                   $"Результат планировщика: {ai.LastTacticalPositionResult}\n\n" +
                   "<b>СТРАТЕГИЧЕСКОЕ РЕШЕНИЕ</b>\n" + strategic;
        }

        private void EnsureStyles()
        {
            if (headerStyle != null) return;
            headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(0.7f, 0.86f, 0.66f) }
            };
            detailsStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                wordWrap = true,
                richText = true,
                normal = { textColor = new Color(0.84f, 0.86f, 0.8f) }
            };
        }
    }
}
