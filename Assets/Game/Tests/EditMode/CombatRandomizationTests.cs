using System;
using System.IO;
using System.Linq;
using LocalTanks.Editor;
using NUnit.Framework;
using UnityEngine;

namespace LocalTanks.Tests
{
    public sealed class CombatRandomizationTests
    {
        private static CombatRules Rules => new CombatRules
        {
            minimumArmorCosine = 0.1f,
            penetrationRandomMinimum = 0.75f,
            penetrationRandomMaximum = 1.25f,
            damageRandomMinimum = 0.75f,
            damageRandomMaximum = 1.25f
        };

        [Test]
        public void DispersionRadius_GrowsWithDistanceAndAngleWithoutAnUpperClamp()
        {
            float near = GunDispersionMath.RadiusAtDistance(10f, 1f);
            float far = GunDispersionMath.RadiusAtDistance(100f, 1f);
            float wider = GunDispersionMath.RadiusAtDistance(100f, 3f);

            Assert.That(far, Is.GreaterThan(near));
            Assert.That(wider, Is.GreaterThan(far));
            Assert.That(GunDispersionMath.RadiusAtDistance(10000f, 3f), Is.GreaterThan(150f),
                "The world-space disk must remain honest even when it extends beyond the screen.");

            float threeMetres = GunDispersionMath.RadiusAtDistance(GameScale.MetresToUnits(3f), 1f);
            float oneHundredFiftyMetres = GunDispersionMath.RadiusAtDistance(GameScale.MetresToUnits(150f), 1f);
            Assert.That(oneHundredFiftyMetres, Is.EqualTo(threeMetres * 50f).Within(0.0001f),
                "At a fixed angular dispersion the sight radius must grow linearly from 3 to 150 metres.");
            Assert.That(PlayerAimReticle.FormatDistance(GameScale.MetresToUnits(150f)), Is.EqualTo("150 м"));
        }

        [Test]
        public void DistanceDispersionTable_CoversEveryClassInTheAuthoredOrder()
        {
            CollectionAssert.AreEquivalent(
                Enum.GetValues(typeof(VehicleClass)).Cast<VehicleClass>(),
                DistanceDispersionTable.Classes);
            CollectionAssert.AreEqual(
                new[]
                {
                    VehicleClass.Artillery,
                    VehicleClass.TankDestroyer,
                    VehicleClass.MediumTank,
                    VehicleClass.LightTank,
                    VehicleClass.HeavyTank
                },
                DistanceDispersionTable.Classes);

            Assert.That(
                DistanceDispersionTable.MultiplierPerStep(VehicleClass.Artillery),
                Is.EqualTo(0f));
            Assert.That(
                DistanceDispersionTable.MultiplierPerStep(VehicleClass.TankDestroyer),
                Is.LessThan(DistanceDispersionTable.MultiplierPerStep(VehicleClass.MediumTank)));
            Assert.That(
                DistanceDispersionTable.MultiplierPerStep(VehicleClass.MediumTank),
                Is.LessThan(DistanceDispersionTable.MultiplierPerStep(VehicleClass.LightTank)));
            Assert.That(
                DistanceDispersionTable.MultiplierPerStep(VehicleClass.LightTank),
                Is.LessThan(DistanceDispersionTable.MultiplierPerStep(VehicleClass.HeavyTank)));
        }

        [Test]
        public void DistanceDispersion_AddsOneSmoothClassMultiplierEveryTwentyMetres()
        {
            const float baseDispersion = 0.5f;
            float tenMetres = DistanceDispersionTable.EffectiveDegrees(
                VehicleClass.LightTank,
                GameScale.MetresToUnits(10f),
                baseDispersion);
            float twentyMetres = DistanceDispersionTable.EffectiveDegrees(
                VehicleClass.LightTank,
                GameScale.MetresToUnits(20f),
                baseDispersion);
            float fortyMetres = DistanceDispersionTable.EffectiveDegrees(
                VehicleClass.LightTank,
                GameScale.MetresToUnits(40f),
                baseDispersion);

            Assert.That(tenMetres, Is.EqualTo(0.55f).Within(0.0001f));
            Assert.That(twentyMetres, Is.EqualTo(0.60f).Within(0.0001f));
            Assert.That(fortyMetres, Is.EqualTo(0.70f).Within(0.0001f));
        }

