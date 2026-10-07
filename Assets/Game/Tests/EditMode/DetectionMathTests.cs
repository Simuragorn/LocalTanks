using NUnit.Framework;

namespace LocalTanks.Tests
{
    public sealed class DetectionMathTests
    {
        [Test]
        public void Concealment_ReducesDetectionDistance()
        {
            DetectionResult clear = Resolve(0f, 0f, 0f, 0f, 7f);
            DetectionResult concealed = Resolve(0.4f, 0f, 0f, 0f, 7f);

            Assert.That(concealed.DetectionDistance, Is.LessThan(clear.DetectionDistance));
            Assert.That(clear.Detected, Is.True);
            Assert.That(concealed.Detected, Is.False);
        }

        [Test]
        public void MovementAndFiring_IncreaseDetectionDistance()
        {
            DetectionResult stationary = Resolve(0.5f, 0f, 0f, 0f, 6f);
            DetectionResult movingAndFiring = Resolve(0.5f, 0f, 0.2f, 0.2f, 6f);

            Assert.That(movingAndFiring.DetectionDistance, Is.GreaterThan(stationary.DetectionDistance));
        }

        [Test]
        public void BushBonus_IsLimitedByMaximumConcealment()
        {
            DetectionResult oneBush = Resolve(0.2f, 0.3f, 0f, 0f, 4f);
            DetectionResult manyBushes = Resolve(0.2f, 2f, 0f, 0f, 4f);

            Assert.That(manyBushes.Concealment, Is.EqualTo(0.8f).Within(0.001f));
            Assert.That(manyBushes.DetectionDistance, Is.LessThan(oneBush.DetectionDistance));
        }

        [Test]
        public void GuaranteedRange_IgnoresConcealmentButNotHardBlocker()
        {
            DetectionResult visible = Resolve(0.8f, 0.45f, 0f, 0f, 1f, false);
            DetectionResult blocked = Resolve(0.8f, 0.45f, 0f, 0f, 1f, true);

            Assert.That(visible.Detected, Is.True);
            Assert.That(visible.Reason, Is.EqualTo(DetectionReason.GuaranteedRange));
            Assert.That(blocked.Detected, Is.False);
            Assert.That(blocked.Reason, Is.EqualTo(DetectionReason.HardBlocker));
        }

        private static DetectionResult Resolve(
            float concealment,
            float bush,
            float movementPenalty,
            float firingPenalty,
            float distance,
            bool blocked = false)
        {
            return DetectionMath.Resolve(new DetectionInput(
                distance,
                10f,
                concealment,
                bush,
                movementPenalty,
                firingPenalty,
                0.8f,
                0.2f,
                1.5f,
                blocked));
        }
    }
}
