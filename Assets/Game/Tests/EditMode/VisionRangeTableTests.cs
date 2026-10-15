using System;
using System.IO;
using LocalTanks.Editor;
using NUnit.Framework;
using UnityEditor;

namespace LocalTanks.Tests
{
    /// <summary>
    /// View range is derived from the vehicle class alone (ADR-064). These checks hold the three halves of
    /// that rule together: the table itself is complete and ordered, a tank file may not carry a range, and the
    /// generated asset the game actually reads carries exactly what the table says.
    /// </summary>
    public sealed class VisionRangeTableTests
    {
        [Test]
        public void TheTable_CoversEveryClass()
        {
            foreach (VehicleClass vehicleClass in Enum.GetValues(typeof(VehicleClass)))
            {
                Assert.That(
                    VisionRangeTable.Classes,
                    Does.Contain(vehicleClass),
                    $"The view range table has no row for {vehicleClass}.");
            }

            foreach (VehicleClass vehicleClass in VisionRangeTable.Classes)
                Assert.That(VisionRangeTable.MetresFor(vehicleClass), Is.GreaterThan(0f));
        }

        [Test]
        public void TheTable_UsesTheFiveAuthoredClassBands()
        {
            Assert.That(VisionRangeTable.MetresFor(VehicleClass.LightTank), Is.EqualTo(200f));
            Assert.That(VisionRangeTable.MetresFor(VehicleClass.MediumTank), Is.EqualTo(160f));
            Assert.That(VisionRangeTable.MetresFor(VehicleClass.HeavyTank), Is.EqualTo(120f));
            Assert.That(VisionRangeTable.MetresFor(VehicleClass.TankDestroyer), Is.EqualTo(100f));
            Assert.That(VisionRangeTable.MetresFor(VehicleClass.Artillery), Is.EqualTo(60f));
        }

        [Test]
        public void TheDocumentedMachines_GetTheDocumentedRange()
        {
            Assert.That(VisionRangeTable.UnitsFor(VehicleClass.LightTank), Is.EqualTo(52.15f));
            Assert.That(VisionRangeTable.UnitsFor(VehicleClass.HeavyTank), Is.EqualTo(31.29f));

            // The conversion is the point: the revealed distance in metres equals the authored game metres, so a
            // distance shown to the player cannot disagree with the map.
            Assert.That(
                VisionRangeTable.UnitsFor(VehicleClass.LightTank) * GameScale.MetresPerUnit,
                Is.EqualTo(200f).Within(0.05f));
            Assert.That(
                VisionRangeTable.GuaranteedDetectionUnits(VisionRangeTable.UnitsFor(VehicleClass.LightTank)),
                Is.EqualTo(8.34f));
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
                float expected = VisionRangeTable.UnitsFor(tank.vehicleClass);
                Assert.That(
                    tank.viewRange,
                    Is.EqualTo(expected).Within(0.001f),
                    $"'{tank.id}' must read its view range from the class table.");
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
