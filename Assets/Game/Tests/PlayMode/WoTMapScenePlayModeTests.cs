using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LocalTanks.Tests
{
    /// <summary>
    /// The converted arena is loaded as data and its colliders are built at load time, so these checks
    /// are the only thing standing between a missing map asset and a battle scene with no obstacles in
    /// it. The scene is skipped when the local map asset has not been imported.
    /// </summary>
    public sealed class WoTMapScenePlayModeTests
    {
        private const string SceneName = "Battle_Mines";

        private static bool SceneExists =>
            SceneUtility.GetScenePathByBuildIndex(SceneManager.sceneCountInBuildSettings - 1) != null &&
            Enumerable.Range(0, SceneManager.sceneCountInBuildSettings)
                .Any(index => SceneUtility.GetScenePathByBuildIndex(index).EndsWith($"/{SceneName}.unity"));

        [UnityTest]
        public IEnumerator ConvertedArena_BuildsItsContentOnLoad()
        {
            if (!SceneExists) Assert.Ignore($"Scene '{SceneName}' is not in the build settings yet.");

            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            WoTMapContentBuilder builder = Object.FindAnyObjectByType<WoTMapContentBuilder>();
            Assert.That(builder, Is.Not.Null, "The scene must carry the content builder.");
            Assert.That(builder.Map, Is.Not.Null, "The builder must reference the imported map asset.");

            Assert.That(builder.BuiltObstacles, Is.GreaterThan(1000),
                "The arena carries thousands of obstacles; a handful means the asset or the builder is wrong.");
            Assert.That(builder.BuiltPlants, Is.GreaterThan(500));

            NavigationMap map = Object.FindAnyObjectByType<NavigationMap>();
            Assert.That(map, Is.Not.Null, "The scene must carry a navigation map.");
            WoTNavigationMapSource source = map.GetComponent<WoTNavigationMapSource>();
            Assert.That(source, Is.Not.Null, "The scene must load navigation from the generated map asset.");
            Assert.That(source.Source, Is.SameAs(builder.Map),
                "Collider content and navigation must read the same map asset.");
            Assert.That(map.Width, Is.EqualTo(builder.Map.navigationColumns));
            Assert.That(map.Height, Is.EqualTo(builder.Map.navigationRows));
            BattleDirector director = Object.FindAnyObjectByType<BattleDirector>();
            Assert.That(director, Is.Not.Null);
            Assert.That(director.NavigationBakeReport.ColliderCount, Is.GreaterThan(1000),
                "Runtime navigation must inspect the actual arena colliders before tanks spawn.");
            Assert.That(director.NavigationBakeReport.BlockedCellCount, Is.GreaterThan(1000));
            Assert.That(director.TacticalAnalysisReport.WasAnalyzed, Is.True,
                "Bots must discover tactical lanes from the freshly baked runtime grid.");
            Assert.That(director.PreBattleAnalysisMilliseconds, Is.LessThan(1000),
                "Runtime analysis should finish during ordinary scene loading without a countdown.");
            Assert.That(director.TacticalMap.mapId, Is.EqualTo("Battle_Mines"));
            Assert.That(director.AudioDirector.HasProfile, Is.True,
                "The scene ID used by runtime analysis must resolve the Mines audio profile.");
            Assert.That(Object.FindObjectsByType<CaptureBase>(FindObjectsSortMode.None).Length,
                Is.EqualTo(2), "The arena has two bases.");
        }

        /// <summary>
        /// Vegetation is cover, so it must not stop a tank or a shell. A standing tree used to carry a
        /// solid collider because it can be knocked down, which made forests block both movement and
        /// shooting - the one thing cover must never do.
        /// </summary>
        [UnityTest]
        public IEnumerator ConvertedArena_VegetationNeverBlocksMovementOrFire()
        {
            if (!SceneExists) Assert.Ignore($"Scene '{SceneName}' is not in the build settings yet.");

            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            WoTMapContentBuilder builder = Object.FindAnyObjectByType<WoTMapContentBuilder>();
            Assert.That(builder, Is.Not.Null);

            GameObject vegetation = GameObject.Find("Vegetation");
            Assert.That(vegetation, Is.Not.Null, "The arena must group its plants under one root.");

            System.Collections.Generic.List<Collider2D> solid = vegetation
                .GetComponentsInChildren<Collider2D>(true)
                .Where(collider => !collider.isTrigger)
                .ToList();
            Assert.That(solid, Is.Empty,
                $"{solid.Count} plants carry a solid collider, so they stop tanks and shells. " +
                $"First offenders: {string.Join(", ", solid.Take(5).Select(item => item.name))}");

            Assert.That(vegetation.GetComponentsInChildren<ConcealmentZone>(true).Length,
                Is.EqualTo(builder.BuiltPlants), "Every plant must conceal; that is all it does.");
        }

        /// <summary>
        /// The developer keys must work on every arena. The converted map originally carried only vision
        /// and the hidden-unit setting, so F4 (AI inspector), F5 (time acceleration) and the roster were
        /// all dead here while they worked everywhere else. This checks the whole documented set.
        /// </summary>
        [UnityTest]
        public IEnumerator ConvertedArena_CarriesTheDeveloperOverlays()
        {
            if (!SceneExists) Assert.Ignore($"Scene '{SceneName}' is not in the build settings yet.");

            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            Assert.That(Object.FindAnyObjectByType<CombatAiDebugOverlay>(), Is.Not.Null,
                "Without this overlay F4 and Tab do nothing.");
            Assert.That(Object.FindAnyObjectByType<DeveloperTimeScaleController>(), Is.Not.Null,
                "Without this overlay F5 cannot accelerate time.");
            Assert.That(Object.FindAnyObjectByType<VisionDebugOverlay>(), Is.Not.Null,
                "Without this overlay F3 cannot show the vision model.");
            Assert.That(Object.FindAnyObjectByType<BattleRoster>(), Is.Not.Null,
                "Without a roster the AI inspector has no bots to switch between.");
            BattleDirector director = Object.FindAnyObjectByType<BattleDirector>();
            Assert.That(director, Is.Not.Null);
            Assert.That(director.DiagnosticsOverlay, Is.Not.Null,
                "Without the diagnostics overlay F1 shows no help and F6 no map analysis.");
            NavigationDebugOverlay navigation = Object.FindAnyObjectByType<NavigationDebugOverlay>();
            Assert.That(navigation, Is.Not.Null,
                "Without this overlay F2 cannot show the navigation grid.");
            Assert.That(navigation.Visible, Is.False,
                "A playable map must wait for F2 before showing navigation.");
        }

        /// <summary>
        /// The deterministic team plan is installed just after each bot receives its connected lane route.
        /// It may describe the same broad direction, but it must not replace the next route leg: doing so
        /// skips the early lane anchors, then sends the whole team back to them when the 60-second decision
        /// expires. That produced a synchronized retreat before either side had made contact.
        /// </summary>
        [UnityTest]
        public IEnumerator ConvertedArena_InitialStrategyPreservesUnfinishedDeploymentRoutes()
        {
            if (!SceneExists) Assert.Ignore($"Scene '{SceneName}' is not in the build settings yet.");

            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            BattleDirector director = Object.FindAnyObjectByType<BattleDirector>();
            Assert.That(director, Is.Not.Null);
            Assert.That(director.StrategicCoordinator, Is.Not.Null);
            Assert.That(director.StrategicCoordinator.Brains, Has.All.Matches<BotStrategicBrain>(brain =>
                brain.CurrentDecision != null), "Every bot should have received its deterministic plan.");

            foreach (CombatTankAI ai in Object.FindObjectsByType<CombatTankAI>(FindObjectsSortMode.None))
            {
                NavigationAgent agent = ai.GetComponent<NavigationAgent>();
                Assert.That(ai.RoutePoints.Length, Is.GreaterThan(1), $"{ai.name} has no deployment route.");
                Assert.That(agent, Is.Not.Null, $"{ai.name} has no navigation agent.");
                Assert.That(agent.MovementPaused, Is.False,
                    $"{ai.name} was held at spawn by a strategic Wait before finishing deployment.");
                Assert.That(Vector2.Distance(agent.Destination, ai.RoutePoints[ai.RouteIndex]), Is.LessThan(0.1f),
                    $"{ai.name} skipped its current deployment leg for the initial strategic fallback.");
            }
        }

        [UnityTest]
        public IEnumerator ConvertedArena_BotsAreEvenlySplitAcrossOperationalDirections()
        {
            if (!SceneExists) Assert.Ignore($"Scene '{SceneName}' is not in the build settings yet.");

            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            BattleDirector director = Object.FindAnyObjectByType<BattleDirector>();
            Assert.That(director, Is.Not.Null);
            string[] operational = RuntimeTacticalMapAnalyzer.SelectOperationalLanes(director.TacticalMap)
                .Select(item => item.id)
                .ToArray();
            Assert.That(operational, Has.Length.EqualTo(3));

            foreach (TeamId team in new[] { TeamId.TeamA, TeamId.TeamB })
            {
                BotStrategicBrain[] teamBots = director.StrategicCoordinator.Brains
                    .Where(brain => brain.GetComponent<TeamMember>().Team == team)
                    .ToArray();
                int[] counts = operational.Select(laneId => teamBots.Count(brain =>
                        brain.CurrentSnapshot?.plan?.assignedLaneId == laneId))
                    .ToArray();
                foreach (BotStrategicBrain brain in teamBots.OrderBy(item => item.BotId, System.StringComparer.Ordinal))
                {
                    CombatTankAI ai = brain.TacticalAi;
                    Debug.Log($"Lane route {team}/{brain.BotId}: {ai.Lane}; " +
                              string.Join(" -> ", ai.RoutePoints.Select(point => point.ToString("F1"))));
                    Assert.That(ai.RoutePoints.Length, Is.GreaterThanOrEqualTo(4),
                        $"{brain.BotId} lost the mandatory bends of its {ai.Lane} route.");
                }
                Assert.That(counts, Has.All.GreaterThan(0),
                    $"{team} left an operational direction empty: {string.Join("/", counts)}.");
                Assert.That(counts.Max() - counts.Min(), Is.LessThanOrEqualTo(1),
                    $"{team} is concentrated on one route: {string.Join("/", counts)}.");

                Vector2[] laneCentres = System.Enum.GetValues(typeof(BattleLane)).Cast<BattleLane>()
                    .Select(lane => teamBots.First(brain => brain.TacticalAi.Lane == lane).TacticalAi.RoutePoints)
                    .Select(route => route[route.Length / 2])
                    .ToArray();
                for (int first = 0; first < laneCentres.Length; first++)
                for (int second = first + 1; second < laneCentres.Length; second++)
                {
                    Assert.That(Vector2.Distance(laneCentres[first], laneCentres[second]),
                        Is.GreaterThan(director.NavigationMap.CellSize * 2f),
                        $"{team} routes {first} and {second} collapse onto the same physical corridor.");
                }
            }
        }

        [UnityTest]
        public IEnumerator ConvertedArena_LaneGroupsPhysicallyDivergeDuringDeployment()
        {
            if (!SceneExists) Assert.Ignore($"Scene '{SceneName}' is not in the build settings yet.");

            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            BattleDirector director = Object.FindAnyObjectByType<BattleDirector>();
            Assert.That(director, Is.Not.Null);
            float previousScale = Time.timeScale;
            try
            {
                Time.timeScale = 5f;
                while (director.SimulationTime < 40f) yield return null;
            }
            finally
            {
                Time.timeScale = previousScale;
            }

            foreach (TeamId team in new[] { TeamId.TeamA, TeamId.TeamB })
            {
                CombatTankAI[] bots = director.SpawnedMembers
                    .Where(member => member != null && member.Team == team && !member.IsPlayerControlled)
                    .Select(member => member.GetComponent<CombatTankAI>())
                    .Where(ai => ai != null)
                    .ToArray();
                Vector2[] centres = System.Enum.GetValues(typeof(BattleLane)).Cast<BattleLane>()
                    .Select(lane => bots.Where(ai => ai.Lane == lane)
                        .Aggregate(Vector2.zero, (sum, ai) => sum + (Vector2)ai.transform.position) /
                        bots.Count(ai => ai.Lane == lane))
                    .ToArray();
                Debug.Log($"Lane centres at 40 s for {team}: " +
                          string.Join(" | ", centres.Select(point => point.ToString("F1"))));
                for (int first = 0; first < centres.Length; first++)
                for (int second = first + 1; second < centres.Length; second++)
                {
                    Assert.That(Vector2.Distance(centres[first], centres[second]),
                        Is.GreaterThan(director.NavigationMap.CellSize * 2f),
                        $"{team} lane groups {first} and {second} still form one convoy after 40 seconds.");
                }
            }
        }

        /// <summary>
        /// A capture base marks ground a tank must be able to occupy. CaptureBase carries
        /// RequireComponent for its zone, and a required collider is created solid, which once made each
        /// base a wall the size of its ring and trapped tanks inside their own spawn.
        /// </summary>
        [UnityTest]
        public IEnumerator ConvertedArena_CaptureZonesDoNotStopTanks()
        {
            if (!SceneExists) Assert.Ignore($"Scene '{SceneName}' is not in the build settings yet.");

            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            CaptureBase[] bases = Object.FindObjectsByType<CaptureBase>(FindObjectsSortMode.None);
            Assert.That(bases.Length, Is.EqualTo(2));

            foreach (CaptureBase captureBase in bases)
            {
                Collider2D zone = captureBase.GetComponent<Collider2D>();
                Assert.That(zone, Is.Not.Null, $"Base {captureBase.BaseId} must have a capture zone.");
                Assert.That(zone.isTrigger, Is.True,
                    $"Base {captureBase.BaseId} has a solid capture zone, so tanks cannot drive onto it.");
            }
        }

        [UnityTest]
        public IEnumerator ConvertedArena_HasGroundAndCollidersInTheWorld()
        {
            if (!SceneExists) Assert.Ignore($"Scene '{SceneName}' is not in the build settings yet.");

            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            // The ground must be drawn, otherwise the arena renders as an empty void. It is one sprite
            // carrying the baked ground rather than a tilemap: a tilemap of navigation cells was a debugging
            // view, and drawn over the map it showed a field of dark squares.
            GameObject ground = GameObject.Find("Ground");
            Assert.That(ground, Is.Not.Null, "The arena needs a ground object.");
            SpriteRenderer[] groundSprites = ground.GetComponentsInChildren<SpriteRenderer>(true);
            Assert.That(groundSprites.Length, Is.GreaterThan(0),
                "The ground must carry its surface sprite, or the arena renders as an empty void.");
            Assert.That(groundSprites.Any(item => item.sprite != null), Is.True,
                "The ground sprite must actually have a texture assigned.");

            Collider2D[] colliders = Object.FindObjectsByType<Collider2D>(FindObjectsSortMode.None);
            Assert.That(colliders.Length, Is.GreaterThan(3000),
                "Obstacles and plants must become colliders in the world.");

            // Every collider must be inside the playable field, because a double coordinate conversion
            // once pushed the whole arena half a field away from the camera.
            WoTMapAsset asset = Object.FindAnyObjectByType<WoTMapContentBuilder>().Map;
            float limit = asset.playableSize.x * 0.5f + 20f;
            int outside = colliders.Count(item =>
                Mathf.Abs(item.bounds.center.x) > limit || Mathf.Abs(item.bounds.center.y) > limit);
            Assert.That(outside, Is.Zero,
                $"{outside} colliders sit outside the playable field, so the arena is offset.");
        }
        [UnityTest]
        public IEnumerator ConvertedArena_DrawsItsEnvironmentArt()
        {
            if (!SceneExists) Assert.Ignore($"Scene '{SceneName}' is not in the build settings yet.");

            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            WoTMapContentBuilder builder = Object.FindAnyObjectByType<WoTMapContentBuilder>();
            Assert.That(builder, Is.Not.Null);

            SpriteRenderer[] renderers = Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
            Assert.That(renderers.Length, Is.GreaterThan(100),
                "The arena must draw environment art, not only colliders.");

            // Art must be scaled to the footprint it stands on. A sprite left at its drawn size would show
            // a building at its library scale, which is metres away from the arena scale.
            WoTMapObstacle[] obstacles = builder.Map.obstacles;
            int outside = renderers.Count(renderer =>
            {
                Bounds bounds = renderer.bounds;
                float limit = builder.Map.playableSize.x * 0.5f + 40f;
                return Mathf.Abs(bounds.center.x) > limit || Mathf.Abs(bounds.center.y) > limit;
            });
            Assert.That(outside, Is.Zero,
                $"{outside} sprites are drawn outside the playable field, so the art is offset from the map.");

            Assert.That(obstacles.Any(item => item.blocksMovement), Is.True);
            Debug.Log($"Arena art: {renderers.Length} sprite renderers for {builder.Map.ObstacleCount} obstacles " +
                      $"and {builder.Map.VegetationCount} plants.");
        }
    }
}
