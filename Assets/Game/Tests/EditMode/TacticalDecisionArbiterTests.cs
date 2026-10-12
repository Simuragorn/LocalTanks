using NUnit.Framework;

namespace LocalTanks.Tests
{
    /// <summary>
    /// The behavior audit reports cover/combat task thrashing when a bot re-decides its
    /// protective task several times inside one six-second window. These tests pin the
    /// arbitration rules that keep a decided maneuver committed.
    /// </summary>
    public sealed class TacticalDecisionArbiterTests
    {
        [Test]
        public void ShouldReplace_KeepsRunningManeuverAgainstEqualPriority()
        {
            bool replace = TacticalDecisionArbiter.ShouldReplace(
                TacticalMovementPurpose.MoveToCover,
                TacticalDecisionPriority.UseCover,
                20f,
                TacticalMovementPurpose.MoveToCover,
                TacticalDecisionPriority.UseCover,
                5f);

            Assert.That(replace, Is.True,
                "Re-requesting the same maneuver must not restart it; it is already running.");
        }

        [Test]
        public void ShouldReplace_RefusesWeakerRequestWhileManeuverIsRunning()
        {
            bool replace = TacticalDecisionArbiter.ShouldReplace(
                TacticalMovementPurpose.HoldCover,
                TacticalDecisionPriority.SurviveCrossfire,
                20f,
                TacticalMovementPurpose.MoveToCover,
                TacticalDecisionPriority.UseCover,
                5f);

            Assert.That(replace, Is.False,
                "A weaker protective task must not interrupt a stronger one before it expires.");
        }

        [Test]
        public void ShouldReplace_AcceptsStrongerRequestWhileManeuverIsRunning()
        {
            bool replace = TacticalDecisionArbiter.ShouldReplace(
                TacticalMovementPurpose.MoveToCover,
                TacticalDecisionPriority.UseCover,
                20f,
                TacticalMovementPurpose.DefensiveRetreat,
                TacticalDecisionPriority.SurviveCrossfire,
                5f);

            Assert.That(replace, Is.True,
                "Surviving crossfire must still be able to interrupt a normal cover move.");
        }

        [Test]
        public void ShouldReplace_AcceptsAnyRequestAfterExpiry()
        {
            bool replace = TacticalDecisionArbiter.ShouldReplace(
                TacticalMovementPurpose.MoveToCover,
                TacticalDecisionPriority.UseCover,
                4f,
                TacticalMovementPurpose.MoveToCover,
                TacticalDecisionPriority.UseCover,
                5f);

            Assert.That(replace, Is.True);
        }
    }
}
