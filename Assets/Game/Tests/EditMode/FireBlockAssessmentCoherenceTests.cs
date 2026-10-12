using NUnit.Framework;
using UnityEngine;

namespace LocalTanks.Tests
{
    /// <summary>
    /// The fire block reason and the line-of-fire assessment are both read by the behavior
    /// audit and the F4 diagnostics. A reason such as NoVisibleTarget describes no blocking
    /// object, so it must never be paired with an assessment left over from an earlier
    /// moment, where a blocking ally may still have been alive.
    /// </summary>
    public sealed class FireBlockAssessmentCoherenceTests
    {
        [Test]
        public void ReasonWithoutBlocker_DropsStaleAssessment()
        {
            LineOfFireAssessment stale = new LineOfFireAssessment(
                FireBlockReason.AllyInLine,
                LineOfFireBlockType.Ally,
                null,
                Vector2.one,
                Vector2.up,
                3f,
                false);

            bool describesBlocker = CombatAiPolicy.DescribesBlocker(
                FireBlockReason.NoVisibleTarget,
                ref stale);

            Assert.That(describesBlocker, Is.False);
            Assert.That(stale.BlockType, Is.EqualTo(LineOfFireBlockType.None),
                "A reason that names no blocker must not keep a stale blocking assessment.");
            Assert.That(stale.Reason, Is.EqualTo(FireBlockReason.None));
        }

        [Test]
        public void RangeReasonWithoutBlocker_DropsStaleAssessment()
        {
            LineOfFireAssessment stale = new LineOfFireAssessment(
                FireBlockReason.AllyInLine,
                LineOfFireBlockType.Ally,
                null,
                Vector2.one,
                Vector2.up,
                3f,
                false);

            Assert.That(CombatAiPolicy.DescribesBlocker(FireBlockReason.OutOfRange, ref stale), Is.False);
            Assert.That(stale.BlockType, Is.EqualTo(LineOfFireBlockType.None));
        }

        [Test]
        public void ReasonAboutBlocker_KeepsAssessment()
        {
            LineOfFireAssessment ally = new LineOfFireAssessment(
                FireBlockReason.AllyInLine,
                LineOfFireBlockType.Ally,
                null,
                Vector2.one,
                Vector2.up,
                3f,
                false);

            Assert.That(CombatAiPolicy.DescribesBlocker(FireBlockReason.AllyInLine, ref ally), Is.True);
            Assert.That(ally.BlockType, Is.EqualTo(LineOfFireBlockType.Ally),
                "A reason that names a blocker must keep the assessment that produced it.");

            LineOfFireAssessment obstacle = new LineOfFireAssessment(
                FireBlockReason.ObstacleInLine,
                LineOfFireBlockType.Wreck,
                null,
                Vector2.one,
                Vector2.up,
                3f,
                false);

            Assert.That(CombatAiPolicy.DescribesBlocker(FireBlockReason.ObstacleInLine, ref obstacle), Is.True);
            Assert.That(obstacle.BlockType, Is.EqualTo(LineOfFireBlockType.Wreck));
        }
    }
}
