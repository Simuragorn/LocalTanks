using LocalTanks.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LocalTanks.Tests
{
    /// <summary>
    /// A match ends for exactly one reason, and the reason is decided in one place (ADR-066). These checks
    /// hold the decision table, the data it reads and the sentence the interface shows, because "the battle
    /// ended" without a stated reason is what a player cannot act on.
    /// </summary>
    public sealed class MatchRulesTests
    {
        private static MatchRules Limited(float seconds, bool draw = true)
        {
            return new MatchRules { timeLimitSeconds = seconds, timeLimitIsDraw = draw, friendlyFire = true };
        }

        [Test]
        public void AWipedOutTeamEndsTheBattleAndAMutualWipeIsADraw()
        {
            MatchResult bWiped = MatchOutcomeRules.Resolve(4, 0, TeamId.Neutral, 12f, Limited(900f));
            Assert.That(bWiped.Outcome, Is.EqualTo(MatchOutcome.TeamWipedOut));
            Assert.That(bWiped.Winner, Is.EqualTo(TeamId.TeamA));

            MatchResult aWiped = MatchOutcomeRules.Resolve(0, 3, TeamId.Neutral, 12f, Limited(900f));
            Assert.That(aWiped.Outcome, Is.EqualTo(MatchOutcome.TeamWipedOut));
            Assert.That(aWiped.Winner, Is.EqualTo(TeamId.TeamB));

            MatchResult both = MatchOutcomeRules.Resolve(0, 0, TeamId.Neutral, 12f, Limited(900f));
            Assert.That(both.Outcome, Is.EqualTo(MatchOutcome.TeamWipedOut));
            Assert.That(both.IsDraw, Is.True);
        }

        [Test]
        public void ACapturedBaseEndsTheBattleForTheCapturingSide()
        {
            MatchResult captured = MatchOutcomeRules.Resolve(4, 4, TeamId.TeamB, 30f, Limited(900f));
            Assert.That(captured.Outcome, Is.EqualTo(MatchOutcome.BaseCaptured));
            Assert.That(captured.Winner, Is.EqualTo(TeamId.TeamB));
        }

        [Test]
        public void AWipeOutranksACaptureAndACaptureOutranksTheClock()
        {
            // A destroyed team cannot be brought back, so it is decided first even in the same instant.
            MatchResult both = MatchOutcomeRules.Resolve(0, 2, TeamId.TeamB, 30f, Limited(10f));
            Assert.That(both.Outcome, Is.EqualTo(MatchOutcome.TeamWipedOut));
            Assert.That(both.Winner, Is.EqualTo(TeamId.TeamB));

            MatchResult captureAtTheLimit = MatchOutcomeRules.Resolve(2, 2, TeamId.TeamA, 900f, Limited(900f));
            Assert.That(captureAtTheLimit.Outcome, Is.EqualTo(MatchOutcome.BaseCaptured));
            Assert.That(captureAtTheLimit.Winner, Is.EqualTo(TeamId.TeamA));
        }

        [Test]
        public void TheTimeLimitIsADrawOrGoesToTheLargerTeam()
        {
            MatchResult draw = MatchOutcomeRules.Resolve(3, 1, TeamId.Neutral, 900f, Limited(900f));
            Assert.That(draw.Outcome, Is.EqualTo(MatchOutcome.TimeLimit));
            Assert.That(draw.IsDraw, Is.True);
            Assert.That(draw.Describe(TeamId.TeamA), Does.Contain("ничья"));

            MatchResult decided = MatchOutcomeRules.Resolve(3, 1, TeamId.Neutral, 900f, Limited(900f, draw: false));
            Assert.That(decided.Outcome, Is.EqualTo(MatchOutcome.TimeLimit));
            Assert.That(decided.Winner, Is.EqualTo(TeamId.TeamA));

            MatchResult tied = MatchOutcomeRules.Resolve(2, 2, TeamId.Neutral, 900f, Limited(900f, draw: false));
            Assert.That(tied.IsDraw, Is.True);
        }

        [Test]
        public void ARunningBattleHasNoResult()
        {
            MatchResult running = MatchOutcomeRules.Resolve(7, 7, TeamId.Neutral, 899.9f, Limited(900f));
            Assert.That(running.HasEnded, Is.False);
            Assert.That(running.Outcome, Is.EqualTo(MatchOutcome.None));
            Assert.That(running.Describe(TeamId.TeamA), Does.Contain("продолжается"));
        }

        [Test]
        public void Rules_RejectAnImpossibleTimeLimit()
        {
            Assert.That(new MatchRules { timeLimitSeconds = 900f }.Validate("Test:"), Is.Empty);
            Assert.That(new MatchRules { timeLimitSeconds = 0f }.Validate("Test:"), Is.Not.Empty);
            Assert.That(new MatchRules { timeLimitSeconds = -5f }.Validate("Test:"), Is.Not.Empty);
        }

        [Test]
        public void FriendlyFireSwitch_DecidesWhoMayBeDamaged()
        {
            MatchRules allowed = new MatchRules { friendlyFire = true };
            Assert.That(allowed.AllowsDamageTo(TeamId.TeamA, TeamId.TeamA), Is.True);

            MatchRules forbidden = new MatchRules { friendlyFire = false };
            Assert.That(forbidden.AllowsDamageTo(TeamId.TeamA, TeamId.TeamA), Is.False);
            Assert.That(forbidden.AllowsDamageTo(TeamId.TeamA, TeamId.TeamB), Is.True);
            Assert.That(forbidden.AllowsDamageTo(TeamId.Neutral, TeamId.TeamA), Is.True);
            Assert.That(forbidden.AllowsDamageTo(TeamId.TeamA, TeamId.Neutral), Is.True);
        }

        [Test]
        public void TheScenarioFileAndItsAssetCarryTheMatchRules()
        {
            BattleScenarioJson source = BattleScenarioImporter.LoadSource();
            Assert.That(source.rules, Is.Not.Null, "The scenario file must state the match rules.");
            Assert.That(source.rules.timeLimitSeconds, Is.EqualTo(900f));
            Assert.That(source.rules.timeLimitIsDraw, Is.True);
            Assert.That(source.rules.friendlyFire, Is.True);

            BattleScenario asset = AssetDatabase.LoadAssetAtPath<BattleScenario>(BattleScenarioImporter.AssetPath);
            Assert.That(asset, Is.Not.Null, "The generated scenario asset is missing.");
            Assert.That(asset.rules, Is.Not.Null);
            Assert.That(asset.rules.timeLimitSeconds, Is.EqualTo(source.rules.timeLimitSeconds));
            Assert.That(asset.rules.friendlyFire, Is.EqualTo(source.rules.friendlyFire));
        }

        [Test]
        public void AScenarioWithoutRules_IsRejected()
        {
            BattleScenario scenario = ScriptableObject.CreateInstance<BattleScenario>();
            try
            {
                CombatDatabase database = AssetDatabase.LoadAssetAtPath<CombatDatabase>(CombatDefinitionImporter.DatabasePath);
                BattleScenario source = AssetDatabase.LoadAssetAtPath<BattleScenario>(BattleScenarioImporter.AssetPath);
                scenario.id = source.id;
                scenario.sceneId = source.sceneId;
                scenario.kind = source.kind;
                scenario.entries = source.entries;
                scenario.rules = null;

                Assert.That(
                    scenario.Validate(database),
                    Has.Some.Matches<string>(error => error.Contains("no match rules")));
            }
            finally
            {
                Object.DestroyImmediate(scenario);
            }
        }
    }
}
