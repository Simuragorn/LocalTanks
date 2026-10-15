using NUnit.Framework;

namespace LocalTanks.Tests
{
    public sealed class RammingDamageTests
    {
        private static RammingRules Rules => new RammingRules
        {
            enabled = true,
            minimumImpactSpeedMps = 1.5f,
            hitPointsPerKilojoule = 1f,
            armorProtectionReferenceMm = 50f,
            repeatContactCooldownSeconds = 0.35f
        };

        [Test]
        public void ASlowContactDoesNotCauseDamage()
        {
            RammingDamageResult result = RammingDamageMath.Calculate(10f, 10f, 1.49f, 20f, 20f, Rules);

            Assert.That(result.HasDamage, Is.False);
            Assert.That(result.CollisionEnergyKilojoules, Is.Zero);
        }

        [Test]
        public void EqualVehiclesReceiveEqualDamageAndSpeedRaisesItQuadratically()
        {
            RammingDamageResult slow = RammingDamageMath.Calculate(10f, 10f, 4f, 20f, 20f, Rules);
            RammingDamageResult fast = RammingDamageMath.Calculate(10f, 10f, 8f, 20f, 20f, Rules);

            Assert.That(slow.DamageToFirst, Is.EqualTo(slow.DamageToSecond));
            Assert.That(fast.DamageToFirst, Is.EqualTo(fast.DamageToSecond));
            Assert.That(fast.CollisionEnergyKilojoules,
                Is.EqualTo(slow.CollisionEnergyKilojoules * 4f).Within(0.001f));
            Assert.That(fast.DamageToFirst, Is.GreaterThan(slow.DamageToFirst * 3));
        }

        [Test]
        public void HeavierVehicleHurtsTheLighterVehicleMoreThanItHurtsItself()
        {
            RammingDamageResult result = RammingDamageMath.Calculate(60f, 6f, 10f, 40f, 40f, Rules);

            Assert.That(result.DamageToSecond, Is.GreaterThan(result.DamageToFirst * 5));
        }

        [Test]
        public void ThickContactArmorReducesDamageToThatHull()
        {
            RammingDamageResult result = RammingDamageMath.Calculate(20f, 20f, 8f, 150f, 20f, Rules);

            Assert.That(result.DamageToFirst, Is.LessThan(result.DamageToSecond));
        }

        [Test]
        public void InvalidRammingBalanceIsRejectedByCombatRules()
        {
            CombatRules rules = new CombatRules
            {
                ramming = new RammingRules
                {
                    enabled = true,
                    minimumImpactSpeedMps = -1f,
                    hitPointsPerKilojoule = 0f,
                    armorProtectionReferenceMm = 0f,
                    repeatContactCooldownSeconds = -1f
                }
            };

            Assert.That(rules.Validate("Test"), Has.Length.EqualTo(4));
            rules.ramming = null;
            Assert.That(rules.Validate("Test"), Has.Some.Contains("ramming rules are missing"));
        }
    }
}
