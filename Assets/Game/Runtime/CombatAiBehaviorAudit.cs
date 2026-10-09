using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace LocalTanks
{
    public enum CombatAiAuditIssueKind
    {
        StateThrashing,
        TargetThrashing,
        MovementStall,
        Circling,
        NavigationDeadEnd,
        CombatInactivity,
        FiredWithoutVisibleTarget
    }

    [Serializable]
    public sealed class CombatAiAuditRules
    {
        [Min(1f)] public float transitionWindowSeconds = 5f;
        [Min(2)] public int maximumStateTransitions = 8;
        [Min(2)] public int maximumTargetChanges = 6;
        [Min(1f)] public float movementWindowSeconds = 6f;
        [Min(0.01f)] public float minimumMovementDistance = 0.25f;
        [Min(1f)] public float minimumUsefulTurnDegrees = 18f;
        [Min(1f)] public float circlingWindowSeconds = 6f;
        [Min(45f)] public float circlingRotationDegrees = 180f;
        [Min(0.1f)] public float navigationDeadEndSeconds = 1.25f;
        [Min(1f)] public float combatInactivitySeconds = 15f;
        [Min(0.1f)] public float duplicateIssueCooldownSeconds = 6f;
    }

    public readonly struct CombatAiAuditSample
    {
        public CombatAiAuditSample(
            float time,
            string botId,
            string nickname,
            CombatAiState state,
            Vector2 position,
            bool alive,
            bool movementPaused,
            bool hasDestination,
            bool destinationReached,
            bool hasNavigationPath,
            string targetId,
            bool targetVisible,
            FireBlockReason fireBlockReason,
            int shotsFired,
            int shotsWithoutVisibleTarget = 0,
            float headingDegrees = 0f)
        {
            Time = time;
            BotId = botId ?? string.Empty;
            Nickname = nickname ?? string.Empty;
            State = state;
            Position = position;
            Alive = alive;
            MovementPaused = movementPaused;
            HasDestination = hasDestination;
            DestinationReached = destinationReached;
            HasNavigationPath = hasNavigationPath;
            TargetId = targetId ?? string.Empty;
            TargetVisible = targetVisible;
            FireBlockReason = fireBlockReason;
            ShotsFired = Mathf.Max(0, shotsFired);
            ShotsWithoutVisibleTarget = Mathf.Max(0, shotsWithoutVisibleTarget);
            HeadingDegrees = headingDegrees;
        }

        public float Time { get; }
        public string BotId { get; }
        public string Nickname { get; }
        public CombatAiState State { get; }
        public Vector2 Position { get; }
        public bool Alive { get; }
        public bool MovementPaused { get; }
        public bool HasDestination { get; }
        public bool DestinationReached { get; }
        public bool HasNavigationPath { get; }
        public string TargetId { get; }
        public bool TargetVisible { get; }
        public FireBlockReason FireBlockReason { get; }
        public int ShotsFired { get; }
        public int ShotsWithoutVisibleTarget { get; }
        public float HeadingDegrees { get; }
    }

    public sealed class CombatAiAuditIssue
    {
        public CombatAiAuditIssue(CombatAiAuditIssueKind kind, string botId, string nickname, float time, string details)
        {
            Kind = kind;
            BotId = botId;
            Nickname = nickname;
            Time = time;
            Details = details;
        }

        public CombatAiAuditIssueKind Kind { get; }
        public string BotId { get; }
        public string Nickname { get; }
        public float Time { get; }
        public string Details { get; }
    }

    public sealed class CombatAiBehaviorAuditor
    {
        private readonly CombatAiAuditRules rules;
        private readonly Dictionary<string, BotTrack> tracks = new Dictionary<string, BotTrack>(StringComparer.Ordinal);
        private readonly List<CombatAiAuditIssue> issues = new List<CombatAiAuditIssue>();

        public CombatAiBehaviorAuditor(CombatAiAuditRules auditRules = null)
        {
            rules = auditRules ?? new CombatAiAuditRules();
        }

        public IReadOnlyList<CombatAiAuditIssue> Issues => issues;
        public int SampleCount { get; private set; }

        public void Record(CombatAiAuditSample sample)
        {
            if (string.IsNullOrWhiteSpace(sample.BotId))
                throw new ArgumentException("Behavior audit sample requires a bot ID.", nameof(sample));

            SampleCount++;
            if (!tracks.TryGetValue(sample.BotId, out BotTrack track))
            {
                track = new BotTrack(sample);
                tracks.Add(sample.BotId, track);
                return;
            }

            if (!sample.Alive)
            {
                track.ResetTransient(sample);
                return;
            }

            TrackStateChanges(track, sample);
            TrackTargetChanges(track, sample);
            TrackMovement(track, sample);
            TrackCircling(track, sample);
            TrackNavigationDeadEnd(track, sample);
            TrackCombatActivity(track, sample);
            track.TotalDistance += Vector2.Distance(track.LastSample.Position, sample.Position);
            track.LastSample = sample;
        }

        public string BuildReport(float durationSeconds)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("# Combat AI behavior audit");
            report.AppendLine();
            report.AppendLine($"Duration: {durationSeconds:0.0} s; bots: {tracks.Count}; samples: {SampleCount}; issues: {issues.Count}.");
            if (issues.Count == 0)
            {
                report.AppendLine("No rule violations detected.");
            }
            else
            {
                foreach (CombatAiAuditIssue issue in issues.OrderBy(item => item.Time).ThenBy(item => item.BotId, StringComparer.Ordinal))
                {
                    report.AppendLine($"- {issue.Time:0.0}s [{issue.Kind}] {issue.Nickname} ({issue.BotId}): {issue.Details}");
                }
            }
            report.AppendLine();
            report.AppendLine("## Bot summaries");
            foreach (KeyValuePair<string, BotTrack> item in tracks.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                BotTrack track = item.Value;
                report.AppendLine($"- {track.LastSample.Nickname} ({item.Key}): state={track.LastSample.State}, " +
                                  $"distance={track.TotalDistance:0.0} m, shots={track.LastSample.ShotsFired}, " +
                                  $"state changes={track.TotalStateChanges}, target changes={track.TotalTargetChanges}.");
            }
            return report.ToString();
        }

        private void TrackStateChanges(BotTrack track, CombatAiAuditSample sample)
        {
            if (sample.State != track.LastSample.State)
            {
                track.StateChanges.Enqueue(new TimedValue<CombatAiState>(sample.Time, sample.State));
                track.TotalStateChanges++;
            }
            Trim(track.StateChanges, sample.Time - rules.transitionWindowSeconds);
            if (track.StateChanges.Count >= rules.maximumStateTransitions)
            {
                string sequence = string.Join(" → ", track.StateChanges.Select(item => item.Value.ToString()));
                Emit(track, sample, CombatAiAuditIssueKind.StateThrashing,
                    $"{track.StateChanges.Count} transitions in {rules.transitionWindowSeconds:0.#} s: {sequence}.");
                track.StateChanges.Clear();
            }
        }

        private void TrackTargetChanges(BotTrack track, CombatAiAuditSample sample)
        {
            if (!string.Equals(sample.TargetId, track.LastSample.TargetId, StringComparison.Ordinal))
            {
                track.TargetChanges.Enqueue(new TimedValue<string>(sample.Time, sample.TargetId));
                track.TotalTargetChanges++;
            }
            Trim(track.TargetChanges, sample.Time - rules.transitionWindowSeconds);
            if (track.TargetChanges.Count >= rules.maximumTargetChanges)
            {
                string sequence = string.Join(" → ", track.TargetChanges.Select(item =>
                    string.IsNullOrWhiteSpace(item.Value) ? "none" : item.Value));
                Emit(track, sample, CombatAiAuditIssueKind.TargetThrashing,
                    $"{track.TargetChanges.Count} target changes in {rules.transitionWindowSeconds:0.#} s: {sequence}.");
                track.TargetChanges.Clear();
            }
        }

        private void TrackMovement(BotTrack track, CombatAiAuditSample sample)
        {
            bool expected = MovementExpected(sample);
            if (!expected)
            {
                track.MovementExpectedSince = -1f;
                track.MovementAnchorPosition = sample.Position;
                track.MovementAnchorHeading = sample.HeadingDegrees;
                return;
            }

            if (track.MovementExpectedSince < 0f)
            {
                track.MovementExpectedSince = sample.Time;
                track.MovementAnchorPosition = sample.Position;
                return;
            }

            float distance = Vector2.Distance(track.MovementAnchorPosition, sample.Position);
            float headingChange = Mathf.Abs(Mathf.DeltaAngle(track.MovementAnchorHeading, sample.HeadingDegrees));
            if (distance >= rules.minimumMovementDistance || headingChange >= rules.minimumUsefulTurnDegrees)
            {
                track.MovementExpectedSince = sample.Time;
                track.MovementAnchorPosition = sample.Position;
                track.MovementAnchorHeading = sample.HeadingDegrees;
            }
            else if (sample.Time - track.MovementExpectedSince >= rules.movementWindowSeconds)
            {
                Emit(track, sample, CombatAiAuditIssueKind.MovementStall,
                    $"Expected movement in {sample.State}, but moved only {distance:0.00} m in " +
                    $"{sample.Time - track.MovementExpectedSince:0.0} s and turned {headingChange:0}°.");
                track.MovementExpectedSince = sample.Time;
                track.MovementAnchorPosition = sample.Position;
                track.MovementAnchorHeading = sample.HeadingDegrees;
            }
        }

        private void TrackCircling(BotTrack track, CombatAiAuditSample sample)
        {
            if (!MovementExpected(sample))
            {
                track.ResetRotationWindow(sample);
                return;
            }

            if (track.RotationWindowSince < 0f)
            {
                track.RotationWindowSince = sample.Time;
                track.RotationAnchorPosition = sample.Position;
                track.LastHeading = sample.HeadingDegrees;
                track.AccumulatedRotation = 0f;
                return;
            }

            track.AccumulatedRotation += Mathf.Abs(Mathf.DeltaAngle(track.LastHeading, sample.HeadingDegrees));
            track.LastHeading = sample.HeadingDegrees;
            float displacement = Vector2.Distance(track.RotationAnchorPosition, sample.Position);
            if (displacement >= rules.minimumMovementDistance * 2f)
            {
                track.ResetRotationWindow(sample);
            }
            else if (sample.Time - track.RotationWindowSince >= rules.circlingWindowSeconds)
            {
                if (track.AccumulatedRotation >= rules.circlingRotationDegrees)
                {
                    Emit(track, sample, CombatAiAuditIssueKind.Circling,
                        $"Rotated {track.AccumulatedRotation:0}° while moving only {displacement:0.00} m in " +
                        $"{sample.Time - track.RotationWindowSince:0.0} s.");
                }
                track.ResetRotationWindow(sample);
            }
        }

        private void TrackNavigationDeadEnd(BotTrack track, CombatAiAuditSample sample)
        {
            bool deadEnd = MovementState(sample.State) && !sample.MovementPaused && sample.HasDestination &&
                           !sample.DestinationReached && !sample.HasNavigationPath;
            if (!deadEnd)
            {
                track.NavigationDeadEndSince = -1f;
                return;
            }

            if (track.NavigationDeadEndSince < 0f) track.NavigationDeadEndSince = sample.Time;
            else if (sample.Time - track.NavigationDeadEndSince >= rules.navigationDeadEndSeconds)
            {
                Emit(track, sample, CombatAiAuditIssueKind.NavigationDeadEnd,
                    $"State {sample.State} has a destination but no path for " +
                    $"{sample.Time - track.NavigationDeadEndSince:0.0} s.");
                track.NavigationDeadEndSince = sample.Time;
            }
        }

        private void TrackCombatActivity(BotTrack track, CombatAiAuditSample sample)
        {
            bool fired = sample.ShotsFired > track.LastSample.ShotsFired;
            bool targetChanged = !string.Equals(
                sample.TargetId, track.LastSample.TargetId, StringComparison.Ordinal);
            if (sample.ShotsWithoutVisibleTarget > track.LastSample.ShotsWithoutVisibleTarget)
            {
                Emit(track, sample, CombatAiAuditIssueKind.FiredWithoutVisibleTarget,
                    "Weapon fired without a visible current target at the moment of the shot.");
            }

            bool combatExpected = sample.TargetVisible &&
                                  sample.FireBlockReason != FireBlockReason.OutOfRange &&
                                  (sample.State == CombatAiState.Engage || sample.State == CombatAiState.Reposition);
            if (!combatExpected || fired || targetChanged)
            {
                track.CombatInactiveSince = -1f;
                if (combatExpected && !fired) track.CombatInactiveSince = sample.Time;
                return;
            }

            if (track.CombatInactiveSince < 0f) track.CombatInactiveSince = sample.Time;
            else if (sample.Time - track.CombatInactiveSince >= rules.combatInactivitySeconds)
            {
                Emit(track, sample, CombatAiAuditIssueKind.CombatInactivity,
                    $"Visible target '{sample.TargetId}' was not fired at for " +
                    $"{sample.Time - track.CombatInactiveSince:0.0} s; block={sample.FireBlockReason}.");
                track.CombatInactiveSince = sample.Time;
            }
        }

        private void Emit(BotTrack track, CombatAiAuditSample sample, CombatAiAuditIssueKind kind, string details)
        {
            if (track.LastIssueTimes.TryGetValue(kind, out float previous) &&
                sample.Time - previous < rules.duplicateIssueCooldownSeconds) return;
            track.LastIssueTimes[kind] = sample.Time;
            issues.Add(new CombatAiAuditIssue(kind, sample.BotId, sample.Nickname, sample.Time, details));
        }

        private static bool MovementExpected(CombatAiAuditSample sample)
        {
            return MovementState(sample.State) && !sample.MovementPaused && sample.HasDestination &&
                   !sample.DestinationReached && sample.HasNavigationPath;
        }

        private static bool MovementState(CombatAiState state)
        {
            return state == CombatAiState.Deploy || state == CombatAiState.Advance ||
                   state == CombatAiState.Engage || state == CombatAiState.Investigate ||
                   state == CombatAiState.Reposition;
        }

        private static void Trim<T>(Queue<TimedValue<T>> values, float minimumTime)
        {
            while (values.Count > 0 && values.Peek().Time < minimumTime) values.Dequeue();
        }

        private readonly struct TimedValue<T>
        {
            public TimedValue(float time, T value)
            {
                Time = time;
                Value = value;
            }

            public float Time { get; }
            public T Value { get; }
        }

        private sealed class BotTrack
        {
            public BotTrack(CombatAiAuditSample sample)
            {
                LastSample = sample;
                MovementAnchorPosition = sample.Position;
                MovementAnchorHeading = sample.HeadingDegrees;
                ResetRotationWindow(sample);
            }

            public CombatAiAuditSample LastSample;
            public readonly Queue<TimedValue<CombatAiState>> StateChanges = new Queue<TimedValue<CombatAiState>>();
            public readonly Queue<TimedValue<string>> TargetChanges = new Queue<TimedValue<string>>();
            public readonly Dictionary<CombatAiAuditIssueKind, float> LastIssueTimes =
                new Dictionary<CombatAiAuditIssueKind, float>();
            public float MovementExpectedSince = -1f;
            public Vector2 MovementAnchorPosition;
            public float MovementAnchorHeading;
            public float NavigationDeadEndSince = -1f;
            public float CombatInactiveSince = -1f;
            public float RotationWindowSince = -1f;
            public Vector2 RotationAnchorPosition;
            public float LastHeading;
            public float AccumulatedRotation;
            public float TotalDistance;
            public int TotalStateChanges;
            public int TotalTargetChanges;

            public void ResetTransient(CombatAiAuditSample sample)
            {
                TotalDistance += Vector2.Distance(LastSample.Position, sample.Position);
                LastSample = sample;
                StateChanges.Clear();
                TargetChanges.Clear();
                MovementExpectedSince = -1f;
                NavigationDeadEndSince = -1f;
                CombatInactiveSince = -1f;
                ResetRotationWindow(sample);
            }

            public void ResetRotationWindow(CombatAiAuditSample sample)
            {
                RotationWindowSince = -1f;
                RotationAnchorPosition = sample.Position;
                LastHeading = sample.HeadingDegrees;
                AccumulatedRotation = 0f;
            }
        }
    }

    public sealed class CombatAiAuditSession : IDisposable
    {
        private readonly BattleDirector director;
        private readonly CombatTankAI[] bots;
        private readonly Dictionary<CombatTankAI, int> shotCounts = new Dictionary<CombatTankAI, int>();
        private readonly Dictionary<CombatTankAI, int> invalidShotCounts = new Dictionary<CombatTankAI, int>();
        private readonly Dictionary<CombatTankAI, WeaponController> weapons = new Dictionary<CombatTankAI, WeaponController>();

        public CombatAiAuditSession(BattleDirector battleDirector, CombatAiAuditRules rules = null)
        {
            director = battleDirector ?? throw new ArgumentNullException(nameof(battleDirector));
            Auditor = new CombatAiBehaviorAuditor(rules);
            bots = UnityEngine.Object.FindObjectsByType<CombatTankAI>(FindObjectsSortMode.None)
                .Where(item => item != null)
                .OrderBy(item => director.GetEntityId(item.GetComponent<TeamMember>()), StringComparer.Ordinal)
                .ToArray();
            foreach (CombatTankAI bot in bots)
            {
                shotCounts[bot] = 0;
                invalidShotCounts[bot] = 0;
                WeaponController weapon = bot.GetComponent<WeaponController>();
                if (weapon == null) continue;
                weapons[bot] = weapon;
                weapon.Fired += OnWeaponFired;
            }
        }

        public CombatAiBehaviorAuditor Auditor { get; }

        public void Sample(float time)
        {
            foreach (CombatTankAI bot in bots)
            {
                if (bot == null) continue;
                TeamMember member = bot.GetComponent<TeamMember>();
                NavigationAgent navigation = bot.GetComponent<NavigationAgent>();
                TeamMember target = bot.CurrentTarget;
                string botId = director.GetEntityId(member);
                string targetId = target != null ? director.GetEntityId(target) : string.Empty;
                bool targetVisible = target != null && member != null &&
                                     director.VisionSystem.IsVisibleTo(member.Team, target);
                Auditor.Record(new CombatAiAuditSample(
                    time,
                    botId,
                    bot.Identity?.nickname ?? bot.name,
                    bot.State,
                    bot.transform.position,
                    member != null && member.IsAlive,
                    navigation == null || navigation.MovementPaused,
                    navigation != null && navigation.HasDestination,
                    navigation != null && navigation.DestinationReached,
                    navigation != null && navigation.HasPath,
                    targetId,
                    targetVisible,
                    bot.CurrentFireBlockReason,
                    shotCounts.TryGetValue(bot, out int shots) ? shots : 0,
                    invalidShotCounts.TryGetValue(bot, out int invalidShots) ? invalidShots : 0,
                    bot.transform.eulerAngles.z));
            }
        }

        public void Dispose()
        {
            foreach (KeyValuePair<CombatTankAI, WeaponController> pair in weapons)
            {
                if (pair.Value != null) pair.Value.Fired -= OnWeaponFired;
            }
            weapons.Clear();
        }

        private void OnWeaponFired(WeaponController weapon)
        {
            CombatTankAI bot = weapon != null ? weapon.GetComponent<CombatTankAI>() : null;
            if (bot == null || !shotCounts.ContainsKey(bot)) return;
            shotCounts[bot]++;
            TeamMember member = bot.GetComponent<TeamMember>();
            TeamMember target = bot.CurrentTarget;
            bool visible = member != null && target != null &&
                           director.VisionSystem.IsVisibleTo(member.Team, target);
            if (!visible) invalidShotCounts[bot]++;
        }
    }
}
