using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LocalTanks.Tests
{
    public sealed class SceneNavigationBakerTests
    {
        [Test]
        public void MovingASolidCollider_RebuildsOccupancyWithoutMapImport()
        {
            NavigationMap map = CreateMap(7, 4);
            GameObject rock = new GameObject("MovedRock");
            BoxCollider2D collider = rock.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(0.9f, 0.9f);
            rock.transform.position = map.CellToWorld(new Vector2Int(2, 1));

            try
            {
                SceneNavigationBakeReport first = SceneNavigationBaker.Rebuild(map, SceneManager.GetActiveScene());
                Assert.That(first.WasRebuilt, Is.True);
                Assert.That(map.IsBlocked(new Vector2Int(2, 1)), Is.True);

                rock.transform.position = map.CellToWorld(new Vector2Int(4, 2));
                SceneNavigationBaker.Rebuild(map, SceneManager.GetActiveScene());

                Assert.That(map.IsBlocked(new Vector2Int(2, 1)), Is.False,
                    "The old imported position must not remain blocked after the collider moves.");
                Assert.That(map.IsBlocked(new Vector2Int(4, 2)), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(rock);
                Object.DestroyImmediate(map.gameObject);
            }
        }

        [Test]
        public void TriggersAndVegetation_DoNotBecomeNavigationWalls()
        {
            NavigationMap map = CreateMap(5, 3);
            GameObject trigger = new GameObject("CaptureLikeTrigger");
            BoxCollider2D triggerCollider = trigger.AddComponent<BoxCollider2D>();
            triggerCollider.isTrigger = true;
            trigger.transform.position = map.CellToWorld(new Vector2Int(1, 1));
            GameObject bush = new GameObject("Bush");
            bush.transform.position = map.CellToWorld(new Vector2Int(3, 1));
            bush.AddComponent<BoxCollider2D>();
            bush.AddComponent<ConcealmentZone>();

            try
            {
                SceneNavigationBaker.Rebuild(map, SceneManager.GetActiveScene());

                Assert.That(map.IsBlocked(new Vector2Int(1, 1)), Is.False);
                Assert.That(map.IsBlocked(new Vector2Int(3, 1)), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(trigger);
                Object.DestroyImmediate(bush);
                Object.DestroyImmediate(map.gameObject);
            }
        }

        [Test]
        public void BlockedLaneHint_IsReplacedByAConnectedWalkableRoute()
        {
            NavigationMap map = CreateMap(7, 3);
            map.ReplaceSceneObstacleMask(new[]
            {
                new Vector2Int(3, 1),
                new Vector2Int(3, 2)
            });

            try
            {
                Vector2[] route = SceneNavigationBaker.BuildConnectedRoute(
                    map,
                    new[] { new Vector2Int(0, 1), new Vector2Int(3, 1), new Vector2Int(6, 1) },
                    map.CellToWorld(new Vector2Int(0, 1)),
                    map.CellToWorld(new Vector2Int(6, 1)));

                Assert.That(route, Has.Length.GreaterThanOrEqualTo(2));
                foreach (Vector2 point in route)
                {
                    Assert.That(map.IsBlocked(map.WorldToCell(point)), Is.False);
                }

                for (int index = 1; index < route.Length; index++)
                {
                    List<Vector2Int> leg = map.FindCellPath(
                        map.WorldToCell(route[index - 1]),
                        map.WorldToCell(route[index]));
                    Assert.That(leg, Is.Not.Empty, $"Route leg {index - 1}->{index} is disconnected.");
                }
            }
            finally
            {
                Object.DestroyImmediate(map.gameObject);
            }
        }

        [Test]
        public void LaneEndpoints_DoNotPullSpreadSpawnBackThroughOwnBase()
        {
            NavigationMap map = CreateMap(20, 3);
            Vector2Int[] lane =
            {
                new Vector2Int(1, 1),
                new Vector2Int(3, 1),
                new Vector2Int(5, 1),
                new Vector2Int(7, 1),
                new Vector2Int(9, 1),
                new Vector2Int(11, 1),
                new Vector2Int(13, 1),
                new Vector2Int(15, 1),
                new Vector2Int(17, 1)
            };

            try
            {
                Vector2[] route = SceneNavigationBaker.BuildConnectedRoute(
                    map,
                    lane,
                    map.CellToWorld(new Vector2Int(3, 1)),
                    map.CellToWorld(new Vector2Int(19, 1)));

                Assert.That(route, Has.Length.GreaterThanOrEqualTo(2));
                for (int index = 1; index < route.Length; index++)
                {
                    Assert.That(route[index].x, Is.GreaterThan(route[index - 1].x),
                        $"Route leg {index - 1}->{index} turns a departing tank back towards its own base.");
                }
            }
            finally
            {
                Object.DestroyImmediate(map.gameObject);
            }
        }

        [Test]
        public void SparseLaneAnchors_ArePreservedWhenTheirDeltasShareADirection()
        {
            NavigationMap map = CreateMap(30, 30);
            Vector2Int[] lane =
            {
                new Vector2Int(1, 1),
                new Vector2Int(2, 4),
                new Vector2Int(4, 8),
                new Vector2Int(7, 9),
                new Vector2Int(9, 12),
                new Vector2Int(12, 15),
                new Vector2Int(15, 18),
                new Vector2Int(18, 21),
                new Vector2Int(22, 24)
            };

            try
            {
                Vector2[] route = SceneNavigationBaker.BuildConnectedRoute(
                    map,
                    lane,
                    map.CellToWorld(new Vector2Int(1, 1)),
                    map.CellToWorld(new Vector2Int(27, 27)));

                Assert.That(route.Select(map.WorldToCell), Is.EqualTo(new[]
                {
                    new Vector2Int(1, 1),
                    lane[2],
                    lane[4],
                    lane[6],
                    new Vector2Int(27, 27)
                }), "Sparse tactical bends must not be treated as adjacent collinear A* cells.");
            }
            finally
            {
                Object.DestroyImmediate(map.gameObject);
            }
        }

        private static NavigationMap CreateMap(int width, int height)
        {
            int count = width * height;
            TerrainKind[] terrain = new TerrainKind[count];
            float[] costs = new float[count];
            float[] speeds = new float[count];
            float[] accelerations = new float[count];
            bool[] blocked = new bool[count];
            for (int index = 0; index < count; index++)
            {
                costs[index] = 1f;
                speeds[index] = 1f;
                accelerations[index] = 1f;
            }

            GameObject root = new GameObject("NavigationMap");
            NavigationMap map = root.AddComponent<NavigationMap>();
            map.Configure(width, height, 1f, Vector2.zero, terrain, costs, speeds, accelerations, blocked);
            return map;
        }
    }
}
