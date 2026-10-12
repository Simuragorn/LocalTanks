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

        [UnityTest]
        public IEnumerator ConvertedArena_HasGroundAndCollidersInTheWorld()
        {
            if (!SceneExists) Assert.Ignore($"Scene '{SceneName}' is not in the build settings yet.");

            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            // The ground sheet must exist, otherwise the arena renders as an empty void.
            Assert.That(Object.FindObjectsByType<UnityEngine.Tilemaps.Tilemap>(FindObjectsSortMode.None).Length,
                Is.GreaterThan(0), "The arena needs a ground sheet.");

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
