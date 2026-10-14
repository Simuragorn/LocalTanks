using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    /// <summary>Why a match ended. There is one result and one reason for it, not three separate checks.</summary>
    public enum MatchOutcome
    {
        None,
        TeamWipedOut,
        BaseCaptured,
        TimeLimit
    }

    /// <summary>
    /// The rules of a match, authored with the battle scenario rather than coded (ADR-066).
    ///
    /// The time limit belongs here and not in a component: a test laboratory keeps its own window for a
    /// different purpose, and the battle must not end because a laboratory timer expired.
    /// </summary>
    [Serializable]
    public sealed class MatchRules
    {
        /// <summary>Battle length in seconds. The match ends in a draw when it expires.</summary>
        [Min(1f)] public float timeLimitSeconds = 900f;

        /// <summary>True when the time limit produces a draw; false hands the win to the side with more tanks.</summary>
        public bool timeLimitIsDraw = true;

        /// <summary>
        /// False makes a shot pass through a living machine of the shooter's own team. The default allows it:
        /// the player may shoot a teammate and is not punished for it, while the AI still refuses to fire
        /// through an ally because its line-of-fire check rejects one.
        /// </summary>
        public bool friendlyFire = true;

        public string[] Validate(string owner)
        {
            List<string> errors = new List<string>();
            if (timeLimitSeconds < 1f)
            {
                errors.Add($"{owner} time limit must be at least one second, found {timeLimitSeconds}.");
            }

            return errors.ToArray();
        }

        /// <summary>True when a shell from <paramref name="shooter"/> may damage <paramref name="victim"/>.</summary>
        public bool AllowsDamageTo(TeamId shooter, TeamId victim)
        {
            if (friendlyFire) return true;
            if (shooter == TeamId.Neutral || victim == TeamId.Neutral) return true;
            return shooter != victim;
        }
    }

    /// <summary>The result of a match: one outcome, one winner and the moment it was decided.</summary>
    public readonly struct MatchResult
    {
        public static readonly MatchResult None = default;

        public MatchResult(MatchOutcome outcome, TeamId winner, float elapsedSeconds)
        {
            Outcome = outcome;
            Winner = winner;
            ElapsedSeconds = elapsedSeconds;
        }

        public MatchOutcome Outcome { get; }

        /// <summary>The winning team, or <see cref="TeamId.Neutral"/> for a draw.</summary>
        public TeamId Winner { get; }

        public float ElapsedSeconds { get; }

        public bool HasEnded => Outcome != MatchOutcome.None;

        public bool IsDraw => HasEnded && Winner == TeamId.Neutral;

        /// <summary>A sentence for the interface and the log, so the reason is never guessed from the winner.</summary>
        public string Describe(TeamId playerTeam)
        {
            switch (Outcome)
            {
                case MatchOutcome.TeamWipedOut:
                    return IsDraw
                        ? "ничья: обе команды уничтожены"
                        : Winner == playerTeam
                            ? "победа: команда противника уничтожена"
                            : "поражение: наша команда уничтожена";
                case MatchOutcome.BaseCaptured:
                    return Winner == playerTeam
                        ? "победа: база противника захвачена"
                        : "поражение: наша база захвачена";
                case MatchOutcome.TimeLimit:
                    return IsDraw
                        ? "ничья: время боя истекло"
                        : Winner == playerTeam
                            ? "победа: время боя истекло"
                            : "поражение: время боя истекло";
                default:
                    return "бой продолжается";
            }
        }
    }

    /// <summary>
    /// The single decision that ends a battle, kept out of the director so it can be tested as a table.
    ///
    /// Priority: a wiped-out team ends the battle at once, because it cannot be undone; a captured base
    /// comes next; the time limit is last, because it is the fallback when neither team finished anything.
    /// Two teams destroyed in the same instant are a draw rather than a win for an arbitrary side.
    /// </summary>
    public static class MatchOutcomeRules
    {
        public static MatchResult Resolve(
            int teamAAlive,
            int teamBAlive,
            TeamId capturedBaseOwner,
            float elapsedSeconds,
            MatchRules rules)
        {
            if (teamAAlive <= 0 && teamBAlive <= 0)
            {
                return new MatchResult(MatchOutcome.TeamWipedOut, TeamId.Neutral, elapsedSeconds);
            }

            if (teamBAlive <= 0)
            {
                return new MatchResult(MatchOutcome.TeamWipedOut, TeamId.TeamA, elapsedSeconds);
            }

            if (teamAAlive <= 0)
            {
                return new MatchResult(MatchOutcome.TeamWipedOut, TeamId.TeamB, elapsedSeconds);
            }

            if (capturedBaseOwner == TeamId.TeamA || capturedBaseOwner == TeamId.TeamB)
            {
                return new MatchResult(MatchOutcome.BaseCaptured, capturedBaseOwner, elapsedSeconds);
            }

            if (rules != null && elapsedSeconds >= rules.timeLimitSeconds)
            {
                TeamId winner = TeamId.Neutral;
                if (!rules.timeLimitIsDraw)
                {
                    winner = teamAAlive == teamBAlive
                        ? TeamId.Neutral
                        : teamAAlive > teamBAlive ? TeamId.TeamA : TeamId.TeamB;
                }

                return new MatchResult(MatchOutcome.TimeLimit, winner, elapsedSeconds);
            }

            return MatchResult.None;
        }
    }
}
