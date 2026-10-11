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
            return FindObjectsByType<CombatTankAI>()
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

            BotIdentity identity = selected.Identity;
            BotPersonalityProfile personality = identity?.personality;
            string botName = identity != null ? identity.nickname : selected.name;
            TeamMember member = selected.GetComponent<TeamMember>();
            string tankName = member?.Definition != null ? member.Definition.displayName : "неизвестная машина";
            BotStrategicBrain brain = selected.GetComponent<BotStrategicBrain>();
            BotStrategicDecision decision = brain?.CurrentDecision;
            EnsureStyles();
            float width = Mathf.Min(390f, Screen.width - 24f);
            float top = 118f;
            float height = Mathf.Clamp(Screen.height - top - 12f, 240f, 450f);
            Rect panel = new Rect(Screen.width - width - 12f, top, width, height);
            GUI.Box(panel, GUIContent.none);

            List<CombatTankAI> livingBots = GetLivingBots();
            int number = Mathf.Max(0, livingBots.IndexOf(selected)) + 1;
            GUI.Label(new Rect(panel.x + 16f, panel.y + 12f, panel.width - 32f, 30f),
                $"{botName} · {tankName}   {number}/{livingBots.Count}", headerStyle);
            GUI.Label(new Rect(panel.x + 16f, panel.y + 40f, panel.width - 32f, 22f),
                "Tab — следующий   Shift+Tab — предыдущий", detailsStyle);

            string details = BuildDetailsText(selected, personality, brain, decision);
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
            BotStrategicDecision decision)
        {
            string temperament = personality != null
                ? personality.DominantTrait
                : "не определён";
            string strategic = decision != null
                ? $"{DescribeIntent(decision.intent)} · " +
                  $"{(brain != null && brain.DecisionFromLlm ? "локальная модель" : "базовый план")}" +
                  (string.IsNullOrWhiteSpace(decision.explanation) ? string.Empty : $"\nПочему: {decision.explanation}")
                : "Следует базовому плану боя";
            string tacticalReason = ai.TacticalMovementPurpose != TacticalMovementPurpose.None &&
                                    !string.IsNullOrWhiteSpace(ai.TacticalDecisionReason)
                ? $"\nПочему: {ai.TacticalDecisionReason}"
                : string.Empty;

            return "<b>КТО ЭТО</b>\n" +
                   $"Характер: {temperament}\n" +
                   $"Роль: {DescribeRole(ai.Role)}, направление — {DescribeLane(ai.Lane)}\n\n" +
                   "<b>ПЛАН</b>\n" + strategic + "\n\n" +
                   "<b>ЧТО ДЕЛАЕТ СЕЙЧАС</b>\n" +
                   $"Действие: {DescribeAction(ai)}{tacticalReason}\n" +
                   $"Цель: {DescribeTarget(ai.CurrentTarget)}\n" +
                   $"Орудие: {DescribeFireState(ai.CurrentFireBlockReason)}\n" +
                   $"Угроза: {DescribeThreat(ai)}";
        }

        private static string DescribeTarget(TeamMember target)
        {
            if (target == null) return "нет видимой цели";
            BotIdentityComponent identity = target.GetComponent<BotIdentityComponent>();
            string tank = target.Definition != null ? target.Definition.displayName : "машина";
            if (target.IsPlayerControlled) return $"игрок ({tank})";
            return identity?.Identity != null ? $"{identity.Identity.nickname} ({tank})" : tank;
        }

        private static string DescribeAction(CombatTankAI ai)
        {
            switch (ai.TacticalMovementPurpose)
            {
                case TacticalMovementPurpose.GainFiringLine: return "объезжает препятствие для выстрела";
                case TacticalMovementPurpose.MoveToCover: return "едет в укрытие";
                case TacticalMovementPurpose.HoldCover: return "ждёт в укрытии";
                case TacticalMovementPurpose.PeekToFire: return "выезжает из укрытия для выстрела";
                case TacticalMovementPurpose.ReturnToCover: return "возвращается в укрытие";
                case TacticalMovementPurpose.DefensiveRetreat: return "отступает под защиту укрытия";
            }

            switch (ai.State)
            {
                case CombatAiState.Deploy: return "занимает исходную позицию";
                case CombatAiState.Advance: return "движется по своему направлению";
                case CombatAiState.Engage: return "ведёт бой";
                case CombatAiState.Investigate: return "ищет потерянного противника";
                case CombatAiState.Reposition: return "меняет позицию";
                case CombatAiState.Hold: return "удерживает позицию";
                default: return "уничтожен";
            }
        }

        private static string DescribeFireState(FireBlockReason reason)
        {
            switch (reason)
            {
                case FireBlockReason.None: return "готово к выстрелу";
                case FireBlockReason.NoVisibleTarget: return "нет видимой цели";
                case FireBlockReason.OutOfRange: return "цель вне дальности";
                case FireBlockReason.Aiming: return "наводится";
                case FireBlockReason.Reloading: return "перезаряжается";
                case FireBlockReason.AllyInLine: return "линия огня перекрыта союзником";
                default: return "линия огня перекрыта препятствием";
            }
        }

        private static string DescribeThreat(CombatTankAI ai)
        {
            if (ai.ActiveThreatSectorCount <= 0) return "не обнаружена";
            if (ai.IsUnderCrossfire) return "перекрёстный огонь — старается подставить лоб";
            return "одно основное направление — использует выгодный угол брони";
        }

        private static string DescribeIntent(BotStrategicIntentType intent)
        {
            switch (intent)
            {
                case BotStrategicIntentType.AdvanceLane: return "продвигается по направлению";
                case BotStrategicIntentType.HoldZone: return "удерживает позицию";
                case BotStrategicIntentType.ScoutLane: return "разведывает направление";
                case BotStrategicIntentType.SupportAlly: return "помогает союзнику";
                case BotStrategicIntentType.DefendBase: return "защищает базу";
                case BotStrategicIntentType.Reposition: return "меняет позицию";
                case BotStrategicIntentType.HuntKnownTarget: return "ищет известную цель";
                case BotStrategicIntentType.TakeCover: return "ищет укрытие";
                case BotStrategicIntentType.AngleArmor: return "занимает выгодный угол брони";
                case BotStrategicIntentType.WithdrawUnderFire: return "отходит под огнём";
                case BotStrategicIntentType.BreakCrossfire: return "выходит из перекрёстного огня";
                case BotStrategicIntentType.PressAttack: return "усиливает атаку";
                case BotStrategicIntentType.Retreat: return "отступает";
                case BotStrategicIntentType.Wait: return "ожидает";
                default: return "следует командному плану";
            }
        }

        private static string DescribeRole(CombatAiRole role)
        {
            switch (role)
            {
                case CombatAiRole.Assault: return "штурм";
                case CombatAiRole.Support: return "поддержка";
                default: return "фланг";
            }
        }

        private static string DescribeLane(BattleLane lane)
        {
            switch (lane)
            {
                case BattleLane.North: return "север";
                case BattleLane.Center: return "центр";
                default: return "юг";
            }
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
