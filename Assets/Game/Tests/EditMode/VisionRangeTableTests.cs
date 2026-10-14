using System;
using System.Collections.Generic;
using System.IO;
using LocalTanks.Editor;
using NUnit.Framework;
using UnityEditor;

namespace LocalTanks.Tests
{
    /// <summary>
    /// View range is derived from the vehicle class and tier (ADR-064). These checks hold the three halves of
    /// that rule together: the table itself is complete and ordered, a tank file may not carry a range, and the
    /// generated asset the game actually reads carries exactly what the table says.
    /// </summary>
    public sealed class VisionRangeTableTests
    {
        [Test]
        public void TheTable_CoversEveryClassAndTier()
        {
            foreach (VehicleClass vehicleClass in Enum.GetValues(typeof(VehicleClass)))
            {
                Assert.That(
                    VisionRangeTable.Classes,
                    Does.Contain(vehicleClass),
                    $"The view range table has no row for {vehicleClass}.");
            }

            foreach (VehicleClass vehicleClass in VisionRangeTable.Classes)
            {
                for (int tier = VisionRangeTable.MinimumTier; tier <= VisionRangeTable.MaximumTier; tier++)
                {
                    Assert.That(
                        VisionRangeTable.MetresFor(vehicleClass, tier),
                        Is.GreaterThan(0f),
                        $"{vehicleClass} at tier {tier} has no positive range.");
                }
            }

            Assert.That(
                () => VisionRangeTable.MetresFor(VehicleClass.LightTank, VisionRangeTable.MaximumTier + 1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void TheTable_GrowsWithTierAndKeepsTheClassOrder()
        {
            foreach (VehicleClass vehicleClass in VisionRangeTable.Classes)
            {
                for (int tier = VisionRangeTable.MinimumTier; tier < VisionRangeTable.MaximumTier; tier++)
                {
                    Assert.That(
                        VisionRangeTable.MetresFor(vehicleClass, tier + 1),
                        Is.GreaterThan(VisionRangeTable.MetresFor(vehicleClass, tier)),
                        $"{vehicleClass} must see further at tier {tier + 1} than at tier {tier}.");
                }
            }

            // Light tanks see furthest and artillery least at every tier: the hierarchy World of Tanks keeps and
            // the reason the table exists at all.
            for (int tier = VisionRangeTable.MinimumTier; tier <= VisionRangeTable.MaximumTier; tier++)
            {
                Assert.That(
                    VisionRangeTable.MetresFor(VehicleClass.LightTank, tier),
                    Is.GreaterThanOrEqualTo(VisionRangeTable.MetresFor(VehicleClass.MediumTank, tier)));
                Assert.That(
                    VisionRangeTable.MetresFor(VehicleClass.MediumTank, tier),
                    Is.GreaterThanOrEqualTo(VisionRangeTable.MetresFor(VehicleClass.HeavyTank, tier)));
                Assert.That(
                    VisionRangeTable.MetresFor(VehicleClass.HeavyTank, tier),
                    Is.GreaterThanOrEqualTo(VisionRangeTable.MetresFor(VehicleClass.TankDestroyer, tier)));
                Assert.That(
                    VisionRangeTable.MetresFor(VehicleClass.TankDestroyer, tier),
                    Is.GreaterThanOrEqualTo(VisionRangeTable.MetresFor(VehicleClass.Artillery, tier)));
            }
        }

        [Test]
        public void TheDocumentedMachines_GetTheDocumentedRange()
        {
            // Docs/SPRINT_009.md, стадия 0: a light tank at tier I sees 240 m, the heavy at tier VIII 310 m.
            Assert.That(VisionRangeTable.UnitsFor(VehicleClass.LightTank, 1), Is.EqualTo(62.58f));
            Assert.That(VisionRangeTable.UnitsFor(VehicleClass.HeavyTank, 8), Is.EqualTo(80.83f));

            // The conversion is the point: the revealed distance in metres equals the client's metres, so a
            // distance shown to the player cannot disagree with the map.
            Assert.That(
                VisionRangeTable.UnitsFor(VehicleClass.LightTank, 1) * GameScale.MetresPerUnit,
                Is.EqualTo(240f).Within(0.05f));
            Assert.That(
                VisionRangeTable.GuaranteedDetectionUnits(VisionRangeTable.UnitsFor(VehicleClass.LightTank, 1)),
                Is.EqualTo(10.01f));
        }

        [Test]
        public void NoTankFile_DeclaresItsOwnViewRange()
        {
            foreach (string file in Directory.GetFiles("Assets/Game/GameData/Definitions/Tanks", "*.json"))
            {
                string json = File.ReadAllText(file);
                Assert.That(json, Does.Not.Contain("\"viewRange\""), $"{file} must not author a view range.");
                Assert.That(
                    json,
                    Does.Not.Contain("\"guaranteedDetectionRange\""),
                    $"{file} must not author a guaranteed detection range.");
            }
        }

        [Test]
        public void TheGeneratedDatabase_CarriesExactlyWhatTheTableSays()
        {
            CombatDatabase database = AssetDatabase.LoadAssetAtPath<CombatDatabase>(CombatDefinitionImporter.DatabasePath);
            Assert.That(database, Is.Not.Null, "The combat database was not generated.");

            foreach (TankDefinition tank in database.tanks)
            {
                float expected = VisionRangeTable.UnitsFor(tank.vehicleClass, tank.tier);
                Assert.That(
                    tank.viewRange,
                    Is.EqualTo(expected).Within(0.001f),
                    $"'{tank.id}' must read its view range from the class and tier table.");
                Assert.That(
                    tank.guaranteedDetectionRange,
                    Is.EqualTo(VisionRangeTable.GuaranteedDetectionUnits(expected)).Within(0.001f),
                    $"'{tank.id}' must derive its guaranteed detection radius.");
                Assert.That(tank.guaranteedDetectionRange, Is.LessThanOrEqualTo(tank.viewRange));
            }
        }

        [Test]
        public void TheDataPipeline_NoLongerWritesAViewRange()
        {
            string script = File.ReadAllText("Automation/pipelines/WoTDataPipeline/Scripts/sync_wot_top_profiles.py");
            Assert.That(script, Does.Not.Contain("\"viewRange\""));
            Assert.That(script, Does.Not.Contain("\"guaranteedDetectionRange\""));
        }
    }
}
