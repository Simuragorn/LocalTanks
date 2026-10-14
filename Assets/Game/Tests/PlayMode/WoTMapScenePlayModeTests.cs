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
        private const string SceneName = "Battle_10_hills";

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
            Assert.That(map.Width, Is.EqualTo(builder.Map.navigationColumns));
            Assert.That(map.Height, Is.EqualTo(builder.Map.navigationRows));
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
            Assert.That(Object.FindAnyObjectByType<NavigationDebugOverlay>(), Is.Not.Null,
                "Without this overlay F2 cannot show the navigation grid.");
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
