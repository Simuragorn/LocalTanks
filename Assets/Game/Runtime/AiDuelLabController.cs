using System;
using System.Collections;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace LocalTanks
{
    public sealed class AiDuelLabController : MonoBehaviour
    {
        private const float SampleInterval = 0.2f;
        private static int requestedCaseIndex;

        [SerializeField] private BattleDirector director;
        [SerializeField] private AiDuelLabCatalog catalog;

        private CombatAiAuditSession auditSession;
        private AiDuelLabCase activeCase;
        private float startedAt;
        private float nextSampleAt;
        private bool ready;
        private bool paused;
        private bool complete;
        private bool hasPreviousRandomState;
        private UnityEngine.Random.State previousRandomState;
        private string finalReport = string.Empty;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle issueStyle;

        public AiDuelLabCase ActiveCase => activeCase;
        public CombatAiBehaviorAuditor Auditor => auditSession?.Auditor;
        public bool IsReady => ready;
        public bool IsPaused => paused;
        public bool IsComplete => complete;
        public string FinalReport => finalReport;
        public int ActiveCaseIndex { get; private set; }

        private void Awake()
        {
            previousRandomState = UnityEngine.Random.state;
            hasPreviousRandomState = true;
            // The scene may have been generated before this laboratory opted in, so the runtime
            // value is applied here as well as at build time.
            FindAnyObjectByType<BattleVisibilitySettings>()?.Configure(showHiddenTanksForTesting);
            int count = catalog?.cases?.Length ?? 0;
            if (count <= 0) return;
            int caseIndex = (requestedCaseIndex % count + count) % count;
            UnityEngine.Random.InitState(catalog.cases[caseIndex].seed);
        }

        /// <summary>
        /// The duel laboratory is a developer scene whose purpose is watching two bots maneuver, so
        /// this scene value opts into the semi-transparent silhouette for unseen tanks. It is
        /// written by the scene builder and can be changed per scene without touching code.
        /// </summary>
        [SerializeField] private bool showHiddenTanksForTesting = true;

        public bool ShowHiddenTanksForTesting => showHiddenTanksForTesting;

        public void Configure(BattleDirector battleDirector, AiDuelLabCatalog caseCatalog, bool showHiddenTanks = true)
        {
            director = battleDirector;
            catalog = caseCatalog;
            showHiddenTanksForTesting = showHiddenTanks;
            FindAnyObjectByType<BattleVisibilitySettings>()?.Configure(showHiddenTanks);
        }

        private IEnumerator Start()
        {
            while (director != null && director.SpawnedMembers.Count < 2)
            {
                yield return null;
            }

            // The setting may have changed after the tanks were spawned, so apply it explicitly
            // instead of relying on the order between Awake and BattleDirector.SpawnAll.
            director?.ReapplyVisibilitySettings();
            InitializeCase(requestedCaseIndex);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.rKey.wasPressedThisFrame &&
                    (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed))
                    ReloadCase(ActiveCaseIndex);
                else if (keyboard.nKey.wasPressedThisFrame) ReloadCase(ActiveCaseIndex + 1);
                else if (keyboard.bKey.wasPressedThisFrame) ReloadCase(ActiveCaseIndex - 1);
                else if (keyboard.spaceKey.wasPressedThisFrame) SetPaused(!paused);
            }

            if (!ready || paused || complete || auditSession == null) return;
            float elapsed = Time.time - startedAt;
            if (elapsed >= nextSampleAt)
            {
                auditSession.Sample(elapsed);
                nextSampleAt += SampleInterval;
            }
            if (elapsed >= activeCase.durationSeconds)
            {
                auditSession.Sample(activeCase.durationSeconds);
                finalReport = auditSession.Auditor.BuildReport(activeCase.durationSeconds);
                complete = true;
                Debug.Log($"AI Duel Lab '{activeCase.id}' completed.\n{finalReport}");
                SetPaused(true);
            }
        }

        private void OnDestroy()
        {
            ModalUiState.ClearPointerRegion(this);
            auditSession?.Dispose();
            auditSession = null;
            if (hasPreviousRandomState) UnityEngine.Random.state = previousRandomState;
            if (paused) Time.timeScale = 1f;
        }

        public void ReloadCase(int index)
        {
            int count = catalog?.cases?.Length ?? 0;
            if (count == 0) return;
            requestedCaseIndex = (index % count + count) % count;
            Time.timeScale = 1f;
            SceneManager.LoadScene(gameObject.scene.name);
        }

        public void SetPaused(bool value)
        {
            paused = value;
            Time.timeScale = paused ? 0f : 1f;
        }

        private void InitializeCase(int index)
        {
            if (director == null) throw new InvalidOperationException("AI Duel Lab has no BattleDirector.");
            if (director.Scenario == null || director.Scenario.kind != BattleScenarioKind.AiDuelLab)
                throw new InvalidOperationException("AI Duel Lab must use an AiDuelLab battle scenario.");
            string[] errors = catalog != null ? catalog.Validate(director.NavigationMap) : new[] { "Catalog is missing." };
            if (errors.Length > 0)
                throw new InvalidOperationException("AI Duel Lab configuration is invalid:\n- " + string.Join("\n- ", errors));

            ActiveCaseIndex = (index % catalog.cases.Length + catalog.cases.Length) % catalog.cases.Length;
            requestedCaseIndex = ActiveCaseIndex;
            activeCase = catalog.cases[ActiveCaseIndex];
            TacticalCoverReservations.Clear();

            TeamMember teamA = director.SpawnedMembers.Single(item => item.Team == TeamId.TeamA);
            TeamMember teamB = director.SpawnedMembers.Single(item => item.Team == TeamId.TeamB);
            ConfigureBot(teamA, activeCase.teamAStart, activeCase.teamAHeading, activeCase.teamARoute, 0f);
            ConfigureBot(teamB, activeCase.teamBStart, activeCase.teamBHeading, activeCase.teamBRoute, 0.11f);
            Physics2D.SyncTransforms();

            Camera camera = Camera.main;
            if (camera != null)
            {
                CameraFollow2D follow = camera.GetComponent<CameraFollow2D>();
                follow?.Configure(null);
                Vector2 midpoint = ((Vector2)teamA.transform.position + (Vector2)teamB.transform.position) * 0.5f;
                camera.transform.position = new Vector3(midpoint.x, midpoint.y, camera.transform.position.z);
                follow?.SetZoom(15f, true);
                PlayerAimReticle reticle = camera.GetComponent<PlayerAimReticle>();
                if (reticle != null) reticle.enabled = false;
            }

            auditSession = new CombatAiAuditSession(director);
            startedAt = Time.time;
            nextSampleAt = 0f;
            ready = true;
            paused = false;
            complete = false;
            finalReport = string.Empty;
        }

        private void ConfigureBot(
            TeamMember member,
            Vector2Int startCell,
            float heading,
            Vector2Int[] routeCells,
            float decisionPhase)
        {
            NavigationMap map = director.NavigationMap;
            Vector2 position = map.CellToWorld(startCell);
            member.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, heading));
            Rigidbody2D body = member.GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.position = position;
                body.rotation = heading;
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
            }
            TankMotor motor = member.GetComponent<TankMotor>();
            motor?.StopImmediately();
            NavigationAgent navigation = member.GetComponent<NavigationAgent>();
            CombatTankAI ai = member.GetComponent<CombatTankAI>();
            Vector2[] route = routeCells.Select(map.CellToWorld).ToArray();
            ai.Configure(director.VisionSystem, navigation, BattleLane.Center, CombatAiRole.Assault, route, decisionPhase);
        }

        private void OnGUI()
        {
            EnsureStyles();
            float width = Mathf.Min(510f, Screen.width - 24f);
            string description = activeCase != null
                ? activeCase.description
                : string.Empty;
            float descriptionHeight = ready && activeCase != null
                ? Mathf.Max(46f, bodyStyle.CalcHeight(new GUIContent(
                    description + $"\nSeed: {activeCase.seed}   Время: {activeCase.durationSeconds:0} с"), width - 28f))
                : 30f;
            float panelHeight = ready && activeCase != null ? 190f + descriptionHeight : 64f;
            Rect panel = LocalTanksGuiTheme.ClampToScreen(new Rect(12f, 12f, width, panelHeight));
            ModalUiState.SetPointerRegion(this, panel);
            LocalTanksGuiTheme.DrawPanel(panel, false);
            if (!ready || activeCase == null)
            {
                GUI.Label(new Rect(panel.x + 14f, panel.y + 12f, panel.width - 28f, 28f),
                    "AI Duel Lab — подготовка сценария…", titleStyle);
                return;
            }

            float elapsed = Mathf.Min(activeCase.durationSeconds, Mathf.Max(0f, Time.time - startedAt));
            int issueCount = auditSession?.Auditor?.Issues.Count ?? 0;
            GUI.Label(new Rect(panel.x + 14f, panel.y + 10f, panel.width - 28f, 28f),
                $"AI Duel Lab · {ActiveCaseIndex + 1}/{catalog.cases.Length} · {activeCase.displayName}", titleStyle);
            GUI.Label(new Rect(panel.x + 14f, panel.y + 40f, panel.width - 28f, descriptionHeight),
                activeCase.description + $"\nSeed: {activeCase.seed}   Время: {elapsed:0.0}/{activeCase.durationSeconds:0} с   Нарушения: {issueCount}", bodyStyle);

            float buttonY = panel.y + 48f + descriptionHeight;
            float buttonGap = 6f;
            float buttonWidth = (panel.width - 28f - buttonGap * 3f) / 4f;
            if (LocalTanksGuiTheme.Button(new Rect(panel.x + 14f, buttonY, buttonWidth, 30f),
                    paused ? "Продолжить" : "Пауза"))
                SetPaused(!paused);
            if (LocalTanksGuiTheme.Button(new Rect(panel.x + 14f + (buttonWidth + buttonGap), buttonY,
                    buttonWidth, 30f), "Заново"))
                ReloadCase(ActiveCaseIndex);
            if (LocalTanksGuiTheme.Button(new Rect(panel.x + 14f + (buttonWidth + buttonGap) * 2f, buttonY,
                    buttonWidth, 30f), "Предыдущий"))
                ReloadCase(ActiveCaseIndex - 1);
            if (LocalTanksGuiTheme.Button(new Rect(panel.x + 14f + (buttonWidth + buttonGap) * 3f, buttonY,
                    buttonWidth, 30f), "Следующий"))
                ReloadCase(ActiveCaseIndex + 1);

            string status = complete ? "Сценарий завершён и поставлен на паузу." :
                paused ? "Пауза." : "Идёт автоматическое наблюдение.";
            float statusY = buttonY + 38f;
            GUI.Label(new Rect(panel.x + 14f, statusY, panel.width - 28f, 38f),
                status + "   Space — пауза, R — заново, B/N — сценарии.", bodyStyle);
            string issues = BuildRecentIssues();
            GUI.Label(new Rect(panel.x + 14f, statusY + 42f, panel.width - 28f,
                panel.yMax - statusY - 54f), issues, issueStyle);
        }

        private string BuildRecentIssues()
        {
            CombatAiAuditIssue[] issues = auditSession?.Auditor?.Issues.TakeLast(3).ToArray() ?? Array.Empty<CombatAiAuditIssue>();
            if (issues.Length == 0) return "Пока нарушений поведения не обнаружено.";
            StringBuilder text = new StringBuilder("Последние нарушения:\n");
            foreach (CombatAiAuditIssue issue in issues)
                text.AppendLine($"• {issue.Time:0.0}с {issue.Nickname}: {DescribeIssue(issue.Kind)}");
            return text.ToString();
        }

        private static string DescribeIssue(CombatAiAuditIssueKind kind)
        {
            switch (kind)
            {
                case CombatAiAuditIssueKind.TacticalTaskThrashing: return "мечется между боем и укрытием";
                case CombatAiAuditIssueKind.FiringLineThrashing: return "повторяет поиск прострела";
                case CombatAiAuditIssueKind.RearExposedToVisibleTarget: return "долго держит корму к противнику";
                case CombatAiAuditIssueKind.ReverseWithoutRetreatTask: return "продолжает ехать назад после отмены отхода";
                case CombatAiAuditIssueKind.MovementStall: return "не движется при активном маршруте";
                case CombatAiAuditIssueKind.Circling: return "кружит на месте";
                case CombatAiAuditIssueKind.CombatInactivity: return "не атакует доступную цель";
                default: return kind.ToString();
            }
        }

        private void EnsureStyles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(LocalTanksGuiTheme.SectionTitle)
            {
                fontSize = 17
            };
            bodyStyle = new GUIStyle(LocalTanksGuiTheme.Body)
            {
                fontSize = 13
            };
            issueStyle = new GUIStyle(LocalTanksGuiTheme.Warning) { wordWrap = true };
        }
    }
}
