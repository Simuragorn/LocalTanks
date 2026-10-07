using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace LocalTanks
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class BattleHudController : MonoBehaviour
    {
        [SerializeField] private BattleRoster roster;
        [SerializeField] private StyleSheet styleSheet;

        private UIDocument document;
        private VisualElement allyList;
        private VisualElement enemyList;
        private Label allyScore;
        private Label enemyScore;

        public VisualElement AllyList => allyList;
        public VisualElement EnemyList => enemyList;

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

        public void Configure(BattleRoster battleRoster, StyleSheet styles)
        {
            if (roster != null)
            {
                roster.Changed -= Rebuild;
            }

            roster = battleRoster;
            styleSheet = styles;
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
            Rebuild();
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
                    row.Add(icon);
                    row.Add(data);
                }
                else
                {
                    row.Add(data);
                    row.Add(icon);
                }

                container.Add(row);
            }
        }
    }
}
