using NUnit.Framework;
using UnityEngine;

namespace LocalTanks.Tests
{
    public sealed class TacticalFiringPositionPlannerTests
    {
        [Test]
        public void LineOfFireAssessment_IdentifiesConcreteHardObstacle()
        {
            GameObject owner = new GameObject("TacticalLineOwner");
            GameObject obstacle = new GameObject("TacticalHardObstacle");
            GameObject target = new GameObject("TacticalLineTarget");
            try
            {
                owner.transform.position = new Vector2(1200f, 1200f);
                owner.AddComponent<BoxCollider2D>().size = Vector2.one;
                obstacle.transform.position = new Vector2(1200f, 1203f);
                BoxCollider2D obstacleCollider = obstacle.AddComponent<BoxCollider2D>();
                obstacleCollider.size = Vector2.one;
                target.transform.position = new Vector2(1200f, 1206f);
                target.AddComponent<BoxCollider2D>().size = Vector2.one;
                Physics2D.SyncTransforms();

                LineOfFireAssessment result = CombatLineOfFire.Evaluate(
                    owner.transform,
                    TeamId.TeamA,
                    new Vector2(1200f, 1201f),
                    target.transform.position,
                    target.transform);

                Assert.That(result.IsClear, Is.False);
                Assert.That(result.Reason, Is.EqualTo(FireBlockReason.ObstacleInLine));
                Assert.That(result.BlockType, Is.EqualTo(LineOfFireBlockType.HardObstacle));
                Assert.That(result.Blocker, Is.EqualTo(obstacleCollider));
                Assert.That(result.BlockPoint.y, Is.EqualTo(1202.5f).Within(0.02f));
                Assert.That(result.CanBeDestroyed, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(obstacle);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void LineOfFireAssessment_RecognizesDestructibleObstacle()
        {
            GameObject owner = new GameObject("TacticalLineOwner");
            GameObject obstacle = new GameObject("TacticalDestructibleObstacle");
            GameObject target = new GameObject("TacticalLineTarget");
            try
            {
                owner.transform.position = new Vector2(1300f, 1300f);
                obstacle.transform.position = new Vector2(1300f, 1303f);
                BoxCollider2D obstacleCollider = obstacle.AddComponent<BoxCollider2D>();
                DestructibleObstacle destructible = obstacle.AddComponent<DestructibleObstacle>();
                destructible.Configure(null, System.Array.Empty<Vector2Int>(), 100);
                target.transform.position = new Vector2(1300f, 1306f);
                target.AddComponent<BoxCollider2D>();
                Physics2D.SyncTransforms();

                LineOfFireAssessment result = CombatLineOfFire.Evaluate(
                    owner.transform,
                    TeamId.TeamA,
                    new Vector2(1300f, 1301f),
                    target.transform.position,
                    target.transform);

                Assert.That(result.Blocker, Is.EqualTo(obstacleCollider));
                Assert.That(result.BlockType, Is.EqualTo(LineOfFireBlockType.DestructibleObstacle));
                Assert.That(result.CanBeDestroyed, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(obstacle);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void LineOfFireAssessment_ClassifiesDestroyedTankAsWreckInsteadOfAlly()
        {
            GameObject owner = new GameObject("WreckLineOwner");
            GameObject wreck = new GameObject("DestroyedAllyWreck");
            GameObject target = new GameObject("WreckLineTarget");
            TankDefinition definition = ScriptableObject.CreateInstance<TankDefinition>();
            try
            {
                definition.maxHitPoints = 100;
                owner.transform.position = new Vector2(1400f, 1400f);
                wreck.transform.position = new Vector2(1400f, 1403f);
                TankHealth health = wreck.AddComponent<TankHealth>();
                health.Configure(definition);
                TeamMember member = wreck.AddComponent<TeamMember>();
                member.Configure(TeamId.TeamA, false);
                BoxCollider2D wreckCollider = wreck.AddComponent<BoxCollider2D>();
                health.ApplyDamage(100);
                target.transform.position = new Vector2(1400f, 1406f);
                target.AddComponent<BoxCollider2D>();
                Physics2D.SyncTransforms();

                LineOfFireAssessment result = CombatLineOfFire.Evaluate(
                    owner.transform,
                    TeamId.TeamA,
                    new Vector2(1400f, 1401f),
                    target.transform.position,
                    target.transform);

                Assert.That(result.IsClear, Is.False);
                Assert.That(result.Reason, Is.EqualTo(FireBlockReason.ObstacleInLine));
                Assert.That(result.BlockType, Is.EqualTo(LineOfFireBlockType.Wreck));
                Assert.That(result.Blocker, Is.EqualTo(wreckCollider));
                Assert.That(CombatLineOfFire.IsProtectiveCover(result.BlockType), Is.True);
                Assert.That(CombatLineOfFire.IsProtectiveCover(LineOfFireBlockType.Ally), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(wreck);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void Planner_FindsReachablePositionAroundBlockedLine()
        {
            NavigationMap map = CreateMap(12, 12, (x, y) => x == 5 && y >= 3 && y <= 7);
            try
            {
                Vector2 origin = map.CellToWorld(new Vector2Int(1, 5));
                Vector2 target = map.CellToWorld(new Vector2Int(9, 5));

                bool found = TacticalFiringPositionPlanner.TryFind(
                    map,
                    origin,
                    target,
                    preferredDistance: 6f,
                    maximumFiringDistance: 12f,
                    clearanceCells: 0,
                    maximumSearchDistance: 10f,
                    candidate => candidate.y >= 8.5f,
                    null,
                    out TacticalFiringPositionSolution solution);

                Assert.That(found, Is.True);
                Assert.That(solution.Position.y, Is.GreaterThanOrEqualTo(8.5f));
                Assert.That(solution.PathLength, Is.GreaterThan(Vector2.Distance(origin, solution.Position) - 0.01f));
                Assert.That(solution.EvaluatedCandidates, Is.GreaterThan(0));
            }
            finally
            {
                Object.DestroyImmediate(map.gameObject);
            }
        }

        [Test]
        public void Planner_DoesNotRepeatExcludedFailedCell()
        {
            NavigationMap map = CreateMap(10, 10, (_, _) => false);
            try
            {
                Vector2 origin = map.CellToWorld(new Vector2Int(1, 4));
                Vector2 target = map.CellToWorld(new Vector2Int(8, 4));
                Assert.That(TacticalFiringPositionPlanner.TryFind(
                    map, origin, target, 5f, 10f, 0, 7f,
                    _ => true, null, out TacticalFiringPositionSolution first), Is.True);

                Assert.That(TacticalFiringPositionPlanner.TryFind(
                    map, origin, target, 5f, 10f, 0, 7f,
                    _ => true, cell => cell == first.Cell,
                    out TacticalFiringPositionSolution second), Is.True);

                Assert.That(second.Cell, Is.Not.EqualTo(first.Cell));
            }
            finally
            {
                Object.DestroyImmediate(map.gameObject);
            }
        }

        [Test]
        public void Planner_RejectsClearPositionWhenNoPathExists()
        {
            NavigationMap map = CreateMap(10, 10, (x, _) => x == 4);
            try
            {
                Vector2 origin = map.CellToWorld(new Vector2Int(1, 4));
                Vector2 target = map.CellToWorld(new Vector2Int(8, 4));
                bool found = TacticalFiringPositionPlanner.TryFind(
                    map,
                    origin,
                    target,
                    preferredDistance: 4f,
                    maximumFiringDistance: 10f,
                    clearanceCells: 0,
                    maximumSearchDistance: 9f,
                    candidate => candidate.x > 4.5f,
                    null,
                    out _);

                Assert.That(found, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(map.gameObject);
            }
        }

        [Test]
        public void FiringPositionPlanner_PenalizesLongRearExposingRouteAndAllowsShortReverse()
        {
            Vector2[] longRearRoute =
            {
                Vector2.zero,
                Vector2.down * 4f,
                Vector2.down * 8f
            };
            Vector2 threatDirection = Vector2.up;

            Assert.That(TacticalFiringPositionPlanner.ThreatExposurePenalty(
                longRearRoute, threatDirection, 6f), Is.GreaterThan(0f));

            TacticalFiringPositionSolution shortRearSolution = new TacticalFiringPositionSolution(
                Vector2Int.zero,
                Vector2.down * 4f,
                0f,
                4f,
                6f,
                1,
                Vector2.down);
            TacticalFiringPositionSolution longRearSolution = new TacticalFiringPositionSolution(
                Vector2Int.zero,
                Vector2.down * 8f,
                0f,
                8f,
                6f,
                1,
                Vector2.down);

            Assert.That(TacticalFiringPositionPlanner.ShouldApproachInReverse(
                shortRearSolution, threatDirection, 2f, 6f), Is.True);
            Assert.That(TacticalFiringPositionPlanner.ShouldApproachInReverse(
                longRearSolution, threatDirection, 2f, 6f), Is.False);
        }

        [Test]
        public void ArmorUseProfile_DerivesUsefulAngleFromActualFrontAndSideArmor()
        {
            TankDefinition definition = ScriptableObject.CreateInstance<TankDefinition>();
            try
            {
                definition.vehicleClass = VehicleClass.HeavyTank;
                definition.armor = new ArmorProfile(150f, 80f, 80f, 80f);

                ArmorUseProfile profile = ArmorUseProfile.From(definition);

                Assert.That(profile.CanAngle, Is.True);
                Assert.That(profile.SafeAngleDegrees, Is.EqualTo(28.1f).Within(0.2f));
                Assert.That(profile.EffectiveProtection(profile.SafeAngleDegrees),
                    Is.GreaterThan(profile.EffectiveProtection(0f)));
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void ArmorUseProfile_RejectsAnglingWithVeryWeakSideArmor()
        {
            TankDefinition definition = ScriptableObject.CreateInstance<TankDefinition>();
            try
            {
                definition.vehicleClass = VehicleClass.HeavyTank;
                definition.armor = new ArmorProfile(150f, 20f, 20f, 20f);

                ArmorUseProfile profile = ArmorUseProfile.From(definition);

                Assert.That(profile.CanAngle, Is.False);
                Assert.That(profile.SafeAngleDegrees, Is.Zero);
                Assert.That(profile.DesiredForward(Vector2.right, 1), Is.EqualTo(Vector2.right));
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void ThreatMemory_UsesRecentSevereObservationsWithoutRevealingShooter()
        {
            ThreatMemory memory = new ThreatMemory();
            memory.Record(new ArmorImpactObservation(
                Vector2.up, ImpactOutcome.Blocked, ArmorZone.Front, 0, 0f));
            memory.Record(new ArmorImpactObservation(
                Vector2.right, ImpactOutcome.Penetrated, ArmorZone.Left, 40, 8f));

            Assert.That(memory.TryGetPrimaryDirection(9f, out Vector2 direction), Is.True);
            Assert.That(direction.x, Is.GreaterThan(direction.y));
            Assert.That(memory.ActiveCount(19.1f), Is.Zero);
        }

        [Test]
        public void ThreatMemory_SeparatesCrossfireIntoDistinctSectors()
        {
            ThreatMemory memory = new ThreatMemory();
            memory.Record(new ArmorImpactObservation(
                Vector2.up, ImpactOutcome.Blocked, ArmorZone.Front, 0, 4f));
            memory.Record(new ArmorImpactObservation(
                Vector2.right, ImpactOutcome.Penetrated, ArmorZone.Left, 40, 4.5f));

            ThreatAnalysis analysis = memory.Analyze(5f, null);

            Assert.That(analysis.SectorCount, Is.EqualTo(2));
            Assert.That(analysis.IsCrossfire, Is.True);
            Assert.That(analysis.CrossfireAngle, Is.EqualTo(90f).Within(0.1f));
            Assert.That(analysis.PrimaryDirection.x, Is.GreaterThan(analysis.PrimaryDirection.y));
        }

        [Test]
        public void TacticalBehaviorProfile_UsesVehicleClassAndTemperament()
        {
            TankDefinition light = ScriptableObject.CreateInstance<TankDefinition>();
            TankDefinition heavy = ScriptableObject.CreateInstance<TankDefinition>();
            try
            {
                light.vehicleClass = VehicleClass.LightTank;
                heavy.vehicleClass = VehicleClass.HeavyTank;
                BotPersonalityProfile cautious = new BotPersonalityProfile
                {
                    caution = 0.8f,
                    aggression = 0.2f,
                    patience = 0.7f
                };

                TacticalBehaviorProfile lightProfile = TacticalBehaviorProfile.From(light, cautious);
                TacticalBehaviorProfile heavyProfile = TacticalBehaviorProfile.From(heavy, cautious);

                Assert.That(lightProfile.CoverPreference, Is.GreaterThan(heavyProfile.CoverPreference));
                Assert.That(lightProfile.PeekReloadThreshold, Is.GreaterThan(heavyProfile.PeekReloadThreshold));
                Assert.That(lightProfile.ShouldSeekCoverWhileReloading(0.1f, true, false, false, 1f), Is.False);
                Assert.That(lightProfile.ShouldSeekCoverWhileReloading(0.1f, true, false, true, 1f), Is.True);
                Assert.That(lightProfile.ShouldSeekCoverWhileReloading(0.1f, true, true, false, 1f), Is.True);
                Assert.That(heavyProfile.ShouldSeekCoverWhileReloading(0.9f, true, false, true, 1f), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(light);
                Object.DestroyImmediate(heavy);
            }
        }

        [Test]
        public void TacticalDecisionArbiter_PreservesHigherPrioritySurvivalTask()
        {
            Assert.That(TacticalDecisionArbiter.ShouldReplace(
                TacticalMovementPurpose.DefensiveRetreat,
                TacticalDecisionPriority.SurviveCrossfire,
                currentExpiresAt: 10f,
                TacticalMovementPurpose.GainFiringLine,
                TacticalDecisionPriority.GainFiringLine,
                time: 5f), Is.False);
            Assert.That(TacticalDecisionArbiter.ShouldReplace(
                TacticalMovementPurpose.MoveToCover,
                TacticalDecisionPriority.UseCover,
                currentExpiresAt: 10f,
                TacticalMovementPurpose.HoldCover,
                TacticalDecisionPriority.UseCover,
                time: 5f), Is.True);
        }

        [Test]
        public void TacticalDecisionArbiter_FiresReadyClearShotBeforeNonCriticalCover()
        {
            Assert.That(TacticalDecisionArbiter.ShouldFireBeforeCover(true, true, true, false, 0.8f), Is.True);
            Assert.That(TacticalDecisionArbiter.ShouldFireBeforeCover(true, true, true, true, 0.8f), Is.False);
            Assert.That(TacticalDecisionArbiter.ShouldFireBeforeCover(true, true, true, false, 0.2f), Is.False);
            Assert.That(TacticalDecisionArbiter.ShouldFireBeforeCover(false, true, true, false, 0.8f), Is.False);
            Assert.That(TacticalDecisionArbiter.ShouldFireBeforeCover(true, true, false, false, 0.8f), Is.False);
        }

        [Test]
        public void CoverPlanner_FindsReachableProtectedCell()
        {
            NavigationMap map = CreateMap(12, 10, (x, y) => x == 5 && y >= 2 && y <= 7);
            try
            {
                Vector2 origin = map.CellToWorld(new Vector2Int(7, 5));
                bool found = TacticalCoverPlanner.TryFind(
                    map,
                    origin,
                    clearanceCells: 0,
                    maximumSearchDistance: 5f,
                    candidate => candidate.x > 5.5f && candidate.y <= 7.5f,
                    _ => true,
                    out TacticalCoverSolution solution);

                Assert.That(found, Is.True);
                Assert.That(solution.Position.x, Is.GreaterThan(5.5f));
                Assert.That(solution.PathLength, Is.GreaterThan(0f));
                Assert.That(solution.EvaluatedCandidates, Is.GreaterThan(0));
            }
            finally
            {
                Object.DestroyImmediate(map.gameObject);
            }
        }

        [Test]
        public void CoverPlanner_BuildsShelterAndReachableFiringPointPair()
        {
            NavigationMap map = CreateMap(12, 12, (x, y) => x == 5 && y >= 2 && y <= 9);
            try
            {
                Vector2 origin = map.CellToWorld(new Vector2Int(8, 5));
                bool found = TacticalCoverPlanner.TryFindPair(
                    map,
                    origin,
                    clearanceCells: 0,
                    maximumSearchDistance: 5f,
                    maximumPeekDistance: 3.5f,
                    candidate => candidate.x > 5.5f,
                    _ => true,
                    candidate => candidate.y >= 8.5f,
                    out TacticalCoverSolution solution);

                Assert.That(found, Is.True);
                Assert.That(solution.HasFiringPosition, Is.True);
                Assert.That(solution.FiringPosition, Is.Not.EqualTo(solution.Position));
                Assert.That(solution.FiringPosition.y, Is.GreaterThanOrEqualTo(8.5f));
                Assert.That(solution.FiringPathLength, Is.GreaterThan(0f));
            }
            finally
            {
                Object.DestroyImmediate(map.gameObject);
            }
        }

        [Test]
        public void CoverPlanner_PenalizesLongRoutesThatInitiallyExposeRearArmor()
        {
            Vector2[] longRetreat =
            {
                Vector2.zero,
                Vector2.down * 4f,
                Vector2.down * 8f
            };
            Vector2[] shortRetreat =
            {
                Vector2.zero,
                Vector2.down * 2f,
                Vector2.down * 4f
            };

            Assert.That(TacticalCoverPlanner.ThreatExposurePenalty(longRetreat, Vector2.up, 6f),
                Is.GreaterThan(10f));
            Assert.That(TacticalCoverPlanner.ThreatExposurePenalty(shortRetreat, Vector2.up, 6f),
                Is.EqualTo(0f));
            Assert.That(TacticalCoverPlanner.ThreatExposurePenalty(longRetreat, Vector2.down, 6f),
                Is.EqualTo(0f));
        }

        [Test]
        public void CoverPlanner_UsesReverseOnlyForShortRouteAwayFromThreat()
        {
            TacticalCoverSolution shortRetreat = new TacticalCoverSolution(
                new Vector2Int(1, 1), Vector2.down * 4f, 1f, 4f, 1,
                default, Vector2.zero, 0f, false, Vector2.down);
            TacticalCoverSolution longRetreat = new TacticalCoverSolution(
                new Vector2Int(1, 1), Vector2.down * 8f, 1f, 8f, 1,
                default, Vector2.zero, 0f, false, Vector2.down);

            Assert.That(TacticalCoverPlanner.ShouldApproachInReverse(
                shortRetreat, Vector2.up, 1f, 6f), Is.True);
            Assert.That(TacticalCoverPlanner.ShouldApproachInReverse(
                longRetreat, Vector2.up, 1f, 6f), Is.False);
            Assert.That(TacticalCoverPlanner.ShouldApproachInReverse(
                shortRetreat, Vector2.down, 1f, 6f), Is.False);
        }

        [Test]
        public void CoverReservations_PreventTwoBotsFromClaimingSameCell()
        {
            TacticalCoverReservations.Clear();
            Vector2Int cell = new Vector2Int(4, 7);
            try
            {
                Assert.That(TacticalCoverReservations.TryReserve(cell, "bot-a", 1f, 10f), Is.True);
                Assert.That(TacticalCoverReservations.IsAvailable(cell, "bot-a", 2f), Is.True);
                Assert.That(TacticalCoverReservations.IsAvailable(cell, "bot-b", 2f), Is.False);
                Assert.That(TacticalCoverReservations.IsAvailable(cell, "bot-b", 12f), Is.True);
            }
            finally
            {
                TacticalCoverReservations.Clear();
            }
        }

        [Test]
        public void NavigationAgent_CanFollowAssignedRouteInReverse()
        {
            NavigationMap map = CreateMap(8, 8, (_, _) => false);
            GameObject tank = new GameObject("ReverseNavigationTank");
            try
            {
                tank.transform.position = map.CellToWorld(new Vector2Int(4, 4));
                tank.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                TankMotor motor = tank.AddComponent<TankMotor>();
                NavigationAgent navigation = tank.AddComponent<NavigationAgent>();
                navigation.Configure(map, motor, System.Array.Empty<Vector2>(), 0, false);

                bool pathBuilt = navigation.SetDestination(
                    map.CellToWorld(new Vector2Int(4, 2)),
                    minimumClearanceCells: 0,
                    reverse: true);

                Assert.That(pathBuilt, Is.True);
                Assert.That(navigation.MovingInReverse, Is.True);
                Assert.That(navigation.HasPath, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(tank);
                Object.DestroyImmediate(map.gameObject);
            }
        }

        private static NavigationMap CreateMap(int width, int height, System.Func<int, int, bool> blockedAt)
        {
            int count = width * height;
            TerrainKind[] terrain = new TerrainKind[count];
            float[] costs = new float[count];
            float[] speeds = new float[count];
            float[] accelerations = new float[count];
            bool[] blocked = new bool[count];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    bool isBlocked = blockedAt(x, y);
                    terrain[index] = isBlocked ? TerrainKind.Blocked : TerrainKind.Grass;
                    costs[index] = 1f;
                    speeds[index] = 1f;
                    accelerations[index] = 1f;
                    blocked[index] = isBlocked;
                }
            }

            NavigationMap map = new GameObject("TacticalPlannerMap").AddComponent<NavigationMap>();
            map.Configure(width, height, 1f, Vector2.zero, terrain, costs, speeds, accelerations, blocked);
            return map;
        }
    }
}
