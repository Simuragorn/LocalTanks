using NUnit.Framework;
using UnityEngine;

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
            float speed = TankMotionMath.ApproachSpeed(0f, 2f, 3f, 4f, 8f, 1f);
            Assert.That(speed, Is.EqualTo(2f));
        }

        [Test]
        public void Speed_UsesGroundResistanceWhenInputIsReleased()
        {
            float speed = TankMotionMath.ApproachSpeed(2f, 0f, 0.3f, 1.25f, 3f, 1f);
            Assert.That(speed, Is.EqualTo(0.75f).Within(0.001f));
        }

        [Test]
        public void Speed_UsesBrakingWhenChangingDirection()
        {
            float speed = TankMotionMath.ApproachSpeed(2f, -1f, 0.3f, 1f, 1.5f, 1f);
            Assert.That(speed, Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void Speed_UsesGroundResistanceWhenReducingThrottle()
        {
            float speed = TankMotionMath.ApproachSpeed(2f, 1f, 0.3f, 1f, 3f, 1f);
            Assert.That(speed, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void TurretAngle_IsLimitedByTurnRate()
        {
            float angle = TankMotionMath.StepAngle(0f, 90f, 30f, 1f);
            Assert.That(angle, Is.EqualTo(30f).Within(0.001f));
        }

        [Test]
        public void Steering_IsInvertedWhileReversing()
        {
            Assert.That(TankMotionMath.SteeringInput(1f, -0.5f), Is.EqualTo(-1f));
        }

        [Test]
        public void Steering_IsNotInvertedWhileMovingForward()
        {
            Assert.That(TankMotionMath.SteeringInput(1f, 0.5f), Is.EqualTo(1f));
        }

        [Test]
        public void PathFollowing_DrivesStraightWhenAligned()
        {
            Vector2 input = TankMotionMath.PathFollowingInput(0f);

            Assert.That(input.x, Is.EqualTo(1f).Within(0.001f));
            Assert.That(input.y, Is.EqualTo(0f).Within(0.001f));
        }

        [TestCase(90f, -1f)]
        [TestCase(-90f, 1f)]
        [TestCase(180f, -1f)]
        public void PathFollowing_PivotsBeforeDrivingTowardSharpTurn(float angle, float expectedTurn)
        {
            Vector2 input = TankMotionMath.PathFollowingInput(angle);

            Assert.That(input.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(input.y, Is.EqualTo(expectedTurn).Within(0.001f));
        }

        [Test]
        public void PathFollowing_ReducesThrottleAsTurnBecomesSharper()
        {
            float shallowDrive = TankMotionMath.PathFollowingInput(20f).x;
            float mediumDrive = TankMotionMath.PathFollowingInput(50f).x;

            Assert.That(shallowDrive, Is.GreaterThan(mediumDrive));
            Assert.That(mediumDrive, Is.GreaterThan(0f));
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

        [TestCase(0f, 1f, ArmorZone.Front)]
        [TestCase(0f, -1f, ArmorZone.Rear)]
        [TestCase(-1f, 0f, ArmorZone.Left)]
        [TestCase(1f, 0f, ArmorZone.Right)]
        [TestCase(0.7f, 0.7f, ArmorZone.Front)]
        public void ArmorZone_UsesLocalColliderNormal(float x, float y, ArmorZone expected)
        {
            Assert.That(ArmorMath.SelectZone(new UnityEngine.Vector2(x, y)), Is.EqualTo(expected));
        }

        [Test]
        public void EffectiveArmor_IncreasesWithColliderImpactAngle()
        {
            float straight = ArmorMath.EffectiveArmor(100f, 1f, 0.1f);
            float angled = ArmorMath.EffectiveArmor(100f, 0.5f, 0.1f);

            Assert.That(straight, Is.EqualTo(100f));
            Assert.That(angled, Is.EqualTo(200f));
        }

        [Test]
        public void Impact_PenetratesWhenPenetrationEqualsEffectiveArmor()
        {
            ImpactResult result = ResolveImpact(
                UnityEngine.Vector2.down,
                UnityEngine.Vector2.up,
                penetration: 150f,
                remainingRicochets: 1);

            Assert.That(result.Outcome, Is.EqualTo(ImpactOutcome.Penetrated));
            Assert.That(result.Zone, Is.EqualTo(ArmorZone.Front));
        }

        [Test]
        public void Impact_BlocksWithoutDamageAtDirectAngle()
        {
            ImpactResult result = ResolveImpact(
                UnityEngine.Vector2.down,
                UnityEngine.Vector2.up,
                penetration: 149f,
                remainingRicochets: 1);

            Assert.That(result.Outcome, Is.EqualTo(ImpactOutcome.Blocked));
        }

        [Test]
        public void Impact_RicochetsFromAngledColliderSegment()
        {
            UnityEngine.Vector2 normal = new UnityEngine.Vector2(0.94f, 0.342f).normalized;
            ImpactResult result = ResolveImpact(
                UnityEngine.Vector2.down,
                normal,
                penetration: 100f,
                remainingRicochets: 1);

            Assert.That(result.Outcome, Is.EqualTo(ImpactOutcome.Ricocheted));
            Assert.That(result.ImpactAngle, Is.EqualTo(70f).Within(0.1f));
            Assert.That(result.OutgoingSpeed, Is.EqualTo(7f).Within(0.001f));
            Assert.That(result.RemainingPenetration, Is.EqualTo(65f).Within(0.001f));
            Assert.That(UnityEngine.Vector2.Dot(result.OutgoingDirection, normal), Is.GreaterThan(0f));
        }

        [Test]
        public void Impact_CannotRicochetAfterLimitIsExhausted()
        {
            UnityEngine.Vector2 normal = new UnityEngine.Vector2(0.94f, 0.342f).normalized;
            ImpactResult result = ResolveImpact(
                UnityEngine.Vector2.down,
                normal,
                penetration: 100f,
                remainingRicochets: 0);

            Assert.That(result.Outcome, Is.EqualTo(ImpactOutcome.Blocked));
        }

        [Test]
        public void Health_ClampsAtZeroAndDestroysOnlyOnce()
        {
            HealthState health = new HealthState(100);

            DamageResult first = health.ApplyDamage(150);
            DamageResult second = health.ApplyDamage(10);

            Assert.That(first.AppliedDamage, Is.EqualTo(100));
            Assert.That(first.RemainingHitPoints, Is.Zero);
            Assert.That(first.WasDestroyed, Is.True);
            Assert.That(second.AppliedDamage, Is.Zero);
            Assert.That(second.WasDestroyed, Is.False);
        }

        [Test]
        public void WeightedAStar_AvoidsBlockedCells()
        {
            NavigationGridModel grid = new NavigationGridModel(5, 3);
            grid.SetBlocked(new Vector2Int(2, 1), true);

            var path = WeightedAStar.FindPath(grid, new Vector2Int(0, 1), new Vector2Int(4, 1));

            Assert.That(path, Is.Not.Empty);
            Assert.That(path, Has.None.EqualTo(new Vector2Int(2, 1)));
        }

        [Test]
        public void WeightedAStar_PrefersLongerRoadOverExpensiveMud()
        {
            NavigationGridModel grid = new NavigationGridModel(5, 3);
            for (int x = 1; x < 4; x++)
            {
                grid.SetCell(new Vector2Int(x, 1), 8f, false);
            }

            var path = WeightedAStar.FindPath(
                grid,
                new Vector2Int(0, 1),
                new Vector2Int(4, 1),
                heuristicWeight: 1f);

            Assert.That(path, Is.Not.Empty);
            Assert.That(path.Exists(cell => cell.y != 1), Is.True);
        }

        [Test]
        public void WeightedAStar_DoesNotCutBlockedDiagonalCorner()
        {
            NavigationGridModel grid = new NavigationGridModel(3, 3);
            grid.SetBlocked(new Vector2Int(1, 0), true);
            grid.SetBlocked(new Vector2Int(0, 1), true);

            var path = WeightedAStar.FindPath(grid, Vector2Int.zero, new Vector2Int(2, 2));

            Assert.That(path, Is.Empty);
        }

        [Test]
        public void WeightedAStar_ClearanceRejectsNarrowPassage()
        {
            NavigationGridModel grid = new NavigationGridModel(7, 7);
            for (int y = 0; y < 7; y++)
            {
                if (y != 3)
                {
                    grid.SetBlocked(new Vector2Int(3, y), true);
                }
            }

            var pointPath = WeightedAStar.FindPath(grid, new Vector2Int(1, 3), new Vector2Int(5, 3), 0);
            var widePath = WeightedAStar.FindPath(grid, new Vector2Int(1, 3), new Vector2Int(5, 3), 1);

            Assert.That(pointPath, Is.Not.Empty);
            Assert.That(widePath, Is.Empty);
        }

        private static ImpactResult ResolveImpact(
            UnityEngine.Vector2 direction,
            UnityEngine.Vector2 surfaceNormal,
            float penetration,
            int remainingRicochets)
        {
            return ImpactResolver.Resolve(new ImpactRequest(
                direction,
                surfaceNormal,
                surfaceNormal,
                new ArmorProfile(150f, 80f, 80f, 80f),
                penetration,
                speed: 10f,
                ricochetAngle: 70f,
                ricochetSpeedMultiplier: 0.7f,
                ricochetPenetrationMultiplier: 0.65f,
                remainingRicochets));
        }
    }
}