        [Test]
        public void DistanceDispersion_IsSevereAtViewLimitExceptForTankDestroyersAndArtillery()
        {
            const float representativeAimedDispersion = 0.5f;
            float artillery = EffectiveAtViewLimit(VehicleClass.Artillery, representativeAimedDispersion);
            float tankDestroyer = EffectiveAtViewLimit(VehicleClass.TankDestroyer, representativeAimedDispersion);
            float medium = EffectiveAtViewLimit(VehicleClass.MediumTank, representativeAimedDispersion);
            float light = EffectiveAtViewLimit(VehicleClass.LightTank, representativeAimedDispersion);
            float heavy = EffectiveAtViewLimit(VehicleClass.HeavyTank, representativeAimedDispersion);

            Assert.That(artillery, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(tankDestroyer, Is.EqualTo(0.625f).Within(0.0001f));
            Assert.That(medium, Is.EqualTo(1.1f).Within(0.0001f));
            Assert.That(light, Is.EqualTo(1.5f).Within(0.0001f));
            Assert.That(heavy, Is.EqualTo(1.7f).Within(0.0001f));

            float zeroDegreeHeavy = DistanceDispersionTable.EffectiveDegrees(
                VehicleClass.HeavyTank,
                VisionRangeTable.UnitsFor(VehicleClass.HeavyTank),
                0f);
            Assert.That(zeroDegreeHeavy, Is.EqualTo(1.2f).Within(0.0001f),
                "An authored zero-degree gun must not bypass the distance penalty.");
        }

        private static float EffectiveAtViewLimit(VehicleClass vehicleClass, float currentDispersion)
        {
            return DistanceDispersionTable.EffectiveDegrees(
                vehicleClass,
                VisionRangeTable.UnitsFor(vehicleClass),
                currentDispersion);
        }

        [Test]
        public void PlayerAimDeadZone_HoldsTheLastDirectionAcrossTheTurretPivot()
        {
            Vector2 origin = new Vector2(2f, -1f);
            float deadZone = GameScale.MetresToUnits(PlayerTankInput.AimDeadZoneMetres);
            Vector2 held = PlayerTankInput.StabilizeAimTarget(
                origin,
                origin + Vector2.left * deadZone * 0.1f,
                Vector2.up,
                deadZone);
            Vector2 outside = origin + Vector2.right * deadZone * 1.1f;

            Assert.That(held, Is.EqualTo(origin + Vector2.up * deadZone));
            Assert.That(PlayerTankInput.StabilizeAimTarget(origin, outside, Vector2.up, deadZone),
                Is.EqualTo(outside));
        }

        [Test]
        public void EverySampledShot_IntersectsInsideTheDisplayedDispersionDisk()
        {
            const float distance = 70f;
            const float dispersion = 2.5f;
            float radius = GunDispersionMath.RadiusAtDistance(distance, dispersion);
            for (int sequence = 0; sequence < 256; sequence++)
            {
                float angle = GunDispersionMath.SampleOffsetDegrees(dispersion, sequence);
                Assert.That(GunDispersionMath.OffsetAtDistance(distance, angle),
                    Is.LessThanOrEqualTo(radius + 0.0001f));
            }
        }

        [Test]
        public void ShotRandomStream_IsReproducibleAndUsesIndependentRolls()
        {
            ShotRandomStream first = new ShotRandomStream(4127);
            ShotRandomStream second = new ShotRandomStream(4127);
            bool foundDifferentChannels = false;
            bool foundDifferentShots = false;
            ShotRandomization previous = default;
            for (int index = 0; index < 32; index++)
            {
                ShotRandomization a = first.Next(Rules);
                ShotRandomization b = second.Next(Rules);
                Assert.That(a.Sequence, Is.EqualTo(index));
                Assert.That(a.PenetrationMultiplier, Is.EqualTo(b.PenetrationMultiplier));
                Assert.That(a.DamageMultiplier, Is.EqualTo(b.DamageMultiplier));
                Assert.That(a.PenetrationMultiplier, Is.InRange(0.75f, 1.25f));
                Assert.That(a.DamageMultiplier, Is.InRange(0.75f, 1.25f));
                foundDifferentChannels |= !Mathf.Approximately(a.PenetrationMultiplier, a.DamageMultiplier);
                if (index > 0)
                {
                    foundDifferentShots |= !Mathf.Approximately(a.PenetrationMultiplier, previous.PenetrationMultiplier) ||
                                           !Mathf.Approximately(a.DamageMultiplier, previous.DamageMultiplier);
                }
                previous = a;
            }

            Assert.That(foundDifferentChannels, Is.True, "Penetration and damage must not share one roll.");
            Assert.That(foundDifferentShots, Is.True, "Advancing the stream must change later shots.");
        }

        [Test]
        public void RandomRange_IncludesBothAuthoredBoundaries()
        {
            Assert.That(CombatRandomMath.ScaleNormalized(0f, 0.75f, 1.25f), Is.EqualTo(0.75f));
            Assert.That(CombatRandomMath.ScaleNormalized(1f, 0.75f, 1.25f), Is.EqualTo(1.25f));
        }

        [Test]
        public void PenetrationChance_IsMonotonicAcrossTheSameRandomRange()
        {
            float guaranteed = ArmorCombatMath.PenetrationChance(100f, 75f, 0.75f, 1.25f);
            float even = ArmorCombatMath.PenetrationChance(100f, 100f, 0.75f, 1.25f);
            float impossible = ArmorCombatMath.PenetrationChance(100f, 125f, 0.75f, 1.25f);

            Assert.That(guaranteed, Is.EqualTo(1f));
            Assert.That(even, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(impossible, Is.Zero);
            Assert.That(guaranteed, Is.GreaterThan(even));
            Assert.That(even, Is.GreaterThan(impossible));
        }

        [TestCase(80f, ImpactOutcome.Blocked)]
        [TestCase(120f, ImpactOutcome.Penetrated)]
        public void ImpactResolver_ReportsTheSameForecastChanceForThePresentedPlate(
            float rolledPenetration,
            ImpactOutcome expected)
        {
            ImpactResult result = ImpactResolver.Resolve(new ImpactRequest(
                Vector2.down,
                Vector2.up,
                Vector2.up,
                new ArmorProfile(100f, 100f, 100f, 100f),
                rolledPenetration,
                10f,
                70f,
                0.7f,
                0.65f,
                0,
                0.1f,
                100f,
                0.75f,
                1.25f));

            Assert.That(result.Outcome, Is.EqualTo(expected));
            Assert.That(result.PenetrationChance, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void ReticleColor_MovesContinuouslyFromRedTowardGreen()
        {
            Color blocked = PlayerAimReticle.PenetrationChanceColor(0f);
            Color uncertain = PlayerAimReticle.PenetrationChanceColor(0.5f);
            Color guaranteed = PlayerAimReticle.PenetrationChanceColor(1f);

            Assert.That(blocked.r, Is.GreaterThan(uncertain.r));
            Assert.That(uncertain.r, Is.GreaterThan(guaranteed.r));
            Assert.That(blocked.g, Is.LessThan(uncertain.g));
            Assert.That(uncertain.g, Is.LessThan(guaranteed.g));
        }

        [Test]
        public void PenetrationIndicator_IsNeutralWithoutTargetAndColouredWithSolution()
        {
            Color neutral = PlayerAimReticle.PenetrationIndicatorColor(false, 1f);
            Color blocked = PlayerAimReticle.PenetrationIndicatorColor(true, 0f);
            Color penetrable = PlayerAimReticle.PenetrationIndicatorColor(true, 1f);

            Assert.That(neutral.r, Is.EqualTo(neutral.g).Within(0.05f));
            Assert.That(blocked.r, Is.GreaterThan(blocked.g));
            Assert.That(penetrable.g, Is.GreaterThan(penetrable.r));
            Assert.That(neutral, Is.Not.EqualTo(penetrable),
                "A loaded weapon without a target must not look like a confirmed high penetration chance.");
        }

        [Test]
        public void HoverOutlineColor_FollowsTeamRelation()
        {
            Assert.That(PlayerAimReticle.RelationOutlineColor(TeamId.TeamA, TeamId.TeamA),
                Is.EqualTo(TeamPalette.Ally));
            Assert.That(PlayerAimReticle.RelationOutlineColor(TeamId.TeamA, TeamId.TeamB),
                Is.EqualTo(TeamPalette.Enemy));
            Assert.That(PlayerAimReticle.RelationOutlineColor(TeamId.Neutral, TeamId.TeamB),
                Is.EqualTo(TeamPalette.Neutral));
        }

        [Test]
        public void ScenarioData_CarriesSeedAndCompleteCombatRules()
        {
            BattleScenarioJson source = BattleScenarioImporter.LoadSource();
            BattleScenario scenario = BattleScenarioImporter.Reimport();

            Assert.That(source.rules.randomSeed, Is.Not.Zero);
            Assert.That(source.rules.combat, Is.Not.Null);
            Assert.That(scenario.rules.randomSeed, Is.EqualTo(source.rules.randomSeed));
            Assert.That(scenario.rules.combat.Validate("test"), Is.Empty);
            Assert.That(scenario.rules.combat.penetrationRandomMinimum, Is.EqualTo(0.75f));
            Assert.That(scenario.rules.combat.penetrationRandomMaximum, Is.EqualTo(1.25f));
        }

        [Test]
        public void AimAndImpactPaths_UseTheSharedArmorCombatMath()
        {
            string aimSource = File.ReadAllText("Assets/Game/Runtime/ArmorAimProbe.cs");
            string impactSource = File.ReadAllText("Assets/Game/Core/ArmorCombatMath.cs");
            Assert.That(aimSource, Does.Contain("ArmorCombatMath.EvaluateProjection"));
            Assert.That(aimSource, Does.Contain("ArmorCombatMath.PenetrationChance"));
            Assert.That(impactSource, Does.Contain("ArmorCombatMath.EvaluateProjection"));
            Assert.That(impactSource, Does.Contain("ArmorCombatMath.PenetrationChance"));
            Assert.That(aimSource, Does.Not.Contain("EffectiveArmor("),
                "The reticle must not grow a second armor formula.");
        }
    }
}
