using System.Linq;
using LocalTanks.Editor;
using NUnit.Framework;

namespace LocalTanks.Tests
{
    public sealed class CombatDefinitionTests
    {
        [Test]
        public void ProjectDefinitions_AreValid()
        {
            CombatDefinitionSet definitions = CombatDefinitionImporter.LoadAndValidate();

            Assert.That(definitions.Tanks.Select(item => item.id), Is.EquivalentTo(new[]
            {
                "e_100", "panzer_iv", "t_34_76", "tiger_ii"
            }));
            Assert.That(definitions.Weapons.Select(item => item.id), Does.Contain("kwk_43_l71"));
            Assert.That(definitions.Shells.Select(item => item.id), Does.Contain("pzgr_39_43"));
        }

        [Test]
        public void Validator_RejectsMissingWeaponReference()
        {
            CombatDefinitionSet definitions = new CombatDefinitionSet(
                new[]
                {
                    new TankDefinitionJson
                    {
                        schemaVersion = 1,
                        id = "test_tank",
                        displayName = "Test Tank",
                        maxHitPoints = 100,
                        weaponId = "missing_weapon",
                        mobility = new MobilityJson(),
                        armor = new ArmorJson()
                    }
                },
                new WeaponDefinitionJson[0],
                new ShellDefinitionJson[0]);

            string[] errors = CombatDefinitionValidator.Validate(definitions);

            Assert.That(errors.Any(error => error.Contains("missing weapon")), Is.True);
        }

        [Test]
        public void Validator_RejectsDuplicateIdsAcrossDefinitionTypes()
        {
            CombatDefinitionSet definitions = new CombatDefinitionSet(
                new TankDefinitionJson[0],
                new[]
                {
                    new WeaponDefinitionJson
                    {
                        schemaVersion = 1,
                        id = "duplicate",
                        displayName = "Weapon",
                        reloadSeconds = 1f,
                        shellId = "duplicate"
                    }
                },
                new[]
                {
                    new ShellDefinitionJson
                    {
                        schemaVersion = 1,
                        id = "duplicate",
                        displayName = "Shell",
                        damage = 1,
                        penetration = 1f,
                        speed = 1f,
                        radius = 0.01f,
                        lifetimeSeconds = 1f,
                        maximumRange = 1f,
                        ricochetAngle = 70f,
                        ricochetSpeedMultiplier = 0.7f,
                        ricochetPenetrationMultiplier = 0.65f,
                        maximumRicochets = 1
                    }
                });

            string[] errors = CombatDefinitionValidator.Validate(definitions);

            Assert.That(errors.Any(error => error.Contains("Duplicate definition id")), Is.True);
        }
    }
}
