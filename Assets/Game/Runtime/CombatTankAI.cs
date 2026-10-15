using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LocalTanks
{
    [RequireComponent(typeof(TeamMember), typeof(NavigationAgent), typeof(TankMotor))]
    [DefaultExecutionOrder(-100)]
    public sealed class CombatTankAI : MonoBehaviour
    {
        private const float DecisionInterval = 0.22f;
        private const float AimToleranceDegrees = 4f;
        private const float DestinationRefreshDistance = 0.8f;
        private const float AimRecoveryDelay = 3f;
        private const float FailedPositionMemorySeconds = 10f;
        private const float RecentPenetrationDangerSeconds = 6f;
        private const float MaximumReverseCoverPathLength = 6f;
        private const float MaximumReverseFiringLinePathLength = 6f;
        private const float DefensiveRearDangerAngle = 115f;
        private const float CloseCombatDisengageDistance = 4.5f;
        private const float CloseCombatDisengageDuration = 10f;
        private const float CloseCombatRetryCooldown = 2f;
        /// <summary>
        /// After a protective task ends, the bot must fight for at least this long before it may
        /// start another proactive cover cycle of the same or lower priority. Without this
        /// commitment a bot that has just left cover immediately re-decides to seek it again,
        /// which shows up as cover/combat task thrashing in the behavior audit.
        /// </summary>
        private const float ProactiveCoverCommitmentSeconds = 2.5f;
        private static double lastCoordinatedFixedTime = double.NegativeInfinity;

        [SerializeField] private TeamMember member;
        [SerializeField] private TeamVisionSystem visionSystem;
        [SerializeField] private NavigationAgent navigation;
        [SerializeField] private TankMotor motor;
        [SerializeField] private TurretAiming turret;
        [SerializeField] private WeaponController weapon;
        [SerializeField] private TankHealth health;
        [SerializeField] private TankArmor armor;
        [SerializeField] private BotIdentityComponent identity;
        [SerializeField] private BattleLane lane;
        [SerializeField] private CombatAiRole role;

        private Vector2[] routePoints = Array.Empty<Vector2>();
        private int routeIndex;
        private float nextDecisionTime;
        private TeamMember target;
        private Vector2 lastKnownTargetPosition;
        private bool hasRememberedTarget;
        private bool investigationDestinationSet;
        private bool routeComplete;

        /// <summary>The route this bot was given, in world units. Exposed for diagnostics and tests.</summary>
        public Vector2[] RoutePoints => routePoints;

        /// <summary>How far along its route the bot has walked. Exposed for diagnostics and tests.</summary>
        public int RouteIndex => routeIndex;

        /// <summary>
        /// How many route destinations the bot has actually assigned, and how many times the strategic
        /// layer took the decision away from it. Two counters, because a route that is never continued and
        /// a route that is continually overridden produce the same symptom - a bot that does not move along
        /// its route - and telling them apart by reading code has already cost several attempts. Merely
        /// re-evaluating an unfinished destination does not increment this counter.
        /// </summary>
        public int RouteContinuationCount { get; private set; }

        /// <summary>How many times a strategic decision replaced the route destination.</summary>
        public int StrategicOverrideCount { get; private set; }

        /// <summary>How many times the tank was in the stall-recovery manoeuvre.</summary>
        public int StallRecoveryCount { get; private set; }
        private float preferredDistance;
        private Vector2 repositionDestination;
        private float repositionExpiresAt;
        private float nextRepositionTime;
        private float firingLineClearSince = -1f;
        private float lastTargetVisibleTime = float.NegativeInfinity;
        private float defaultPreferredDistance;
        private BotStrategicDecision strategicDecision;
        private float strategicDecisionExpiresAt;
        private bool strategicHasDestination;
        private Vector2 strategicDestination;
        private TeamMember strategicTarget;
        private TeamMember strategicAlly;
        private float aimingBlockedSince = -1f;
        private float targetLockedUntil;
        private readonly Dictionary<Vector2Int, float> failedFiringPositions = new Dictionary<Vector2Int, float>();
        private LineOfFireAssessment currentLineOfFire;
        private TacticalFiringPositionSolution firingPositionSolution;
        private bool hasFiringPositionSolution;
        private string lastTacticalPositionResult = "Не требовалось";
        private readonly ThreatMemory threatMemory = new ThreatMemory();
        private ArmorUseProfile armorUseProfile;
        private TacticalBehaviorProfile tacticalBehaviorProfile;
        private ThreatAnalysis threatAnalysis;
        private TacticalMovementPurpose tacticalMovementPurpose;
        private TacticalDecisionPriority tacticalDecisionPriority;
        private string tacticalDecisionReason = "Нет активной тактической задачи";
        private float tacticalDecisionExpiresAt;
        private TacticalCoverSolution coverSolution;
        private bool hasCoverPosition;
        private Vector2 coverPosition;
        private bool hasCoverFiringPosition;
        private Vector2 coverFiringPosition;
        private float coverValidUntil;
        private bool coverRequested;
        private string coverRequestReason;
        private TacticalDecisionPriority coverRequestPriority;
        private float nextCoverDecisionTime;
        private TacticalDecisionPriority lastProtectiveTaskPriority;
        private float proactiveCoverAllowedAt;
        private float coverShotOpportunityUntil = float.NegativeInfinity;
        private float holdCoverUntil;
        private float coverReachedAt;
        private float peekReachedAt = float.NegativeInfinity;
        private bool holdingTacticalCover;
        private bool returnForwardFallbackUsed;
        private bool lastCoverReturnStartedInReverse;
        private int completedCoverCycleShots;
        private Vector2 primaryThreatDirection;
        private float lastPenetratingHitTime = float.NegativeInfinity;
        private float defensiveRearExposureSince = float.NegativeInfinity;
        private TeamMember closeCombatTrackedTarget;
        private Vector2 lastCloseCombatTargetDirection;
        private float lastCloseCombatSampleTime = float.NegativeInfinity;
        private float targetBearingRateDegrees;
        private float nextCloseCombatTime;
        private Vector2 closeCombatDestination;
        private BattleDirector battleDirector;
        private Rigidbody2D body;

        private float Now => battleDirector != null ? (float)battleDirector.SimulationTime : Time.time;
        private Vector2 PhysicsPosition => body != null ? body.position : (Vector2)transform.position;

        public CombatAiState State { get; private set; } = CombatAiState.Deploy;
        public FireBlockReason CurrentFireBlockReason { get; private set; } = FireBlockReason.NoVisibleTarget;
        public TeamMember CurrentTarget => target;
        public Vector2 LastKnownTargetPosition => lastKnownTargetPosition;
        public bool HasRememberedTarget => hasRememberedTarget;
        public float PreferredDistance => preferredDistance;
        public Vector2 RepositionDestination => repositionDestination;
        public BattleLane Lane => lane;
        public CombatAiRole Role => role;
        public BotIdentity Identity => identity != null ? identity.Identity : null;
        public BotStrategicDecision StrategicDecision => strategicDecision;
        public bool HasActiveStrategicDecision => strategicDecision != null && Now < strategicDecisionExpiresAt;
        public LineOfFireAssessment CurrentLineOfFire => currentLineOfFire;
        public bool HasFiringPositionSolution => hasFiringPositionSolution;
        public TacticalFiringPositionSolution FiringPositionSolution => firingPositionSolution;
        public string LastTacticalPositionResult => lastTacticalPositionResult;
        public ArmorUseProfile ArmorProfileUsage => armorUseProfile;
        public TacticalMovementPurpose TacticalMovementPurpose => tacticalMovementPurpose;
        public TacticalDecisionPriority TacticalDecisionPriority => tacticalDecisionPriority;
        public string TacticalDecisionReason => tacticalDecisionReason;
        public int ActiveThreatCount => threatMemory.ActiveCount(Now);
        public int ActiveThreatSectorCount => threatAnalysis.SectorCount;
        public bool IsUnderCrossfire => threatAnalysis.IsCrossfire;
        public float CrossfireAngle => threatAnalysis.CrossfireAngle;
        public Vector2 SecondaryThreatDirection => threatAnalysis.SecondaryDirection;
        public Vector2 PrimaryThreatDirection => primaryThreatDirection;
        public bool HasCoverPosition => hasCoverPosition;
        public Vector2 CoverPosition => coverPosition;
        public bool HasCoverFiringPosition => hasCoverFiringPosition;
        public Vector2 CoverFiringPosition => coverFiringPosition;
        public TacticalBehaviorProfile TacticalBehavior => tacticalBehaviorProfile;
        public bool LastCoverReturnStartedInReverse => lastCoverReturnStartedInReverse;
        public int CompletedCoverCycleShots => completedCoverCycleShots;
        public float CurrentTargetDistance => target != null
            ? Vector2.Distance(transform.position, target.transform.position)
            : float.PositiveInfinity;
        public float TurretAimErrorDegrees => turret != null ? turret.AimErrorDegrees : 180f;
        public float TargetBearingRateDegrees => targetBearingRateDegrees;
        public bool IsBreakingCloseOrbit => tacticalMovementPurpose == TacticalMovementPurpose.BreakCloseOrbit;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (health != null)
            {
                health.Destroyed -= OnDestroyed;
                health.Destroyed += OnDestroyed;
            }
            if (armor != null)
            {
                armor.ImpactResolved -= OnImpactResolved;
                armor.ImpactResolved += OnImpactResolved;
            }
        }

        private void OnDisable()
        {
            if (health != null)
            {
                health.Destroyed -= OnDestroyed;
            }
            if (armor != null)
            {
                armor.ImpactResolved -= OnImpactResolved;
            }

            TacticalCoverReservations.Release(GetEntityId().ToString());
            navigation?.SetPaused(true);
        }

        public void Configure(
            TeamVisionSystem teamVision,
            NavigationAgent navigationAgent,
            BattleLane assignedLane,
            CombatAiRole assignedRole,
            Vector2[] tacticalRoute,
            float decisionPhase)
        {
            ResolveReferences();
            visionSystem = teamVision;
            navigation = navigationAgent;
            lane = assignedLane;
            role = assignedRole;
            routePoints = tacticalRoute ?? Array.Empty<Vector2>();
            routeIndex = 0;
            routeComplete = false;
            defaultPreferredDistance = CombatAiPolicy.PreferredDistance(member != null ? member.Definition : null, role);
            preferredDistance = defaultPreferredDistance;
            nextDecisionTime = Now + Mathf.Repeat(decisionPhase, DecisionInterval);
            State = CombatAiState.Deploy;
            target = null;
            hasRememberedTarget = false;
            investigationDestinationSet = false;
            lastTargetVisibleTime = float.NegativeInfinity;
            strategicDecision = null;
            strategicHasDestination = false;
            strategicTarget = null;
            strategicAlly = null;
            aimingBlockedSince = -1f;
            firingLineClearSince = -1f;
            targetLockedUntil = 0f;
            failedFiringPositions.Clear();
            currentLineOfFire = default;
            hasFiringPositionSolution = false;
            lastTacticalPositionResult = "Не требовалось";
            threatMemory.Clear();
            armorUseProfile = ArmorUseProfile.From(member != null ? member.Definition : null);
            tacticalBehaviorProfile = TacticalBehaviorProfile.From(
                member != null ? member.Definition : null,
                identity?.Identity?.personality);
            threatAnalysis = default;
            tacticalMovementPurpose = TacticalMovementPurpose.None;
            tacticalDecisionPriority = TacticalDecisionPriority.None;
            tacticalDecisionReason = "Нет активной тактической задачи";
            tacticalDecisionExpiresAt = 0f;
            hasCoverPosition = false;
            hasCoverFiringPosition = false;
            coverRequested = false;
            coverRequestReason = string.Empty;
            coverRequestPriority = TacticalDecisionPriority.None;
            nextCoverDecisionTime = 0f;
            lastProtectiveTaskPriority = TacticalDecisionPriority.None;
            proactiveCoverAllowedAt = 0f;
            coverShotOpportunityUntil = float.NegativeInfinity;
            holdCoverUntil = 0f;
            coverReachedAt = 0f;
            peekReachedAt = float.NegativeInfinity;
            holdingTacticalCover = false;
            completedCoverCycleShots = 0;
            primaryThreatDirection = Vector2.zero;
            lastPenetratingHitTime = float.NegativeInfinity;
            defensiveRearExposureSince = float.NegativeInfinity;
            closeCombatTrackedTarget = null;
            lastCloseCombatTargetDirection = Vector2.zero;
            lastCloseCombatSampleTime = float.NegativeInfinity;
            targetBearingRateDegrees = 0f;
            nextCloseCombatTime = 0f;
            closeCombatDestination = Vector2.zero;
            StartCurrentRoutePoint();
        }

        public void ApplyStrategicDecision(
            BotStrategicDecision decision,
            bool hasDestination,
            Vector2 resolvedDestination,
            TeamMember preferredTarget,
            TeamMember allyToSupport)
        {
            if (decision == null || health == null || health.IsDestroyed) return;
            strategicDecision = decision;
            strategicDecisionExpiresAt = Now + Mathf.Clamp(decision.durationSeconds, 1f, 60f);
            strategicHasDestination = hasDestination;
            strategicDestination = resolvedDestination;
            strategicTarget = preferredTarget;
            strategicAlly = allyToSupport;
            preferredDistance = decision.desiredDistance > 0f
                ? decision.desiredDistance
                : defaultPreferredDistance;
            bool defensiveOverride = tacticalMovementPurpose == TacticalMovementPurpose.MoveToCover ||
                                     tacticalMovementPurpose == TacticalMovementPurpose.DefensiveRetreat ||
                                     tacticalMovementPurpose == TacticalMovementPurpose.PeekToFire ||
                                     tacticalMovementPurpose == TacticalMovementPurpose.ReturnToCover ||
                                     tacticalMovementPurpose == TacticalMovementPurpose.HoldCover ||
                                     tacticalMovementPurpose == TacticalMovementPurpose.BreakCloseOrbit ||
                                     holdingTacticalCover;
            // The connected route is the bot's deployment mission. The coordinator installs its
            // deterministic fallback immediately after the tank is configured, and used to replace that
            // route here with a point much farther down the lane. Sixty seconds later the fallback
            // expired and StartCurrentRoutePoint sent every tank back to the route point it had skipped.
            // Store the strategic decision now, but do not let it steer or pause an unfinished deployment;
            // visible contact still takes control in the regular tactical decision loop.
            bool deploymentInProgress = !routeComplete && routePoints.Length > 1 && !hasRememberedTarget;
            if (strategicHasDestination && navigation != null && !defensiveOverride && !deploymentInProgress)
            {
                navigation.SetPaused(false);
                navigation.SetDestination(strategicDestination);
            }
            else if (decision.intent == BotStrategicIntentType.Wait && !defensiveOverride && !deploymentInProgress)
            {
                navigation?.SetPaused(true);
                State = CombatAiState.Hold;
            }
        }

        public void HandleDestroyed()
        {
            State = CombatAiState.Destroyed;
            CurrentFireBlockReason = FireBlockReason.NoVisibleTarget;
            target = null;
            hasRememberedTarget = false;
            strategicDecision = null;
            strategicHasDestination = false;
            strategicTarget = null;
            strategicAlly = null;
            lastTargetVisibleTime = float.NegativeInfinity;
            aimingBlockedSince = -1f;
            firingLineClearSince = -1f;
            targetLockedUntil = 0f;
            failedFiringPositions.Clear();
            currentLineOfFire = default;
            hasFiringPositionSolution = false;
            lastTacticalPositionResult = "Машина уничтожена";
            threatMemory.Clear();
            threatAnalysis = default;
            lastPenetratingHitTime = float.NegativeInfinity;
            tacticalMovementPurpose = TacticalMovementPurpose.None;
            tacticalDecisionPriority = TacticalDecisionPriority.None;
            tacticalDecisionReason = "Машина уничтожена";
            hasCoverPosition = false;
            hasCoverFiringPosition = false;
            coverRequested = false;
            coverShotOpportunityUntil = float.NegativeInfinity;
            holdingTacticalCover = false;
            closeCombatTrackedTarget = null;
            targetBearingRateDegrees = 0f;
            TacticalCoverReservations.Release(GetEntityId().ToString());
            navigation?.ClearDestination();
            motor?.StopImmediately();
            turret?.ClearTarget();
        }

        private void FixedUpdate()
        {
            using var performanceScope = BattlePerformanceProbe.Measure(BattlePerformanceSubsystem.CombatAi);
            double fixedTime = Time.fixedTimeAsDouble;
            if (fixedTime.Equals(lastCoordinatedFixedTime)) return;
            lastCoordinatedFixedTime = fixedTime;

            // Unity guarantees class-level execution order, but not the order of six instances of this
            // class. Reservation winners, event order and target reactions therefore used to depend on
            // transient instance ids. TeamMember exposes the canonical battle order, so one callback owns
            // the whole tactical tick and subsequent instance callbacks for this physics step are no-ops.
            foreach (TeamMember activeMember in TeamMember.ActiveMembers)
            {
                CombatTankAI ai = activeMember != null ? activeMember.GetComponent<CombatTankAI>() : null;
                if (ai != null && ai.isActiveAndEnabled) ai.TickBot();
            }
        }

        private void TickBot()
        {
            if (health == null || health.IsDestroyed)
            {
                HandleDestroyed();
                return;
            }

            if (Now >= nextDecisionTime)
            {
                nextDecisionTime = Now + DecisionInterval;
                Think();
            }

            ActOnTarget();
        }

        private void Think()
        {
            RefreshStrategicDecision();
            RefreshThreatAnalysis();
            UpdateCloseCombatTracking();
            if (ContinueCloseCombatDisengage())
            {
                return;
            }
            if (TryBeginCloseCombatDisengage())
            {
                return;
            }
            if (ContinueDefensiveMovement())
            {
                return;
            }
            if (tacticalMovementPurpose == TacticalMovementPurpose.PeekToFire &&
                State == CombatAiState.Hold)
            {
                bool reacquisitionExpired = !IsTargetVisible(target) &&
                                            Now >= peekReachedAt + 1.2f;
                if (reacquisitionExpired || Now >= tacticalDecisionExpiresAt)
                {
                    BeginReturnToCover();
                }
                return;
            }
            RequestProactiveCoverWhenExposed();
            if (coverRequested && ResolveShotBeforeCover())
            {
                return;
            }
            if (coverRequested && Now >= nextCoverDecisionTime && TryBeginCoverMovement())
            {
                return;
            }
            if (State == CombatAiState.Hold && holdingTacticalCover)
            {
                navigation?.SetPaused(true);
                UpdateArmorFacing(primaryThreatDirection);
                bool minimumHoldComplete = Now >= coverReachedAt +
                                           tacticalBehaviorProfile.MinimumCoverHoldSeconds;
                if (minimumHoldComplete && target != null && target.IsAlive && hasRememberedTarget &&
                    weapon != null && weapon.ReloadProgress >= tacticalBehaviorProfile.PeekReloadThreshold &&
                    TryBeginPeekFromCover())
                {
                    return;
                }

                if (Now < holdCoverUntil)
                {
                    return;
                }

                ClearTacticalTask("Ожидание в укрытии завершено");
                holdingTacticalCover = false;
                bool visibleAfterCover = IsTargetVisible(target);
                State = visibleAfterCover ? CombatAiState.Engage : CombatAiState.Advance;
                if (!visibleAfterCover) StartCurrentRoutePoint();
            }
            bool targetVisible = IsTargetVisible(target);
            if (!targetVisible)
            {
                UpdateMemoryFromContact();
            }

            TeamMember selected = IsTargetVisible(strategicTarget) ? strategicTarget : SelectBestVisibleTarget();
            if (selected != null)
            {
                if (selected != target)
                {
                    if (State == CombatAiState.Reposition)
                    {
                        navigation?.ClearDestination();
                        State = CombatAiState.Engage;
                        ClearTacticalTask("Манёвр отменён после смены цели");
                        lastTacticalPositionResult = "Манёвр отменён после смены цели";
                    }
                    hasFiringPositionSolution = false;
                    firingLineClearSince = -1f;
                    targetLockedUntil = Now + CombatAiPolicy.TargetLockSeconds;
                    aimingBlockedSince = -1f;
                }
                target = selected;
                lastKnownTargetPosition = selected.transform.position;
                lastTargetVisibleTime = Now;
                hasRememberedTarget = true;
                investigationDestinationSet = false;
                if (State == CombatAiState.Reposition && navigation != null &&
                    tacticalMovementPurpose == TacticalMovementPurpose.GainFiringLine)
                {
                    currentLineOfFire = EvaluateLineOfFireAssessment(selected);
                    if (currentLineOfFire.IsClear)
                    {
                        if (firingLineClearSince < 0f) firingLineClearSince = Now;
                        if (CombatAiPolicy.ShouldCompleteFiringLineManeuver(
                                true,
                                navigation.DestinationReached,
                                firingLineClearSince,
                                Now))
                        {
                            hasFiringPositionSolution = false;
                            nextRepositionTime = Now + CombatAiPolicy.FiringLineRetryCooldownSeconds;
                            ClearTacticalTask("Устойчивый прострел получен");
                            lastTacticalPositionResult = "Устойчивый прострел получен";
                        }
                        else
                        {
                            return;
                        }
                    }
                    else
                    {
                        firingLineClearSince = -1f;
                        if (Now < repositionExpiresAt && !navigation.DestinationReached)
                        {
                            return;
                        }

                        RememberFailedFiringPosition();
                    }
                }

                // An unfinished deployment outranks a distant contact: the bot keeps walking to its assigned
                // position while the enemy stays tracked, and fights from where it was told to be. Without this
                // a map whose whole surface is visible from the spawn freezes every bot on its slot.
                if (CombatAiPolicy.ShouldDeploymentOutrankContact(
                        routeComplete,
                        Vector2.Distance(transform.position, selected.transform.position),
                        CombatAiPolicy.CloseThreatDistance(member != null ? member.Definition : null)))
                {
                    ResumeDeploymentRoute("Развёртывание продолжается: контакт дальше дистанции ближней угрозы");
                    return;
                }

                State = CombatAiState.Engage;
                return;
            }

            if (target != null)
            {
                UpdateMemoryFromContact();
            }

            if (State == CombatAiState.Reposition && navigation != null &&
                CombatAiPolicy.ShouldKeepRepositioning(
                    hasRememberedTarget,
                    navigation.DestinationReached,
                    Now,
                    repositionExpiresAt))
            {
                navigation.SetPaused(false);
                return;
            }

            if (hasRememberedTarget)
            {
                if (CombatAiPolicy.ShouldDelayInvestigation(Now, lastTargetVisibleTime))
                {
                    return;
                }

                State = CombatAiState.Investigate;
                BeginOrContinueInvestigation();
            }
            else if (ExecuteStrategicDirective())
            {
                return;
            }
            else
            {
                bool routeStarted = routeIndex > 0;
                State = CombatAiPolicy.ResolveState(true, false, false, routeStarted);
                ContinueRoute();
            }
        }

        private void ActOnTarget()
        {
            if ((State != CombatAiState.Engage && State != CombatAiState.Reposition &&
                 State != CombatAiState.Hold) || !IsTargetVisible(target))
            {
                // A bot that is still deploying keeps its turret on a visible enemy without stopping for it:
                // its slot is the worst place to fight from, and the route is the team's decision. The hull is
                // turned toward the enemy as well, because driving to a position is no reason to show the rear.
                if (State == CombatAiState.Deploy || State == CombatAiState.Advance)
                {
                    if (target != null && IsTargetVisible(target))
                    {
                        turret?.SetTarget(target.transform.position);
                        UpdateArmorFacing((Vector2)target.transform.position - (Vector2)transform.position);
                    }
                    else
                    {
                        navigation?.ClearFacingDirection();
                    }
                }

                aimingBlockedSince = -1f;
                if (State != CombatAiState.Investigate)
                {
                    SetFireBlockWithoutBlocker(FireBlockReason.NoVisibleTarget);
                }

                return;
            }

            Vector2 targetPosition = target.transform.position;
            lastKnownTargetPosition = targetPosition;
            lastTargetVisibleTime = Now;
            hasRememberedTarget = true;
            turret?.SetTarget(targetPosition);

            float distance = Vector2.Distance(transform.position, targetPosition);
            if (State == CombatAiState.Hold)
            {
                navigation?.SetPaused(true);
                UpdateArmorFacing((targetPosition - (Vector2)transform.position).normalized);
                CurrentFireBlockReason = EvaluateFirePermission(target, distance);
                if (holdingTacticalCover && hasCoverFiringPosition)
                {
                    return;
                }
                if (CurrentFireBlockReason == FireBlockReason.None && weapon.TryFire())
                {
                    BeginReturnToCover(true);
                }
                return;
            }

            if (State == CombatAiState.Reposition)
            {
                navigation?.SetPaused(false);
                CurrentFireBlockReason = EvaluateFirePermission(target, distance);
                if (tacticalMovementPurpose == TacticalMovementPurpose.BreakCloseOrbit)
                {
                    if (CurrentFireBlockReason == FireBlockReason.None)
                    {
                        weapon?.TryFire();
                    }
                    return;
                }
                if (tacticalMovementPurpose == TacticalMovementPurpose.MoveToCover ||
                    tacticalMovementPurpose == TacticalMovementPurpose.ReturnToCover ||
                    tacticalMovementPurpose == TacticalMovementPurpose.DefensiveRetreat ||
                    tacticalMovementPurpose == TacticalMovementPurpose.PeekToFire)
                {
                    MaintainArmorFacingDuringDefensiveMovement(targetPosition);
                    return;
                }
                if (CurrentFireBlockReason == FireBlockReason.None && weapon.TryFire())
                {
                    BeginReturnToCover(true);
                }

                return;
            }

            if (distance > preferredDistance * 1.12f)
            {
                Vector2 targetDirection = (targetPosition - (Vector2)transform.position).normalized;
                float targetRelativeAngle = Vector2.SignedAngle(transform.up, targetDirection);
                if (CombatAiPolicy.ShouldAlignHullBeforeAdvancing(targetRelativeAngle))
                {
                    navigation?.SetPaused(true);
                    UpdateArmorFacing(targetDirection);
                }
                else
                {
                    navigation?.ClearFacingDirection();
                    navigation?.SetPaused(false);
                    if (navigation != null &&
                        (!navigation.HasDestination || Vector2.Distance(navigation.Destination, targetPosition) > DestinationRefreshDistance))
                    {
                        navigation.SetDestination(targetPosition);
                    }
                }
            }
            else
            {
                navigation?.SetPaused(true);
                UpdateArmorFacing((targetPosition - (Vector2)transform.position).normalized);
            }

            CurrentFireBlockReason = EvaluateFirePermission(target, distance);
            UpdateAimRecovery();
            if (CurrentFireBlockReason == FireBlockReason.None)
            {
                aimingBlockedSince = -1f;
                if (weapon.TryFire())
                {
                    BeginReturnToCover(true);
                }
            }
            else if ((CurrentFireBlockReason == FireBlockReason.AllyInLine ||
                      CurrentFireBlockReason == FireBlockReason.ObstacleInLine) &&
                     Now >= nextRepositionTime)
            {
                BeginReposition(targetPosition);
            }
        }

        private void UpdateAimRecovery()
        {
            if (CurrentFireBlockReason != FireBlockReason.Aiming)
            {
                aimingBlockedSince = -1f;
                return;
            }

            if (aimingBlockedSince < 0f)
            {
                aimingBlockedSince = Now;
                return;
            }

            if (Now - aimingBlockedSince >= AimRecoveryDelay)
            {
                navigation?.SetPaused(true);
            }
        }

        private void UpdateCloseCombatTracking()
        {
            if (!IsTargetVisible(target))
            {
                closeCombatTrackedTarget = null;
                lastCloseCombatTargetDirection = Vector2.zero;
                lastCloseCombatSampleTime = float.NegativeInfinity;
                targetBearingRateDegrees = 0f;
                return;
            }

            Vector2 direction = (Vector2)target.transform.position - (Vector2)transform.position;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                targetBearingRateDegrees = 0f;
                return;
            }

            direction.Normalize();
            if (closeCombatTrackedTarget == target && lastCloseCombatTargetDirection.sqrMagnitude > 0.0001f &&
                !float.IsNegativeInfinity(lastCloseCombatSampleTime))
            {
                float elapsed = Mathf.Max(0.02f, Now - lastCloseCombatSampleTime);
                float measuredRate = Mathf.Abs(Vector2.SignedAngle(lastCloseCombatTargetDirection, direction)) / elapsed;
                targetBearingRateDegrees = Mathf.Lerp(targetBearingRateDegrees, measuredRate, 0.5f);
            }
            else
            {
                targetBearingRateDegrees = 0f;
            }

            closeCombatTrackedTarget = target;
            lastCloseCombatTargetDirection = direction;
            lastCloseCombatSampleTime = Now;
        }

        private bool TryBeginCloseCombatDisengage()
        {
            if (Now < nextCloseCombatTime || target == null || !IsTargetVisible(target) ||
                navigation?.Map == null || member?.Definition == null ||
                tacticalMovementPurpose == TacticalMovementPurpose.BreakCloseOrbit)
            {
                return false;
            }

            float distance = Vector2.Distance(transform.position, target.transform.position);
            float triggerDistance = Mathf.Max(
                CombatAiPolicy.MinimumCloseCombatDistance,
                navigation.Map.CellSize * 4f);
            if (!CombatAiPolicy.ShouldBreakCloseOrbit(
                    distance,
                    triggerDistance,
                    TurretAimErrorDegrees,
                    targetBearingRateDegrees,
                    member.Definition.turretTurnSpeed))
            {
                return false;
            }

            if (!TryFindCloseCombatDestination(target.transform.position, distance, out Vector2 destination) ||
                !navigation.SetDestination(destination, navigation.ClearanceCells, true))
            {
                nextCloseCombatTime = Now + 0.75f;
                lastTacticalPositionResult = "Не найден путь для разрыва ближнего боя";
                return false;
            }

            TacticalCoverReservations.Release(GetEntityId().ToString());
            hasCoverPosition = false;
            hasCoverFiringPosition = false;
            holdingTacticalCover = false;
            coverRequested = false;
            coverShotOpportunityUntil = float.NegativeInfinity;
            hasFiringPositionSolution = false;
            closeCombatDestination = destination;
            repositionDestination = destination;
            repositionExpiresAt = Now + CloseCombatDisengageDuration;
            SetTacticalTask(
                TacticalMovementPurpose.BreakCloseOrbit,
                TacticalDecisionPriority.EscapeCloseCombat,
                "Разрыв слишком близкого кругового боя задним ходом",
                repositionExpiresAt);
            State = CombatAiState.Reposition;
            navigation.SetPaused(false);
            lastTacticalPositionResult =
                $"Разрывает ближний бой: дистанция {distance:0.0} м, " +
                $"ошибка башни {TurretAimErrorDegrees:0}°, угловая скорость цели {targetBearingRateDegrees:0}°/с";
            return true;
        }

        private bool ContinueCloseCombatDisengage()
        {
            if (State != CombatAiState.Reposition ||
                tacticalMovementPurpose != TacticalMovementPurpose.BreakCloseOrbit || navigation == null)
            {
                return false;
            }

            float distance = target != null
                ? Vector2.Distance(transform.position, target.transform.position)
                : float.PositiveInfinity;
            bool complete = target == null || !target.IsAlive ||
                            distance >= CloseCombatDisengageDistance ||
                            navigation.DestinationReached ||
                            Vector2.Distance(transform.position, closeCombatDestination) <= 0.55f ||
                            Now >= repositionExpiresAt;
            if (!complete)
            {
                navigation.SetPaused(false);
                return true;
            }

            navigation.SetPaused(true);
            ClearTacticalTask("Ближняя дистанция разорвана");
            State = IsTargetVisible(target) ? CombatAiState.Engage : CombatAiState.Investigate;
            nextCloseCombatTime = Now + CloseCombatRetryCooldown;
            lastTacticalPositionResult = distance >= CloseCombatDisengageDistance
                ? $"Ближний бой разорван до {distance:0.0} м"
                : "Манёвр разрыва ближнего боя завершён";
            if (target != null)
            {
                UpdateArmorFacing(((Vector2)target.transform.position - (Vector2)transform.position).normalized);
            }
            return true;
        }

        private bool TryFindCloseCombatDestination(
            Vector2 targetPosition,
            float currentDistance,
            out Vector2 destination)
        {
            destination = default;
            NavigationMap map = navigation?.Map;
            if (map == null) return false;

            Vector2 origin = transform.position;
            Vector2 away = origin - targetPosition;
            if (away.sqrMagnitude <= 0.0001f) away = -transform.up;
            away.Normalize();
            float[] angles = { 0f, -25f, 25f, -50f, 50f };
            float[] distances = { 4.5f, 3.5f, 2.5f };
            HashSet<Vector2Int> tested = new HashSet<Vector2Int>();
            bool found = false;
            float bestScore = float.NegativeInfinity;
            foreach (float retreatDistance in distances)
            {
                foreach (float angle in angles)
                {
                    Vector2 direction = Rotate(away, angle);
                    Vector2Int cell = map.WorldToCell(origin + direction * retreatDistance);
                    if (!tested.Add(cell) || !map.Contains(cell) || map.IsBlocked(cell)) continue;
                    Vector2 candidate = map.CellToWorld(cell);
                    float targetDistance = Vector2.Distance(candidate, targetPosition);
                    if (targetDistance < currentDistance + 1f || !IsFiringPositionAvailable(candidate)) continue;
                    List<Vector2> path = map.FindWorldPath(origin, candidate, navigation.ClearanceCells);
                    if (path == null || path.Count < 2) continue;
                    float pathLength = 0f;
                    for (int index = 1; index < path.Count; index++)
                    {
                        pathLength += Vector2.Distance(path[index - 1], path[index]);
                    }
                    float score = targetDistance - pathLength * 0.12f - Mathf.Abs(angle) * 0.005f;
                    if (found && score <= bestScore) continue;
                    found = true;
                    bestScore = score;
                    destination = candidate;
                }
            }

            return found;
        }

        private static Vector2 Rotate(Vector2 direction, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            return new Vector2(
                direction.x * cosine - direction.y * sine,
                direction.x * sine + direction.y * cosine);
        }

        private TeamMember SelectBestVisibleTarget()
        {
            if (member == null || visionSystem == null || member.Definition == null)
            {
                return null;
            }

            TeamMember best = null;
            float bestScore = float.NegativeInfinity;
            float currentScore = float.NegativeInfinity;
            bool currentVisible = IsTargetVisible(target);
            foreach (TeamMember candidate in TeamMember.ActiveMembers)
            {
                if (candidate == null || candidate.Team == member.Team || candidate.Team == TeamId.Neutral ||
                    !IsTargetVisible(candidate))
                {
                    continue;
                }

                float distance = Vector2.Distance(transform.position, candidate.transform.position);
                float healthRatio = candidate.Health != null && candidate.Health.MaximumHitPoints > 0
                    ? (float)candidate.Health.CurrentHitPoints / candidate.Health.MaximumHitPoints
                    : 1f;
                float aimError = turret != null
                    ? Mathf.Abs(Vector2.SignedAngle(turret.transform.up,
                        ((Vector2)candidate.transform.position - (Vector2)turret.transform.position).normalized))
                    : 180f;
                FireBlockReason lineResult = EvaluateLineOfFire(candidate);
                float score = CombatAiPolicy.ScoreTarget(
                    distance,
                    member.Definition.viewRange,
                    healthRatio,
                    aimError,
                    lineResult == FireBlockReason.None,
                    candidate == target);
                if (candidate == strategicTarget) score += 100f;
                if (candidate == target) currentScore = score;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            if (currentVisible && best != null && best != target &&
                !CombatAiPolicy.ShouldSwitchVisibleTarget(
                    Now, targetLockedUntil, currentScore, bestScore))
            {
                return target;
            }

            return best;
        }

        private FireBlockReason EvaluateFirePermission(TeamMember candidate, float distance)
        {
            if (!IsTargetVisible(candidate))
            {
                SetFireBlockWithoutBlocker(FireBlockReason.NoVisibleTarget);
                return FireBlockReason.NoVisibleTarget;
            }

            if (member?.Definition == null || distance > member.Definition.viewRange)
            {
                SetFireBlockWithoutBlocker(FireBlockReason.OutOfRange);
                return FireBlockReason.OutOfRange;
            }

            // Keep the tactical ordering stable: a concrete blocker outranks aiming and reload state.
            // The query itself is allocation-free, so preserving this diagnostic and firing behaviour no
            // longer creates a stream of garbage during an exchange.
            currentLineOfFire = EvaluateLineOfFireAssessment(candidate);
            if (!currentLineOfFire.IsClear) return currentLineOfFire.Reason;
            if (navigation != null && navigation.MovementPaused && navigation.HasFacingDirection &&
                Mathf.Abs(Vector2.SignedAngle(transform.up, navigation.FacingDirection)) > 5f)
            {
                return FireBlockReason.Aiming;
            }
            if (turret == null || turret.AimErrorDegrees > AimToleranceDegrees) return FireBlockReason.Aiming;
            if (weapon == null || !weapon.IsReady) return FireBlockReason.Reloading;
            return FireBlockReason.None;
        }

        /// <summary>
        /// Sets a fire block reason and keeps the paired line-of-fire assessment coherent: a
        /// reason that names no blocking object drops the stale assessment with it. The audit and
        /// the diagnostics compare the current reason against the current assessment, so a
        /// leftover assessment from an earlier target or an earlier moment would be reported as a
        /// fresh misclassification.
        /// </summary>
        private void SetFireBlockWithoutBlocker(FireBlockReason reason)
        {
            CurrentFireBlockReason = reason;
            CombatAiPolicy.DescribesBlocker(reason, ref currentLineOfFire);
        }

        private FireBlockReason EvaluateLineOfFire(TeamMember candidate)
        {
            return EvaluateLineOfFireAssessment(candidate).Reason;
        }

        private LineOfFireAssessment EvaluateLineOfFireAssessment(TeamMember candidate)
        {
            if (candidate == null || weapon == null || member == null)
            {
                return new LineOfFireAssessment(
                    FireBlockReason.ObstacleInLine,
                    LineOfFireBlockType.HardObstacle,
                    null,
                    transform.position,
                    Vector2.zero,
                    0f,
                    false);
            }

            Vector2 origin = weapon.Muzzle != null ? weapon.Muzzle.position : transform.position;
            return CombatLineOfFire.Evaluate(
                transform,
                member.Team,
                origin,
                candidate.transform.position,
                candidate.transform);
        }

        private void BeginOrContinueInvestigation()
        {
            if (!hasRememberedTarget)
            {
                ResumeRouteAfterContact();
                return;
            }

            navigation?.SetPaused(false);
            if (!investigationDestinationSet)
            {
                investigationDestinationSet = navigation != null && navigation.SetDestination(lastKnownTargetPosition);
                if (!investigationDestinationSet)
                {
                    hasRememberedTarget = false;
                    target = null;
                    ResumeRouteAfterContact();
                    return;
                }
            }

            if (navigation == null || navigation.DestinationReached ||
                Vector2.Distance(transform.position, lastKnownTargetPosition) < 0.45f)
            {
                hasRememberedTarget = false;
                target = null;
                investigationDestinationSet = false;
                ResumeRouteAfterContact();
            }
        }

        private void BeginReposition(Vector2 targetPosition)
        {
            if (navigation == null || navigation.Map == null || target == null || member?.Definition == null)
            {
                return;
            }

            RemoveExpiredFailedPositions();
            currentLineOfFire = EvaluateLineOfFireAssessment(target);
            float viewRange = Mathf.Max(1f, member.Definition.viewRange);
            float searchDistance = currentLineOfFire.BlockType == LineOfFireBlockType.Ally
                ? Mathf.Min(5f, viewRange)
                : Mathf.Min(TacticalFiringPositionPlanner.DefaultSearchDistance, viewRange);
            bool found = TacticalFiringPositionPlanner.TryFind(
                navigation.Map,
                transform.position,
                targetPosition,
                preferredDistance,
                viewRange,
                navigation.ClearanceCells,
                searchDistance,
                candidate => IsFiringPositionAvailable(candidate) &&
                             EvaluateLineOfFireFrom(candidate, target).IsClear,
                IsFailedFiringPosition,
                path => TacticalFiringPositionPlanner.ThreatExposurePenalty(
                    path,
                    targetPosition - (Vector2)transform.position,
                    MaximumReverseFiringLinePathLength),
                out TacticalFiringPositionSolution solution);

            nextRepositionTime = Now + (found ? 1.2f : 2.5f);
            bool reverse = found && TacticalFiringPositionPlanner.ShouldApproachInReverse(
                solution,
                targetPosition - (Vector2)transform.position,
                member.Definition.maxReverseSpeed,
                MaximumReverseFiringLinePathLength);
            if (!found || !navigation.SetDestination(solution.Position, navigation.ClearanceCells, reverse))
            {
                hasFiringPositionSolution = false;
                ClearTacticalTask("Огневая позиция не найдена");
                lastTacticalPositionResult = currentLineOfFire.Blocker != null
                    ? $"Нет доступной позиции вокруг {currentLineOfFire.BlockerName}"
                    : "Нет доступной огневой позиции";
                navigation.SetPaused(true);
                State = CombatAiState.Engage;
                return;
            }

            investigationDestinationSet = false;
            firingLineClearSince = -1f;
            firingPositionSolution = solution;
            hasFiringPositionSolution = true;
            repositionDestination = solution.Position;
            float speed = Mathf.Max(0.5f, member.Definition.maxForwardSpeed * 0.55f);
            repositionExpiresAt = Now + Mathf.Clamp(solution.PathLength / speed + 3f, 4f, 18f);
            SetTacticalTask(
                TacticalMovementPurpose.GainFiringLine,
                TacticalDecisionPriority.GainFiringLine,
                $"Получить прострел вокруг {currentLineOfFire.BlockerName}",
                repositionExpiresAt);
            lastTacticalPositionResult =
                $"Объезд {currentLineOfFire.BlockerName}; {solution.EvaluatedCandidates} кандидатов";
            State = CombatAiState.Reposition;
            navigation.SetPaused(false);
        }

        private LineOfFireAssessment EvaluateLineOfFireFrom(Vector2 origin, TeamMember candidate)
        {
            if (candidate == null || member == null)
            {
                return new LineOfFireAssessment(
                    FireBlockReason.ObstacleInLine,
                    LineOfFireBlockType.HardObstacle,
                    null,
                    origin,
                    Vector2.zero,
                    0f,
                    false);
            }

            Vector2 destination = candidate.transform.position;
            return EvaluateLineOfFireFromPosition(origin, destination, candidate.transform);
        }

        private LineOfFireAssessment EvaluateLineOfFireFromPosition(
            Vector2 origin,
            Vector2 destination,
            Transform intendedTarget)
        {
            Vector2 direction = (destination - origin).normalized;
            Vector2 projectedMuzzle = origin + direction * 0.35f;
            return CombatLineOfFire.Evaluate(
                transform,
                member.Team,
                projectedMuzzle,
                destination,
                intendedTarget);
        }

        private bool IsFailedFiringPosition(Vector2Int cell)
        {
            return failedFiringPositions.TryGetValue(cell, out float expiresAt) && expiresAt > Now;
        }

        private bool IsFiringPositionAvailable(Vector2 position)
        {
            float radius = navigation != null && navigation.Map != null
                ? Mathf.Max(0.35f, navigation.Map.CellSize * 0.65f)
                : 0.5f;
            foreach (Collider2D collider in Physics2D.OverlapCircleAll(position, radius))
            {
                if (collider == null || collider.isTrigger ||
                    collider.transform == transform || collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                // The navigation grid describes passable cells, but the real hull still needs
                // enough room for its collider. Reject scenery, wrecks and other tanks here so
                // a tactical point cannot be mathematically reachable yet physically occupied.
                return false;
            }

            return true;
        }

        private void RememberFailedFiringPosition()
        {
            if (!hasFiringPositionSolution)
            {
                return;
            }

            failedFiringPositions[firingPositionSolution.Cell] = Now + FailedPositionMemorySeconds;
            nextRepositionTime = Now + CombatAiPolicy.FiringLineRetryCooldownSeconds;
            lastTacticalPositionResult = navigation != null && navigation.DestinationReached
                ? "Выбранная позиция достигнута, но прострел не появился"
                : "Манёвр не завершён в расчётное время";
            hasFiringPositionSolution = false;
            firingLineClearSince = -1f;
            ClearTacticalTask(lastTacticalPositionResult);
        }

        private void RemoveExpiredFailedPositions()
        {
            if (failedFiringPositions.Count == 0)
            {
                return;
            }

            Vector2Int[] expired = failedFiringPositions
                .Where(item => item.Value <= Now)
                .Select(item => item.Key)
                .ToArray();
            foreach (Vector2Int cell in expired)
            {
                failedFiringPositions.Remove(cell);
            }
        }

        private void RefreshThreatAnalysis()
        {
            List<Vector2> visibleDirections = new List<Vector2>();
            if (member != null && visionSystem != null)
            {
                foreach (TeamMember candidate in TeamMember.ActiveMembers)
                {
                    if (candidate == null || candidate.Team == member.Team || candidate.Team == TeamId.Neutral ||
                        !IsTargetVisible(candidate))
                    {
                        continue;
                    }

                    Vector2 direction = (Vector2)candidate.transform.position - (Vector2)transform.position;
                    if (direction.sqrMagnitude > 0.001f) visibleDirections.Add(direction.normalized);
                }
            }

            threatAnalysis = threatMemory.Analyze(Now, visibleDirections);
            primaryThreatDirection = threatAnalysis.PrimaryDirection;
        }

        private void RequestProactiveCoverWhenExposed()
        {
            if (coverRequested || holdingTacticalCover || hasCoverPosition && Now < coverValidUntil ||
                navigation == null || weapon == null || member?.Definition == null)
            {
                return;
            }

            if (threatAnalysis.IsCrossfire)
            {
                RequestCover("Перекрёстный огонь", TacticalDecisionPriority.SurviveCrossfire);
                return;
            }

            if (!IsTargetVisible(target) || weapon.IsReady) return;
            LineOfFireAssessment exposure = EvaluateLineOfFireAssessment(target);
            float healthRatio = health != null && health.MaximumHitPoints > 0
                ? (float)health.CurrentHitPoints / health.MaximumHitPoints
                : 1f;
            bool recentlyDamaged = Now - lastPenetratingHitTime <= RecentPenetrationDangerSeconds;
            if (tacticalBehaviorProfile.ShouldSeekCoverWhileReloading(
                    weapon.ReloadProgress,
                    exposure.IsClear,
                    false,
                    recentlyDamaged,
                    healthRatio))
            {
                RequestCover("Перезарядка под видимой угрозой", TacticalDecisionPriority.UseCover);
            }
        }

        private bool ResolveShotBeforeCover()
        {
            if (!coverRequested || weapon == null || target == null || health == null) return false;
            bool visible = IsTargetVisible(target);
            LineOfFireAssessment line = visible ? EvaluateLineOfFireAssessment(target) : default;
            float healthRatio = health.MaximumHitPoints > 0
                ? (float)health.CurrentHitPoints / health.MaximumHitPoints
                : 0f;
            bool criticalSurvival = coverRequestPriority == TacticalDecisionPriority.SurviveCrossfire ||
                                    threatAnalysis.IsCrossfire;
            if (!TacticalDecisionArbiter.ShouldFireBeforeCover(
                    weapon.IsReady,
                    visible,
                    visible && line.IsClear,
                    criticalSurvival,
                    healthRatio))
            {
                coverShotOpportunityUntil = float.NegativeInfinity;
                return false;
            }

            float distance = Vector2.Distance(transform.position, target.transform.position);
            CurrentFireBlockReason = EvaluateFirePermission(target, distance);
            if (CurrentFireBlockReason == FireBlockReason.None && weapon.TryFire())
            {
                coverShotOpportunityUntil = float.NegativeInfinity;
                tacticalDecisionReason = "Выстрел перед началом защитного отхода";
                return true;
            }

            if (CurrentFireBlockReason != FireBlockReason.Aiming)
            {
                coverShotOpportunityUntil = float.NegativeInfinity;
                return false;
            }

            if (float.IsNegativeInfinity(coverShotOpportunityUntil))
            {
                coverShotOpportunityUntil = Now + 0.8f;
            }
            navigation?.SetPaused(true);
            return Now < coverShotOpportunityUntil;
        }

        private void RequestCover(string reason, TacticalDecisionPriority priority)
        {
            if (Now < nextCoverDecisionTime || priority < coverRequestPriority) return;
            if (priority <= lastProtectiveTaskPriority && Now < proactiveCoverAllowedAt) return;
            coverRequested = true;
            coverRequestReason = reason;
            coverRequestPriority = priority;
        }

        private bool ContinueDefensiveMovement()
        {
            bool defensiveMovement = tacticalMovementPurpose == TacticalMovementPurpose.MoveToCover ||
                                     tacticalMovementPurpose == TacticalMovementPurpose.ReturnToCover ||
                                     tacticalMovementPurpose == TacticalMovementPurpose.DefensiveRetreat ||
                                     tacticalMovementPurpose == TacticalMovementPurpose.PeekToFire;
            if (State != CombatAiState.Reposition || !defensiveMovement || navigation == null)
            {
                // The maneuver no longer owns the movement, so it must not leave reverse approach
                // mode behind: the audit reports a stale reverse as ReverseWithoutRetreatTask.
                navigation?.ClearReverseMode();
                return false;
            }

            Vector2 expectedPosition = tacticalMovementPurpose == TacticalMovementPurpose.PeekToFire
                ? coverFiringPosition
                : coverPosition;
            bool tacticallyProtectedArrival = tacticalMovementPurpose != TacticalMovementPurpose.PeekToFire &&
                                                IsTacticallyProtectedNear(expectedPosition);
            if (!navigation.DestinationReached && !tacticallyProtectedArrival &&
                Now < repositionExpiresAt)
            {
                navigation.SetPaused(false);
                if (navigation.MovingInReverse) UpdateArmorFacing(primaryThreatDirection);
                return true;
            }

            float arrivalTolerance = tacticalMovementPurpose == TacticalMovementPurpose.PeekToFire &&
                                     navigation.Map != null
                ? Mathf.Max(0.55f, navigation.Map.CellSize * 1.5f)
                : 0.55f;
            bool reached = navigation.DestinationReached ||
                           Vector2.Distance(transform.position, expectedPosition) <= arrivalTolerance ||
                           tacticallyProtectedArrival;
            TacticalMovementPurpose completedPurpose = tacticalMovementPurpose;
            TacticalDecisionPriority completedPriority = tacticalDecisionPriority;
            if (!reached)
            {
                if (completedPurpose == TacticalMovementPurpose.PeekToFire)
                {
                    lastTacticalPositionResult = "Не удалось выехать на огневую точку; возврат в укрытие";
                    BeginReturnToCover();
                    return true;
                }

                if (completedPurpose == TacticalMovementPurpose.ReturnToCover &&
                    TryContinueReturnForwards(completedPriority))
                {
                    return true;
                }

                AbandonCover(
                    $"Не удалось достичь укрытия {coverPosition.x:0.0}, {coverPosition.y:0.0}; " +
                    $"остаток {Vector2.Distance(transform.position, coverPosition):0.0} м");
                return false;
            }

            navigation.SetPaused(true);
            navigation.ClearDestination();
            UpdateArmorFacing(primaryThreatDirection);
            State = CombatAiState.Hold;
            if (completedPurpose == TacticalMovementPurpose.PeekToFire)
            {
                holdingTacticalCover = false;
                peekReachedAt = Now;
                SetTacticalTask(
                    TacticalMovementPurpose.PeekToFire,
                    completedPriority,
                    "Огневая точка достигнута; наведение и один выстрел",
                    Now + 12f);
                lastTacticalPositionResult = "Выезд из укрытия завершён; готовится выстрел";
                return true;
            }

            holdingTacticalCover = true;
            coverReachedAt = Now;
            holdCoverUntil = Now + tacticalBehaviorProfile.MaximumCoverHoldSeconds;
            SetTacticalTask(
                TacticalMovementPurpose.HoldCover,
                completedPriority,
                "Ожидание перезарядки в защищённой позиции",
                holdCoverUntil);
            lastTacticalPositionResult = completedPurpose == TacticalMovementPurpose.ReturnToCover
                ? "Возврат в укрытие после выстрела"
                : completedPurpose == TacticalMovementPurpose.DefensiveRetreat
                    ? "Защищённое отступление завершено"
                    : "Укрытие достигнуто";
            return true;
        }

        private bool TryBeginCoverMovement()
        {
            string requestReason = string.IsNullOrWhiteSpace(coverRequestReason)
                ? "Поиск безопасной позиции"
                : coverRequestReason;
            TacticalDecisionPriority requestPriority = coverRequestPriority == TacticalDecisionPriority.None
                ? TacticalDecisionPriority.UseCover
                : coverRequestPriority;
            coverRequested = false;
            coverShotOpportunityUntil = float.NegativeInfinity;
            coverRequestReason = string.Empty;
            coverRequestPriority = TacticalDecisionPriority.None;
            nextCoverDecisionTime = Now + 3f;
            if (navigation == null || navigation.Map == null || member?.Definition == null ||
                primaryThreatDirection.sqrMagnitude <= 0.001f)
            {
                return false;
            }

            Vector2 origin = transform.position;
            Vector2 threatOrigin = origin + primaryThreatDirection.normalized * member.Definition.viewRange;
            int coverClearance = Mathf.Max(1, navigation.ClearanceCells);
            string ownerId = GetEntityId().ToString();
            Func<Vector2, bool> positionAvailable = candidate =>
                IsFiringPositionAvailable(candidate) &&
                TacticalCoverReservations.IsAvailable(
                    navigation.Map.WorldToCell(candidate), ownerId, Now);
            TeamMember coverTarget = IsTargetVisible(target) ? target : SelectBestVisibleTarget();
            if (coverTarget != null && coverTarget != target)
            {
                target = coverTarget;
                lastKnownTargetPosition = coverTarget.transform.position;
                lastTargetVisibleTime = Now;
                hasRememberedTarget = true;
                targetLockedUntil = Now + CombatAiPolicy.TargetLockSeconds;
            }
            Func<Vector2, bool> canFireFrom = coverTarget != null
                ? candidate => EvaluateLineOfFireFrom(candidate, coverTarget).IsClear
                : null;
            bool found = TryPlanCover(
                origin,
                threatOrigin,
                coverClearance,
                positionAvailable,
                canFireFrom,
                out TacticalCoverSolution solution);
            if (!found && canFireFrom != null)
            {
                found = TryPlanCover(
                    origin,
                    threatOrigin,
                    coverClearance,
                    positionAvailable,
                    null,
                    out solution);
            }
            if (!found && navigation.ClearanceCells < coverClearance)
            {
                coverClearance = navigation.ClearanceCells;
                found = TryPlanCover(
                    origin,
                    threatOrigin,
                    coverClearance,
                    positionAvailable,
                    canFireFrom,
                    out solution);
                if (!found && canFireFrom != null)
                {
                    found = TryPlanCover(
                        origin,
                        threatOrigin,
                        coverClearance,
                        positionAvailable,
                        null,
                        out solution);
                }
            }
            if (!found || !TacticalCoverReservations.TryReserve(
                    solution.Cell, ownerId, Now, 30f))
            {
                lastTacticalPositionResult = "Поблизости нет достижимого свободного укрытия";
                return false;
            }

            bool reverseRetreat = ShouldReverseToCover(solution);
            if (!navigation.SetDestination(solution.Position, coverClearance, reverseRetreat))
            {
                TacticalCoverReservations.Release(ownerId);
                lastTacticalPositionResult = "Маршрут к выбранному укрытию недоступен";
                return false;
            }

            coverSolution = solution;
            coverPosition = solution.Position;
            hasCoverPosition = true;
            lastCoverReturnStartedInReverse = false;
            hasCoverFiringPosition = solution.HasFiringPosition;
            coverFiringPosition = solution.FiringPosition;
            coverValidUntil = Now + 24f;
            repositionDestination = solution.Position;
            float speed = Mathf.Max(0.2f, Mathf.Min(
                member.Definition.maxReverseSpeed * 0.4f,
                member.Definition.maxForwardSpeed * 0.35f));
            repositionExpiresAt = Now + Mathf.Clamp(solution.PathLength / speed + 8f, 10f, 38f);
            TacticalMovementPurpose purpose = reverseRetreat
                ? TacticalMovementPurpose.DefensiveRetreat
                : TacticalMovementPurpose.MoveToCover;
            SetTacticalTask(purpose, requestPriority, requestReason, repositionExpiresAt);
            lastTacticalPositionResult = solution.HasFiringPosition
                ? $"Укрытие и точка выстрела выбраны из {solution.EvaluatedCandidates} позиций"
                : $"Защитное укрытие выбрано из {solution.EvaluatedCandidates} позиций";
            State = CombatAiState.Reposition;
            navigation.SetPaused(false);
            return true;
        }

        private bool TryPlanCover(
            Vector2 origin,
            Vector2 threatOrigin,
            int clearance,
            Func<Vector2, bool> isAvailable,
            Func<Vector2, bool> canFireFrom,
            out TacticalCoverSolution solution)
        {
            return TacticalCoverPlanner.TryFindPair(
                navigation.Map,
                origin,
                clearance,
                tacticalBehaviorProfile.CoverSearchDistance,
                maximumPeekDistance: navigation.Map.CellSize * 6f,
                candidate => IsProtectedFromThreat(threatOrigin, candidate),
                isAvailable,
                canFireFrom,
                path => TacticalCoverPlanner.ThreatExposurePenalty(
                    path,
                    primaryThreatDirection,
                    MaximumReverseCoverPathLength),
                out solution);
        }

        private bool ShouldReverseToCover(TacticalCoverSolution solution)
        {
            return member?.Definition != null && TacticalCoverPlanner.ShouldApproachInReverse(
                solution,
                primaryThreatDirection,
                member.Definition.maxReverseSpeed,
                MaximumReverseCoverPathLength);
        }

        private bool IsProtectedFromThreat(Vector2 threatOrigin, Vector2 candidate)
        {
            LineOfFireAssessment assessment = CombatLineOfFire.Evaluate(
                transform,
                member.Team,
                threatOrigin,
                candidate,
                null);
            return CombatLineOfFire.IsProtectiveCover(assessment.BlockType);
        }

        private bool IsTacticallyProtectedNear(Vector2 expectedPosition)
        {
            if (navigation?.Map == null || member?.Definition == null ||
                primaryThreatDirection.sqrMagnitude <= 0.001f ||
                Vector2.Distance(transform.position, expectedPosition) > navigation.Map.CellSize * 2f)
            {
                return false;
            }

            Vector2 currentPosition = transform.position;
            Vector2 threatOrigin = currentPosition +
                                   primaryThreatDirection.normalized * member.Definition.viewRange;
            return IsProtectedFromThreat(threatOrigin, currentPosition);
        }

        private bool TryBeginPeekFromCover()
        {
            if (!hasCoverPosition || !hasCoverFiringPosition || navigation == null ||
                Now >= coverValidUntil || target == null || !target.IsAlive || !hasRememberedTarget)
            {
                return false;
            }

            if (!EvaluateLineOfFireFromPosition(
                    coverFiringPosition,
                    lastKnownTargetPosition,
                    target.transform).IsClear)
            {
                hasCoverFiringPosition = false;
                lastTacticalPositionResult = "Огневая точка больше не имеет прострела";
                return false;
            }

            if (!navigation.SetDestination(coverFiringPosition, 1, false))
            {
                hasCoverFiringPosition = false;
                return false;
            }

            holdingTacticalCover = false;
            repositionDestination = coverFiringPosition;
            float speed = Mathf.Max(0.35f, member.Definition.maxForwardSpeed * 0.45f);
            repositionExpiresAt = Now + Mathf.Clamp(
                coverSolution.FiringPathLength / speed + 8f,
                8f,
                18f);
            TacticalDecisionPriority cyclePriority = tacticalDecisionPriority == TacticalDecisionPriority.None
                ? TacticalDecisionPriority.UseCover
                : tacticalDecisionPriority;
            SetTacticalTask(
                TacticalMovementPurpose.PeekToFire,
                cyclePriority,
                "Орудие готово; короткий выезд для одного выстрела",
                repositionExpiresAt);
            State = CombatAiState.Reposition;
            navigation.SetPaused(false);
            lastTacticalPositionResult = "Выезжает из укрытия на проверенную огневую точку";
            return true;
        }

        private void BeginReturnToCover(bool afterShot = false)
        {
            if (!hasCoverPosition || navigation == null ||
                Vector2.Distance(transform.position, coverPosition) <= 0.55f)
            {
                return;
            }

            if (!navigation.SetDestination(coverPosition, 1, true))
            {
                AbandonCover("Не удалось построить обратный маршрут в укрытие");
                return;
            }

            if (afterShot)
            {
                completedCoverCycleShots++;
            }
            lastCoverReturnStartedInReverse = true;
            returnForwardFallbackUsed = false;
            repositionDestination = coverPosition;
            float speed = member?.Definition != null
                ? Mathf.Max(0.2f, member.Definition.maxReverseSpeed * 0.4f)
                : 0.25f;
            repositionExpiresAt = Now + Mathf.Clamp(
                coverSolution.FiringPathLength / speed + 4f,
                5f,
                18f);
            SetTacticalTask(
                TacticalMovementPurpose.ReturnToCover,
                threatAnalysis.IsCrossfire
                    ? TacticalDecisionPriority.SurviveCrossfire
                    : TacticalDecisionPriority.UseCover,
                "Возврат задним ходом после выстрела",
                repositionExpiresAt);
            State = CombatAiState.Reposition;
            navigation.SetPaused(false);
            lastTacticalPositionResult = "Возвращается в укрытие после выстрела";
        }

        private bool TryContinueReturnForwards(TacticalDecisionPriority priority)
        {
            if (returnForwardFallbackUsed || navigation == null ||
                !navigation.SetDestination(coverPosition, 1, false))
            {
                return false;
            }

            returnForwardFallbackUsed = true;
            float speed = member?.Definition != null
                ? Mathf.Max(0.35f, member.Definition.maxForwardSpeed * 0.35f)
                : 0.35f;
            repositionExpiresAt = Now + Mathf.Clamp(
                coverSolution.FiringPathLength / speed + 6f,
                8f,
                24f);
            SetTacticalTask(
                TacticalMovementPurpose.ReturnToCover,
                priority,
                "Задний ход заблокирован; завершение возврата передним ходом",
                repositionExpiresAt);
            State = CombatAiState.Reposition;
            navigation.SetPaused(false);
            lastTacticalPositionResult = "Возврат задним ходом заблокирован; путь в укрытие перестроен";
            return true;
        }

        private void UpdateArmorFacing(Vector2 towardThreat)
        {
            if (navigation == null || towardThreat.sqrMagnitude <= 0.001f)
            {
                return;
            }

            Vector2 positive = threatAnalysis.IsCrossfire
                ? towardThreat.normalized
                : armorUseProfile.DesiredForward(towardThreat, 1);
            Vector2 negative = threatAnalysis.IsCrossfire
                ? towardThreat.normalized
                : armorUseProfile.DesiredForward(towardThreat, -1);
            float positiveTurn = Mathf.Abs(Vector2.SignedAngle(transform.up, positive));
            float negativeTurn = Mathf.Abs(Vector2.SignedAngle(transform.up, negative));
            Vector2 desired = Mathf.Approximately(positiveTurn, negativeTurn)
                ? (StableDecisionSeed() & 1) == 0 ? positive : negative
                : positiveTurn < negativeTurn ? positive : negative;
            navigation.SetFacingDirection(desired);
        }

        private void MaintainArmorFacingDuringDefensiveMovement(Vector2 targetPosition)
        {
            if (navigation == null) return;
            if (motor == null || Mathf.Abs(motor.CurrentSpeed) <= 0.35f)
            {
                defensiveRearExposureSince = float.NegativeInfinity;
                return;
            }
            Vector2 targetDirection = targetPosition - (Vector2)transform.position;
            if (targetDirection.sqrMagnitude <= 0.001f) return;
            float targetRelativeAngle = Mathf.Abs(Vector2.SignedAngle(
                transform.up,
                targetDirection.normalized));
            if (targetRelativeAngle < DefensiveRearDangerAngle)
            {
                defensiveRearExposureSince = float.NegativeInfinity;
                return;
            }
            if (float.IsNegativeInfinity(defensiveRearExposureSince))
            {
                defensiveRearExposureSince = Now;
                return;
            }
            if (Now - defensiveRearExposureSince < 2.2f) return;

            AbandonCover(
                $"Защитный маршрут отменён: угроза за кормой {targetRelativeAngle:0}°, " +
                $"скорость {motor.CurrentSpeed:0.00} м/с");
            State = CombatAiState.Engage;
            navigation.SetPaused(true);
            UpdateArmorFacing(targetDirection.normalized);
            defensiveRearExposureSince = float.NegativeInfinity;
        }

        private int StableDecisionSeed()
        {
            string entityId = identity?.Identity?.entityId;
            return BotPersonalityGenerator.StableSeed(
                string.IsNullOrWhiteSpace(entityId) ? gameObject.name : entityId);
        }

        private void OnImpactResolved(TankArmor source, ArmorImpactObservation observation)
        {
            if (health == null || health.IsDestroyed)
            {
                return;
            }

            threatMemory.Record(observation);
            primaryThreatDirection = observation.ThreatDirection;
            UpdateArmorFacing(primaryThreatDirection);
            float healthRatio = health.MaximumHitPoints > 0
                ? (float)health.CurrentHitPoints / health.MaximumHitPoints
                : 0f;
            if (observation.Outcome == ImpactOutcome.Penetrated)
            {
                lastPenetratingHitTime = Now;
            }
            if (observation.Outcome == ImpactOutcome.Penetrated &&
                (tacticalBehaviorProfile.CoverPreference >= 0.45f || healthRatio < 0.65f) &&
                Now >= nextCoverDecisionTime)
            {
                RequestCover(
                    "Получено пробитие",
                    threatAnalysis.IsCrossfire
                        ? TacticalDecisionPriority.SurviveCrossfire
                        : TacticalDecisionPriority.UseCover);
            }
        }

        private static bool IsCoverPurpose(TacticalMovementPurpose purpose)
        {
            return purpose == TacticalMovementPurpose.MoveToCover ||
                   purpose == TacticalMovementPurpose.HoldCover ||
                   purpose == TacticalMovementPurpose.PeekToFire ||
                   purpose == TacticalMovementPurpose.ReturnToCover;
        }

        private void SetTacticalTask(
            TacticalMovementPurpose purpose,
            TacticalDecisionPriority priority,
            string reason,
            float expiresAt)
        {
            if (!TacticalDecisionArbiter.ShouldReplace(
                    tacticalMovementPurpose,
                    tacticalDecisionPriority,
                    tacticalDecisionExpiresAt,
                    purpose,
                    priority,
                    Now))
            {
                return;
            }

            tacticalMovementPurpose = purpose;
            tacticalDecisionPriority = priority;
            tacticalDecisionReason = string.IsNullOrWhiteSpace(reason) ? purpose.ToString() : reason;
            tacticalDecisionExpiresAt = expiresAt;
        }

        private void ClearTacticalTask(string reason)
        {
            if (IsCoverPurpose(tacticalMovementPurpose))
            {
                lastProtectiveTaskPriority = tacticalDecisionPriority;
                proactiveCoverAllowedAt = Now + ProactiveCoverCommitmentSeconds;
            }

            navigation?.ClearDestination();
            tacticalMovementPurpose = TacticalMovementPurpose.None;
            tacticalDecisionPriority = TacticalDecisionPriority.None;
            tacticalDecisionReason = string.IsNullOrWhiteSpace(reason)
                ? "Нет активной тактической задачи"
                : reason;
            tacticalDecisionExpiresAt = 0f;
            firingLineClearSince = -1f;
            defensiveRearExposureSince = float.NegativeInfinity;
            peekReachedAt = float.NegativeInfinity;
            navigation?.ClearFacingDirection();
        }

        private void AbandonCover(string reason)
        {
            TacticalCoverReservations.Release(GetEntityId().ToString());
            hasCoverPosition = false;
            hasCoverFiringPosition = false;
            holdingTacticalCover = false;
            returnForwardFallbackUsed = false;
            coverValidUntil = 0f;
            ClearTacticalTask(reason);
            lastTacticalPositionResult = reason;
        }

        private void ResumeRouteAfterContact()
        {
            if (!holdingTacticalCover)
            {
                ClearTacticalTask("Контакт потерян; возвращение к маршруту");
            }
            State = routeIndex > 0 ? CombatAiState.Advance : CombatAiState.Deploy;
            StartCurrentRoutePoint();
        }

        private bool ExecuteStrategicDirective()
        {
            if (!HasActiveStrategicDecision) return false;

            // A route is a mission, so it outranks the strategic layer until it is walked. Without this
            // the two systems deadlocked: the route index advances only once the tank reaches its current
            // point, while the strategy kept steering the tank back towards its own base, so the tank never
            // arrived and the route never progressed. Measured over one battle, the strategy took the
            // decision 240 times out of 360 route continuations and the route index stayed at zero.
            // Contact still overrides everything: a bot that sees an enemy fights, and the route resumes
            // when the contact ends.
            if (!routeComplete && routePoints.Length > 1 && !hasRememberedTarget)
            {
                return false;
            }

            StrategicOverrideCount++;
            if (strategicDecision.intent == BotStrategicIntentType.Wait)
            {
                navigation?.SetPaused(true);
                State = CombatAiState.Hold;
                return true;
            }

            if (strategicDecision.intent == BotStrategicIntentType.SupportAlly &&
                strategicAlly != null && strategicAlly.IsAlive)
            {
                Vector2 allyPosition = strategicAlly.transform.position;
                float distance = Vector2.Distance(transform.position, allyPosition);
                if (distance <= 3.5f)
                {
                    navigation?.SetPaused(true);
                    State = CombatAiState.Hold;
                }
                else
                {
                    navigation?.SetPaused(false);
                    if (navigation != null &&
                        (!navigation.HasDestination || Vector2.Distance(navigation.Destination, allyPosition) > 1.2f))
                        navigation.SetDestination(allyPosition);
                    State = CombatAiState.Advance;
                }
                return true;
            }

            if (strategicHasDestination)
            {
                navigation?.SetPaused(false);
                if (navigation != null &&
                    (!navigation.HasDestination || Vector2.Distance(navigation.Destination, strategicDestination) > DestinationRefreshDistance))
                    navigation.SetDestination(strategicDestination);
                State = strategicDecision.intent == BotStrategicIntentType.HuntKnownTarget
                    ? CombatAiState.Investigate
                    : CombatAiState.Advance;
                return true;
            }

            return false;
        }

        private void RefreshStrategicDecision()
        {
            if (strategicDecision == null || Now < strategicDecisionExpiresAt) return;
            strategicDecision = null;
            strategicHasDestination = false;
            strategicTarget = null;
            strategicAlly = null;
            preferredDistance = defaultPreferredDistance;
            if (tacticalMovementPurpose != TacticalMovementPurpose.MoveToCover &&
                tacticalMovementPurpose != TacticalMovementPurpose.DefensiveRetreat &&
                tacticalMovementPurpose != TacticalMovementPurpose.PeekToFire &&
                tacticalMovementPurpose != TacticalMovementPurpose.ReturnToCover &&
                tacticalMovementPurpose != TacticalMovementPurpose.HoldCover &&
                tacticalMovementPurpose != TacticalMovementPurpose.BreakCloseOrbit &&
                !holdingTacticalCover)
            {
                // Expiration is not a new movement order. If the tank is already walking the current
                // deployment leg, preserve its detailed path and progress instead of rebuilding from its
                // present position. Rebuilding was the synchronized U-turn seen at the 60-second mark.
                ResumeDeploymentRoute("Стратегическое решение завершено; продолжение маршрута");
            }
        }

        private void ContinueRoute()
        {
            navigation?.SetPaused(false);
            if (routePoints.Length == 0)
            {
                navigation?.ClearDestination();
                return;
            }

            if (routeComplete)
            {
                navigation?.ClearDestination();
                return;
            }

            if (navigation == null)
            {
                return;
            }

            // Advance only when there is nothing to walk to. Advancing because the tank merely stands
            // near a route point is wrong: a route's first point is normally where the tank spawned, and
            // treating that as "already reached" walked the whole index to the end in a few frames and
            // then reported the march complete, which looked exactly like an idle bot.
            if (navigation.HasDestination && !navigation.DestinationReached)
            {
                return;
            }

            // A path finder can finish at the closest walkable point when the authored route cell itself
            // overlaps dense imported geometry. That is a valid completion of this route leg, not a reason
            // to submit the same unreachable cell every decision tick. Only accept the completion when the
            // destination still belongs to the current route point; a completed cover or firing-line task
            // must not silently advance the strategic route.
            bool completedAssignedRoutePoint = navigation.DestinationReached &&
                                               routeIndex < routePoints.Length &&
                                               Vector2.Distance(navigation.Destination, routePoints[routeIndex]) <=
                                               DestinationRefreshDistance;
            bool completedFinalRoutePoint = completedAssignedRoutePoint &&
                                            routeIndex >= routePoints.Length - 1;
            if (completedAssignedRoutePoint && routeIndex < routePoints.Length - 1)
            {
                routeIndex++;
            }

            // Skip any route points the tank already stands on, so a spawn point at the head of the
            // route does not stall the march.
            SkipReachedRoutePoints();

            if (routeIndex >= routePoints.Length - 1 &&
                (completedFinalRoutePoint ||
                 Vector2.Distance(PhysicsPosition, routePoints[routeIndex]) < RoutePointTolerance))
            {
                routeComplete = true;
                State = CombatAiState.Advance;
                navigation.ClearDestination();
                return;
            }

            State = routeIndex > 0 ? CombatAiState.Advance : CombatAiState.Deploy;
            RouteContinuationCount++;
            navigation.SetDestination(routePoints[routeIndex]);
        }

        private void ResumeDeploymentRoute(string reason)
        {
            bool hasCurrentRouteDestination = navigation != null && navigation.HasDestination &&
                                              routeIndex < routePoints.Length &&
                                              Vector2.Distance(navigation.Destination, routePoints[routeIndex]) <=
                                              DestinationRefreshDistance;
            if (tacticalMovementPurpose != TacticalMovementPurpose.None)
            {
                ClearTacticalTask(reason);
            }
            else
            {
                tacticalDecisionReason = reason;
                if (navigation != null && navigation.HasDestination && !hasCurrentRouteDestination)
                {
                    navigation.ClearDestination();
                }
            }

            ContinueRoute();
        }

        /// <summary>
        /// Tolerance at which a route point counts as reached. It matches the navigation agent rather
        /// than using a small constant: navigation cells are far coarser than a hull, so a route point
        /// routinely sits where a tank cannot stand, and demanding a tighter approach than the agent's
        /// own left bots circling their first waypoint for an entire battle.
        /// </summary>
        private float RoutePointTolerance => navigation != null ? Mathf.Max(0.45f, navigation.WaypointTolerance) : 0.45f;

        /// <summary>
        /// Moves the route index past every point the tank already stands on. A route normally begins at
        /// the spawn point itself, so without this the first waypoint is already "reached", the index
        /// never leaves zero, and the bot stays in Deploy circling its own start while the reported
        /// destination looks perfectly valid.
        /// </summary>
        private void SkipReachedRoutePoints()
        {
            while (routeIndex < routePoints.Length - 1 &&
                   Vector2.Distance(PhysicsPosition, routePoints[routeIndex]) < RoutePointTolerance)
            {
                routeIndex++;
            }
        }

        private void StartCurrentRoutePoint()
        {
            if (navigation == null || routePoints.Length == 0)
            {
                return;
            }

            routeIndex = Mathf.Clamp(routeIndex, 0, routePoints.Length - 1);
            SkipReachedRoutePoints();
            navigation.SetPaused(false);
            State = routeIndex > 0 ? CombatAiState.Advance : CombatAiState.Deploy;
            navigation.SetDestination(routePoints[routeIndex]);
        }

        private bool IsTargetVisible(TeamMember candidate)
        {
            return candidate != null && candidate.isActiveAndEnabled && candidate.gameObject.activeInHierarchy &&
                   member != null && visionSystem != null &&
                   visionSystem.IsVisibleTo(member.Team, candidate) && candidate.IsAlive;
        }

        private void UpdateMemoryFromContact()
        {
            if (target == null || member == null || visionSystem == null)
            {
                return;
            }

            if (visionSystem.TryGetContact(member.Team, target, out VisionContact contact) && contact.HasMemory)
            {
                lastKnownTargetPosition = contact.LastKnownPosition;
                hasRememberedTarget = true;
            }
            else
            {
                hasRememberedTarget = false;
                target = null;
            }
        }

        private void ResolveReferences()
        {
            if (member == null) member = GetComponent<TeamMember>();
            if (body == null) body = GetComponent<Rigidbody2D>();
            if (navigation == null) navigation = GetComponent<NavigationAgent>();
            if (motor == null) motor = GetComponent<TankMotor>();
            if (turret == null) turret = GetComponentInChildren<TurretAiming>(true);
            if (weapon == null) weapon = GetComponent<WeaponController>();
            if (health == null) health = GetComponent<TankHealth>();
            if (armor == null) armor = GetComponent<TankArmor>();
            if (identity == null) identity = GetComponent<BotIdentityComponent>();
            if (battleDirector == null) battleDirector = GetComponentInParent<BattleDirector>();
        }

        private void OnDestroyed(TankHealth destroyedHealth)
        {
            HandleDestroyed();
        }

    }
}
