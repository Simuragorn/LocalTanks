using System;
using System.IO;
using System.Linq;
using LocalTanks.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LocalTanks.Tests
{
    public sealed class TacticalMapAnalyzerTests
    {
        [Test]
        public void RiverCrossing_FindsThreeOrderedStrategicLanes()
        {
            TacticalMapDescriptor descriptor = AnalyzeRiverCrossing();

            Assert.That(descriptor.lanes, Has.Length.EqualTo(3));
            Assert.That(descriptor.lanes.Select(item => item.id), Is.EqualTo(new[]
            {
                "lane_north", "lane_center", "lane_south"
            }));
            Assert.That(descriptor.lanes[0].crossingCell.y, Is.GreaterThan(descriptor.lanes[1].crossingCell.y));
            Assert.That(descriptor.lanes[1].crossingCell.y, Is.GreaterThan(descriptor.lanes[2].crossingCell.y));
            Assert.That(descriptor.lanes.All(item => item.hasMandatoryCrossing), Is.True);
        }

        [Test]
        public void LaneDescriptors_ContainUsableMetricsAndClassScores()
        {
            TacticalMapDescriptor descriptor = AnalyzeRiverCrossing();

            foreach (TacticalLaneDescriptor lane in descriptor.lanes)
            {
                Assert.That(lane.pathCells.Length, Is.GreaterThan(20), lane.id);
                Assert.That(lane.pathCells[0], Is.EqualTo(descriptor.baseA), lane.id);
                Assert.That(lane.pathCells[lane.pathCells.Length - 1], Is.EqualTo(descriptor.baseB), lane.id);
                Assert.That(lane.lengthWorld, Is.GreaterThan(0f), lane.id);
                Assert.That(lane.averageWidthWorld, Is.GreaterThan(0f), lane.id);
                Assert.That(lane.averageNavigationCost, Is.GreaterThanOrEqualTo(1f), lane.id);
                Assert.That(lane.averageSightlineWorld, Is.GreaterThan(0f), lane.id);
                Assert.That(lane.maximumSightlineWorld, Is.GreaterThanOrEqualTo(lane.averageSightlineWorld), lane.id);
                Assert.That(lane.terrainShares.Sum(item => item.share), Is.EqualTo(1f).Within(0.001f), lane.id);
                AssertScore(lane.suitability.lightTank, lane.id);
                AssertScore(lane.suitability.mediumTank, lane.id);
                AssertScore(lane.suitability.heavyTank, lane.id);
                AssertScore(lane.suitability.tankDestroyer, lane.id);
                AssertScore(lane.suitability.artillery, lane.id);
            }
        }

        [Test]
        public void RiverCrossing_ClassRecommendationsReflectMapFeatures()
        {
            TacticalLaneDescriptor[] lanes = AnalyzeRiverCrossing().lanes;
            TacticalLaneDescriptor north = lanes.Single(item => item.id == "lane_north");
            TacticalLaneDescriptor center = lanes.Single(item => item.id == "lane_center");
            TacticalLaneDescriptor south = lanes.Single(item => item.id == "lane_south");

            Assert.That(center.suitability.heavyTank, Is.GreaterThan(north.suitability.heavyTank));
            Assert.That(center.suitability.heavyTank, Is.GreaterThan(south.suitability.heavyTank));
            Assert.That(center.suitability.mediumTank, Is.GreaterThan(north.suitability.mediumTank));
            Assert.That(center.suitability.mediumTank, Is.GreaterThan(south.suitability.mediumTank));
            Assert.That(south.suitability.lightTank, Is.GreaterThan(center.suitability.lightTank));
            Assert.That(south.suitability.tankDestroyer, Is.GreaterThan(north.suitability.tankDestroyer));
            Assert.That(south.suitability.tankDestroyer, Is.GreaterThan(center.suitability.tankDestroyer));
        }

        [Test]
        public void Analysis_IsDeterministic()
        {
            TacticalMapSource source = TacticalMapDescriptorImporter.LoadSource();
            string first = JsonUtility.ToJson(TacticalMapAnalyzer.Analyze(source));
            string second = JsonUtility.ToJson(TacticalMapAnalyzer.Analyze(source));

            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void SourceHash_ChangesWhenTacticalGeometryChanges()
        {
            TacticalMapSource original = TacticalMapDescriptorImporter.LoadSource();
            TacticalMapSource changed = TacticalMapSource.Parse(JsonUtility.ToJson(original));
            string row = changed.rows.First(value => value.Contains('.'));
            int rowIndex = Array.IndexOf(changed.rows, row);
            int column = row.IndexOf('.');
            char[] cells = row.ToCharArray();
            cells[column] = 'b';
            changed.rows[rowIndex] = new string(cells);

            Assert.That(TacticalMapAnalyzer.ComputeSourceHash(changed),
                Is.Not.EqualTo(TacticalMapAnalyzer.ComputeSourceHash(original)));
        }

        [Test]
        public void GeneratedAssetAndDocument_MatchCurrentMapAnalysis()
        {
            TacticalMapDescriptor expected = AnalyzeRiverCrossing();
            TacticalMapDescriptorAsset asset = AssetDatabase.LoadAssetAtPath<TacticalMapDescriptorAsset>(
                TacticalMapDescriptorImporter.AssetPath);

            Assert.That(asset, Is.Not.Null, "Rebuild the tactical map analysis asset.");
            Assert.That(asset.Validate(), Is.Empty);
            Assert.That(asset.descriptor.sourceHash, Is.EqualTo(expected.sourceHash));
            Assert.That(File.ReadAllText(Path.GetFullPath(TacticalMapDescriptorImporter.DocumentPath)),
                Is.EqualTo(TacticalMapDescriptorImporter.BuildDocument(expected)));
        }

        private static TacticalMapDescriptor AnalyzeRiverCrossing()
        {
            return TacticalMapAnalyzer.Analyze(TacticalMapDescriptorImporter.LoadSource());
        }

        private static void AssertScore(float value, string laneId)
        {
            Assert.That(value, Is.InRange(0f, 1f), laneId);
        }
    }
}
