using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LocalTanks.Tests
{
    /// <summary>
    /// Unseen tanks are invisible in a normal battle; the semi-transparent silhouette is a per-scene
    /// developer aid. The main scene carries the value as data, and each laboratory carries it on its
    /// own controller, because the generated laboratories are copies of the main scene and must not
    /// silently inherit the battle default.
    /// </summary>
    public sealed class BattleVisibilitySceneSettingsTests
    {
        private const string RiverCrossingScene = "Assets/Game/Scenes/Battle_RiverCrossing.unity";
        private const string AiDuelLabScene = "Assets/Game/Scenes/Battle_AiDuelLab.unity";
        private const string PlayerAiTestLabScene = "Assets/Game/Scenes/Battle_PlayerAiTestLab.unity";

        [Test]
        public void RiverCrossing_HidesUnseenTanks()
        {
            using (SceneScope scope = SceneScope.Open(RiverCrossingScene))
            {
                // A scene without an explicit opt-in keeps the production behaviour: hidden tanks.
                BattleVisibilitySettings settings = Find<BattleVisibilitySettings>(scope.Scene);
                if (settings != null)
                {
                    Assert.That(settings.ShowHiddenTanksForTesting, Is.False,
                        "A normal battle must not keep unseen tanks rendered as a silhouette.");
                }

                Assert.That(Find<AiDuelLabController>(scope.Scene), Is.Null);
                Assert.That(Find<PlayerAiTestLabController>(scope.Scene), Is.Null);
            }
        }

        [Test]
        public void AiDuelLab_KeepsTheDebugSilhouette()
        {
            using (SceneScope scope = SceneScope.Open(AiDuelLabScene))
            {
                AiDuelLabController controller = Find<AiDuelLabController>(scope.Scene);
                Assert.That(controller, Is.Not.Null);
                Assert.That(controller.ShowHiddenTanksForTesting, Is.True,
                    "The duel laboratory must keep the silhouette so enemy movement stays observable.");
            }
        }

        [Test]
        public void PlayerAiTestLab_KeepsTheDebugSilhouette()
        {
            using (SceneScope scope = SceneScope.Open(PlayerAiTestLabScene))
            {
                PlayerAiTestLabController controller = Find<PlayerAiTestLabController>(scope.Scene);
                Assert.That(controller, Is.Not.Null);
                Assert.That(controller.ShowHiddenTanksForTesting, Is.True,
                    "The manual test laboratory must keep the silhouette for observation.");
            }
        }

        [Test]
        public void EveryBattleScene_ResolvesOneVisibilityOwner()
        {
            foreach (string path in new[] { RiverCrossingScene, AiDuelLabScene, PlayerAiTestLabScene })
            {
                using (SceneScope scope = SceneScope.Open(path))
                {
                    int settingsCount = scope.Scene.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<BattleVisibilitySettings>(true))
                        .Count();
                    int optInCount = scope.Scene.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<AiDuelLabController>(true))
                        .Count()
                        + scope.Scene.GetRootGameObjects()
                            .SelectMany(root => root.GetComponentsInChildren<PlayerAiTestLabController>(true))
                            .Count();

                    Assert.That(settingsCount + optInCount, Is.LessThanOrEqualTo(1),
                        $"Scene '{path}' must not stack a scene setting and a laboratory opt-in.");
                }
            }
        }

        private static T Find<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .FirstOrDefault();
        }

        private readonly struct SceneScope : System.IDisposable
        {
            private SceneScope(Scene scene)
            {
                Scene = scene;
            }

            public Scene Scene { get; }

            public static SceneScope Open(string path)
            {
                return new SceneScope(EditorSceneManager.OpenScene(path, OpenSceneMode.Additive));
            }

            public void Dispose()
            {
                if (Scene.IsValid() && Scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(Scene, true);
                }
            }
        }
    }
}
