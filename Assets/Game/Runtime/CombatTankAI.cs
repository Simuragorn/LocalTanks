using System;
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

        [SerializeField] private TeamMember member;
        [SerializeField] private TeamVisionSystem visionSystem;
        [SerializeField] private NavigationAgent navigation;
        [SerializeField] private TankMotor motor;
        [SerializeField] private TurretAiming turret;
        [SerializeField] private WeaponController weapon;
        [SerializeField] private TankHealth health;
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

        public CombatAiState State { get; private set; } = CombatAiState.Deploy;
        public FireBlockReason CurrentFireBlockReason { get; private set; } = FireBlockReason.NoVisibleTarget;
        public TeamMember CurrentTarget => target;
        public Vector2 LastKnownTargetPosition => lastKnownTargetPosition;
        public bool HasRememberedTarget => hasRememberedTarget;
        public float PreferredDistance => preferredDistance;
        public Vector2 RepositionDestination => repositionDestination;
        public BattleLane Lane => lane;
        public CombatAiRole Role => role;

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
        }

        private void OnDisable()
        {
            if (health != null)
            {
                health.Destroyed -= OnDestroyed;
            }

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
            preferredDistance = CombatAiPolicy.PreferredDistance(member != null ? member.Definition : null, role);
            nextDecisionTime = Time.time + Mathf.Repeat(decisionPhase, DecisionInterval);
            State = CombatAiState.Deploy;
            target = null;
            hasRememberedTarget = false;
            investigationDestinationSet = false;
            StartCurrentRoutePoint();
        }

        public void HandleDestroyed()
        {
            State = CombatAiState.Destroyed;
            CurrentFireBlockReason = FireBlockReason.NoVisibleTarget;
            target = null;
            hasRememberedTarget = false;
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
            bool targetVisible = IsTargetVisible(target);
            if (!targetVisible)
            {
                UpdateMemoryFromContact();
            }

            TeamMember selected = SelectBestVisibleTarget();
            if (selected != null)
            {
                target = selected;
                lastKnownTargetPosition = selected.transform.position;
                hasRememberedTarget = true;
                investigationDestinationSet = false;
                if (State == CombatAiState.Reposition && Time.time < repositionExpiresAt &&
                    navigation != null && !navigation.DestinationReached &&
                    EvaluateLineOfFire(selected) != FireBlockReason.None)
                {
                    return;
                }

                State = CombatAiState.Engage;
                return;
            }

            if (target != null)
            {
                UpdateMemoryFromContact();
            }

            bool routeStarted = routeIndex > 0;
            State = CombatAiPolicy.ResolveState(true, false, hasRememberedTarget, routeStarted);
            if (State == CombatAiState.Investigate)
            {
                BeginOrContinueInvestigation();
            }
            else
            {
                ContinueRoute();
            }
        }

        private void ActOnTarget()
        {
            if ((State != CombatAiState.Engage && State != CombatAiState.Reposition) || !IsTargetVisible(target))
            {
                if (State != CombatAiState.Investigate)
                {
                    CurrentFireBlockReason = FireBlockReason.NoVisibleTarget;
                }

                return;
            }

            Vector2 targetPosition = target.transform.position;
            lastKnownTargetPosition = targetPosition;
            hasRememberedTarget = true;
            turret?.SetTarget(targetPosition);

            float distance = Vector2.Distance(transform.position, targetPosition);
            if (State == CombatAiState.Reposition)
            {
                navigation?.SetPaused(false);
                CurrentFireBlockReason = EvaluateFirePermission(target, distance);
                if (CurrentFireBlockReason == FireBlockReason.None)
                {
                    weapon.TryFire();
                }

                return;
            }

            if (distance > preferredDistance * 1.12f)
            {
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
            }

            CurrentFireBlockReason = EvaluateFirePermission(target, distance);
            if (CurrentFireBlockReason == FireBlockReason.None)
            {
                weapon.TryFire();
            }
            else if ((CurrentFireBlockReason == FireBlockReason.AllyInLine ||
                      CurrentFireBlockReason == FireBlockReason.ObstacleInLine) &&
                     Time.time >= nextRepositionTime)
            {
                BeginReposition(targetPosition);
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
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        private FireBlockReason EvaluateFirePermission(TeamMember candidate, float distance)
        {
            if (!IsTargetVisible(candidate)) return FireBlockReason.NoVisibleTarget;
            if (member?.Definition == null || distance > member.Definition.viewRange) return FireBlockReason.OutOfRange;
            if (turret == null || turret.AimErrorDegrees > AimToleranceDegrees) return FireBlockReason.Aiming;
            FireBlockReason lineResult = EvaluateLineOfFire(candidate);
            if (lineResult != FireBlockReason.None) return lineResult;
            if (weapon == null || !weapon.IsReady) return FireBlockReason.Reloading;
            return FireBlockReason.None;
        }

        private FireBlockReason EvaluateLineOfFire(TeamMember candidate)
        {
            if (candidate == null || weapon == null)
            {
                return FireBlockReason.ObstacleInLine;
            }

            Vector2 origin = weapon.Muzzle != null ? weapon.Muzzle.position : transform.position;
            Vector2 destination = candidate.transform.position;
            Vector2 delta = destination - origin;
            float distance = delta.magnitude;
            if (distance <= 0.001f)
            {
                return FireBlockReason.None;
            }

            foreach (RaycastHit2D hit in Physics2D.RaycastAll(origin, delta / distance, distance + 0.2f))
            {
                if (hit.collider == null || hit.collider.isTrigger || BelongsTo(hit.collider.transform, transform))
                {
                    continue;
                }

                if (BelongsTo(hit.collider.transform, candidate.transform))
                {
                    return FireBlockReason.None;
                }

                TeamMember hitMember = hit.collider.GetComponentInParent<TeamMember>();
                if (hitMember != null && hitMember.Team == member.Team)
                {
                    return FireBlockReason.AllyInLine;
                }

                return FireBlockReason.ObstacleInLine;
            }

            return FireBlockReason.None;
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
            if (navigation == null)
            {
                return;
            }

            Vector2 direction = (targetPosition - (Vector2)transform.position).normalized;
            if (direction.sqrMagnitude < 0.001f)
            {
                direction = transform.up;
            }

            Vector2 perpendicular = new Vector2(-direction.y, direction.x);
            float side = GetEntityId().GetHashCode() % 2 == 0 ? 1f : -1f;
            Vector2 origin = transform.position;
            Vector2 candidate = origin + perpendicular * (2.2f * side) - direction * 0.5f;
            bool found = navigation.SetDestination(candidate);
            if (!found)
            {
                candidate = origin - perpendicular * (2.2f * side) - direction * 0.5f;
                found = navigation.SetDestination(candidate);
            }

            nextRepositionTime = Time.time + 1.2f;
            if (!found)
            {
                State = CombatAiState.Engage;
                return;
            }

            repositionDestination = candidate;
            repositionExpiresAt = Time.time + 3f;
            State = CombatAiState.Reposition;
            navigation.SetPaused(false);
        }

        private void ResumeRouteAfterContact()
        {
            State = routeIndex > 0 ? CombatAiState.Advance : CombatAiState.Deploy;
            StartCurrentRoutePoint();
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
            return candidate != null && member != null && visionSystem != null &&
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
        }

        private void OnDestroyed(TankHealth destroyedHealth)
        {
            HandleDestroyed();
        }

        private static bool BelongsTo(Transform candidate, Transform root)
        {
            return candidate == root || candidate.IsChildOf(root);
        }
    }
}
