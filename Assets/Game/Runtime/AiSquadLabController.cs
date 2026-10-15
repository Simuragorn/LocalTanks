using System;
using System.Collections;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace LocalTanks
{
    /// <summary>
    /// Runs the reproducible three-versus-three step between a duel and a full battle. It deliberately
    /// uses the production BattleDirector, map, tanks and auditor while keeping strategic planning out,
    /// so a group defect still has only six tactical actors to inspect.
    /// </summary>
    public sealed class AiSquadLabController : MonoBehaviour
    {
        private const float SampleInterval = 0.2f;
        private const int ExpectedBotCount = 6;
        private static int requestedCaseIndex;

        [SerializeField] private BattleDirector director;
        [SerializeField] private AiSquadLabCatalog catalog;
        [SerializeField] private bool showHiddenTanksForTesting = true;

        private CombatAiAuditSession auditSession;
        private AiSquadLabCase activeCase;
        private float startedAt;
        private float nextSampleAt;
        private bool ready;
        private bool paused;
        private bool startupPaused;
        private bool complete;
        private bool hasPreviousRandomState;
        private UnityEngine.Random.State previousRandomState;
        private string finalReport = string.Empty;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle issueStyle;

        public AiSquadLabCase ActiveCase => activeCase;
        public CombatAiBehaviorAuditor Auditor => auditSession?.Auditor;
        public bool IsReady => ready;
        public bool IsComplete => complete;
        public string FinalReport => finalReport;
        public int ActiveCaseIndex { get; private set; }
        public bool ShowHiddenTanksForTesting => showHiddenTanksForTesting;

        public void Configure(
            BattleDirector battleDirector,
            AiSquadLabCatalog caseCatalog,
            bool showHiddenTanks = true)
        {
            director = battleDirector;
            catalog = caseCatalog;
            showHiddenTanksForTesting = showHiddenTanks;
            FindAnyObjectByType<BattleVisibilitySettings>()?.Configure(showHiddenTanks);
        }

        private void Awake()
        {
            // Scene loading and Start coroutine scheduling can otherwise give the freshly spawned bots one
            // or two unobserved simulation steps. The number of warm-up steps depends on import/frame timing
            // and made the same seed start from a different pose after a reload.
            Time.timeScale = 0f;
            startupPaused = true;
            previousRandomState = UnityEngine.Random.state;
            hasPreviousRandomState = true;
            FindAnyObjectByType<BattleVisibilitySettings>()?.Configure(showHiddenTanksForTesting);
            int count = catalog?.cases?.Length ?? 0;
            if (count <= 0) return;
            int caseIndex = NormalizeIndex(requestedCaseIndex, count);
            int seed = catalog.cases[caseIndex].seed;
            UnityEngine.Random.InitState(seed);
            if (director != null && director.Scenario != null && director.Scenario.rules != null)
                director.Scenario.rules.randomSeed = seed;
        }

        private IEnumerator Start()
        {
            while (director != null && director.SpawnedMembers.Count < ExpectedBotCount)
                yield return null;

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
            while (elapsed >= nextSampleAt && nextSampleAt < activeCase.durationSeconds)
            {
                auditSession.Sample(nextSampleAt);
                nextSampleAt += SampleInterval;
            }
            if (elapsed < activeCase.durationSeconds) return;

            auditSession.Sample(activeCase.durationSeconds);
            finalReport = auditSession.Auditor.BuildReport(activeCase.durationSeconds);
            complete = true;
            Debug.Log($"AI Squad Lab '{activeCase.id}' completed.\n{finalReport}");
            SetPaused(true);
        }

        private void OnDestroy()
        {
            ModalUiState.ClearPointerRegion(this);
            auditSession?.Dispose();
            auditSession = null;
            if (hasPreviousRandomState) UnityEngine.Random.state = previousRandomState;
            if (startupPaused || paused) Time.timeScale = 1f;
        }

        public void ReloadCase(int index)
        {
            int count = catalog?.cases?.Length ?? 0;
            if (count == 0) return;
            requestedCaseIndex = NormalizeIndex(index, count);
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
            if (director == null) throw new InvalidOperationException("AI Squad Lab has no BattleDirector.");
            if (director.Scenario == null || director.Scenario.kind != BattleScenarioKind.AiSquadLab)
                throw new InvalidOperationException("AI Squad Lab must use an AiSquadLab battle scenario.");
            string[] errors = catalog != null ? catalog.Validate() : new[] { "Catalog is missing." };
            if (errors.Length > 0)
                throw new InvalidOperationException("AI Squad Lab configuration is invalid:\n- " +
                                                    string.Join("\n- ", errors));
            if (director.SpawnedMembers.Count != ExpectedBotCount)
                throw new InvalidOperationException(
                    $"AI Squad Lab expected {ExpectedBotCount} bots, found {director.SpawnedMembers.Count}.");

            ActiveCaseIndex = NormalizeIndex(index, catalog.cases.Length);
            requestedCaseIndex = ActiveCaseIndex;
            activeCase = catalog.cases[ActiveCaseIndex];
            TacticalCoverReservations.Clear();
            director.ReapplyVisibilitySettings();
            Physics2D.SyncTransforms();

            Camera camera = Camera.main;
            if (camera != null)
            {
                CameraFollow2D follow = camera.GetComponent<CameraFollow2D>();
                follow?.Configure(null);
                Vector2 centre = director.SpawnedMembers
                    .Aggregate(Vector2.zero, (sum, member) => sum + (Vector2)member.transform.position) /
                    ExpectedBotCount;
                camera.transform.position = new Vector3(centre.x, centre.y, camera.transform.position.z);
                follow?.SetZoom(18f, true);
                PlayerAimReticle reticle = camera.GetComponent<PlayerAimReticle>();
                if (reticle != null) reticle.enabled = false;
            }

            auditSession = new CombatAiAuditSession(director);
            startedAt = Time.time;
            nextSampleAt = 0f;
            ready = true;
            paused = true;
            complete = false;
            finalReport = string.Empty;
            startupPaused = false;
            Time.timeScale = 0f;
        }

        private static int NormalizeIndex(int index, int count)
        {
            return (index % count + count) % count;
        }

        private void OnGUI()
        {
            EnsureStyles();
            float width = Mathf.Min(540f, Screen.width - 24f);
            Rect panel = LocalTanksGuiTheme.ClampToScreen(new Rect(12f, 12f, width, ready ? 250f : 64f));
            ModalUiState.SetPointerRegion(this, panel);
            LocalTanksGuiTheme.DrawPanel(panel, false);
            if (!ready || activeCase == null)
            {
                GUI.Label(new Rect(panel.x + 14f, panel.y + 12f, panel.width - 28f, 28f),
                    "AI Squad Lab — подготовка боя 3×3…", titleStyle);
                return;
            }

            float elapsed = Mathf.Min(activeCase.durationSeconds, Mathf.Max(0f, Time.time - startedAt));
            int issues = auditSession?.Auditor?.Issues.Count ?? 0;
            GUI.Label(new Rect(panel.x + 14f, panel.y + 10f, panel.width - 28f, 28f),
                $"AI Squad Lab · {ActiveCaseIndex + 1}/{catalog.cases.Length} · {activeCase.displayName}", titleStyle);
            GUI.Label(new Rect(panel.x + 14f, panel.y + 40f, panel.width - 28f, 64f),
                activeCase.description +
                $"\nSeed: {activeCase.seed}   Время: {elapsed:0.0}/{activeCase.durationSeconds:0} с   Нарушения: {issues}",
                bodyStyle);

            float buttonY = panel.y + 112f;
            float gap = 6f;
            float buttonWidth = (panel.width - 28f - gap * 3f) / 4f;
            if (LocalTanksGuiTheme.Button(new Rect(panel.x + 14f, buttonY, buttonWidth, 30f),
                    paused ? "Продолжить" : "Пауза")) SetPaused(!paused);
            if (LocalTanksGuiTheme.Button(new Rect(panel.x + 14f + (buttonWidth + gap), buttonY,
                    buttonWidth, 30f), "Заново")) ReloadCase(ActiveCaseIndex);
            if (LocalTanksGuiTheme.Button(new Rect(panel.x + 14f + (buttonWidth + gap) * 2f, buttonY,
                    buttonWidth, 30f), "Предыдущий")) ReloadCase(ActiveCaseIndex - 1);
            if (LocalTanksGuiTheme.Button(new Rect(panel.x + 14f + (buttonWidth + gap) * 3f, buttonY,
                    buttonWidth, 30f), "Следующий")) ReloadCase(ActiveCaseIndex + 1);

            GUI.Label(new Rect(panel.x + 14f, buttonY + 38f, panel.width - 28f, 34f),
                complete ? "Прогон завершён." : paused ? "Пауза." : "Идёт наблюдение шести тактических ботов.",
                bodyStyle);
            GUI.Label(new Rect(panel.x + 14f, buttonY + 72f, panel.width - 28f, 58f),
                BuildRecentIssues(), issueStyle);
        }

        private string BuildRecentIssues()
        {
            CombatAiAuditIssue[] issues = auditSession?.Auditor?.Issues.TakeLast(2).ToArray() ??
                                          Array.Empty<CombatAiAuditIssue>();
            if (issues.Length == 0) return "Пока нарушений поведения не обнаружено.";
            StringBuilder result = new StringBuilder("Последние нарушения:\n");
            foreach (CombatAiAuditIssue issue in issues)
                result.AppendLine($"• {issue.Time:0.0}с {issue.Nickname}: {issue.Kind}");
            return result.ToString();
        }

        private void EnsureStyles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(LocalTanksGuiTheme.SectionTitle) { fontSize = 17 };
            bodyStyle = new GUIStyle(LocalTanksGuiTheme.Body) { fontSize = 13 };
            issueStyle = new GUIStyle(LocalTanksGuiTheme.Warning) { wordWrap = true };
        }
    }
}
