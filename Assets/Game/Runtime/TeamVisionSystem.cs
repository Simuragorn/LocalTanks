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

    public sealed class TeamVisionSystem : MonoBehaviour
    {
        [SerializeField] private VisionRules rules;
        [SerializeField] private TeamId localPlayerTeam = TeamId.TeamA;

        private readonly Queue<ObservationPair> pendingPairs = new Queue<ObservationPair>();
        private readonly HashSet<ContactKey> spottedThisSweep = new HashSet<ContactKey>();
        private readonly Dictionary<ContactKey, VisionContact> contacts = new Dictionary<ContactKey, VisionContact>();
        private readonly List<VisionDiagnostic> diagnostics = new List<VisionDiagnostic>();
        private float nextSweepTime;
        private bool sweepInProgress;

        public event Action<TeamId, TeamMember, bool> VisibilityChanged;

        public VisionRules Rules => rules;
        public TeamId LocalPlayerTeam => localPlayerTeam;
        public IReadOnlyList<VisionDiagnostic> Diagnostics => diagnostics;

        private void OnEnable()
        {
            TeamMember.MembershipChanged += OnMembershipChanged;
            nextSweepTime = 0f;
            RefreshPresenters();
        }

        private void OnDisable()
        {
            TeamMember.MembershipChanged -= OnMembershipChanged;
            foreach (TeamMember member in TeamMember.ActiveMembers)
            {
                member?.VisibilityPresenter?.SetVisible(true);
            }
        }

        private void Update()
        {
            if (rules == null)
            {
                return;
            }

            if (pendingPairs.Count == 0 && Time.time >= nextSweepTime)
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
            if (distance > observerDefinition.viewRange)
            {
                return DetectionMath.Resolve(CreateInput(observer, target, distance, 0f, false));
            }

            bool hardBlocker = false;
            HashSet<ConcealmentZone> zones = new HashSet<ConcealmentZone>();
            if (distance > 0.001f)
            {
                foreach (RaycastHit2D hit in Physics2D.RaycastAll(origin, delta / distance, distance))
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
                    if (zone != null && zones.Add(zone))
                    {
                        bushBonus += zone.ConcealmentBonus;
                    }
                }
            }

            bushBonus = Mathf.Min(bushBonus, rules.maximumBushBonus);
            return DetectionMath.Resolve(CreateInput(observer, target, distance, bushBonus, hardBlocker));
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
            bool hardBlocker)
        {
            TankDefinition observerDefinition = observer.Definition;
            TankDefinition targetDefinition = target.Definition;
            float movementPenalty = target.IsMoving ? targetDefinition.movementRevealPenalty : 0f;
            float firingPenalty = Time.time - target.LastFiredTime <= targetDefinition.firingRevealDuration
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
                hardBlocker);
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
            TeamMember[] members = TeamMember.ActiveMembers.Where(member => member != null).ToArray();
            foreach (TeamMember target in members)
            {
                if (target.Team == TeamId.Neutral)
                {
                    continue;
                }

                TeamId viewingTeam = target.Team == TeamId.TeamA ? TeamId.TeamB : TeamId.TeamA;
                ContactKey key = new ContactKey(viewingTeam, target);
                bool visible = target.IsAlive && spottedThisSweep.Contains(key);
                if (!contacts.TryGetValue(key, out VisionContact contact))
                {
                    contact = new VisionContact();
                    contacts.Add(key, contact);
                }

                bool changed = contact.IsVisible != visible;
                contact.IsVisible = visible;
                if (visible)
                {
                    contact.LastKnownPosition = target.transform.position;
                    contact.LastSeenTime = Time.time;
                    contact.HasMemory = true;
                }
                else if (contact.HasMemory && rules != null &&
                         Time.time - contact.LastSeenTime > rules.contactMemorySeconds)
                {
                    contact.HasMemory = false;
                }

                if (changed)
                {
                    VisibilityChanged?.Invoke(viewingTeam, target, visible);
                }
            }

            RefreshPresenters();
            nextSweepTime = Time.time + Mathf.Max(0.05f, rules != null ? rules.checkInterval : 0.2f);
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
    }
}
