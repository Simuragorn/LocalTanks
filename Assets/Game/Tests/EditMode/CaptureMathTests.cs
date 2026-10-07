using NUnit.Framework;

namespace LocalTanks.Tests
{
    public sealed class CaptureMathTests
    {
        [TestCase(1, 180f)]
        [TestCase(2, 90f)]
        [TestCase(3, 60f)]
        [TestCase(4, 60f)]
        public void CaptureDuration_UsesAtMostThreeTanks(int tankCount, float expectedSeconds)
        {
            Assert.That(CaptureMath.CaptureDuration(180f, tankCount, 3), Is.EqualTo(expectedSeconds));
        }

        [Test]
        public void Advance_AccumulatesNormalizedProgress()
        {
            float oneTank = CaptureMath.Advance(0f, 30f, 180f, 1, 3);
            float threeTanks = CaptureMath.Advance(0f, 30f, 180f, 3, 3);

            Assert.That(oneTank, Is.EqualTo(1f / 6f).Within(0.0001f));
            Assert.That(threeTanks, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void RemainingSeconds_AccountsForProgressAndContributors()
        {
            Assert.That(CaptureMath.RemainingSeconds(0.5f, 180f, 2, 3), Is.EqualTo(45f));
        }

        [Test]
        public void Recovery_DecreasesProgressWithoutGoingBelowZero()
        {
            Assert.That(CaptureMath.Recover(0.5f, 15f, 60f), Is.EqualTo(0.25f));
            Assert.That(CaptureMath.Recover(0.1f, 60f, 60f), Is.EqualTo(0f));
        }
    }
}
