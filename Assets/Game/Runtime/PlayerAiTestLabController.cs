using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace LocalTanks
{
    [Serializable]
    public sealed class PlayerAiTestLabSettings
    {
        [Range(0, BattleScenario.TeamSize - 1)] public int alliedBotCount = 2;
        [Range(1, BattleScenario.TeamSize)] public int enemyBotCount = 3;
        [Range(1, 10)] public int botTier = 1;
        public string playerTankId = string.Empty;
        public int seed = 1701;
    }

    public static class PlayerAiTestLabScenarioFactory
    {
        private static readonly int[] PreferredSpawnSlots = { 7, 5, 9, 3, 11, 1, 13 };

        public static string[] Validate(
            BattleDirector director,
            BattleScenario template,
            PlayerAiTestLabSettings settings)
        {
            List<string> errors = new List<string>();
            if (director == null) errors.Add("Не найден BattleDirector.");
            if (template == null) errors.Add("Не найден шаблон карты.");
            if (settings == null) errors.Add("Не заданы настройки боя.");
            if (errors.Count > 0) return errors.ToArray();

            IReadOnlyList<TankDefinition> spawnable = director.SpawnableTankDefinitions;
            TankDefinition player = spawnable.FirstOrDefault(item => item.id == settings.playerTankId);
            if (player == null) errors.Add("Выбранная техника игрока недоступна в этой сцене.");
            if (settings.alliedBotCount < 0 || settings.alliedBotCount >= BattleScenario.TeamSize)
                errors.Add($"Союзных ботов должно быть от 0 до {BattleScenario.TeamSize - 1}.");
            if (settings.enemyBotCount < 1 || settings.enemyBotCount > BattleScenario.TeamSize)
                errors.Add($"Врагов должно быть от 1 до {BattleScenario.TeamSize}.");
            if (settings.botTier < 1 || settings.botTier > 10)
                errors.Add("Уровень ботов должен быть от 1 до 10.");

            TankDefinition[] tierVehicles = spawnable.Where(item => item.tier == settings.botTier).ToArray();
            if (tierVehicles.Length == 0)
                errors.Add($"В сцене нет доступной техники уровня {settings.botTier}.");
            if (player != null && settings.alliedBotCount > 0 &&
                tierVehicles.All(item => item.nation != player.nation))
            {
                errors.Add($"Для союзников нет доступной техники уровня {settings.botTier} нации {player.nation}.");
            }

            BattleScenarioEntry[] templateEntries = template.entries ?? Array.Empty<BattleScenarioEntry>();
            if (templateEntries.Count(item => item != null && item.team == TeamId.TeamA) < BattleScenario.TeamSize ||
                templateEntries.Count(item => item != null && item.team == TeamId.TeamB) < BattleScenario.TeamSize)
            {
                errors.Add("Шаблон карты не содержит полный набор стартовых позиций и маршрутов.");
            }
            return errors.ToArray();
        }

        public static BattleScenario Create(
            BattleDirector director,
            BattleScenario template,
            PlayerAiTestLabSettings settings)
        {
            string[] errors = Validate(director, template, settings);
            if (errors.Length > 0)
                throw new InvalidOperationException("Player AI Test Lab settings are invalid:\n- " + string.Join("\n- ", errors));

            TankDefinition[] spawnable = director.SpawnableTankDefinitions.ToArray();
            TankDefinition player = spawnable.First(item => item.id == settings.playerTankId);
            TankDefinition[] tierVehicles = spawnable.Where(item => item.tier == settings.botTier).ToArray();
            TankDefinition[] alliedVehicles = tierVehicles.Where(item => item.nation == player.nation).ToArray();
            TankNation[] enemyNations = tierVehicles.Select(item => item.nation).Distinct().OrderBy(item => item).ToArray();
            TankNation[] preferredEnemyNations = enemyNations.Where(item => item != player.nation).ToArray();
            System.Random random = new System.Random(settings.seed);
            TankNation[] enemyNationPool = preferredEnemyNations.Length > 0 ? preferredEnemyNations : enemyNations;
            TankNation enemyNation = enemyNationPool[random.Next(enemyNationPool.Length)];
            TankDefinition[] enemyVehicles = tierVehicles.Where(item => item.nation == enemyNation).ToArray();

            List<BattleScenarioEntry> entries = new List<BattleScenarioEntry>();
            BattleScenarioEntry[] teamATemplates = OrderedTemplates(template, TeamId.TeamA, true);
            BattleScenarioEntry[] teamBTemplates = OrderedTemplates(template, TeamId.TeamB, false);
            entries.Add(CloneEntry(teamATemplates[0], "player", player.id, true));
            for (int index = 0; index < settings.alliedBotCount; index++)
            {
                entries.Add(CloneEntry(
                    teamATemplates[index + 1],
                    $"ally_bot_{index + 1:00}",
                    alliedVehicles[random.Next(alliedVehicles.Length)].id,
                    false));
            }
            for (int index = 0; index < settings.enemyBotCount; index++)
            {
                entries.Add(CloneEntry(
                    teamBTemplates[index],
                    $"enemy_bot_{index + 1:00}",
                    enemyVehicles[random.Next(enemyVehicles.Length)].id,
                    false));
            }

            BattleScenario result = ScriptableObject.CreateInstance<BattleScenario>();
            result.name = $"PlayerAiTestLab_{settings.seed}";
            result.id = $"player_ai_test_{settings.seed}";
            result.sceneId = template.sceneId;
            result.kind = BattleScenarioKind.PlayerAiTestLab;
            result.entries = entries.ToArray();
            string[] scenarioErrors = result.Validate(director.CombatDatabase);
            if (scenarioErrors.Length > 0)
                throw new InvalidOperationException("Generated Player AI Test Lab scenario is invalid:\n- " +
                                                    string.Join("\n- ", scenarioErrors));
            return result;
        }

        private static BattleScenarioEntry[] OrderedTemplates(
            BattleScenario template,
            TeamId team,
            bool playerFirst)
        {
            BattleScenarioEntry[] source = template.entries
                .Where(item => item != null && item.team == team)
                .ToArray();
            return PreferredSpawnSlots
                .Select(slot => source.FirstOrDefault(item => item.spawnSlot == slot))
                .Where(item => item != null)
                .Concat(source.Where(item => !PreferredSpawnSlots.Contains(item.spawnSlot))
                    .OrderBy(item => item.spawnSlot))
                .OrderByDescending(item => playerFirst && item.playerControlled)
                .ThenBy(item => SpawnPreference(item.spawnSlot))
                .Take(BattleScenario.TeamSize)
                .ToArray();
        }

        private static int SpawnPreference(int spawnSlot)
        {
            int order = Array.IndexOf(PreferredSpawnSlots, spawnSlot);
            return order >= 0 ? order : PreferredSpawnSlots.Length + spawnSlot;
        }

        private static BattleScenarioEntry CloneEntry(
            BattleScenarioEntry template,
            string id,
            string tankId,
            bool playerControlled)
        {
            return new BattleScenarioEntry
            {
                id = id,
                tankId = tankId,
                team = template.team,
                playerControlled = playerControlled,
                baseId = template.baseId,
                spawnSlot = template.spawnSlot,
                lane = template.lane,
                role = template.role,
                routeCells = template.routeCells != null ? template.routeCells.ToArray() : Array.Empty<Vector2Int>()
            };
        }
    }

    [Serializable]
    public sealed class PlayerAiTestTelemetrySample
    {
        public float time;
        public string entityId;
        public string nickname;
        public string team;
        public bool playerControlled;
        public string tankId;
        public bool alive;
        public int hitPoints;
        public int maximumHitPoints;
        public float positionX;
        public float positionY;
        public float hullHeading;
        public float speed;
        public int shotsFired;
        public string aiState;
        public string tacticalTask;
        public string targetId;
        public bool targetVisible;
        public string fireBlockReason;
        public float targetDistance;
        public float turretAimErrorDegrees;
        public float targetBearingRateDegrees;
        public string lineBlockType;
        public string lineBlockerId;
        public bool lineBlockerDestroyed;
        public bool hasPath;
        public bool movingInReverse;
    }

    [Serializable]
    public sealed class PlayerAiTestRosterRecord
    {
        public string entityId;
        public string nickname;
        public string team;
        public bool playerControlled;
        public string tankId;
        public string tankName;
        public int tier;
        public string nation;
    }

    [Serializable]
    public sealed class PlayerAiTestIssueRecord
    {
        public float time;
        public string kind;
        public string botId;
        public string nickname;
        public string details;
    }

    [Serializable]
    public sealed class PlayerAiTestBattleReport
    {
        public int schemaVersion = 2;
        public string sessionId;
        public string startedUtc;
        public string completedUtc;
        public string unityVersion;
        public string scene;
        public string mapId;
        public int seed;
        public string playerTankId;
        public int botTier;
        public int alliedBotCount;
        public int enemyBotCount;
        public bool localLlmEnabled;
        public float durationSeconds;
        public string outcome;
        public int teamAAlive;
        public int teamBAlive;
        public int telemetrySamples;
        public string playerComment;
        public PlayerAiTestRosterRecord[] roster;
        public PlayerAiTestIssueRecord[] issues;
    }

    public sealed class PlayerAiTestLabController : MonoBehaviour
    {
        private const float SampleInterval = 0.2f;
        private const float MaximumBattleDuration = 900f;
        private static readonly PlayerAiTestLabSettings SavedSettings = new PlayerAiTestLabSettings();

        [SerializeField] private BattleDirector director;

        private BattleScenario templateScenario;
        private BattleScenario runtimeScenario;
        private CombatAiAuditSession auditSession;
        private readonly List<PlayerAiTestTelemetrySample> samples = new List<PlayerAiTestTelemetrySample>();
        private readonly Dictionary<TeamMember, int> shotCounts = new Dictionary<TeamMember, int>();
        private readonly Dictionary<WeaponController, TeamMember> weaponOwners = new Dictionary<WeaponController, TeamMember>();
        private PlayerAiTestBattleReport report;
        private TankDefinition[] playerChoices = Array.Empty<TankDefinition>();
        private int playerChoiceIndex;
        private float elapsed;
        private float nextSampleAt;
        private bool started;
        private bool paused;
        private bool complete;
        private string comment = string.Empty;
        private string validationMessage = string.Empty;
        private string savedDirectory = string.Empty;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle warningStyle;
        private PlayerAimReticle aimReticle;

        public BattleDirector Director => director;
        public static PlayerAiTestLabSettings CurrentSettings => SavedSettings;
        public bool IsBattleStarted => started;
        public bool IsComplete => complete;
        public string SavedDirectory => savedDirectory;
        public PlayerAiTestBattleReport Report => report;

        /// <summary>
        /// The manual test laboratory is a developer scene whose purpose is watching a battle, so
        /// this scene value opts into the semi-transparent silhouette for unseen tanks. It is
        /// written by the scene builder and can be changed per scene without touching code.
        /// </summary>
        [SerializeField] private bool showHiddenTanksForTesting = true;

        public bool ShowHiddenTanksForTesting => showHiddenTanksForTesting;

        public void Configure(BattleDirector battleDirector)
        {
            director = battleDirector;
        }

        private void Awake()
        {
            if (director == null) director = FindAnyObjectByType<BattleDirector>();
            if (director == null) throw new InvalidOperationException("Player AI Test Lab has no BattleDirector.");
            templateScenario = director.Scenario;
            director.enabled = false;
            // This laboratory exists to watch a manual battle, so it opts into the semi-transparent
            // silhouette for tanks the team cannot see instead of inheriting the battle default.
            FindAnyObjectByType<BattleVisibilitySettings>()?.Configure(showHiddenTanksForTesting);
            aimReticle = Camera.main != null ? Camera.main.GetComponent<PlayerAimReticle>() : null;
            aimReticle?.SetUiInteractionActive(true);
        }

        private void Start()
        {
            playerChoices = director.SpawnableTankDefinitions.ToArray();
            if (playerChoices.Length == 0)
            {
                validationMessage = "В сцене нет доступной техники.";
                return;
            }
            playerChoiceIndex = Mathf.Max(0, Array.FindIndex(playerChoices, item => item.id == SavedSettings.playerTankId));
            SavedSettings.playerTankId = playerChoices[playerChoiceIndex].id;
            ValidateSelection();
        }

        private void Update()
        {
            if (!started || complete) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.pKey.wasPressedThisFrame) SetPaused(!paused);
            if (paused) return;

            elapsed += Time.deltaTime;
            if (elapsed >= nextSampleAt)
            {
                auditSession?.Sample(elapsed);
                RecordTelemetry();
                nextSampleAt += SampleInterval;
            }

            if (director.TeamAAlive <= 0 || director.TeamBAlive <= 0 || elapsed >= MaximumBattleDuration)
                FinishBattle(BuildOutcome());
        }

        private void OnDestroy()
        {
            ModalUiState.ClearPointerRegion(this);
            UnsubscribeWeapons();
            auditSession?.Dispose();
            auditSession = null;
            if (runtimeScenario != null) Destroy(runtimeScenario);
            if (paused) Time.timeScale = 1f;
        }

        public void StartBattle()
        {
            ValidateSelection();
            if (!string.IsNullOrEmpty(validationMessage)) return;
            runtimeScenario = PlayerAiTestLabScenarioFactory.Create(director, templateScenario, SavedSettings);
            director.OverrideScenario(runtimeScenario);
            director.SpawnAll();
            // The setting is scene-owned and may have been applied before the tanks existed.
            director.ReapplyVisibilitySettings();
            if (aimReticle == null && Camera.main != null)
                aimReticle = Camera.main.GetComponent<PlayerAimReticle>();
            aimReticle?.SetUiInteractionActive(false);
            SubscribeWeapons();
            auditSession = new CombatAiAuditSession(director);
            samples.Clear();
            elapsed = 0f;
            nextSampleAt = 0f;
            started = true;
            complete = false;
            paused = false;
            comment = string.Empty;
            savedDirectory = string.Empty;
            Time.timeScale = 1f;
            report = CreateReportHeader();
            auditSession.Sample(0f);
            RecordTelemetry();
        }

        public void FinishBattle(string outcome = "StoppedByPlayer")
        {
            if (!started || complete) return;
            auditSession?.Sample(elapsed);
            RecordTelemetry();
            complete = true;
            report.durationSeconds = elapsed;
            report.outcome = outcome;
            report.teamAAlive = director.TeamAAlive;
            report.teamBAlive = director.TeamBAlive;
            report.completedUtc = DateTime.UtcNow.ToString("O");
            SaveReport();
            SetPaused(true);
            aimReticle?.SetUiInteractionActive(true);
        }

        public void SaveReport()
        {
            if (report == null) return;
            report.playerComment = comment ?? string.Empty;
            report.telemetrySamples = samples.Count;
            report.roster = BuildRosterRecords();
            report.issues = BuildIssueRecords();
            savedDirectory = PlayerAiTestBattleReportStore.Save(
                report,
                samples,
                auditSession?.Auditor?.BuildReport(elapsed) ?? string.Empty,
                auditSession?.Auditor?.BuildTransitionReport() ?? string.Empty);
        }

        public void SetPaused(bool value)
        {
            if (!started || complete && !value) return;
            paused = value;
            Time.timeScale = paused ? 0f : 1f;
        }

        private PlayerAiTestBattleReport CreateReportHeader()
        {
            string sessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + $"_{SavedSettings.seed}";
            return new PlayerAiTestBattleReport
            {
                sessionId = sessionId,
                startedUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                scene = gameObject.scene.name,
                mapId = director.TacticalMap?.mapId ?? string.Empty,
                seed = SavedSettings.seed,
                playerTankId = SavedSettings.playerTankId,
                botTier = SavedSettings.botTier,
                alliedBotCount = SavedSettings.alliedBotCount,
                enemyBotCount = SavedSettings.enemyBotCount,
                localLlmEnabled = false
            };
        }

        private void SubscribeWeapons()
        {
            foreach (TeamMember member in director.SpawnedMembers)
            {
                shotCounts[member] = 0;
                WeaponController weapon = member != null ? member.GetComponent<WeaponController>() : null;
                if (weapon == null) continue;
                weaponOwners[weapon] = member;
                weapon.Fired += OnWeaponFired;
            }
        }

        private void UnsubscribeWeapons()
        {
            foreach (WeaponController weapon in weaponOwners.Keys.ToArray())
                if (weapon != null) weapon.Fired -= OnWeaponFired;
            weaponOwners.Clear();
            shotCounts.Clear();
        }

        private void OnWeaponFired(WeaponController weapon)
        {
            if (weapon != null && weaponOwners.TryGetValue(weapon, out TeamMember member))
                shotCounts[member] = shotCounts.TryGetValue(member, out int count) ? count + 1 : 1;
        }

        private void RecordTelemetry()
        {
            foreach (TeamMember member in director.SpawnedMembers.Where(item => item != null))
            {
                CombatTankAI ai = member.GetComponent<CombatTankAI>();
                NavigationAgent navigation = member.GetComponent<NavigationAgent>();
                TankMotor motor = member.GetComponent<TankMotor>();
                TeamMember target = ai != null ? ai.CurrentTarget : null;
                LineOfFireAssessment line = ai != null ? ai.CurrentLineOfFire : default;
                TeamMember lineBlocker = line.Blocker != null
                    ? line.Blocker.GetComponentInParent<TeamMember>()
                    : null;
                BotIdentityComponent identity = member.GetComponent<BotIdentityComponent>();
                samples.Add(new PlayerAiTestTelemetrySample
                {
                    time = elapsed,
                    entityId = director.GetEntityId(member),
                    nickname = identity?.Identity?.nickname ?? (member.IsPlayerControlled ? "Player" : member.name),
                    team = member.Team.ToString(),
                    playerControlled = member.IsPlayerControlled,
                    tankId = member.Definition?.id ?? string.Empty,
                    alive = member.IsAlive,
                    hitPoints = member.Health?.CurrentHitPoints ?? 0,
                    maximumHitPoints = member.Health?.MaximumHitPoints ?? 0,
                    positionX = member.transform.position.x,
                    positionY = member.transform.position.y,
                    hullHeading = member.transform.eulerAngles.z,
                    speed = motor?.CurrentSpeed ?? 0f,
                    shotsFired = shotCounts.TryGetValue(member, out int shots) ? shots : 0,
                    aiState = ai != null ? ai.State.ToString() : string.Empty,
                    tacticalTask = ai != null ? ai.TacticalMovementPurpose.ToString() : string.Empty,
                    targetId = target != null ? director.GetEntityId(target) : string.Empty,
                    targetVisible = ai != null && target != null &&
                                    director.VisionSystem.IsVisibleTo(member.Team, target),
                    fireBlockReason = ai != null ? ai.CurrentFireBlockReason.ToString() : string.Empty,
                    targetDistance = ai != null && target != null ? ai.CurrentTargetDistance : -1f,
                    turretAimErrorDegrees = ai != null ? ai.TurretAimErrorDegrees : 0f,
                    targetBearingRateDegrees = ai != null ? ai.TargetBearingRateDegrees : 0f,
                    lineBlockType = ai != null ? line.BlockType.ToString() : string.Empty,
                    lineBlockerId = lineBlocker != null ? director.GetEntityId(lineBlocker) : string.Empty,
                    lineBlockerDestroyed = lineBlocker != null && !lineBlocker.IsAlive,
                    hasPath = navigation != null && navigation.HasPath,
                    movingInReverse = navigation != null && navigation.MovingInReverse
                });
            }
        }

        private PlayerAiTestRosterRecord[] BuildRosterRecords()
        {
            return director.SpawnedMembers.Where(item => item != null).Select(member =>
            {
                BotIdentityComponent identity = member.GetComponent<BotIdentityComponent>();
                TankDefinition definition = member.Definition;
                return new PlayerAiTestRosterRecord
                {
                    entityId = director.GetEntityId(member),
                    nickname = identity?.Identity?.nickname ?? (member.IsPlayerControlled ? "Player" : member.name),
                    team = member.Team.ToString(),
                    playerControlled = member.IsPlayerControlled,
                    tankId = definition?.id ?? string.Empty,
                    tankName = definition?.displayName ?? member.name,
                    tier = definition?.tier ?? 0,
                    nation = definition?.nation.ToString() ?? string.Empty
                };
            }).ToArray();
        }

        private PlayerAiTestIssueRecord[] BuildIssueRecords()
        {
            return auditSession?.Auditor?.Issues.Select(issue => new PlayerAiTestIssueRecord
            {
                time = issue.Time,
                kind = issue.Kind.ToString(),
                botId = issue.BotId,
                nickname = issue.Nickname,
                details = issue.Details
            }).ToArray() ?? Array.Empty<PlayerAiTestIssueRecord>();
        }

        private string BuildOutcome()
        {
            if (elapsed >= MaximumBattleDuration) return "TimeLimit";
            if (director.TeamAAlive <= 0 && director.TeamBAlive <= 0) return "Draw";
            if (director.TeamBAlive <= 0) return "PlayerTeamVictory";
            if (director.TeamAAlive <= 0) return "EnemyTeamVictory";
            return "StoppedByPlayer";
        }

        private void ValidateSelection()
        {
            validationMessage = string.Join("\n", PlayerAiTestLabScenarioFactory.Validate(
                director, templateScenario, SavedSettings));
        }

        private void SelectPlayer(int direction)
        {
            if (playerChoices.Length == 0) return;
            playerChoiceIndex = (playerChoiceIndex + direction + playerChoices.Length) % playerChoices.Length;
            SavedSettings.playerTankId = playerChoices[playerChoiceIndex].id;
            ValidateSelection();
        }

        private void ChangeTier(int direction)
        {
            SavedSettings.botTier = ((SavedSettings.botTier - 1 + direction + 10) % 10) + 1;
            ValidateSelection();
        }

        private void OnGUI()
        {
            EnsureStyles();
            if (!started) DrawSetupPanel();
            else if (!complete) DrawBattlePanel();
            else DrawCompletedPanel();
        }

        private void DrawSetupPanel()
        {
            float width = Mathf.Min(650f, Screen.width - 24f);
            float messageHeight = Mathf.Max(55f, (string.IsNullOrEmpty(validationMessage) ? bodyStyle : warningStyle)
                .CalcHeight(new GUIContent(string.IsNullOrEmpty(validationMessage)
                    ? "Состав корректен. Команды сохраняют единый состав по нации; техника ботов выбирается случайно внутри выбранного уровня."
                    : validationMessage), width - 44f));
            Rect panel = LocalTanksGuiTheme.ClampToScreen(
                new Rect((Screen.width - width) * 0.5f, 24f, width, 390f + messageHeight));
            ModalUiState.SetPointerRegion(this, panel);
            LocalTanksGuiTheme.DrawPanel(panel);
            float x = panel.x + 22f;
            float y = panel.y + 16f;
            float contentWidth = panel.width - 44f;
            GUI.Label(new Rect(x, y, contentWidth, 30f), "Тестовый бой: игрок против ИИ", titleStyle);
            y += 42f;
            GUI.Label(new Rect(x, y, contentWidth, 42f),
                "Выберите состав. Боты используют обычный боевой ИИ, а локальная LLM отключена, чтобы телеметрия отражала именно тактическое поведение.", bodyStyle);
            y += 54f;

            DrawSelectorRow(x, ref y, contentWidth, "Техника игрока",
                playerChoices.Length > 0 ? $"{playerChoices[playerChoiceIndex].displayName} · уровень {playerChoices[playerChoiceIndex].tier}" : "нет",
                () => SelectPlayer(-1), () => SelectPlayer(1));
            DrawCountRow(x, ref y, contentWidth, "Союзные боты", SavedSettings.alliedBotCount,
                0, BattleScenario.TeamSize - 1, value => SavedSettings.alliedBotCount = value);
            DrawCountRow(x, ref y, contentWidth, "Вражеские боты", SavedSettings.enemyBotCount,
                1, BattleScenario.TeamSize, value => SavedSettings.enemyBotCount = value);
            DrawSelectorRow(x, ref y, contentWidth, "Уровень техники ботов", SavedSettings.botTier.ToString(),
                () => ChangeTier(-1), () => ChangeTier(1));
            DrawSelectorRow(x, ref y, contentWidth, "Seed состава", SavedSettings.seed.ToString(),
                () => SavedSettings.seed = Math.Max(1, SavedSettings.seed - 1),
                () => SavedSettings.seed++);

            if (LocalTanksGuiTheme.Button(new Rect(x, y, 170f, 30f), "Случайный seed"))
                SavedSettings.seed = Math.Abs(Environment.TickCount == int.MinValue ? 1701 : Environment.TickCount);
            if (LocalTanksGuiTheme.Button(new Rect(panel.xMax - 192f, y, 170f, 30f),
                    "Начать бой", true)) StartBattle();
            y += 40f;
            GUI.Label(new Rect(x, y, contentWidth, messageHeight),
                string.IsNullOrEmpty(validationMessage)
                    ? "Состав корректен. Команды сохраняют единый состав по нации; техника ботов выбирается случайно внутри выбранного уровня."
                    : validationMessage,
                string.IsNullOrEmpty(validationMessage) ? bodyStyle : warningStyle);
        }

        private void DrawBattlePanel()
        {
            Rect panel = LocalTanksGuiTheme.ClampToScreen(new Rect(12f, Screen.height - 126f, 520f, 114f));
            ModalUiState.SetPointerRegion(this, panel);
            LocalTanksGuiTheme.DrawPanel(panel, false);
            GUI.Label(new Rect(panel.x + 14f, panel.y + 10f, panel.width - 28f, 24f),
                $"AI Test · {elapsed:0.0} с · Союзники {director.TeamAAlive} : {director.TeamBAlive} Враги", titleStyle);
            GUI.Label(new Rect(panel.x + 14f, panel.y + 38f, panel.width - 28f, 22f),
                $"Seed {SavedSettings.seed} · боты уровня {SavedSettings.botTier} · P — пауза", bodyStyle);
            if (LocalTanksGuiTheme.Button(new Rect(panel.x + 14f, panel.y + 70f, 150f, 30f),
                    paused ? "Продолжить" : "Пауза"))
                SetPaused(!paused);
            if (LocalTanksGuiTheme.Button(new Rect(panel.x + 174f, panel.y + 70f, 190f, 30f),
                    "Завершить и оценить", true))
                FinishBattle("StoppedByPlayer");
        }

        private void DrawCompletedPanel()
        {
            float width = Mathf.Min(720f, Screen.width - 24f);
            float height = Mathf.Min(520f, Screen.height - 48f);
            Rect panel = LocalTanksGuiTheme.ClampToScreen(
                new Rect((Screen.width - width) * 0.5f, 24f, width, height));
            ModalUiState.SetPointerRegion(this, panel);
            LocalTanksGuiTheme.DrawPanel(panel);
            float x = panel.x + 22f;
            float y = panel.y + 16f;
            float contentWidth = panel.width - 44f;
            GUI.Label(new Rect(x, y, contentWidth, 30f), "Бой завершён", titleStyle);
            y += 38f;
            GUI.Label(new Rect(x, y, contentWidth, 48f),
                $"Результат: {report?.outcome} · {elapsed:0.0} с · осталось {director.TeamAAlive}:{director.TeamBAlive}\n" +
                $"Автоматически найдено нарушений: {auditSession?.Auditor?.Issues.Count ?? 0}", bodyStyle);
            y += 58f;
            GUI.Label(new Rect(x, y, contentWidth, 22f),
                "Ваш комментарий: что показалось неправильным, в какой момент и у какого бота?", bodyStyle);
            y += 26f;
            comment = GUI.TextArea(new Rect(x, y, contentWidth, 190f), comment, 4000,
                LocalTanksGuiTheme.TextArea);
            y += 202f;
            if (LocalTanksGuiTheme.Button(new Rect(x, y, 190f, 32f),
                    "Сохранить комментарий", true)) SaveReport();
            if (LocalTanksGuiTheme.Button(new Rect(x + 202f, y, 170f, 32f), "Новый бой"))
            {
                Time.timeScale = 1f;
                SceneManager.LoadScene(gameObject.scene.name);
            }
            y += 42f;
            GUI.Label(new Rect(x, y, contentWidth, 64f),
                string.IsNullOrEmpty(savedDirectory)
                    ? "Отчёт ещё не сохранён."
                    : "Отчёт и телеметрия:\n" + savedDirectory,
                bodyStyle);
        }

        private void DrawSelectorRow(
            float x,
            ref float y,
            float width,
            string label,
            string value,
            Action previous,
            Action next)
        {
            GUI.Label(new Rect(x, y + 4f, 220f, 24f), label, bodyStyle);
            if (LocalTanksGuiTheme.Button(new Rect(x + width - 280f, y, 34f, 28f), "‹")) previous();
            GUI.Label(new Rect(x + width - 240f, y + 4f, 194f, 24f), value, bodyStyle);
            if (LocalTanksGuiTheme.Button(new Rect(x + width - 38f, y, 34f, 28f), "›")) next();
            y += 38f;
            ValidateSelection();
        }

        private void DrawCountRow(
            float x,
            ref float y,
            float width,
            string label,
            int value,
            int minimum,
            int maximum,
            Action<int> changed)
        {
            DrawSelectorRow(x, ref y, width, label, value.ToString(),
                () => changed(Mathf.Max(minimum, value - 1)),
                () => changed(Mathf.Min(maximum, value + 1)));
        }

        private void EnsureStyles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(LocalTanksGuiTheme.Title)
            {
                fontSize = 18
            };
            bodyStyle = new GUIStyle(LocalTanksGuiTheme.Body)
            {
                fontSize = 13
            };
            warningStyle = new GUIStyle(LocalTanksGuiTheme.Warning) { wordWrap = true };
        }
    }

    public static class PlayerAiTestBattleReportStore
    {
        public static string RootPath => Path.Combine(Application.persistentDataPath, "AI_TestBattles");

        public static string Save(
            PlayerAiTestBattleReport report,
            IReadOnlyList<PlayerAiTestTelemetrySample> samples,
            string auditReport,
            string transitionReport,
            string rootPath = null)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            string root = string.IsNullOrWhiteSpace(rootPath) ? RootPath : rootPath;
            string directory = Path.Combine(root, Sanitize(report.sessionId));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "report.json"), JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            using (StreamWriter writer = new StreamWriter(
                       Path.Combine(directory, "telemetry.jsonl"), false, new UTF8Encoding(false)))
            {
                foreach (PlayerAiTestTelemetrySample sample in samples ?? Array.Empty<PlayerAiTestTelemetrySample>())
                    writer.WriteLine(JsonUtility.ToJson(sample));
            }
            File.WriteAllText(Path.Combine(directory, "ai_audit.md"), auditReport ?? string.Empty, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, "transitions.md"), transitionReport ?? string.Empty, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, "analysis_brief.md"), BuildBrief(report, auditReport), new UTF8Encoding(false));
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "latest_report_path.txt"), directory, new UTF8Encoding(false));
            return directory;
        }

        private static string BuildBrief(PlayerAiTestBattleReport report, string auditReport)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("# Player vs AI test battle");
            text.AppendLine();
            text.AppendLine($"- Session: `{report.sessionId}`");
            text.AppendLine($"- Result: `{report.outcome}`");
            text.AppendLine($"- Duration: {report.durationSeconds:0.0} s");
            text.AppendLine($"- Seed: {report.seed}");
            text.AppendLine($"- Player tank: `{report.playerTankId}`");
            text.AppendLine($"- Bots: tier {report.botTier}, allies {report.alliedBotCount}, enemies {report.enemyBotCount}");
            text.AppendLine($"- Survivors: Team A {report.teamAAlive}, Team B {report.teamBAlive}");
            text.AppendLine($"- LLM enabled: {report.localLlmEnabled}");
            text.AppendLine();
            text.AppendLine("## Player comment");
            text.AppendLine();
            text.AppendLine(string.IsNullOrWhiteSpace(report.playerComment) ? "No comment supplied." : report.playerComment.Trim());
            text.AppendLine();
            text.AppendLine(auditReport ?? string.Empty);
            return text.ToString();
        }

        private static string Sanitize(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            return new string((value ?? "session").Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        }
    }
}
