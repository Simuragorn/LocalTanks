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

        private Vector2 helpScroll;
        private Vector2 mapScroll;
        private GUIStyle panelStyle;
        private GUIStyle titleStyle;
        private GUIStyle sectionStyle;
        private GUIStyle bodyStyle;
        private GUIStyle keyStyle;
        private GUIStyle footerStyle;

        public static bool AnyModalVisible { get; private set; }
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
            RefreshModalState();
        }

        public void ToggleMapAnalysis()
        {
            mapAnalysisVisible = !mapAnalysisVisible;
            if (mapAnalysisVisible) helpVisible = false;
            RefreshModalState();
        }

        public void CloseTopWindow()
        {
            if (helpVisible) helpVisible = false;
            else if (mapAnalysisVisible) mapAnalysisVisible = false;
            RefreshModalState();
        }

        private void OnEnable() => RefreshModalState();

        private void OnDisable()
        {
            helpVisible = false;
            mapAnalysisVisible = false;
            AnyModalVisible = false;
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
            if (!helpVisible && !mapAnalysisVisible) return;

            GUI.depth = -100;
            Color previousColor = GUI.color;
            GUI.color = new Color(0.025f, 0.035f, 0.03f, 0.86f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previousColor;
            if (helpVisible) DrawHelp();
            else if (mapAnalysisVisible) DrawMapAnalysis();
        }

        private void DrawHelp()
        {
            Rect panel = CenteredPanel(760f, 650f);
            GUI.Box(panel, GUIContent.none, panelStyle);
            GUI.Label(new Rect(panel.x + 24f, panel.y + 18f, panel.width - 48f, 34f), "Справка по управлению", titleStyle);

            Rect viewport = new Rect(panel.x + 18f, panel.y + 60f, panel.width - 36f, panel.height - 104f);
            float contentWidth = Mathf.Max(280f, viewport.width - 24f);
            float keyWidth = Mathf.Clamp(contentWidth * 0.28f, 100f, 185f);
            float descriptionWidth = contentWidth - keyWidth - 18f;
            float contentHeight = CalculateHelpHeight(keyWidth, descriptionWidth);
            helpScroll = GUI.BeginScrollView(viewport, helpScroll,
                new Rect(0f, 0f, contentWidth, Mathf.Max(viewport.height, contentHeight)));

            float y = 4f;
            int rowIndex = 0;
            foreach (GameCommandGroup group in Enum.GetValues(typeof(GameCommandGroup)))
            {
                GameCommandInfo[] commands = GameCommandRegistry.Commands.Where(item => item.group == group).ToArray();
                GUI.Label(new Rect(6f, y, contentWidth - 12f, 28f), GroupName(group), sectionStyle);
                y += 32f;
                foreach (GameCommandInfo command in commands)
                {
                    float keyHeight = keyStyle.CalcHeight(new GUIContent(command.binding), keyWidth);
                    float descriptionHeight = bodyStyle.CalcHeight(new GUIContent(command.description), descriptionWidth);
                    float rowHeight = Mathf.Max(26f, Mathf.Max(keyHeight, descriptionHeight)) + 8f;
                    if (rowIndex % 2 == 0)
                    {
                        Color old = GUI.color;
                        GUI.color = new Color(0.18f, 0.22f, 0.18f, 0.35f);
                        GUI.DrawTexture(new Rect(2f, y - 2f, contentWidth - 8f, rowHeight), Texture2D.whiteTexture);
                        GUI.color = old;
                    }
                    GUI.Label(new Rect(10f, y + 2f, keyWidth, rowHeight - 4f), command.binding, keyStyle);
                    GUI.Label(new Rect(10f + keyWidth + 14f, y + 2f, descriptionWidth, rowHeight - 4f), command.description, bodyStyle);
                    y += rowHeight;
                    rowIndex++;
                }
                y += 10f;
            }
            GUI.EndScrollView();

            GUI.Label(new Rect(panel.x + 24f, panel.yMax - 36f, panel.width - 48f, 24f),
                "F1 или Esc — закрыть", footerStyle);
        }

        private void DrawMapAnalysis()
        {
            Rect panel = CenteredPanel(980f, 760f);
            GUI.Box(panel, GUIContent.none, panelStyle);
            BattleStrategicPlanner planner = director != null ? director.StrategicPlanner : null;
            GUI.Label(new Rect(panel.x + 24f, panel.y + 18f, panel.width - 48f, 34f),
                "Стратегическая оценка карты", titleStyle);

            if (planner == null || planner.MapAnalysis == null)
            {
                GUI.Label(new Rect(panel.x + 24f, panel.y + 62f, panel.width - 48f, 120f),
                    "Планировщик ещё не подготовил анализ карты.\n\n" + BuildAudioText(), bodyStyle);
                return;
            }

            string text = BuildAudioText() + "\n\n" + BuildMapAnalysisText(planner);
            Rect viewport = new Rect(panel.x + 18f, panel.y + 58f, panel.width - 36f, panel.height - 102f);
            float contentHeight = Mathf.Max(viewport.height - 4f, bodyStyle.CalcHeight(new GUIContent(text), viewport.width - 32f) + 16f);
            mapScroll = GUI.BeginScrollView(viewport, mapScroll,
                new Rect(0f, 0f, viewport.width - 20f, contentHeight));
            GUI.Label(new Rect(8f, 4f, viewport.width - 38f, contentHeight - 8f), text, bodyStyle);
            GUI.EndScrollView();
            GUI.Label(new Rect(panel.x + 24f, panel.yMax - 34f, panel.width - 48f, 24f),
                "F6 или Esc — закрыть", footerStyle);
        }

        private float CalculateHelpHeight(float keyWidth, float descriptionWidth)
        {
            float height = 4f;
            foreach (GameCommandGroup group in Enum.GetValues(typeof(GameCommandGroup)))
            {
                height += 32f;
                foreach (GameCommandInfo command in GameCommandRegistry.Commands.Where(item => item.group == group))
                {
                    float keyHeight = keyStyle.CalcHeight(new GUIContent(command.binding), keyWidth);
                    float descriptionHeight = bodyStyle.CalcHeight(new GUIContent(command.description), descriptionWidth);
                    height += Mathf.Max(26f, Mathf.Max(keyHeight, descriptionHeight)) + 8f;
                }
                height += 10f;
            }
            return height + 4f;
        }

        /// <summary>
        /// The audio layer, shown with the map analysis because music, ambience and signals belong to the
        /// arena. It reports what is actually playing and why the layer last changed, so a silent battle
        /// has a stated reason instead of an unexplained absence of sound.
        /// </summary>
        private string BuildAudioText()
        {
            BattleAudioDirector audio = director != null ? director.AudioDirector : null;
            StringBuilder text = new StringBuilder();
            text.AppendLine("—— ЗВУК АРЕНЫ ——");
            if (audio == null)
            {
                text.AppendLine("Аудиослой не подключён к этой сцене.");
                return text.ToString();
            }

            text.AppendLine($"Профиль: {(audio.HasProfile ? $"{audio.Profile.mapId} ({audio.Profile.displayName})" : "нет")}   " +
                            $"Библиотека: {audio.Library?.DescribeState() ?? "нет"}");
            BattleMusicController music = audio.Music;
            if (music != null)
            {
                text.AppendLine($"Музыка: {music.State}, настроение {music.Mood}, напряжённость {music.Intensity:0.00}");
                text.AppendLine($"Клип: {Show(music.CurrentClipId)}; переходов {music.TransitionCount}; причина: {music.LastTransitionReason}");
            }

            BattleFlowAudioController flow = audio.Flow;
            if (flow != null)
            {
                text.AppendLine($"Атмосфера: {(flow.IsAmbiencePlaying ? Show(flow.AmbienceClipId) : "не играет")}");
                text.AppendLine($"Захват: {(flow.IsCaptureLoopPlaying ? $"цикл играет ({flow.CapturingBaseCount} баз)" : "цикл остановлен")}; " +
                                $"сигналов {flow.SignalPlayCount}; последнее: {flow.LastReason}");
            }

            CrewVoiceController crew = audio.Crew;
            if (crew != null)
            {
                text.AppendLine($"Экипаж: {(crew.IsActive ? $"язык {crew.Language}" : "нет машины игрока")}; " +
                                $"реплик {crew.PlayCount}; подавлено дублей {crew.SuppressedDuplicates}");
                text.AppendLine($"Последняя реплика: {Show(crew.LastClipId)} — {crew.LastRole}");
            }

            if (audio.Profile != null)
            {
                text.AppendLine($"Уровни профиля: музыка {Percent(audio.Profile.musicVolume)}, атмосфера {Percent(audio.Profile.ambienceVolume)}, " +
                                $"сигналы {Percent(audio.Profile.signalVolume)}");
                text.AppendLine($"Каналы игрока: музыка {Percent(PlayerSettingsStore.MusicMultiplier)}, голос {Percent(PlayerSettingsStore.VoiceMultiplier)}");
            }

            return text.ToString();
        }

        private static string Show(string clipId)
        {
            return string.IsNullOrWhiteSpace(clipId) ? "нет" : clipId;
        }

        private static string Percent(float value)
        {
            return $"{Mathf.RoundToInt(Mathf.Clamp01(value) * 100f)}%";
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

        private void RefreshModalState()
        {
            AnyModalVisible = isActiveAndEnabled && (helpVisible || mapAnalysisVisible);
        }

        private void EnsureStyles()
        {
            if (titleStyle != null) return;
            panelStyle = new GUIStyle(GUI.skin.window)
            {
                padding = new RectOffset(18, 18, 18, 18)
            };
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                normal = { textColor = new Color(0.88f, 0.9f, 0.84f) }
            };
            sectionStyle = new GUIStyle(titleStyle)
            {
                fontSize = 16,
                normal = { textColor = new Color(0.58f, 0.76f, 0.54f) }
            };
            bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                wordWrap = true,
                richText = false,
                normal = { textColor = new Color(0.82f, 0.84f, 0.78f) }
            };
            keyStyle = new GUIStyle(bodyStyle)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.66f, 0.83f, 0.61f) }
            };
            footerStyle = new GUIStyle(bodyStyle)
            {
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.62f, 0.66f, 0.6f) }
            };
        }
    }
}
