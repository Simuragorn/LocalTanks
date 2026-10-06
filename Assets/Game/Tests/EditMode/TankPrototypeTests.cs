using NUnit.Framework;

namespace LocalTanks.Tests
{
    public sealed class TankPrototypeTests
    {
        [Test]
        public void ForwardSpeed_UsesForwardLimit()
        {
            Assert.That(TankMotionMath.TargetSpeed(1f, 5f, 2f), Is.EqualTo(5f));
        }

        [Test]
        public void ReverseSpeed_UsesReverseLimit()
        {
            Assert.That(TankMotionMath.TargetSpeed(-1f, 5f, 2f), Is.EqualTo(-2f));
        }

        [Test]
        public void Speed_AcceleratesWithoutOvershootingTarget()
        {
            float speed = TankMotionMath.ApproachSpeed(0f, 2f, 3f, 8f, 1f);
            Assert.That(speed, Is.EqualTo(2f));
        }

        [Test]
        public void TurretAngle_IsLimitedByTurnRate()
        {
            float angle = TankMotionMath.StepAngle(0f, 90f, 30f, 1f);
            Assert.That(angle, Is.EqualTo(30f).Within(0.001f));
        }

        [Test]
        public void ReloadTimer_BecomesReadyAfterDuration()
        {
            ReloadTimer timer = new ReloadTimer();
            timer.Start(0.8f);
            timer.Tick(0.5f);
            Assert.That(timer.IsReady, Is.False);
            timer.Tick(0.3f);
            Assert.That(timer.IsReady, Is.True);
        }

        [Test]
        public void Projectile_ExpiresAfterLifetime()
        {
            ProjectileTravelBudget budget = new ProjectileTravelBudget();
            budget.Reset(0.5f, 100f);
            budget.Consume(1f, 0.25f);
            Assert.That(budget.IsExpired, Is.False);
            budget.Consume(1f, 0.25f);
            Assert.That(budget.IsExpired, Is.True);
        }

        [Test]
        public void Projectile_StopsAtMaximumRange()
        {
            ProjectileTravelBudget budget = new ProjectileTravelBudget();
            budget.Reset(10f, 1.5f);
            float travelled = budget.Consume(2f, 0.1f);
            Assert.That(travelled, Is.EqualTo(1.5f));
            Assert.That(budget.IsExpired, Is.True);
        }
    }
}
