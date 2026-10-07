using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace LocalTanks
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class BattleHudController : MonoBehaviour
    {
        [SerializeField] private BattleRoster roster;
        [SerializeField] private StyleSheet styleSheet;
        [SerializeField] private CaptureBase[] captureBases;

        private UIDocument document;
        private VisualElement allyList;
        private VisualElement enemyList;
        private Label allyScore;
        private Label enemyScore;
        private VisualElement baseStatusList;
        private float nextCaptureRefreshTime;

        public VisualElement AllyList => allyList;
        public VisualElement EnemyList => enemyList;
        public VisualElement BaseStatusList => baseStatusList;

        private void Awake()
        {
            document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            if (roster != null)
            {
                roster.Changed += Rebuild;
            }

            Invoke(nameof(InitializeDocument), 0f);
        }

        private void OnDisable()
        {
            if (roster != null)
            {
                roster.Changed -= Rebuild;
            }
        }

        public void Configure(BattleRoster battleRoster, StyleSheet styles, CaptureBase[] bases)
        {
            if (roster != null)
            {
                roster.Changed -= Rebuild;
            }

            roster = battleRoster;
            styleSheet = styles;
            captureBases = bases;
            if (isActiveAndEnabled && roster != null)
            {
                roster.Changed += Rebuild;
            }
        }

        private void InitializeDocument()
        {
            if (document == null)
            {
                document = GetComponent<UIDocument>();
            }

            VisualElement root = document.rootVisualElement;
            if (styleSheet != null && !root.styleSheets.Contains(styleSheet))
            {
                root.styleSheets.Add(styleSheet);
            }

            allyList = root.Q<VisualElement>("ally-list");
            enemyList = root.Q<VisualElement>("enemy-list");
            allyScore = root.Q<Label>("ally-score");
            enemyScore = root.Q<Label>("enemy-score");
            baseStatusList = root.Q<VisualElement>("base-status-list");
            Rebuild();
            RefreshCaptureStatus();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextCaptureRefreshTime)
            {
                return;
            }

            nextCaptureRefreshTime = Time.unscaledTime + 0.1f;
            RefreshCaptureStatus();
        }

        private void Rebuild()
        {
            if (roster == null || allyList == null || enemyList == null)
            {
                return;
            }

            BuildSide(allyList, roster.Allies, true);
            BuildSide(enemyList, roster.Enemies, false);
            allyScore.text = roster.AlliesAlive.ToString();
            enemyScore.text = roster.EnemiesAlive.ToString();
        }

        private static void BuildSide(
            VisualElement container,
            IReadOnlyList<BattleRosterEntry> entries,
            bool ally)
        {
            container.Clear();
            foreach (BattleRosterEntry entry in entries)
            {
                VisualElement row = new VisualElement();
                row.AddToClassList("roster-row");
                row.AddToClassList(ally ? "ally-row" : "enemy-row");
                if (!entry.IsAlive)
                {
                    row.AddToClassList("destroyed-row");
                }
                else if (!ally && !entry.IsCurrentlyVisible)
                {
                    row.AddToClassList("unspotted-row");
                }

                Image icon = new Image { sprite = entry.Icon, scaleMode = ScaleMode.ScaleToFit };
                icon.AddToClassList("tank-icon");
                Image classIcon = new Image
                {
                    sprite = entry.ClassIcon,
                    scaleMode = ScaleMode.ScaleToFit,
                    tintColor = entry.TeamColor
                };
                classIcon.AddToClassList("class-icon");
                Label name = new Label(entry.TankName);
                name.AddToClassList("tank-name");
                VisualElement data = new VisualElement();
                data.AddToClassList("tank-data");
                VisualElement bar = new VisualElement();
                bar.AddToClassList("health-track");
                VisualElement fill = new VisualElement();
                fill.AddToClassList("health-fill");
                fill.style.width = Length.Percent(entry.IsAlive ? entry.HealthRatio * 100f : 0f);
                bar.Add(fill);
                data.Add(name);
                data.Add(bar);

                if (ally)
                {
                    row.Add(classIcon);
                    row.Add(icon);
                    row.Add(data);
                }
                else
                {
                    row.Add(data);
                    row.Add(icon);
                    row.Add(classIcon);
                }

                container.Add(row);
            }
        }

        private void RefreshCaptureStatus()
        {
            if (baseStatusList == null || roster == null)
            {
                return;
            }

            baseStatusList.Clear();
            CaptureBase[] activeBases = captureBases == null
                ? Array.Empty<CaptureBase>()
                : captureBases.Where(item => item != null &&
                    (item.State == BaseCaptureState.Capturing || item.State == BaseCaptureState.Contested))
                    .OrderBy(item => item.BaseId, StringComparer.Ordinal)
                    .ToArray();
            baseStatusList.style.display = activeBases.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (CaptureBase captureBase in activeBases)
            {
                bool alliedCapture = captureBase.CapturingTeam == roster.LocalPlayerTeam;
                VisualElement row = new VisualElement();
                row.AddToClassList("base-status-row");
                Label title = new Label(captureBase.State == BaseCaptureState.Contested
                    ? $"{captureBase.DisplayName} • захват остановлен"
                    : alliedCapture
                        ? $"Захват: {captureBase.DisplayName} ({captureBase.CapturingTankCount})"
                        : $"Противник захватывает {captureBase.DisplayName} ({captureBase.CapturingTankCount})");
                title.AddToClassList("base-status-title");
                Color color = captureBase.State == BaseCaptureState.Contested
                    ? new Color32(190, 158, 92, 255)
                    : TeamPalette.ForRelation(alliedCapture);
                title.style.color = color;

                Label timer = new Label(captureBase.State == BaseCaptureState.Contested
                    ? "ПАУЗА"
                    : FormatTime(captureBase.RemainingSeconds));
                timer.AddToClassList("base-status-timer");
                timer.style.color = color;

                VisualElement header = new VisualElement();
                header.AddToClassList("base-status-header");
                header.Add(title);
                header.Add(timer);
                VisualElement track = new VisualElement();
                track.AddToClassList("base-capture-track");
                VisualElement fill = new VisualElement();
                fill.AddToClassList("base-capture-fill");
                fill.style.backgroundColor = color;
                fill.style.width = Length.Percent(captureBase.Progress * 100f);
                track.Add(fill);
                row.Add(header);
                row.Add(track);
                baseStatusList.Add(row);
            }
        }

        private static string FormatTime(float seconds)
        {
            int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{totalSeconds / 60}:{totalSeconds % 60:00}";
        }
    }
}
