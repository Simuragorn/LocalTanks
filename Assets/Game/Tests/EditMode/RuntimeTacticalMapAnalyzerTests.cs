using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LocalTanks.Tests
{
    public sealed class RuntimeTacticalMapAnalyzerTests
    {
        [Test]
        public void LoadedScene_IsAnalyzedWithoutSerializedTacticalInput()
        {
            NavigationMap map = CreateMap(9, 7);
            map.ReplaceSceneObstacleMask(new[]
            {
                new Vector2Int(4, 0),
                new Vector2Int(4, 2),
                new Vector2Int(4, 4),
                new Vector2Int(4, 6)
            });
            CaptureBase baseA = CreateBase("A", TeamId.TeamA, map.CellToWorld(new Vector2Int(1, 3)));
            CaptureBase baseB = CreateBase("B", TeamId.TeamB, map.CellToWorld(new Vector2Int(7, 3)));
            GameObject bush = new GameObject("RuntimeBush");
            BoxCollider2D bushZone = bush.AddComponent<BoxCollider2D>();
            bushZone.isTrigger = true;
            bushZone.size = Vector2.one * 0.9f;
            bush.transform.position = map.CellToWorld(new Vector2Int(2, 1));
            bush.AddComponent<ConcealmentZone>();

            try
            {
                RuntimeTacticalAnalysisResult result = RuntimeTacticalMapAnalyzer.Analyze(
                    map,
                    SceneManager.GetActiveScene(),
                    new[] { baseA, baseB },
                    "unknown_test_map");

                Assert.That(result.Report.WasAnalyzed, Is.True);
                Assert.That(result.Report.CellCount, Is.EqualTo(63));
                Assert.That(result.Report.ConcealmentZoneCount, Is.EqualTo(1));
                Assert.That(result.Descriptor.mapId, Is.EqualTo("unknown_test_map"));
                Assert.That(result.Descriptor.baseA, Is.EqualTo(new Vector2Int(1, 3)));
                Assert.That(result.Descriptor.baseB, Is.EqualTo(new Vector2Int(7, 3)));
                Assert.That(result.Descriptor.lanes, Has.Length.EqualTo(3));
                Assert.That(result.Descriptor.lanes.Any(lane => lane.concealmentDensity > 0f), Is.True);
                Assert.That(result.Descriptor.lanes, Has.All.Matches<TacticalLaneDescriptor>(lane =>
                    lane.pathCells.Length >= 2 && lane.averageSightlineWorld > 0f));

                map.ReplaceSceneObstacleMask(new[]
                {
                    new Vector2Int(4, 0),
                    new Vector2Int(4, 2),
                    new Vector2Int(4, 3),
                    new Vector2Int(4, 4),
                    new Vector2Int(4, 6)
                });
                RuntimeTacticalAnalysisResult changed = RuntimeTacticalMapAnalyzer.Analyze(
                    map,
                    SceneManager.GetActiveScene(),
                    new[] { baseA, baseB },
                    "unknown_test_map");
                Assert.That(changed.Descriptor.sourceHash, Is.Not.EqualTo(result.Descriptor.sourceHash));
                Assert.That(changed.Descriptor.lanes, Has.Length.EqualTo(2),
                    "Changing current geometry must change the discovered strategic directions.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(bush);
                UnityEngine.Object.DestroyImmediate(baseA.gameObject);
                UnityEngine.Object.DestroyImmediate(baseB.gameObject);
                UnityEngine.Object.DestroyImmediate(map.gameObject);
            }
        }

        [Test]
        public void RepresentativeLanes_UseBothFlanksAndTheRealMiddle()
        {
            TacticalLaneDescriptor[] lanes = Enumerable.Range(0, 7)
                .Select(index => new TacticalLaneDescriptor
                {
                    id = $"lane_{index + 1:00}",
                    order = index,
                    pathCells = new[] { new Vector2Int(0, index), new Vector2Int(8, index) }
                })
                .ToArray();
            TacticalMapDescriptor descriptor = new TacticalMapDescriptor { lanes = lanes };

            Assert.That(RuntimeTacticalMapAnalyzer.SelectLane(descriptor, BattleLane.North).id,
                Is.EqualTo("lane_01"));
            Assert.That(RuntimeTacticalMapAnalyzer.SelectLane(descriptor, BattleLane.Center).id,
                Is.EqualTo("lane_04"));
            Assert.That(RuntimeTacticalMapAnalyzer.SelectLane(descriptor, BattleLane.South).id,
                Is.EqualTo("lane_07"));

            Vector2Int[] oriented = RuntimeTacticalMapAnalyzer.OrientFrom(
                lanes[3],
                new Vector2Int(8, 3));
            Assert.That(oriented[0], Is.EqualTo(new Vector2Int(8, 3)));
            Assert.That(oriented[oriented.Length - 1], Is.EqualTo(new Vector2Int(0, 3)));
        }

        private static CaptureBase CreateBase(string id, TeamId owner, Vector2 position)
        {
            GameObject root = new GameObject($"Base_{id}");
            CircleCollider2D zone = root.AddComponent<CircleCollider2D>();
            zone.isTrigger = true;
            root.transform.position = position;
            CaptureBase captureBase = root.AddComponent<CaptureBase>();
            captureBase.Configure(id, id, owner, null, null);
            return captureBase;
        }

        private static NavigationMap CreateMap(int width, int height)
        {
            int count = width * height;
            TerrainKind[] terrain = new TerrainKind[count];
            float[] costs = Enumerable.Repeat(1f, count).ToArray();
            float[] speeds = Enumerable.Repeat(1f, count).ToArray();
            float[] accelerations = Enumerable.Repeat(1f, count).ToArray();
            bool[] blocked = new bool[count];
            GameObject root = new GameObject("RuntimeTacticalMap");
            NavigationMap map = root.AddComponent<NavigationMap>();
            map.Configure(width, height, 1f, Vector2.zero, terrain, costs, speeds, accelerations, blocked);
            return map;
        }
    }
}
