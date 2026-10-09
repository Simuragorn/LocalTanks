using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LocalTanks.Tests
{
    public sealed class TankPrototypeTests
    {
        [Test]
        public void TankArt_UsesOnlyCanonicalWoTGeneratedRoot()
        {
            Assert.That(AssetDatabase.IsValidFolder("Assets/Game/Art/Tanks"), Is.False);
            Assert.That(AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/LocalOnly/WoTGenerated/G16_PzVIB_Tiger_II/G16_PzVIB_Tiger_II_strip2.png"), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/LocalOnly/WoTGenerated/R11_MS-1/R11_MS-1_strip2.png"), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/LocalOnly/WoTGenerated/G12_Ltraktor/G12_Ltraktor_strip2.png"), Is.Not.Null);
        }

        [Test]
        public void TigerIIPrefab_UsesProjectedTurretRingOnBothLayers()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Tanks/TigerII_Player.prefab");
            Transform turretPivot = prefab.transform.Find("TurretPivot");
            Sprite turretSprite = AssetDatabase
                .LoadAllAssetsAtPath("Assets/LocalOnly/WoTGenerated/G16_PzVIB_Tiger_II/G16_PzVIB_Tiger_II_strip2.png")
                .OfType<Sprite>()
                .Single(sprite => sprite.name == "TigerII_Turret");
            Vector2 normalizedTurretPivot = new Vector2(
                turretSprite.pivot.x / turretSprite.rect.width,
                turretSprite.pivot.y / turretSprite.rect.height);

            Assert.That(turretPivot, Is.Not.Null);
            Assert.That(turretPivot.localPosition.x, Is.EqualTo(0.005f).Within(0.002f));
            Assert.That(turretPivot.localPosition.y, Is.EqualTo(0.019f).Within(0.002f));
            Assert.That(normalizedTurretPivot.x, Is.EqualTo(0.49857f).Within(0.002f));
            Assert.That(normalizedTurretPivot.y, Is.EqualTo(0.77531f).Within(0.002f));
        }

        [Test]
        public void MS1Prefab_UsesRenderedGeometryAndProjectedTurretRing()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Tanks/MS1_Player.prefab");
            Assert.That(prefab, Is.Not.Null);
            Transform turretPivot = prefab.transform.Find("TurretPivot");
            Sprite turretSprite = AssetDatabase
                .LoadAllAssetsAtPath("Assets/LocalOnly/WoTGenerated/R11_MS-1/R11_MS-1_strip2.png")
                .OfType<Sprite>()
                .Single(sprite => sprite.name == "MS1_Turret");
            Vector2 normalizedTurretPivot = new Vector2(
                turretSprite.pivot.x / turretSprite.rect.width,
                turretSprite.pivot.y / turretSprite.rect.height);

            Assert.That(prefab.GetComponent<PolygonCollider2D>().points.Length, Is.EqualTo(16));
            Assert.That(prefab.GetComponent<Rigidbody2D>().mass, Is.EqualTo(5.5f).Within(0.01f));
            Assert.That(turretPivot.localPosition.x, Is.Zero.Within(0.002f));
            Assert.That(turretPivot.localPosition.y, Is.EqualTo(0.0872f).Within(0.002f));
            Assert.That(normalizedTurretPivot.x, Is.EqualTo(0.5f).Within(0.002f));
            Assert.That(normalizedTurretPivot.y, Is.EqualTo(0.68027f).Within(0.002f));
        }

        [Test]
        public void MS1Turret_RotatesNinetyDegreesWithoutLeavingProjectedRing()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Tanks/MS1_Player.prefab");
            GameObject instance = Object.Instantiate(prefab);

            try
            {
                Transform turretPivot = instance.transform.Find("TurretPivot");
                Transform turretVisual = turretPivot.Find("TurretVisual");
                Vector3 ringPosition = turretPivot.position;

                Assert.That(turretVisual.localPosition, Is.EqualTo(Vector3.zero));
                turretPivot.localRotation = Quaternion.Euler(0f, 0f, 90f);

                Assert.That(Vector3.Distance(turretPivot.position, ringPosition), Is.LessThan(0.0001f));
                Assert.That(Vector3.Distance(turretVisual.position, ringPosition), Is.LessThan(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void LeichttraktorPrefab_UsesRenderedGeometryAndRearTurretRing()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Tanks/Leichttraktor_Player.prefab");
            Assert.That(prefab, Is.Not.Null);
            Transform turretPivot = prefab.transform.Find("TurretPivot");
            Sprite turretSprite = AssetDatabase
                .LoadAllAssetsAtPath("Assets/LocalOnly/WoTGenerated/G12_Ltraktor/G12_Ltraktor_strip2.png")
                .OfType<Sprite>()
                .Single(sprite => sprite.name == "Leichttraktor_Turret");
            Vector2 normalizedTurretPivot = new Vector2(
                turretSprite.pivot.x / turretSprite.rect.width,
                turretSprite.pivot.y / turretSprite.rect.height);

            Assert.That(prefab.GetComponent<PolygonCollider2D>().points.Length, Is.EqualTo(12));
            Assert.That(prefab.GetComponent<Rigidbody2D>().mass, Is.EqualTo(8.96f).Within(0.01f));
            Assert.That(turretPivot.localPosition.x, Is.Zero.Within(0.002f));
            Assert.That(turretPivot.localPosition.y, Is.EqualTo(-0.2158f).Within(0.002f));
            Assert.That(normalizedTurretPivot.x, Is.EqualTo(0.49867f).Within(0.002f));
            Assert.That(normalizedTurretPivot.y, Is.EqualTo(0.70454f).Within(0.002f));
        }

        [Test]
        public void LeichttraktorTurret_RotatesNinetyDegreesWithoutLeavingRearRing()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Tanks/Leichttraktor_Player.prefab");
            GameObject instance = Object.Instantiate(prefab);

            try
            {
                Transform turretPivot = instance.transform.Find("TurretPivot");
                Transform turretVisual = turretPivot.Find("TurretVisual");
                Vector3 ringPosition = turretPivot.position;

                Assert.That(turretVisual.localPosition, Is.EqualTo(Vector3.zero));
                turretPivot.localRotation = Quaternion.Euler(0f, 0f, 90f);

                Assert.That(Vector3.Distance(turretPivot.position, ringPosition), Is.LessThan(0.0001f));
                Assert.That(Vector3.Distance(turretVisual.position, ringPosition), Is.LessThan(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

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
        public void GunDispersion_RecoversToPerfectAccuracyAfterAimingTime()
        {
            float result = GunDispersionMath.Step(2.8f, 0f, 0f, 2.8f, 2.2f, 2.2f);

            Assert.That(result, Is.Zero.Within(0.0001f));
        }

        [Test]
        public void GunDispersion_TurretMovementHasSmallerPenaltyThanHullMovement()
        {
            float hull = GunDispersionMath.CalculateTarget(0f, 3f, 0f, 1f, 0f, 1.5f, 1.2f, 0.4f);
            float turret = GunDispersionMath.CalculateTarget(0f, 3f, 0f, 0f, 1f, 1.5f, 1.2f, 0.4f);

            Assert.That(turret, Is.LessThan(hull));
        }

        [Test]
        public void GunDispersion_SampledShotStaysInsideCurrentCone()
        {
            for (int sequence = 0; sequence < 100; sequence++)
            {
                Assert.That(Mathf.Abs(GunDispersionMath.SampleOffsetDegrees(2.5f, sequence)), Is.LessThanOrEqualTo(2.5f));
            }

            Assert.That(GunDispersionMath.SampleOffsetDegrees(0f, 3), Is.Zero);
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
