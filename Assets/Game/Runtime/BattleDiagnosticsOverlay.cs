using System;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LocalTanks
{
    public sealed class BattleDiagnosticsOverlay : MonoBehaviour
    {
        [SerializeField] private BattleDirector director;
        [SerializeField] private bool helpVisible;
        [SerializeField] private bool mapAnalysisVisible;

        private Vector2 mapScroll;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;

        public bool HelpVisible => helpVisible;
        public bool MapAnalysisVisible => mapAnalysisVisible;

        public void Configure(BattleDirector battleDirector)
        {
            director = battleDirector;
        }

        public void ToggleHelp()
        {
            helpVisible = !helpVisible;
            if (helpVisible) mapAnalysisVisible = false;
        }

        public void ToggleMapAnalysis()
        {
            mapAnalysisVisible = !mapAnalysisVisible;
            if (mapAnalysisVisible) helpVisible = false;
        }

        public void CloseTopWindow()
        {
            if (helpVisible) helpVisible = false;
            else if (mapAnalysisVisible) mapAnalysisVisible = false;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.f1Key.wasPressedThisFrame)
            {
                ToggleHelp();
            }
            else if (keyboard.f6Key.wasPressedThisFrame)
            {
                ToggleMapAnalysis();
            }
            else if (keyboard.escapeKey.wasPressedThisFrame && (helpVisible || mapAnalysisVisible))
            {
                CloseTopWindow();
            }
        }

        private void OnGUI()
        {
            EnsureStyles();
            if (helpVisible) DrawHelp();
            else if (mapAnalysisVisible) DrawMapAnalysis();
        }

        private void DrawHelp()
        {
            Rect panel = CenteredPanel(760f, 650f);
            GUI.Box(panel, GUIContent.none);
            GUI.Label(new Rect(panel.x + 24f, panel.y + 18f, panel.width - 48f, 34f), "Справка по управлению", titleStyle);

            float y = panel.y + 62f;
            foreach (GameCommandGroup group in Enum.GetValues(typeof(GameCommandGroup)))
            {
                GameCommandInfo[] commands = GameCommandRegistry.Commands.Where(item => item.group == group).ToArray();
                GUI.Label(new Rect(panel.x + 24f, y, panel.width - 48f, 26f), GroupName(group), titleStyle);
                y += 30f;
                foreach (GameCommandInfo command in commands)
                {
                    GUI.Label(new Rect(panel.x + 34f, y, 180f, 24f), command.binding, bodyStyle);
                    GUI.Label(new Rect(panel.x + 215f, y, panel.width - 250f, 24f), command.description, bodyStyle);
                    y += 27f;
                }
                y += 8f;
            }

            GUI.Label(new Rect(panel.x + 24f, panel.yMax - 36f, panel.width - 48f, 24f),
                "F1 или Esc — закрыть", bodyStyle);
        }

        private void DrawMapAnalysis()
        {
            Rect panel = CenteredPanel(980f, 760f);
            GUI.Box(panel, GUIContent.none);
            BattleStrategicPlanner planner = director != null ? director.StrategicPlanner : null;
            GUI.Label(new Rect(panel.x + 24f, panel.y + 18f, panel.width - 48f, 34f),
                "Стратегическая оценка карты", titleStyle);

            if (planner == null || planner.MapAnalysis == null)
            {
                GUI.Label(new Rect(panel.x + 24f, panel.y + 62f, panel.width - 48f, 80f),
                    "Планировщик ещё не подготовил анализ карты.", bodyStyle);
                return;
            }

            string text = BuildMapAnalysisText(planner);
            Rect viewport = new Rect(panel.x + 18f, panel.y + 58f, panel.width - 36f, panel.height - 102f);
            float contentHeight = Mathf.Max(viewport.height - 4f, bodyStyle.CalcHeight(new GUIContent(text), viewport.width - 32f) + 16f);
            mapScroll = GUI.BeginScrollView(viewport, mapScroll,
                new Rect(0f, 0f, viewport.width - 20f, contentHeight));
            GUI.Label(new Rect(8f, 4f, viewport.width - 38f, contentHeight - 8f), text, bodyStyle);
            GUI.EndScrollView();
            GUI.Label(new Rect(panel.x + 24f, panel.yMax - 34f, panel.width - 48f, 24f),
                "F6 или Esc — закрыть", bodyStyle);
        }

        private static string BuildMapAnalysisText(BattleStrategicPlanner planner)
        {
            MapStrategicAnalysis analysis = planner.MapAnalysis;
            StrategicPlanningDiagnostics diagnostics = planner.Diagnostics;
            StringBuilder text = new StringBuilder();
            text.AppendLine($"Состояние: {planner.State}");
            text.AppendLine($"Источник: {diagnostics?.providerName ?? "Deterministic"}; " +
                            $"карта: {(diagnostics != null && diagnostics.mapAnalysisFromLlm ? "LLM" : "fallback")}");
            text.AppendLine($"Карта: {analysis.mapId}   Хэш: {ShortHash(analysis.sourceHash)}");
            text.AppendLine();
            text.AppendLine(analysis.summary);

            foreach (StrategicLaneAnalysis lane in analysis.lanes ?? Array.Empty<StrategicLaneAnalysis>())
            {
                if (lane == null) continue;
                text.AppendLine();
                text.AppendLine($"{lane.name} [{lane.laneId}]");
                text.AppendLine(lane.description);
                text.AppendLine($"Преимущества: {lane.advantages}");
                text.AppendLine($"Риски: {lane.risks}");
                text.AppendLine($"Подходят: {string.Join(", ", lane.preferredClasses ?? Array.Empty<string>())}");
            }

            AppendTeamPlan(text, "КОМАНДА A", planner.TeamAPlan,
                diagnostics != null && diagnostics.teamAPlanFromLlm);
            AppendTeamPlan(text, "КОМАНДА B", planner.TeamBPlan,
                diagnostics != null && diagnostics.teamBPlanFromLlm);
            return text.ToString();
        }

        private static void AppendTeamPlan(StringBuilder text, string heading, TeamBattlePlan plan, bool fromLlm)
        {
            text.AppendLine();
            text.AppendLine($"—— {heading}: {(fromLlm ? "LLM" : "fallback")} ——");
            if (plan == null)
            {
                text.AppendLine("План ещё не подготовлен.");
                return;
            }

            text.AppendLine(plan.summary);
            text.AppendLine($"Главное направление: {plan.primaryLaneId}; второе: {plan.secondaryLaneId}; резерв: {plan.reserveBotId}");
            foreach (TeamPlanAssignment assignment in plan.assignments ?? Array.Empty<TeamPlanAssignment>())
            {
                if (assignment == null) continue;
                text.AppendLine($"• {assignment.botId}: {assignment.task}, {assignment.laneId}, " +
                                $"приоритет {assignment.priority:0.00} — {assignment.rationale}");
            }
        }

        private static string ShortHash(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Length > 10 ? value.Substring(0, 10) : value ?? "—";
        }

        private static string GroupName(GameCommandGroup group)
        {
            switch (group)
            {
                case GameCommandGroup.Battle: return "Бой";
                case GameCommandGroup.Interface: return "Интерфейс";
                default: return "Разработка и диагностика";
            }
        }

        private static Rect CenteredPanel(float preferredWidth, float preferredHeight)
        {
            float width = Mathf.Min(preferredWidth, Screen.width - 32f);
            float height = Mathf.Min(preferredHeight, Screen.height - 32f);
            return new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
        }

        private void EnsureStyles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                normal = { textColor = new Color(0.88f, 0.9f, 0.84f) }
            };
            bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                wordWrap = true,
                richText = false,
                normal = { textColor = new Color(0.82f, 0.84f, 0.78f) }
            };
        }
    }
}
