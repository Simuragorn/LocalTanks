using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LocalTanks
{
    [RequireComponent(typeof(TeamMember), typeof(NavigationAgent), typeof(TankMotor))]
    public sealed class CombatTankAI : MonoBehaviour
    {
        private const float DecisionInterval = 0.22f;
        private const float AimToleranceDegrees = 4f;
        private const float DestinationRefreshDistance = 0.8f;
        private const float AimRecoveryDelay = 3f;
        private const float FailedPositionMemorySeconds = 10f;

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
        private float preferredDistance;
        private Vector2 repositionDestination;
        private float repositionExpiresAt;
        private float nextRepositionTime;
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
        private float holdCoverUntil;
        private float coverReachedAt;
        private bool holdingTacticalCover;
        private bool returnForwardFallbackUsed;
        private bool lastCoverReturnStartedInReverse;
        private int completedCoverCycleShots;
        private Vector2 primaryThreatDirection;

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
        public bool HasActiveStrategicDecision => strategicDecision != null && Time.time < strategicDecisionExpiresAt;
        public LineOfFireAssessment CurrentLineOfFire => currentLineOfFire;
        public bool HasFiringPositionSolution => hasFiringPositionSolution;
        public TacticalFiringPositionSolution FiringPositionSolution => firingPositionSolution;
        public string LastTacticalPositionResult => lastTacticalPositionResult;
        public ArmorUseProfile ArmorProfileUsage => armorUseProfile;
        public TacticalMovementPurpose TacticalMovementPurpose => tacticalMovementPurpose;
        public TacticalDecisionPriority TacticalDecisionPriority => tacticalDecisionPriority;
        public string TacticalDecisionReason => tacticalDecisionReason;
        public int ActiveThreatCount => threatMemory.ActiveCount(Time.time);
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
            nextDecisionTime = Time.time + Mathf.Repeat(decisionPhase, DecisionInterval);
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
            holdCoverUntil = 0f;
            coverReachedAt = 0f;
            holdingTacticalCover = false;
            completedCoverCycleShots = 0;
            primaryThreatDirection = Vector2.zero;
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
            strategicDecisionExpiresAt = Time.time + Mathf.Clamp(decision.durationSeconds, 1f, 60f);
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
                                     holdingTacticalCover;
            if (strategicHasDestination && navigation != null && !defensiveOverride)
            {
                navigation.SetPaused(false);
                navigation.SetDestination(strategicDestination);
            }
            else if (decision.intent == BotStrategicIntentType.Wait && !defensiveOverride)
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
            targetLockedUntil = 0f;
            failedFiringPositions.Clear();
            currentLineOfFire = default;
            hasFiringPositionSolution = false;
            lastTacticalPositionResult = "Машина уничтожена";
            threatMemory.Clear();
            threatAnalysis = default;
            tacticalMovementPurpose = TacticalMovementPurpose.None;
            tacticalDecisionPriority = TacticalDecisionPriority.None;
            tacticalDecisionReason = "Машина уничтожена";
            hasCoverPosition = false;
            hasCoverFiringPosition = false;
            coverRequested = false;
            holdingTacticalCover = false;
            TacticalCoverReservations.Release(GetEntityId().ToString());
            navigation?.ClearDestination();
            motor?.StopImmediately();
            turret?.ClearTarget();
        }

        private void Update()
        {
            if (health == null || health.IsDestroyed)
            {
                HandleDestroyed();
                return;
            }

            if (Time.time >= nextDecisionTime)
            {
                nextDecisionTime = Time.time + DecisionInterval;
                Think();
            }

            ActOnTarget();
        }

        private void Think()
        {
            RefreshStrategicDecision();
            RefreshThreatAnalysis();
            if (ContinueDefensiveMovement())
            {
                return;
            }
            if (tacticalMovementPurpose == TacticalMovementPurpose.PeekToFire &&
                State == CombatAiState.Hold)
            {
                if (!IsTargetVisible(target) || Time.time >= tacticalDecisionExpiresAt)
                {
                    BeginReturnToCover();
                }
                return;
            }
            RequestProactiveCoverWhenExposed();
            if (coverRequested && Time.time >= nextCoverDecisionTime && TryBeginCoverMovement())
            {
                return;
            }
            if (State == CombatAiState.Hold && holdingTacticalCover)
            {
                navigation?.SetPaused(true);
                UpdateArmorFacing(primaryThreatDirection);
                bool minimumHoldComplete = Time.time >= coverReachedAt +
                                           tacticalBehaviorProfile.MinimumCoverHoldSeconds;
                if (minimumHoldComplete && IsTargetVisible(target) &&
                    weapon != null && weapon.ReloadProgress >= tacticalBehaviorProfile.PeekReloadThreshold &&
                    TryBeginPeekFromCover())
                {
                    return;
                }

                if (Time.time < holdCoverUntil)
                {
                    return;
                }

                ClearTacticalTask("Ожидание в укрытии завершено");
                holdingTacticalCover = false;
                State = IsTargetVisible(target) ? CombatAiState.Engage : CombatAiState.Advance;
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
                    targetLockedUntil = Time.time + CombatAiPolicy.TargetLockSeconds;
                    aimingBlockedSince = -1f;
                }
                target = selected;
                lastKnownTargetPosition = selected.transform.position;
                lastTargetVisibleTime = Time.time;
                hasRememberedTarget = true;
                investigationDestinationSet = false;
                if (State == CombatAiState.Reposition && navigation != null)
                {
                    currentLineOfFire = EvaluateLineOfFireAssessment(selected);
                    if (currentLineOfFire.IsClear)
                    {
                        hasFiringPositionSolution = false;
                        ClearTacticalTask("Прострел получен");
                        lastTacticalPositionResult = "Прострел получен";
                    }
                    else if (Time.time < repositionExpiresAt && !navigation.DestinationReached)
                    {
                        return;
                    }
                    else
                    {
                        RememberFailedFiringPosition();
                    }
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
                    Time.time,
                    repositionExpiresAt))
            {
                navigation.SetPaused(false);
                return;
            }

            if (hasRememberedTarget)
            {
                if (CombatAiPolicy.ShouldDelayInvestigation(Time.time, lastTargetVisibleTime))
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
                aimingBlockedSince = -1f;
                if (State != CombatAiState.Investigate)
                {
                    CurrentFireBlockReason = FireBlockReason.NoVisibleTarget;
                }

                return;
            }

            Vector2 targetPosition = target.transform.position;
            lastKnownTargetPosition = targetPosition;
            lastTargetVisibleTime = Time.time;
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
                if (tacticalMovementPurpose == TacticalMovementPurpose.MoveToCover ||
                    tacticalMovementPurpose == TacticalMovementPurpose.ReturnToCover ||
                    tacticalMovementPurpose == TacticalMovementPurpose.DefensiveRetreat ||
                    tacticalMovementPurpose == TacticalMovementPurpose.PeekToFire)
                {
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
                navigation?.ClearFacingDirection();
                navigation?.SetPaused(false);
                if (navigation != null &&
                    (!navigation.HasDestination || Vector2.Distance(navigation.Destination, targetPosition) > DestinationRefreshDistance))
                {
                    navigation.SetDestination(targetPosition);
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
                     Time.time >= nextRepositionTime)
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
                aimingBlockedSince = Time.time;
                return;
            }

            if (Time.time - aimingBlockedSince >= AimRecoveryDelay)
            {
                navigation?.SetPaused(true);
            }
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
            foreach (TeamMember candidate in TeamMember.ActiveMembers.ToArray())
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
                    Time.time, targetLockedUntil, currentScore, bestScore))
            {
                return target;
            }

            return best;
        }

        private FireBlockReason EvaluateFirePermission(TeamMember candidate, float distance)
        {
            if (!IsTargetVisible(candidate)) return FireBlockReason.NoVisibleTarget;
            if (member?.Definition == null || distance > member.Definition.viewRange) return FireBlockReason.OutOfRange;
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
                out TacticalFiringPositionSolution solution);

            nextRepositionTime = Time.time + (found ? 1.2f : 2.5f);
            if (!found || !navigation.SetDestination(solution.Position))
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
            firingPositionSolution = solution;
            hasFiringPositionSolution = true;
            repositionDestination = solution.Position;
            float speed = Mathf.Max(0.5f, member.Definition.maxForwardSpeed * 0.55f);
            repositionExpiresAt = Time.time + Mathf.Clamp(solution.PathLength / speed + 3f, 4f, 18f);
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
            Vector2 direction = (destination - origin).normalized;
            Vector2 projectedMuzzle = origin + direction * 0.35f;
            return CombatLineOfFire.Evaluate(
                transform,
                member.Team,
                projectedMuzzle,
                destination,
                candidate.transform);
        }

        private bool IsFailedFiringPosition(Vector2Int cell)
        {
            return failedFiringPositions.TryGetValue(cell, out float expiresAt) && expiresAt > Time.time;
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

            failedFiringPositions[firingPositionSolution.Cell] = Time.time + FailedPositionMemorySeconds;
            lastTacticalPositionResult = navigation != null && navigation.DestinationReached
                ? "Выбранная позиция достигнута, но прострел не появился"
                : "Манёвр не завершён в расчётное время";
            hasFiringPositionSolution = false;
            ClearTacticalTask(lastTacticalPositionResult);
        }

        private void RemoveExpiredFailedPositions()
        {
            if (failedFiringPositions.Count == 0)
            {
                return;
            }

            Vector2Int[] expired = failedFiringPositions
                .Where(item => item.Value <= Time.time)
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
                foreach (TeamMember candidate in TeamMember.ActiveMembers.ToArray())
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

            threatAnalysis = threatMemory.Analyze(Time.time, visibleDirections);
            primaryThreatDirection = threatAnalysis.PrimaryDirection;
        }

        private void RequestProactiveCoverWhenExposed()
        {
            if (coverRequested || holdingTacticalCover || hasCoverPosition && Time.time < coverValidUntil ||
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
            if (tacticalBehaviorProfile.ShouldSeekCoverWhileReloading(
                    weapon.ReloadProgress,
                    exposure.IsClear,
                    false))
            {
                RequestCover("Перезарядка под видимой угрозой", TacticalDecisionPriority.UseCover);
            }
        }

        private void RequestCover(string reason, TacticalDecisionPriority priority)
        {
            if (Time.time < nextCoverDecisionTime || priority < coverRequestPriority) return;
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
                return false;
            }

            Vector2 expectedPosition = tacticalMovementPurpose == TacticalMovementPurpose.PeekToFire
                ? coverFiringPosition
                : coverPosition;
            bool tacticallyProtectedArrival = tacticalMovementPurpose != TacticalMovementPurpose.PeekToFire &&
                                                IsTacticallyProtectedNear(expectedPosition);
            if (!navigation.DestinationReached && !tacticallyProtectedArrival &&
                Time.time < repositionExpiresAt)
            {
                navigation.SetPaused(false);
                if (navigation.MovingInReverse) UpdateArmorFacing(primaryThreatDirection);
                return true;
            }

            bool reached = navigation.DestinationReached ||
                           Vector2.Distance(transform.position, expectedPosition) <= 0.55f ||
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
            UpdateArmorFacing(primaryThreatDirection);
            State = CombatAiState.Hold;
            if (completedPurpose == TacticalMovementPurpose.PeekToFire)
            {
                holdingTacticalCover = false;
                SetTacticalTask(
                    TacticalMovementPurpose.PeekToFire,
                    completedPriority,
                    "Огневая точка достигнута; наведение и один выстрел",
                    Time.time + 12f);
                lastTacticalPositionResult = "Выезд из укрытия завершён; готовится выстрел";
                return true;
            }

            holdingTacticalCover = true;
            coverReachedAt = Time.time;
            holdCoverUntil = Time.time + tacticalBehaviorProfile.MaximumCoverHoldSeconds;
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
            coverRequestReason = string.Empty;
            coverRequestPriority = TacticalDecisionPriority.None;
            nextCoverDecisionTime = Time.time + 3f;
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
                    navigation.Map.WorldToCell(candidate), ownerId, Time.time);
            Func<Vector2, bool> canFireFrom = IsTargetVisible(target)
                ? candidate => EvaluateLineOfFireFrom(candidate, target).IsClear
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
                    solution.Cell, ownerId, Time.time, 30f))
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
            coverValidUntil = Time.time + 24f;
            repositionDestination = solution.Position;
            float speed = reverseRetreat
                ? Mathf.Max(0.2f, member.Definition.maxReverseSpeed * 0.4f)
                : Mathf.Max(0.35f, member.Definition.maxForwardSpeed * 0.35f);
            repositionExpiresAt = Time.time + Mathf.Clamp(solution.PathLength / speed + 8f, 10f, 38f);
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
                out solution);
        }

        private bool ShouldReverseToCover(TacticalCoverSolution solution)
        {
            if (member?.Definition == null || solution.PathLength > 4.5f ||
                member.Definition.maxReverseSpeed <= 0.05f || primaryThreatDirection.sqrMagnitude <= 0.001f)
            {
                return false;
            }

            return Vector2.Dot(transform.up, primaryThreatDirection.normalized) > 0.15f;
        }

        private bool IsProtectedFromThreat(Vector2 threatOrigin, Vector2 candidate)
        {
            LineOfFireAssessment assessment = CombatLineOfFire.Evaluate(
                transform,
                member.Team,
                threatOrigin,
                candidate,
                null);
            return assessment.BlockType == LineOfFireBlockType.HardObstacle ||
                   assessment.BlockType == LineOfFireBlockType.DestructibleObstacle;
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
                Time.time >= coverValidUntil || !IsTargetVisible(target))
            {
                return false;
            }

            if (!EvaluateLineOfFireFrom(coverFiringPosition, target).IsClear)
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
            repositionExpiresAt = Time.time + Mathf.Clamp(
                coverSolution.FiringPathLength / speed + 3f,
                3f,
                10f);
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

            if (afterShot && hasCoverFiringPosition)
            {
                completedCoverCycleShots++;
            }
            lastCoverReturnStartedInReverse = true;
            returnForwardFallbackUsed = false;
            repositionDestination = coverPosition;
            float speed = member?.Definition != null
                ? Mathf.Max(0.2f, member.Definition.maxReverseSpeed * 0.4f)
                : 0.25f;
            repositionExpiresAt = Time.time + Mathf.Clamp(
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
            repositionExpiresAt = Time.time + Mathf.Clamp(
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
                ? (GetEntityId().GetHashCode() & 1) == 0 ? positive : negative
                : positiveTurn < negativeTurn ? positive : negative;
            navigation.SetFacingDirection(desired);
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
            if (observation.Outcome == ImpactOutcome.Penetrated &&
                (tacticalBehaviorProfile.CoverPreference >= 0.45f || healthRatio < 0.65f) &&
                Time.time >= nextCoverDecisionTime)
            {
                RequestCover(
                    "Получено пробитие",
                    threatAnalysis.IsCrossfire
                        ? TacticalDecisionPriority.SurviveCrossfire
                        : TacticalDecisionPriority.UseCover);
            }
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
                    Time.time))
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
            tacticalMovementPurpose = TacticalMovementPurpose.None;
            tacticalDecisionPriority = TacticalDecisionPriority.None;
            tacticalDecisionReason = string.IsNullOrWhiteSpace(reason)
                ? "Нет активной тактической задачи"
                : reason;
            tacticalDecisionExpiresAt = 0f;
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
            if (strategicDecision == null || Time.time < strategicDecisionExpiresAt) return;
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
                !holdingTacticalCover)
            {
                StartCurrentRoutePoint();
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

            if (!navigation.HasDestination || navigation.DestinationReached ||
                Vector2.Distance(transform.position, routePoints[routeIndex]) < 0.45f)
            {
                if (routeIndex >= routePoints.Length - 1)
                {
                    routeComplete = true;
                    navigation.ClearDestination();
                    return;
                }

                routeIndex = Mathf.Min(routeIndex + 1, routePoints.Length - 1);
                State = routeIndex > 0 ? CombatAiState.Advance : CombatAiState.Deploy;
                navigation.SetDestination(routePoints[routeIndex]);
            }
        }

        private void StartCurrentRoutePoint()
        {
            if (navigation == null || routePoints.Length == 0)
            {
                return;
            }

            routeIndex = Mathf.Clamp(routeIndex, 0, routePoints.Length - 1);
            navigation.SetPaused(false);
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
            if (navigation == null) navigation = GetComponent<NavigationAgent>();
            if (motor == null) motor = GetComponent<TankMotor>();
            if (turret == null) turret = GetComponentInChildren<TurretAiming>(true);
            if (weapon == null) weapon = GetComponent<WeaponController>();
            if (health == null) health = GetComponent<TankHealth>();
            if (armor == null) armor = GetComponent<TankArmor>();
            if (identity == null) identity = GetComponent<BotIdentityComponent>();
        }

        private void OnDestroyed(TankHealth destroyedHealth)
        {
            HandleDestroyed();
        }

    }
}
