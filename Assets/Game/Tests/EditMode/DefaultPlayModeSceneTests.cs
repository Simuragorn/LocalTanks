using LocalTanks.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace LocalTanks.Tests
{
    public sealed class DefaultPlayModeSceneTests
    {
        [Test]
        public void EnsureMainMenuWhenUnset_AssignsMenuButPreservesExplicitSelection()
        {
            SceneAsset previous = EditorSceneManager.playModeStartScene;
            SceneAsset mainMenu = AssetDatabase.LoadAssetAtPath<SceneAsset>(DefaultPlayModeScene.MainMenuPath);
            SceneAsset explicitScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(
                "Assets/Game/Scenes/Labs/Battle_TestRange.unity");
            Assert.That(mainMenu, Is.Not.Null);
            Assert.That(explicitScene, Is.Not.Null);

            try
            {
                EditorSceneManager.playModeStartScene = null;
                DefaultPlayModeScene.EnsureMainMenuWhenUnset();
                Assert.That(EditorSceneManager.playModeStartScene, Is.SameAs(mainMenu));

                EditorSceneManager.playModeStartScene = explicitScene;
                DefaultPlayModeScene.EnsureMainMenuWhenUnset();
                Assert.That(EditorSceneManager.playModeStartScene, Is.SameAs(explicitScene));
            }
            finally
            {
                EditorSceneManager.playModeStartScene = previous;
            }
        }
    }
}
