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
        FiredWithoutVisibleTarget,
        TacticalTaskThrashing,
        FiringLineThrashing,
        RearExposedToVisibleTarget,
        ReverseWithoutRetreatTask
    }

    [Serializable]
    public sealed class CombatAiAuditRules
    {
        [Min(1f)] public float transitionWindowSeconds = 5f;
        [Min(2)] public int maximumStateTransitions = 8;
        [Min(2)] public int maximumTargetChanges = 6;
        [Min(2)] public int maximumTacticalTaskTransitions = 4;
        [Min(1f)] public float tacticalTransitionWindowSeconds = 6f;
        [Min(2)] public int maximumFiringLineTransitions = 3;
        [Min(1f)] public float firingLineTransitionWindowSeconds = 4f;
        [Min(1f)] public float movementWindowSeconds = 6f;
        [Min(0.01f)] public float minimumMovementDistance = 0.25f;
        [Min(1f)] public float minimumUsefulTurnDegrees = 18f;
        [Min(1f)] public float circlingWindowSeconds = 6f;
        [Min(45f)] public float circlingRotationDegrees = 180f;
        [Min(0.1f)] public float navigationDeadEndSeconds = 1.25f;
        [Min(1f)] public float combatInactivitySeconds = 15f;
        [Range(90f, 180f)] public float rearExposureAngleDegrees = 115f;
        [Min(0.5f)] public float rearExposureSeconds = 2.5f;
        [Min(0.25f)] public float reverseWithoutRetreatSeconds = 1f;
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
            float headingDegrees = 0f,
            bool defensiveMovement = false,
            bool recoveringFromStall = false,
            TacticalMovementPurpose tacticalMovementPurpose = TacticalMovementPurpose.None,
            float targetRelativeAngleDegrees = 0f,
            bool movingInReverse = false,
            float currentSpeed = 0f)
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
            DefensiveMovement = defensiveMovement;
            RecoveringFromStall = recoveringFromStall;
            TacticalMovementPurpose = tacticalMovementPurpose;
            TargetRelativeAngleDegrees = Mathf.Abs(targetRelativeAngleDegrees);
            MovingInReverse = movingInReverse;
            CurrentSpeed = currentSpeed;
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
        public bool DefensiveMovement { get; }
        public bool RecoveringFromStall { get; }
        public TacticalMovementPurpose TacticalMovementPurpose { get; }
        public float TargetRelativeAngleDegrees { get; }
        public bool MovingInReverse { get; }
        public float CurrentSpeed { get; }
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

            AccumulateMetrics(track, sample);
            TrackTransition(track, sample);
            TrackStateChanges(track, sample);
            TrackTargetChanges(track, sample);
            TrackTacticalTaskChanges(track, sample);
            TrackFiringLineChanges(track, sample);
            TrackMovement(track, sample);
            TrackCircling(track, sample);
            TrackNavigationDeadEnd(track, sample);
            TrackCombatActivity(track, sample);
            TrackRearExposure(track, sample);
            TrackReverseWithoutRetreatTask(track, sample);
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
                                  $"state changes={track.TotalStateChanges}, target changes={track.TotalTargetChanges}, " +
                                  $"cover/combat changes={track.TotalTacticalTaskChanges}, " +
                                  $"firing-line changes={track.TotalFiringLineChanges}.");
                report.AppendLine($"  exposure: visible={track.VisibleTargetSeconds:0.0}s, " +
                                  $"rear-facing={track.RearFacingVisibleSeconds:0.0}s, " +
                                  $"reverse={track.ReverseSeconds:0.0}s.");
                report.AppendLine($"  states: {FormatDurations(track.StateDurations)}");
                report.AppendLine($"  tasks: {FormatDurations(track.TacticalTaskDurations)}");
                report.AppendLine($"  fire blocks: {FormatDurations(track.FireBlockDurations)}");
            }
            return report.ToString();
        }

        public string BuildTransitionReport()
        {
            StringBuilder report = new StringBuilder("# Combat AI transition trace\n\n");
            TransitionRecord[] transitions = tracks.Values
                .SelectMany(track => track.Transitions)
                .OrderBy(item => item.Time)
                .ThenBy(item => item.BotId, StringComparer.Ordinal)
                .ToArray();
            if (transitions.Length == 0)
            {
                report.AppendLine("No state, task, target, visibility or reverse-mode transitions recorded.");
                return report.ToString();
            }

            foreach (TransitionRecord item in transitions)
                report.AppendLine($"- {item.Time:0.0}s {item.Nickname} ({item.BotId}): {item.Details}");
            return report.ToString();
        }

        private void AccumulateMetrics(BotTrack track, CombatAiAuditSample sample)
        {
            float delta = Mathf.Clamp(sample.Time - track.LastSample.Time, 0f, 1f);
            AddDuration(track.StateDurations, track.LastSample.State, delta);
            AddDuration(track.TacticalTaskDurations, track.LastSample.TacticalMovementPurpose, delta);
            AddDuration(track.FireBlockDurations, track.LastSample.FireBlockReason, delta);
            if (track.LastSample.TargetVisible) track.VisibleTargetSeconds += delta;
            if (track.LastSample.TargetVisible &&
                track.LastSample.TargetRelativeAngleDegrees >= rules.rearExposureAngleDegrees)
            {
                track.RearFacingVisibleSeconds += delta;
            }
            if (track.LastSample.MovingInReverse) track.ReverseSeconds += delta;
        }

        private static void TrackTransition(BotTrack track, CombatAiAuditSample sample)
        {
            List<string> changes = new List<string>();
            CombatAiAuditSample previous = track.LastSample;
            if (sample.State != previous.State) changes.Add($"state {previous.State}→{sample.State}");
            if (sample.TacticalMovementPurpose != previous.TacticalMovementPurpose)
                changes.Add($"task {previous.TacticalMovementPurpose}→{sample.TacticalMovementPurpose}");
            if (!string.Equals(sample.TargetId, previous.TargetId, StringComparison.Ordinal))
                changes.Add($"target {DisplayTarget(previous.TargetId)}→{DisplayTarget(sample.TargetId)}");
            if (sample.TargetVisible != previous.TargetVisible)
                changes.Add($"visible {previous.TargetVisible}→{sample.TargetVisible}");
            if (sample.MovingInReverse != previous.MovingInReverse)
                changes.Add($"reverse {previous.MovingInReverse}→{sample.MovingInReverse}");
            if (changes.Count == 0) return;
            changes.Add($"fire={sample.FireBlockReason}");
            changes.Add($"angle={sample.TargetRelativeAngleDegrees:0}°");
            changes.Add($"pos=({sample.Position.x:0.0},{sample.Position.y:0.0})");
            track.Transitions.Add(new TransitionRecord(
                sample.Time,
                sample.BotId,
                sample.Nickname,
                string.Join("; ", changes)));
        }

        private static string DisplayTarget(string targetId)
        {
            return string.IsNullOrWhiteSpace(targetId) ? "none" : targetId;
        }

        private static void AddDuration<T>(Dictionary<T, float> durations, T key, float delta)
        {
            durations.TryGetValue(key, out float current);
            durations[key] = current + delta;
        }

        private static string FormatDurations<T>(IReadOnlyDictionary<T, float> durations)
        {
            string[] values = durations
                .Where(item => item.Value >= 0.05f)
                .OrderByDescending(item => item.Value)
                .ThenBy(item => item.Key.ToString(), StringComparer.Ordinal)
                .Select(item => $"{item.Key}={item.Value:0.0}s")
                .ToArray();
            return values.Length > 0 ? string.Join(", ", values) : "none";
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
                // A maneuver cancelled because the selected enemy changed is not oscillation
                // around one blocked line of fire. Start a fresh diagnostic window for the new target.
                track.FiringLineChanges.Clear();
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

        private void TrackTacticalTaskChanges(BotTrack track, CombatAiAuditSample sample)
        {
            TacticalMovementPurpose previous = track.LastSample.TacticalMovementPurpose;
            TacticalMovementPurpose current = sample.TacticalMovementPurpose;
            if (current != previous && IsCoverTask(current) != IsCoverTask(previous))
            {
                track.TacticalTaskChanges.Enqueue(new TimedValue<TacticalMovementPurpose>(sample.Time, current));
                track.TotalTacticalTaskChanges++;
            }
            Trim(track.TacticalTaskChanges, sample.Time - rules.tacticalTransitionWindowSeconds);
            if (track.TacticalTaskChanges.Count >= rules.maximumTacticalTaskTransitions)
            {
                string sequence = string.Join(" → ", track.TacticalTaskChanges.Select(item =>
                    item.Value == TacticalMovementPurpose.None ? "Combat" : item.Value.ToString()));
                Emit(track, sample, CombatAiAuditIssueKind.TacticalTaskThrashing,
                    $"{track.TacticalTaskChanges.Count} cover/combat task transitions in " +
                    $"{rules.tacticalTransitionWindowSeconds:0.#} s: {sequence}.");
                track.TacticalTaskChanges.Clear();
            }
        }

        private void TrackFiringLineChanges(BotTrack track, CombatAiAuditSample sample)
        {
            TacticalMovementPurpose previous = track.LastSample.TacticalMovementPurpose;
            TacticalMovementPurpose current = sample.TacticalMovementPurpose;
            if ((IsFiringLineTask(previous) || IsFiringLineTask(current)) &&
                previous != TacticalMovementPurpose.None &&
                current != TacticalMovementPurpose.None)
            {
                // A higher-priority cover task replacing GainFiringLine is arbitration,
                // not a cancel/retry loop around the same firing obstruction.
                track.FiringLineChanges.Clear();
                return;
            }
            if (IsFiringLineTask(current) != IsFiringLineTask(previous))
            {
                track.FiringLineChanges.Enqueue(new TimedValue<TacticalMovementPurpose>(sample.Time, current));
                track.TotalFiringLineChanges++;
            }
            Trim(track.FiringLineChanges, sample.Time - rules.firingLineTransitionWindowSeconds);
            if (track.FiringLineChanges.Count >= rules.maximumFiringLineTransitions)
            {
                string sequence = string.Join(" → ", track.FiringLineChanges.Select(item =>
                    item.Value == TacticalMovementPurpose.GainFiringLine ? "GainFiringLine" : "Combat"));
                Emit(track, sample, CombatAiAuditIssueKind.FiringLineThrashing,
                    $"{track.FiringLineChanges.Count} firing-line/combat transitions in " +
                    $"{rules.firingLineTransitionWindowSeconds:0.#} s: {sequence}.");
                track.FiringLineChanges.Clear();
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

            bool progressingReposition = sample.State == CombatAiState.Reposition &&
                                         sample.HasDestination && !sample.DestinationReached &&
                                         sample.HasNavigationPath;
            bool combatExpected = sample.TargetVisible &&
                                  sample.FireBlockReason != FireBlockReason.OutOfRange &&
                                  !sample.DefensiveMovement &&
                                  !progressingReposition &&
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

        private void TrackRearExposure(BotTrack track, CombatAiAuditSample sample)
        {
            bool exposed = sample.TargetVisible && !string.IsNullOrWhiteSpace(sample.TargetId) &&
                           !sample.MovementPaused &&
                           Mathf.Abs(sample.CurrentSpeed) > 0.35f &&
                           sample.TargetRelativeAngleDegrees >= rules.rearExposureAngleDegrees;
            if (!exposed)
            {
                track.RearExposureSince = -1f;
                return;
            }

            if (track.RearExposureSince < 0f)
            {
                track.RearExposureSince = sample.Time;
                return;
            }
            if (sample.Time - track.RearExposureSince < rules.rearExposureSeconds) return;
            Emit(track, sample, CombatAiAuditIssueKind.RearExposedToVisibleTarget,
                $"Hull rear remained toward visible target '{sample.TargetId}' for " +
                $"{sample.Time - track.RearExposureSince:0.0} s; angle=" +
                $"{sample.TargetRelativeAngleDegrees:0}°, state={sample.State}, " +
                $"task={sample.TacticalMovementPurpose}, reverse={sample.MovingInReverse}, " +
                $"speed={sample.CurrentSpeed:0.00} m/s.");
            track.RearExposureSince = sample.Time;
        }

        private void TrackReverseWithoutRetreatTask(BotTrack track, CombatAiAuditSample sample)
        {
            bool reverseTask = sample.TacticalMovementPurpose == TacticalMovementPurpose.MoveToCover ||
                               sample.TacticalMovementPurpose == TacticalMovementPurpose.ReturnToCover ||
                               sample.TacticalMovementPurpose == TacticalMovementPurpose.DefensiveRetreat ||
                               sample.TacticalMovementPurpose == TacticalMovementPurpose.GainFiringLine;
            bool staleReverse = sample.MovingInReverse && !reverseTask;
            if (!staleReverse)
            {
                track.ReverseWithoutRetreatSince = -1f;
                track.ReverseWithoutRetreatReported = false;
                return;
            }

            if (track.ReverseWithoutRetreatReported) return;
            if (track.ReverseWithoutRetreatSince < 0f)
            {
                track.ReverseWithoutRetreatSince = sample.Time;
                return;
            }
            if (sample.Time - track.ReverseWithoutRetreatSince < rules.reverseWithoutRetreatSeconds) return;
            Emit(track, sample, CombatAiAuditIssueKind.ReverseWithoutRetreatTask,
                $"Navigation remained in reverse for {sample.Time - track.ReverseWithoutRetreatSince:0.0} s " +
                $"without a reverse-capable task; state={sample.State}, task={sample.TacticalMovementPurpose}.");
            track.ReverseWithoutRetreatReported = true;
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
            return MovementState(sample.State) && !sample.MovementPaused && !sample.RecoveringFromStall &&
                   sample.HasDestination &&
                   !sample.DestinationReached && sample.HasNavigationPath;
        }

        private static bool MovementState(CombatAiState state)
        {
            return state == CombatAiState.Deploy || state == CombatAiState.Advance ||
                   state == CombatAiState.Engage || state == CombatAiState.Investigate ||
                   state == CombatAiState.Reposition;
        }

        private static bool IsCoverTask(TacticalMovementPurpose purpose)
        {
            return purpose == TacticalMovementPurpose.MoveToCover ||
                   purpose == TacticalMovementPurpose.HoldCover ||
                   purpose == TacticalMovementPurpose.PeekToFire ||
                   purpose == TacticalMovementPurpose.ReturnToCover ||
                   purpose == TacticalMovementPurpose.DefensiveRetreat;
        }

        private static bool IsFiringLineTask(TacticalMovementPurpose purpose)
        {
            return purpose == TacticalMovementPurpose.GainFiringLine;
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

        private readonly struct TransitionRecord
        {
            public TransitionRecord(float time, string botId, string nickname, string details)
            {
                Time = time;
                BotId = botId;
                Nickname = nickname;
                Details = details;
            }

            public float Time { get; }
            public string BotId { get; }
            public string Nickname { get; }
            public string Details { get; }
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
            public readonly Queue<TimedValue<TacticalMovementPurpose>> TacticalTaskChanges =
                new Queue<TimedValue<TacticalMovementPurpose>>();
            public readonly Queue<TimedValue<TacticalMovementPurpose>> FiringLineChanges =
                new Queue<TimedValue<TacticalMovementPurpose>>();
            public readonly Dictionary<CombatAiAuditIssueKind, float> LastIssueTimes =
                new Dictionary<CombatAiAuditIssueKind, float>();
            public readonly Dictionary<CombatAiState, float> StateDurations =
                new Dictionary<CombatAiState, float>();
            public readonly Dictionary<TacticalMovementPurpose, float> TacticalTaskDurations =
                new Dictionary<TacticalMovementPurpose, float>();
            public readonly Dictionary<FireBlockReason, float> FireBlockDurations =
                new Dictionary<FireBlockReason, float>();
            public readonly List<TransitionRecord> Transitions = new List<TransitionRecord>();
            public float MovementExpectedSince = -1f;
            public Vector2 MovementAnchorPosition;
            public float MovementAnchorHeading;
            public float NavigationDeadEndSince = -1f;
            public float CombatInactiveSince = -1f;
            public float RearExposureSince = -1f;
            public float ReverseWithoutRetreatSince = -1f;
            public bool ReverseWithoutRetreatReported;
            public float RotationWindowSince = -1f;
            public Vector2 RotationAnchorPosition;
            public float LastHeading;
            public float AccumulatedRotation;
            public float TotalDistance;
            public float VisibleTargetSeconds;
            public float RearFacingVisibleSeconds;
            public float ReverseSeconds;
            public int TotalStateChanges;
            public int TotalTargetChanges;
            public int TotalTacticalTaskChanges;
            public int TotalFiringLineChanges;

            public void ResetTransient(CombatAiAuditSample sample)
            {
                TotalDistance += Vector2.Distance(LastSample.Position, sample.Position);
                LastSample = sample;
                StateChanges.Clear();
                TargetChanges.Clear();
                TacticalTaskChanges.Clear();
                FiringLineChanges.Clear();
                MovementExpectedSince = -1f;
                NavigationDeadEndSince = -1f;
                CombatInactiveSince = -1f;
                RearExposureSince = -1f;
                ReverseWithoutRetreatSince = -1f;
                ReverseWithoutRetreatReported = false;
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
            bots = UnityEngine.Object.FindObjectsByType<CombatTankAI>()
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
                TankMotor motor = bot.GetComponent<TankMotor>();
                TeamMember target = bot.CurrentTarget;
                string botId = director.GetEntityId(member);
                string targetId = target != null ? director.GetEntityId(target) : string.Empty;
                bool targetVisible = target != null && member != null &&
                                     director.VisionSystem.IsVisibleTo(member.Team, target);
                Vector2 targetDirection = target != null
                    ? (Vector2)target.transform.position - (Vector2)bot.transform.position
                    : Vector2.zero;
                float targetRelativeAngle = targetDirection.sqrMagnitude > 0.0001f
                    ? Mathf.Abs(Vector2.SignedAngle(bot.transform.up, targetDirection))
                    : 0f;
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
                    bot.transform.eulerAngles.z,
                    bot.TacticalMovementPurpose == TacticalMovementPurpose.MoveToCover ||
                    bot.TacticalMovementPurpose == TacticalMovementPurpose.PeekToFire ||
                    bot.TacticalMovementPurpose == TacticalMovementPurpose.ReturnToCover ||
                    bot.TacticalMovementPurpose == TacticalMovementPurpose.DefensiveRetreat,
                    navigation != null && navigation.IsRecoveringFromStall,
                    bot.TacticalMovementPurpose,
                    targetRelativeAngle,
                    navigation != null && navigation.MovingInReverse,
                    motor != null ? motor.CurrentSpeed : 0f));
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
