using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LocalTanks
{
    public sealed class BattleRosterEntry
    {
        internal BattleRosterEntry(TeamMember member, bool ally)
        {
            Member = member;
            IsAlly = ally;
            TankName = member.Definition != null ? member.Definition.displayName : member.name;
            MaximumHitPoints = member.Health != null ? member.Health.MaximumHitPoints : 1;
            LastKnownHitPoints = member.Health != null ? member.Health.CurrentHitPoints : MaximumHitPoints;
            IsAlive = member.IsAlive;
            IsCurrentlyVisible = ally;
            SpriteRenderer renderer = member.GetComponentsInChildren<SpriteRenderer>(true).FirstOrDefault();
            Icon = renderer != null ? renderer.sprite : null;
        }

        public TeamMember Member { get; }
        public bool IsAlly { get; }
        public string TankName { get; }
        public Sprite Icon { get; }
        public int MaximumHitPoints { get; }
        public int LastKnownHitPoints { get; internal set; }
        public bool IsAlive { get; internal set; }
        public bool IsCurrentlyVisible { get; internal set; }
        public float HealthRatio => MaximumHitPoints > 0
            ? Mathf.Clamp01((float)LastKnownHitPoints / MaximumHitPoints)
            : 0f;
    }

    public sealed class BattleRoster : MonoBehaviour
    {
        [SerializeField] private TeamId localPlayerTeam = TeamId.TeamA;
        [SerializeField] private TeamVisionSystem visionSystem;

        private readonly Dictionary<TeamMember, BattleRosterEntry> entries =
            new Dictionary<TeamMember, BattleRosterEntry>();

        public event Action Changed;

        public TeamId LocalPlayerTeam => localPlayerTeam;
        public IReadOnlyList<BattleRosterEntry> Allies => entries.Values
            .Where(entry => entry.IsAlly)
            .OrderBy(entry => entry.TankName, StringComparer.Ordinal)
            .ToArray();
        public IReadOnlyList<BattleRosterEntry> Enemies => entries.Values
            .Where(entry => !entry.IsAlly)
            .OrderBy(entry => entry.TankName, StringComparer.Ordinal)
            .ToArray();
        public int AlliesAlive => entries.Values.Count(entry => entry.IsAlly && entry.IsAlive);
        public int EnemiesAlive => entries.Values.Count(entry => !entry.IsAlly && entry.IsAlive);

        private void OnEnable()
        {
            TeamMember.MembershipChanged += OnMembershipChanged;
            SubscribeVision();
            foreach (TeamMember member in TeamMember.ActiveMembers.ToArray())
            {
                Register(member);
            }
        }

        private void OnDisable()
        {
            TeamMember.MembershipChanged -= OnMembershipChanged;
            UnsubscribeVision();
            foreach (TeamMember member in entries.Keys.ToArray())
            {
                Unsubscribe(member);
            }
        }

        public void Configure(TeamId playerTeam, TeamVisionSystem system)
        {
            UnsubscribeVision();
            localPlayerTeam = playerTeam;
            visionSystem = system;
            SubscribeVision();
            Rebuild();
        }

        public BattleRosterEntry Find(TeamMember member)
        {
            return member != null && entries.TryGetValue(member, out BattleRosterEntry entry) ? entry : null;
        }

        private void Rebuild()
        {
            foreach (TeamMember member in entries.Keys.ToArray())
            {
                Unsubscribe(member);
            }

            entries.Clear();
            foreach (TeamMember member in TeamMember.ActiveMembers.ToArray())
            {
                Register(member);
            }

            Changed?.Invoke();
        }

        private void Register(TeamMember member)
        {
            if (member == null || member.Team == TeamId.Neutral)
            {
                return;
            }

            if (entries.ContainsKey(member))
            {
                Unsubscribe(member);
                entries.Remove(member);
            }

            BattleRosterEntry entry = new BattleRosterEntry(member, member.Team == localPlayerTeam);
            if (!entry.IsAlly && visionSystem != null)
            {
                entry.IsCurrentlyVisible = visionSystem.IsVisibleTo(localPlayerTeam, member);
            }

            entries.Add(member, entry);
            if (member.Health != null)
            {
                member.Health.Changed += OnHealthChanged;
                member.Health.Destroyed += OnDestroyed;
            }

            Changed?.Invoke();
        }

        private void Unsubscribe(TeamMember member)
        {
            if (member != null && member.Health != null)
            {
                member.Health.Changed -= OnHealthChanged;
                member.Health.Destroyed -= OnDestroyed;
            }
        }

        private void OnMembershipChanged(TeamMember member, bool added)
        {
            if (added)
            {
                Register(member);
                return;
            }

            if (member != null && entries.Remove(member))
            {
                Unsubscribe(member);
                Changed?.Invoke();
            }
        }

        private void OnHealthChanged(TankHealth health)
        {
            TeamMember member = health != null ? health.GetComponent<TeamMember>() : null;
            if (member == null || !entries.TryGetValue(member, out BattleRosterEntry entry))
            {
                return;
            }

            entry.IsAlive = !health.IsDestroyed;
            if (entry.IsAlly || entry.IsCurrentlyVisible || health.IsDestroyed)
            {
                entry.LastKnownHitPoints = health.CurrentHitPoints;
            }

            Changed?.Invoke();
        }

        private void OnDestroyed(TankHealth health)
        {
            OnHealthChanged(health);
        }

        private void OnVisibilityChanged(TeamId viewingTeam, TeamMember member, bool visible)
        {
            if (viewingTeam != localPlayerTeam || member == null ||
                !entries.TryGetValue(member, out BattleRosterEntry entry) || entry.IsAlly)
            {
                return;
            }

            entry.IsCurrentlyVisible = visible;
            if (visible && member.Health != null)
            {
                entry.LastKnownHitPoints = member.Health.CurrentHitPoints;
                entry.IsAlive = !member.Health.IsDestroyed;
            }

            Changed?.Invoke();
        }

        private void SubscribeVision()
        {
            if (visionSystem != null)
            {
                visionSystem.VisibilityChanged -= OnVisibilityChanged;
                visionSystem.VisibilityChanged += OnVisibilityChanged;
            }
        }

        private void UnsubscribeVision()
        {
            if (visionSystem != null)
            {
                visionSystem.VisibilityChanged -= OnVisibilityChanged;
            }
        }
    }
}
