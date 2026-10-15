using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LocalTanks.Editor
{
    /// <summary>
    /// Gives a fresh editor session the player-facing entry point without overriding an explicit
    /// Play Mode start scene selected by a developer for a map or laboratory.
    /// </summary>
    [InitializeOnLoad]
    public static class DefaultPlayModeScene
    {
        public const string MainMenuPath = "Assets/Game/Scenes/Flow/MainMenu.unity";

        static DefaultPlayModeScene()
        {
            if (!Application.isBatchMode)
            {
                EditorApplication.delayCall += EnsureMainMenuWhenUnset;
            }
        }

        public static void EnsureMainMenuWhenUnset()
        {
            if (EditorSceneManager.playModeStartScene != null)
            {
                return;
            }

            SceneAsset mainMenu = AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuPath);
            if (mainMenu != null)
            {
                EditorSceneManager.playModeStartScene = mainMenu;
            }
        }
    }
}
