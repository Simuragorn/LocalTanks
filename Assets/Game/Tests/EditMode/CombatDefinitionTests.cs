using System.IO;
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
                "leichttraktor", "ms_1", "tiger_ii"
            }));
            Assert.That(definitions.Weapons.Select(item => item.id), Does.Contain("kwk_43_l71"));
            Assert.That(definitions.Shells.Select(item => item.id), Does.Contain("pzgr_39_43"));
        }

        [Test]
        public void KeptRoster_HasNationAvailabilityAndDistinctCharacteristics()
        {
            CombatDefinitionSet definitions = CombatDefinitionImporter.LoadAndValidate();
            TankDefinitionJson tiger = definitions.Tanks.Single(item => item.id == "tiger_ii");
            TankDefinitionJson ms1 = definitions.Tanks.Single(item => item.id == "ms_1");
            TankDefinitionJson leichttraktor = definitions.Tanks.Single(item => item.id == "leichttraktor");

            Assert.That(ms1.armor.front, Is.GreaterThan(leichttraktor.armor.front));
            Assert.That(leichttraktor.mobility.maxForwardSpeed, Is.GreaterThan(ms1.mobility.maxForwardSpeed));
            Assert.That(tiger.vehicleClass, Is.EqualTo("HeavyTank"));
            Assert.That(ms1.vehicleClass, Is.EqualTo("LightTank"));
            Assert.That(leichttraktor.vehicleClass, Is.EqualTo("LightTank"));
            Assert.That(ms1.weaponId, Is.EqualTo("gun_20_k_45mm"));
            Assert.That(leichttraktor.weaponId, Is.EqualTo("kwk_l45_37mm"));
            Assert.That(ms1.nation, Is.EqualTo("USSR"));
            Assert.That(leichttraktor.nation, Is.EqualTo("Germany"));
            Assert.That(tiger.nation, Is.EqualTo("Germany"));
            Assert.That(ms1.availableInGame, Is.True);
            Assert.That(leichttraktor.availableInGame, Is.True);
            Assert.That(tiger.availableInGame, Is.False);
            Assert.That(ms1.gunHandling.turretTraverseDispersionDegrees,
                Is.LessThan(ms1.gunHandling.hullTraverseDispersionDegrees));
            Assert.That(leichttraktor.gunHandling.turretTraverseDispersionDegrees,
                Is.LessThan(leichttraktor.gunHandling.movementDispersionDegrees));
        }

        [Test]
        public void TankBalanceDocument_MatchesCurrentJsonDefinitions()
        {
            CombatDefinitionSet definitions = CombatDefinitionImporter.LoadAndValidate();
            string expected = TankBalanceDocumentGenerator.GenerateMarkdown(definitions);
            string actual = File.ReadAllText(Path.GetFullPath(TankBalanceDocumentGenerator.DocumentPath));

            Assert.That(TankBalanceDocumentGenerator.Normalize(actual),
                Is.EqualTo(TankBalanceDocumentGenerator.Normalize(expected)));
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
                        nation = "Germany",
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

        [Test]
        public void Validator_RejectsInvalidVisionProfile()
        {
            CombatDefinitionSet definitions = new CombatDefinitionSet(
                new[]
                {
                    new TankDefinitionJson
                    {
                        schemaVersion = 1,
                        id = "test_tank",
                        displayName = "Test Tank",
                        nation = "Germany",
                        maxHitPoints = 100,
                        weaponId = "test_weapon",
                        mobility = new MobilityJson(),
                        armor = new ArmorJson(),
                        vision = new VisionJson
                        {
                            viewRange = 8f,
                            stationaryConcealment = 1.2f,
                            guaranteedDetectionRange = 9f
                        }
                    }
                },
                new[]
                {
                    new WeaponDefinitionJson
                    {
                        schemaVersion = 1,
                        id = "test_weapon",
                        displayName = "Test Weapon",
                        reloadSeconds = 1f,
                        shellId = "test_shell"
                    }
                },
                new[]
                {
                    new ShellDefinitionJson
                    {
                        schemaVersion = 1,
                        id = "test_shell",
                        displayName = "Test Shell",
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

            Assert.That(errors.Any(error => error.Contains("stationaryConcealment")), Is.True);
            Assert.That(errors.Any(error => error.Contains("cannot exceed viewRange")), Is.True);
        }

        [Test]
        public void Validator_RejectsUnknownVehicleClass()
        {
            CombatDefinitionSet definitions = CombatDefinitionImporter.LoadAndValidate();
            TankDefinitionJson tank = definitions.Tanks[0];
            string previous = tank.vehicleClass;
            tank.vehicleClass = "SuperHeavySpaceship";

            string[] errors = CombatDefinitionValidator.Validate(definitions);
            tank.vehicleClass = previous;

            Assert.That(errors.Any(error => error.Contains("unknown vehicleClass")), Is.True);
        }

        [Test]
        public void Validator_RejectsUnknownNation()
        {
            CombatDefinitionSet definitions = CombatDefinitionImporter.LoadAndValidate();
            TankDefinitionJson tank = definitions.Tanks[0];
            string previous = tank.nation;
            tank.nation = "Atlantis";

            string[] errors = CombatDefinitionValidator.Validate(definitions);
            tank.nation = previous;

            Assert.That(errors.Any(error => error.Contains("unknown nation")), Is.True);
        }
    }
}
