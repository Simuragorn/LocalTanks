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
        public const int MaximumTankNameLength = 20;

        [SerializeField] private BattleRoster roster;
        [SerializeField] private StyleSheet[] styleSheets;
        [SerializeField] private CaptureBase[] captureBases;

        private UIDocument document;
        private VisualElement allyList;
        private VisualElement enemyList;
        private Label allyScore;
        private Label enemyScore;
        private Label allyHealthTotal;
        private Label enemyHealthTotal;
        private VisualElement baseStatusList;
        private Label battleTimer;
        private Label battleResult;
        private Label weaponAmmo;
        private Label weaponReload;
        private BattleDirector director;
        private WeaponController playerWeapon;
        private DamagePopupLayer combatOverlay;
        private float nextCaptureRefreshTime;
        private float nextClockRefreshTime;
        private float nextWeaponRefreshTime;

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

            if (playerWeapon != null)
            {
                playerWeapon.StateChanged -= OnWeaponStateChanged;
                playerWeapon = null;
            }
        }

        public void Configure(BattleRoster battleRoster, StyleSheet[] styles, CaptureBase[] bases)
        {
            if (roster != null)
            {
                roster.Changed -= Rebuild;
            }

            roster = battleRoster;
            styleSheets = styles;
            captureBases = bases;
            if (isActiveAndEnabled && roster != null)
            {
                roster.Changed += Rebuild;
            }

            if (combatOverlay != null && document != null)
            {
                combatOverlay.Configure(roster, Camera.main, document.rootVisualElement);
            }
        }

        private void InitializeDocument()
        {
            if (document == null)
            {
                document = GetComponent<UIDocument>();
            }

            VisualElement root = document.rootVisualElement;
            if (styleSheets != null)
            {
                foreach (StyleSheet sheet in styleSheets)
                {
                    if (sheet != null && !root.styleSheets.Contains(sheet))
                    {
                        root.styleSheets.Add(sheet);
                    }
                }
            }

            allyList = root.Q<VisualElement>("ally-list");
            enemyList = root.Q<VisualElement>("enemy-list");
            allyScore = root.Q<Label>("ally-score");
            enemyScore = root.Q<Label>("enemy-score");
            allyHealthTotal = root.Q<Label>("ally-health-total");
            enemyHealthTotal = root.Q<Label>("enemy-health-total");
            baseStatusList = root.Q<VisualElement>("base-status-list");
            battleTimer = root.Q<Label>("battle-timer");
            battleResult = root.Q<Label>("battle-result");
            weaponAmmo = root.Q<Label>("weapon-ammo");
            weaponReload = root.Q<Label>("weapon-reload");
            director = FindAnyObjectByType<BattleDirector>();
            combatOverlay = GetComponent<DamagePopupLayer>();
            if (combatOverlay == null) combatOverlay = gameObject.AddComponent<DamagePopupLayer>();
            combatOverlay.Configure(roster, Camera.main, root);
            Rebuild();
            RefreshCaptureStatus();
            RefreshClock();
            RefreshWeaponStatus();
        }

        private void Update()
        {
            // The battle clock reads the director, so it updates on its own cadence and never on the capture
            // cadence: a timer that ticks ten times a second is noise, and a result must appear at once.
            if (Time.unscaledTime >= nextClockRefreshTime)
            {
                nextClockRefreshTime = Time.unscaledTime + 0.25f;
                RefreshClock();
            }

            if (Time.unscaledTime >= nextWeaponRefreshTime)
            {
                nextWeaponRefreshTime = Time.unscaledTime + 0.1f;
                RefreshWeaponStatus();
            }

            if (Time.unscaledTime < nextCaptureRefreshTime)
            {
                return;
            }

            nextCaptureRefreshTime = Time.unscaledTime + 0.1f;
            RefreshCaptureStatus();
        }

        /// <summary>
        /// The battle clock and the result come from <see cref="BattleDirector"/>, the single thing that decides
        /// the battle is over. A scene without a director hides the clock rather than showing a frozen zero.
        /// </summary>
        private void RefreshClock()
        {
            if (battleTimer == null || battleResult == null)
            {
                return;
            }

            if (director == null)
            {
                director = FindAnyObjectByType<BattleDirector>();
            }

            VisualElement clock = battleTimer.parent;
            if (director == null)
            {
                if (clock != null)
                {
                    clock.style.display = DisplayStyle.None;
                }

                return;
            }

            if (clock != null)
            {
                clock.style.display = DisplayStyle.Flex;
            }

            battleTimer.text = FormatTime(director.RemainingSeconds);
            bool ended = director.HasEnded;
            battleResult.EnableInClassList("has-result", ended);
            if (ended)
            {
                TeamId playerTeam = roster != null ? roster.LocalPlayerTeam : TeamId.TeamA;
                battleResult.text = director.Result.Describe(playerTeam);
            }
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
            if (allyHealthTotal != null)
                allyHealthTotal.text = FormatHitPoints(roster.AlliesCurrentHitPoints, roster.AlliesMaximumHitPoints);
            if (enemyHealthTotal != null)
                enemyHealthTotal.text = FormatHitPoints(roster.EnemiesCurrentHitPoints, roster.EnemiesMaximumHitPoints);
            ResolvePlayerWeapon();
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
                Label name = new Label(FormatTankName(entry.TankName))
                {
                    tooltip = entry.TankName
                };
                name.AddToClassList("tank-name");
                Label hitPoints = new Label(VisibleHitPoints(entry));
                hitPoints.AddToClassList("health-number");
                if (string.IsNullOrEmpty(hitPoints.text))
                {
                    hitPoints.style.display = DisplayStyle.None;
                }
                VisualElement title = new VisualElement();
                title.AddToClassList("tank-title");
                title.AddToClassList(ally ? "ally-title" : "enemy-title");
                if (ally)
                {
                    title.Add(classIcon);
                    title.Add(name);
                    title.Add(hitPoints);
                }
                else
                {
                    title.Add(hitPoints);
                    title.Add(name);
                    title.Add(classIcon);
                }

                VisualElement data = new VisualElement();
                data.AddToClassList("tank-data");
                VisualElement bar = new VisualElement();
                bar.AddToClassList("health-track");
                bar.AddToClassList("lt-meter-track");
                VisualElement fill = new VisualElement();
                fill.AddToClassList("health-fill");
                fill.style.backgroundColor = entry.TeamColor;
                fill.style.width = Length.Percent(entry.IsAlive ? entry.HealthRatio * 100f : 0f);
                bar.Add(fill);
                data.Add(title);
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

        public static string FormatTankName(string tankName)
        {
            if (string.IsNullOrEmpty(tankName) || tankName.Length <= MaximumTankNameLength)
            {
                return tankName ?? string.Empty;
            }

            return tankName.Substring(0, MaximumTankNameLength - 1) + "…";
        }

        public static string FormatHitPoints(int current, int maximum)
        {
            return TankHealthBar.FormatHitPoints(current, maximum);
        }

        public static string VisibleHitPoints(BattleRosterEntry entry)
        {
            return entry != null && (entry.IsAlly || entry.IsCurrentlyVisible || !entry.IsAlive)
                ? FormatHitPoints(entry.LastKnownHitPoints, entry.MaximumHitPoints)
                : string.Empty;
        }

        private void RefreshWeaponStatus()
        {
            if (weaponAmmo == null || weaponReload == null)
            {
                return;
            }

            ResolvePlayerWeapon();
            VisualElement panel = weaponAmmo.parent;
            if (playerWeapon == null)
            {
                if (panel != null) panel.style.display = DisplayStyle.None;
                return;
            }

            if (panel != null) panel.style.display = DisplayStyle.Flex;
            weaponAmmo.text = $"Снаряды  {playerWeapon.RoundsRemaining} / {playerWeapon.MagazineSize}";
            weaponReload.text = playerWeapon.IsReloading
                ? $"Перезарядка  {playerWeapon.RemainingReloadSeconds:0.0} / {playerWeapon.EffectiveReloadSeconds:0.0} с"
                : $"Перезарядка  {playerWeapon.EffectiveReloadSeconds:0.0} с";
            weaponReload.EnableInClassList("is-reloading", playerWeapon.IsReloading);
        }

        private void ResolvePlayerWeapon()
        {
            TeamMember player = roster != null ? roster.PlayerMember : null;
            WeaponController resolved = player != null ? player.GetComponent<WeaponController>() : null;
            if (resolved == playerWeapon)
            {
                return;
            }

            if (playerWeapon != null)
            {
                playerWeapon.StateChanged -= OnWeaponStateChanged;
            }

            playerWeapon = resolved;
            if (playerWeapon != null)
            {
                playerWeapon.StateChanged += OnWeaponStateChanged;
            }
        }

        private void OnWeaponStateChanged(WeaponController source)
        {
            RefreshWeaponStatus();
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
                row.AddToClassList("lt-panel");
                row.AddToClassList("lt-panel--quiet");
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
                track.AddToClassList("lt-meter-track");
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
