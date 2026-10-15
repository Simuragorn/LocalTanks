using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LocalTanks
{
    public sealed class VisionContact
    {
        public bool IsVisible { get; internal set; }
        public Vector2 LastKnownPosition { get; internal set; }
        public float LastSeenTime { get; internal set; } = float.NegativeInfinity;
        public bool HasMemory { get; internal set; }
    }

    public readonly struct VisionDiagnostic
    {
        public VisionDiagnostic(
            TeamMember observer,
            TeamMember target,
            DetectionResult result,
            float distance,
            float bushBonus)
        {
            Observer = observer;
            Target = target;
            Result = result;
            Distance = distance;
            BushBonus = bushBonus;
        }

        public TeamMember Observer { get; }
        public TeamMember Target { get; }
        public DetectionResult Result { get; }
        public float Distance { get; }
        public float BushBonus { get; }
    }

    [DefaultExecutionOrder(-300)]
    public sealed class TeamVisionSystem : MonoBehaviour
    {
        [SerializeField] private VisionRules rules;
        [SerializeField] private TeamId localPlayerTeam = TeamId.TeamA;

        private readonly Queue<ObservationPair> pendingPairs = new Queue<ObservationPair>();
        private readonly HashSet<ContactKey> spottedThisSweep = new HashSet<ContactKey>();
        private readonly Dictionary<ContactKey, VisionContact> contacts = new Dictionary<ContactKey, VisionContact>();
        private readonly List<VisionDiagnostic> diagnostics = new List<VisionDiagnostic>();
        private readonly List<RaycastHit2D> worldPointHits = new List<RaycastHit2D>(16);
        private readonly List<RaycastHit2D> detectionHits = new List<RaycastHit2D>(64);
        private readonly HashSet<ConcealmentZone> detectionZones = new HashSet<ConcealmentZone>();
        private readonly Dictionary<WorldVisibilityCell, bool> worldVisibilityCache =
            new Dictionary<WorldVisibilityCell, bool>();
        private ContactFilter2D worldPointFilter = new ContactFilter2D().NoFilter();
        private ContactFilter2D detectionFilter = new ContactFilter2D().NoFilter();
        private BattleDirector battleDirector;
        private float nextSweepTime;
        private bool sweepInProgress;

        public event Action<TeamId, TeamMember, bool> VisibilityChanged;

        public VisionRules Rules => rules;
        public TeamId LocalPlayerTeam => localPlayerTeam;
        public IReadOnlyList<VisionDiagnostic> Diagnostics => diagnostics;
        public int WorldVisibilityQueryCount { get; private set; }
        public int WorldVisibilityEvaluationCount { get; private set; }
        private float Now => battleDirector != null ? (float)battleDirector.SimulationTime : Time.time;

        // Track prints are much closer to each other than any useful tactical distinction. Grouping their
        // cosmetic ground-visibility checks into half-metre cells prevents hundreds of neighbouring
        // prints from repeating the same team scan and the same obstacle raycasts in one vision sweep.
        private const float WorldVisibilityCellSize = 0.5f;

        private void OnEnable()
        {
            battleDirector = FindObjectsByType<BattleDirector>()
                .FirstOrDefault(item => item != null && item.gameObject.scene == gameObject.scene);
            TeamMember.MembershipChanged += OnMembershipChanged;
            nextSweepTime = 0f;
            RefreshPresenters();
        }

        private void OnDisable()
        {
            TeamMember.MembershipChanged -= OnMembershipChanged;
            worldVisibilityCache.Clear();
            foreach (TeamMember member in TeamMember.ActiveMembers)
            {
                member?.VisibilityPresenter?.SetVisible(true);
            }
        }

        private void FixedUpdate()
        {
            using var performanceScope = BattlePerformanceProbe.Measure(BattlePerformanceSubsystem.Vision);
            if (rules == null)
            {
                return;
            }

            if (pendingPairs.Count == 0 && Now >= nextSweepTime)
            {
                BeginSweep();
            }

            int budget = Mathf.Max(1, rules.checksPerFrame);
            while (budget-- > 0 && pendingPairs.Count > 0)
            {
                EvaluatePair(pendingPairs.Dequeue());
            }

            if (pendingPairs.Count == 0 && sweepInProgress)
            {
                CompleteSweep();
            }
        }

        public void Configure(VisionRules newRules, TeamId playerTeam)
        {
            rules = newRules;
            localPlayerTeam = playerTeam;
            nextSweepTime = 0f;
            worldVisibilityCache.Clear();
            RefreshPresenters();
        }

        public bool IsVisibleTo(TeamId viewingTeam, TeamMember target)
        {
            if (target == null || target.Team == viewingTeam || target.Team == TeamId.Neutral)
            {
                return true;
            }

            return contacts.TryGetValue(new ContactKey(viewingTeam, target), out VisionContact contact) &&
                   contact.IsVisible;
        }

        public bool TryGetContact(TeamId viewingTeam, TeamMember target, out VisionContact contact)
        {
            return contacts.TryGetValue(new ContactKey(viewingTeam, target), out contact);
        }

        /// <summary>
        /// Whether an unobscured point of terrain is inside the current view of at least one living member
        /// of a team. Ground marks use this instead of their author's visibility: an enemy may have moved
        /// away while the old piece of track is still under observation.
        /// </summary>
        public bool IsWorldPositionVisibleTo(TeamId viewingTeam, Vector2 position)
        {
            if (rules == null)
            {
                return true;
            }

            foreach (TeamMember observer in TeamMember.ActiveMembers)
            {
                if (observer == null || !observer.isActiveAndEnabled || !observer.IsAlive ||
                    observer.Team != viewingTeam || observer.Definition == null)
                {
                    continue;
                }

                Vector2 origin = observer.transform.position;
                Vector2 delta = position - origin;
                float distance = delta.magnitude;
                if (distance > observer.Definition.viewRange)
                {
                    continue;
                }

                if (distance > 0.001f)
                {
                    Vector2 direction = delta / distance;
                    float dot = Vector2.Dot(observer.VisionForward, direction);
                    if (!DetectionMath.IsWithinViewArc(
                            dot,
                            rules.GetViewAngle(observer.Definition.vehicleClass)))
                    {
                        continue;
                    }

                    bool blocked = false;
                    worldPointHits.Clear();
                    Physics2D.Raycast(origin, direction, worldPointFilter, worldPointHits, distance);
                    foreach (RaycastHit2D hit in worldPointHits)
                    {
                        Collider2D collider = hit.collider;
                        if (collider == null || BelongsTo(collider.transform, observer.transform))
                        {
                            continue;
                        }

                        if (collider.GetComponentInParent<VisionBlocker>() != null)
                        {
                            blocked = true;
                            break;
                        }
                    }

                    if (blocked)
                    {
                        continue;
                    }
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Cached variant for dense cosmetic evidence such as track prints. The cache is discarded after
        /// every completed vision sweep, so moving observers and changing lines of sight remain current at
        /// the same cadence as tank visibility while adjacent prints share one expensive evaluation.
        /// </summary>
        public bool IsWorldPositionVisibleToCached(TeamId viewingTeam, Vector2 position)
        {
            WorldVisibilityQueryCount++;
            WorldVisibilityCell key = new WorldVisibilityCell(viewingTeam, position);
            if (worldVisibilityCache.TryGetValue(key, out bool visible))
            {
                return visible;
            }

            WorldVisibilityEvaluationCount++;
            visible = IsWorldPositionVisibleTo(viewingTeam, position);
            worldVisibilityCache.Add(key, visible);
            return visible;
        }

        public DetectionResult Evaluate(TeamMember observer, TeamMember target, out float bushBonus)
        {
            bushBonus = 0f;
            if (rules == null || observer == null || target == null ||
                observer.Definition == null || target.Definition == null)
            {
                return new DetectionResult(false, DetectionReason.OutsideViewRange, 0f, 0f);
            }

            Vector2 origin = observer.transform.position;
            Vector2 destination = target.transform.position;
            Vector2 delta = destination - origin;
            float distance = delta.magnitude;
            TankDefinition observerDefinition = observer.Definition;
            TankDefinition targetDefinition = target.Definition;
            bool withinViewArc = distance <= 0.001f || DetectionMath.IsWithinViewArc(
                Vector2.Dot(observer.VisionForward, delta / distance),
                rules.GetViewAngle(observerDefinition.vehicleClass));
            if (!withinViewArc)
            {
                return DetectionMath.Resolve(CreateInput(observer, target, distance, 0f, false, false));
            }

            if (distance > observerDefinition.viewRange)
            {
                return DetectionMath.Resolve(CreateInput(observer, target, distance, 0f, false, true));
            }

            bool hardBlocker = false;
            detectionZones.Clear();
            if (distance > 0.001f)
            {
                detectionHits.Clear();
                Physics2D.Raycast(origin, delta / distance, detectionFilter, detectionHits, distance);
                foreach (RaycastHit2D hit in detectionHits)
                {
                    Collider2D collider = hit.collider;
                    if (collider == null || BelongsTo(collider.transform, observer.transform) ||
                        BelongsTo(collider.transform, target.transform))
                    {
                        continue;
                    }

                    if (collider.GetComponentInParent<VisionBlocker>() != null)
                    {
                        hardBlocker = true;
                    }

                    ConcealmentZone zone = collider.GetComponentInParent<ConcealmentZone>();
                    if (zone != null && detectionZones.Add(zone))
                    {
                        bushBonus += zone.ConcealmentBonus;
                    }
                }
            }

            bushBonus = Mathf.Min(bushBonus, rules.maximumBushBonus);
            return DetectionMath.Resolve(CreateInput(observer, target, distance, bushBonus, hardBlocker, true));
        }

        public void ForceEvaluateAll()
        {
            pendingPairs.Clear();
            BeginSweep();
            while (pendingPairs.Count > 0)
            {
                EvaluatePair(pendingPairs.Dequeue());
            }

            if (sweepInProgress)
            {
                CompleteSweep();
            }
        }

        private DetectionInput CreateInput(
            TeamMember observer,
            TeamMember target,
            float distance,
            float bushBonus,
            bool hardBlocker,
            bool withinViewArc)
        {
            TankDefinition observerDefinition = observer.Definition;
            TankDefinition targetDefinition = target.Definition;
            float movementPenalty = target.IsMoving ? targetDefinition.movementRevealPenalty : 0f;
            float firingPenalty = Now - target.LastFiredTime <= targetDefinition.firingRevealDuration
                ? targetDefinition.firingRevealPenalty
                : 0f;
            return new DetectionInput(
                distance,
                observerDefinition.viewRange,
                targetDefinition.stationaryConcealment,
                bushBonus,
                movementPenalty,
                firingPenalty,
                rules.maximumConcealment,
                rules.minimumVisibilityFactor,
                observerDefinition.guaranteedDetectionRange,
                hardBlocker,
                withinViewArc);
        }

        private void BeginSweep()
        {
            pendingPairs.Clear();
            spottedThisSweep.Clear();
            diagnostics.Clear();
            sweepInProgress = true;
            TeamMember[] members = TeamMember.ActiveMembers
                .Where(member => member != null && member.isActiveAndEnabled && member.IsAlive && member.Team != TeamId.Neutral)
                .ToArray();
            foreach (TeamMember observer in members)
            {
                foreach (TeamMember target in members)
                {
                    if (observer.Team != target.Team)
                    {
                        pendingPairs.Enqueue(new ObservationPair(observer, target));
                    }
                }
            }

            nextSweepTime = -1f;
            if (pendingPairs.Count == 0)
            {
                CompleteSweep();
            }
        }

        private void EvaluatePair(ObservationPair pair)
        {
            if (pair.Observer == null || pair.Target == null || !pair.Observer.IsAlive || !pair.Target.IsAlive)
            {
                return;
            }

            DetectionResult result = Evaluate(pair.Observer, pair.Target, out float bushBonus);
            float distance = Vector2.Distance(pair.Observer.transform.position, pair.Target.transform.position);
            diagnostics.Add(new VisionDiagnostic(pair.Observer, pair.Target, result, distance, bushBonus));
            if (result.Detected)
            {
                spottedThisSweep.Add(new ContactKey(pair.Observer.Team, pair.Target));
            }
        }

        private void CompleteSweep()
        {
            sweepInProgress = false;
            worldVisibilityCache.Clear();
            TeamMember[] members = TeamMember.ActiveMembers.Where(member => member != null).ToArray();
            foreach (TeamMember target in members)
            {
                if (target.Team == TeamId.Neutral)
                {
                    continue;
                }

                TeamId viewingTeam = target.Team == TeamId.TeamA ? TeamId.TeamB : TeamId.TeamA;
                ContactKey key = new ContactKey(viewingTeam, target);
                if (!contacts.TryGetValue(key, out VisionContact contact))
                {
                    contact = new VisionContact();
                    contacts.Add(key, contact);
                }

                // A wreck is no longer a scout or a detection target, but it does not vanish when the
                // vehicle dies in sight. Preserve only an existing visible contact: this keeps the wreck
                // opaque without revealing an enemy that was destroyed outside the team's vision.
                bool visible = target.IsAlive
                    ? spottedThisSweep.Contains(key)
                    : contact.IsVisible;

                bool changed = contact.IsVisible != visible;
                contact.IsVisible = visible;
                if (visible)
                {
                    contact.LastKnownPosition = target.transform.position;
                    contact.LastSeenTime = Now;
                    contact.HasMemory = true;
                }
                else if (contact.HasMemory && rules != null &&
                         Now - contact.LastSeenTime > rules.contactMemorySeconds)
                {
                    contact.HasMemory = false;
                }

                if (changed)
                {
                    VisibilityChanged?.Invoke(viewingTeam, target, visible);
                }
            }

            RefreshPresenters();
            nextSweepTime = Now + Mathf.Max(0.05f, rules != null ? rules.checkInterval : 0.2f);
        }

        private void RefreshPresenters()
        {
            foreach (TeamMember member in TeamMember.ActiveMembers)
            {
                if (member == null || member.VisibilityPresenter == null)
                {
                    continue;
                }

                member.VisibilityPresenter.SetVisible(IsVisibleTo(localPlayerTeam, member));
            }
        }

        private void OnMembershipChanged(TeamMember member, bool added)
        {
            worldVisibilityCache.Clear();
            if (!added)
            {
                foreach (ContactKey key in contacts.Keys.Where(key => key.Target == member).ToArray())
                {
                    contacts.Remove(key);
                }

                return;
            }

            if (member != null && member.VisibilityPresenter != null)
            {
                member.VisibilityPresenter.SetVisible(IsVisibleTo(localPlayerTeam, member));
            }

            nextSweepTime = 0f;
        }

        private static bool BelongsTo(Transform candidate, Transform root)
        {
            return candidate == root || candidate.IsChildOf(root);
        }

        private readonly struct ObservationPair
        {
            public ObservationPair(TeamMember observer, TeamMember target)
            {
                Observer = observer;
                Target = target;
            }

            public TeamMember Observer { get; }
            public TeamMember Target { get; }
        }

        private readonly struct ContactKey : IEquatable<ContactKey>
        {
            public ContactKey(TeamId viewingTeam, TeamMember target)
            {
                ViewingTeam = viewingTeam;
                Target = target;
            }

            public TeamId ViewingTeam { get; }
            public TeamMember Target { get; }

            public bool Equals(ContactKey other)
            {
                return ViewingTeam == other.ViewingTeam && Target == other.Target;
            }

            public override bool Equals(object obj)
            {
                return obj is ContactKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return ((int)ViewingTeam * 397) ^ (Target != null ? Target.GetEntityId().GetHashCode() : 0);
            }
        }

        private readonly struct WorldVisibilityCell : IEquatable<WorldVisibilityCell>
        {
            public WorldVisibilityCell(TeamId viewingTeam, Vector2 position)
            {
                ViewingTeam = viewingTeam;
                X = Mathf.FloorToInt(position.x / WorldVisibilityCellSize);
                Y = Mathf.FloorToInt(position.y / WorldVisibilityCellSize);
            }

            private TeamId ViewingTeam { get; }
            private int X { get; }
            private int Y { get; }

            public bool Equals(WorldVisibilityCell other)
            {
                return ViewingTeam == other.ViewingTeam && X == other.X && Y == other.Y;
            }

            public override bool Equals(object obj)
            {
                return obj is WorldVisibilityCell other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = (int)ViewingTeam;
                    hash = (hash * 397) ^ X;
                    return (hash * 397) ^ Y;
                }
            }
        }
    }
}
