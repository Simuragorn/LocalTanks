using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LocalTanks
{
    public enum BotStrategicCoordinatorState
    {
        FallbackReady,
        WaitingForProvider,
        WaitingForTeamPlans,
        Processing,
        Complete
    }

    public sealed class BotStrategicCoordinator : MonoBehaviour
    {
        public static readonly BotStrategicIntentType[] SupportedIntents =
        {
            BotStrategicIntentType.AdvanceLane,
            BotStrategicIntentType.ScoutLane,
            BotStrategicIntentType.SupportAlly,
            BotStrategicIntentType.DefendBase,
            BotStrategicIntentType.HuntKnownTarget,
            BotStrategicIntentType.PressAttack,
            BotStrategicIntentType.Retreat,
            BotStrategicIntentType.Wait
        };

        private readonly List<BotStrategicBrain> brains = new List<BotStrategicBrain>();
        private readonly Queue<BotStrategicBrain> queue = new Queue<BotStrategicBrain>();
        private readonly HashSet<string> queuedBotIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> triggers = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<TankHealth, BotStrategicBrain> brainsByHealth = new Dictionary<TankHealth, BotStrategicBrain>();
        private readonly Dictionary<TankHealth, int> lastHitPoints = new Dictionary<TankHealth, int>();
        private BattleDirector director;
        private BattleStrategicPlanner planner;
        private ILocalLlmProvider provider;
        private LocalLlmRuntime observedRuntime;
        private bool processing;
        private bool initialBatchStarted;
        private int initialCompleted;
        private float nextAllowedDynamicRequestRealtime;

        public BotStrategicCoordinatorState State { get; private set; } = BotStrategicCoordinatorState.FallbackReady;
        public IReadOnlyList<BotStrategicBrain> Brains => brains;
        public bool InitialDecisionsFinished => initialBatchStarted && initialCompleted >= brains.Count && !processing && queue.Count == 0;
        public int PendingCount => queue.Count + (processing ? 1 : 0);

        public void Configure(
            BattleDirector battleDirector,
            BattleStrategicPlanner strategicPlanner,
            bool observeLocalRuntime = true)
        {
            director = battleDirector ?? throw new ArgumentNullException(nameof(battleDirector));
            planner = strategicPlanner ?? throw new ArgumentNullException(nameof(strategicPlanner));
            planner.PlanningChanged += OnPlanningChanged;
            brains.Clear();
            foreach (CombatTankAI ai in director.SpawnedMembers
                         .Where(item => item != null && !item.IsPlayerControlled)
                         .Select(item => item.GetComponent<CombatTankAI>())
                         .Where(item => item != null)
                         .OrderBy(item => director.GetEntityId(item.GetComponent<TeamMember>()), StringComparer.Ordinal))
            {
                string botId = director.GetEntityId(ai.GetComponent<TeamMember>());
                BotStrategicBrain brain = ai.GetComponent<BotStrategicBrain>();
                if (brain == null) brain = ai.gameObject.AddComponent<BotStrategicBrain>();
                brain.Configure(this, ai, botId);
                brains.Add(brain);
                TankHealth health = brain.GetComponent<TankHealth>();
                if (health != null)
                {
                    brainsByHealth[health] = brain;
                    lastHitPoints[health] = health.CurrentHitPoints;
                    health.Changed += OnBotHealthChanged;
                }
                ApplyFallback(brain, "battle-start", string.Empty, 0L);
            }

            State = BotStrategicCoordinatorState.FallbackReady;
            if (director.VisionSystem != null) director.VisionSystem.VisibilityChanged += OnVisibilityChanged;
            foreach (CaptureBase captureBase in director.Bases)
            {
                if (captureBase != null) captureBase.Changed += OnBaseChanged;
            }
            if (observeLocalRuntime) ObserveRuntime(LocalLlmRuntime.Instance);
        }

        public void RunInitialDecisionsWithProvider(ILocalLlmProvider decisionProvider)
        {
            if (decisionProvider == null || initialBatchStarted) return;
            provider = decisionProvider;
            StartInitialBatch();
        }

        public void RequestDecision(BotStrategicBrain brain, string trigger)
        {
            if (brain == null || provider == null || !brain.isActiveAndEnabled ||
                brain.GetComponent<TankHealth>()?.IsDestroyed == true) return;
            if (queuedBotIds.Contains(brain.BotId) || brain.RequestState == BotStrategicRequestState.Requesting) return;
            bool initialRequest = string.Equals(trigger, "initial-plan", StringComparison.Ordinal);
            if (!initialRequest)
            {
                if (!initialBatchStarted) return;
                LocalLlmSettings settings = observedRuntime?.Settings;
                bool requestAlreadyActive = processing || queue.Count > 0;
                if (!LocalLlmRequestPolicy.CanQueueDynamic(
                        settings,
                        trigger,
                        Time.realtimeSinceStartup,
                        nextAllowedDynamicRequestRealtime,
                        requestAlreadyActive))
                {
                    return;
                }

                float cooldown = settings != null
                    ? Mathf.Max(5f, settings.dynamicDecisionCooldownSeconds)
                    : 15f;
                nextAllowedDynamicRequestRealtime = Time.realtimeSinceStartup + cooldown;
            }
            queuedBotIds.Add(brain.BotId);
            triggers[brain.BotId] = trigger ?? "event";
            queue.Enqueue(brain);
            brain.SetQueued(trigger);
            if (!processing) StartCoroutine(ProcessQueue());
        }

        public void ApplyToTacticalAi(
            BotStrategicBrain brain,
            BotStrategicDecision decision,
            BotKnowledgeSnapshot snapshot)
        {
            if (brain?.TacticalAi == null || decision == null || snapshot == null) return;
            bool hasDestination = TryResolveDestination(brain, decision, snapshot, out Vector2 destination);
            TeamMember target = director.FindMember(decision.targetId);
            TeamMember ally = director.FindMember(decision.allyId);
            brain.TacticalAi.ApplyStrategicDecision(decision, hasDestination, destination, target, ally);
        }

        private void StartInitialBatch()
        {
            initialBatchStarted = true;
            initialCompleted = 0;
            foreach (BotStrategicBrain brain in brains) RequestDecision(brain, "initial-plan");
            if (brains.Count == 0) State = BotStrategicCoordinatorState.Complete;
        }

        private IEnumerator ProcessQueue()
        {
            processing = true;
            State = BotStrategicCoordinatorState.Processing;
            while (queue.Count > 0)
            {
                BotStrategicBrain brain = queue.Dequeue();
                queuedBotIds.Remove(brain.BotId);
                string trigger = triggers.TryGetValue(brain.BotId, out string value) ? value : "event";
                triggers.Remove(brain.BotId);
                if (brain == null || brain.GetComponent<TankHealth>()?.IsDestroyed == true)
                {
                    initialCompleted++;
                    continue;
                }

                string snapshotId = brain.NextSnapshotId(director.Scenario.id);
                BotKnowledgeSnapshot snapshot = BotKnowledgeSnapshotBuilder.Build(
                    snapshotId, director, brain.TacticalAi, planner, Time.time);
                brain.SetRequesting(snapshot, trigger);
                LocalLlmResponse response = null;
                yield return ExecuteProvider(
                    provider,
                    BotDecisionPromptBuilder.Build(snapshot, SupportedIntents),
                    result => response = result);
                long duration = response?.DurationMilliseconds ?? 0L;
                if (TryAccept(response, snapshot, out BotStrategicDecision decision, out string error))
                    brain.Apply(snapshot, decision, true, trigger, string.Empty, duration);
                else
                    brain.Apply(snapshot, BotStrategicFallback.Create(snapshot), false, trigger, error, duration);
                if (string.Equals(trigger, "initial-plan", StringComparison.Ordinal)) initialCompleted++;
                yield return null;
            }

            processing = false;
            State = InitialDecisionsFinished ? BotStrategicCoordinatorState.Complete : BotStrategicCoordinatorState.FallbackReady;
        }

        private bool TryAccept(
            LocalLlmResponse response,
            BotKnowledgeSnapshot snapshot,
            out BotStrategicDecision decision,
            out string error)
        {
            decision = null;
            if (response == null || !response.Succeeded)
            {
                error = response?.Error ?? "Bot decision provider returned no response.";
                return false;
            }

            if (!BotStrategicDecisionSchema.TryParse(response.Content, out BotStrategicDecision candidate, out error))
                return false;
            string[] errors = BotStrategicDecisionValidator.Validate(candidate, new BotDecisionValidationContext
            {
                requestId = snapshot.snapshotId,
                botId = snapshot.botId,
                knownLaneIds = snapshot.knownLaneIds,
                knownZoneIds = snapshot.knownZoneIds,
                knownTargetIds = snapshot.knownTargetIds,
                allyIds = snapshot.allyIds,
                allowedIntents = SupportedIntents,
                allowChat = false,
                maximumDesiredDistance = Mathf.Max(1f, snapshot.self.viewRange)
            });
            if (errors.Length > 0)
            {
                error = string.Join(" ", errors);
                return false;
            }

            decision = candidate;
            error = string.Empty;
            return true;
        }

        private void ApplyFallback(BotStrategicBrain brain, string trigger, string error, long duration)
        {
            string snapshotId = $"{director.Scenario.id}:{brain.BotId}:fallback";
            BotKnowledgeSnapshot snapshot = BotKnowledgeSnapshotBuilder.Build(
                snapshotId, director, brain.TacticalAi, planner, Time.time);
            brain.Apply(snapshot, BotStrategicFallback.Create(snapshot), false, trigger, error, duration);
        }

        private bool TryResolveDestination(
            BotStrategicBrain brain,
            BotStrategicDecision decision,
            BotKnowledgeSnapshot snapshot,
            out Vector2 destination)
        {
            destination = Vector2.zero;
            TeamMember member = brain.GetComponent<TeamMember>();
            switch (decision.intent)
            {
                case BotStrategicIntentType.AdvanceLane:
                case BotStrategicIntentType.ScoutLane:
                    TacticalLaneDescriptor lane = director.TacticalMap?.lanes?.FirstOrDefault(item =>
                        item != null && item.id == decision.laneId);
                    if (lane == null || director.NavigationMap == null) return false;
                    Vector2Int cell = decision.intent == BotStrategicIntentType.ScoutLane
                        ? lane.crossingCell
                        : member.Team == TeamId.TeamA ? lane.defenseB : lane.defenseA;
                    destination = director.NavigationMap.CellToWorld(cell);
                    return true;
                case BotStrategicIntentType.DefendBase:
                case BotStrategicIntentType.Retreat:
                    CaptureBase ownBase = director.Bases.FirstOrDefault(item => item != null &&
                        item.Owner == member.Team);
                    if (ownBase == null) return false;
                    destination = ownBase.transform.position;
                    return true;
                case BotStrategicIntentType.HuntKnownTarget:
                case BotStrategicIntentType.PressAttack:
                    BotEnemyKnowledge contact = snapshot.enemies?.FirstOrDefault(item =>
                        item != null && item.memberId == decision.targetId && item.hasKnownPosition);
                    if (contact == null) return false;
                    destination = new Vector2(contact.positionX, contact.positionY);
                    return true;
                default:
                    return false;
            }
        }

        private void ObserveRuntime(LocalLlmRuntime runtime)
        {
            if (runtime == null) return;
            observedRuntime = runtime;
            observedRuntime.StateChanged += OnRuntimeStateChanged;
            if (runtime.State == LocalLlmRuntimeState.Ready)
            {
                provider = runtime.Provider;
                TryStartAfterPlans();
            }
            else
            {
                State = BotStrategicCoordinatorState.WaitingForProvider;
            }
        }

        private void OnRuntimeStateChanged(LocalLlmRuntimeState state, string message)
        {
            if (state == LocalLlmRuntimeState.Ready)
            {
                provider = observedRuntime?.Provider;
                TryStartAfterPlans();
            }
            else if (state == LocalLlmRuntimeState.Disabled || state == LocalLlmRuntimeState.Unavailable)
            {
                State = BotStrategicCoordinatorState.FallbackReady;
            }
        }

        private void OnPlanningChanged(BattleStrategicPlanner source)
        {
            if (source != null && source.RemotePlanningFinished)
            {
                foreach (BotStrategicBrain brain in brains) ApplyFallback(brain, "team-plan-ready", string.Empty, 0L);
                TryStartAfterPlans();
            }
        }

        private void OnBotHealthChanged(TankHealth health)
        {
            if (health == null || !brainsByHealth.TryGetValue(health, out BotStrategicBrain brain)) return;
            int previous = lastHitPoints.TryGetValue(health, out int value) ? value : health.MaximumHitPoints;
            lastHitPoints[health] = health.CurrentHitPoints;
            int loss = previous - health.CurrentHitPoints;
            if (loss >= Mathf.Max(1, Mathf.CeilToInt(health.MaximumHitPoints * 0.08f)))
                RequestDecision(brain, "significant-damage");
        }

        private void OnVisibilityChanged(TeamId viewingTeam, TeamMember target, bool visible)
        {
            if (target == null || viewingTeam == TeamId.Neutral || provider == null) return;
            Vector2 knownPosition;
            if (visible)
                knownPosition = target.transform.position;
            else if (director.VisionSystem != null &&
                     director.VisionSystem.TryGetContact(viewingTeam, target, out VisionContact contact) && contact.HasMemory)
                knownPosition = contact.LastKnownPosition;
            else
                return;
            BotStrategicBrain responder = brains
                .Where(item => item != null && item.GetComponent<TeamMember>().Team == viewingTeam &&
                               item.GetComponent<TankHealth>()?.IsDestroyed == false)
                .OrderBy(item => Vector2.SqrMagnitude((Vector2)item.transform.position - knownPosition))
                .ThenBy(item => item.BotId, StringComparer.Ordinal)
                .FirstOrDefault();
            RequestDecision(responder, visible ? "contact-spotted" : "contact-lost");
        }

        private void OnBaseChanged(CaptureBase captureBase)
        {
            if (captureBase == null || provider == null || captureBase.State != BaseCaptureState.Capturing) return;
            foreach (TeamId team in new[] { captureBase.Owner, captureBase.CapturingTeam }
                         .Where(item => item != TeamId.Neutral)
                         .Distinct())
            {
                BotStrategicBrain responder = brains
                    .Where(item => item != null && item.GetComponent<TeamMember>().Team == team &&
                                   item.GetComponent<TankHealth>()?.IsDestroyed == false)
                    .OrderByDescending(item => item.CurrentSnapshot?.plan?.isReserve == true)
                    .ThenBy(item => item.BotId, StringComparer.Ordinal)
                    .FirstOrDefault();
                RequestDecision(responder, "base-capture-changed");
            }
        }

        private void TryStartAfterPlans()
        {
            if (provider == null || initialBatchStarted) return;
            if (!planner.RemotePlanningFinished)
            {
                State = BotStrategicCoordinatorState.WaitingForTeamPlans;
                return;
            }
            LocalLlmSettings settings = observedRuntime?.Settings;
            if (!LocalLlmRequestPolicy.ShouldRunInitialBotRequests(settings))
            {
                initialBatchStarted = true;
                initialCompleted = brains.Count;
                nextAllowedDynamicRequestRealtime = Time.realtimeSinceStartup +
                    Mathf.Max(5f, settings.dynamicDecisionCooldownSeconds);
                State = BotStrategicCoordinatorState.Complete;
                return;
            }
            StartInitialBatch();
        }

        private static IEnumerator ExecuteProvider(
            ILocalLlmProvider requestProvider,
            LocalLlmRequest request,
            Action<LocalLlmResponse> completed)
        {
            IEnumerator routine;
            try
            {
                routine = requestProvider.Generate(request, completed);
            }
            catch (Exception exception)
            {
                completed?.Invoke(LocalLlmResponse.Failure($"Provider failed before request: {exception.Message}"));
                yield break;
            }
            if (routine == null)
            {
                completed?.Invoke(LocalLlmResponse.Failure("Provider returned no request coroutine."));
                yield break;
            }
            while (true)
            {
                bool moved;
                object current = null;
                try
                {
                    moved = routine.MoveNext();
                    if (moved) current = routine.Current;
                }
                catch (Exception exception)
                {
                    completed?.Invoke(LocalLlmResponse.Failure($"Provider request failed: {exception.Message}"));
                    yield break;
                }
                if (!moved) yield break;
                yield return current;
            }
        }

        private void OnDestroy()
        {
            if (planner != null) planner.PlanningChanged -= OnPlanningChanged;
            if (observedRuntime != null) observedRuntime.StateChanged -= OnRuntimeStateChanged;
            if (director?.VisionSystem != null) director.VisionSystem.VisibilityChanged -= OnVisibilityChanged;
            if (director != null)
            {
                foreach (CaptureBase captureBase in director.Bases)
                {
                    if (captureBase != null) captureBase.Changed -= OnBaseChanged;
                }
            }
            foreach (TankHealth health in brainsByHealth.Keys.ToArray())
            {
                if (health != null) health.Changed -= OnBotHealthChanged;
            }
        }
    }
}
