using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace LocalTanks.Editor
{
    public static class MainMenuSceneBuilder
    {
        private const string PanelSettingsPath = "Assets/Game/Generated/UI/MainMenuPanelSettings.asset";
        private const string DatabasePath = "Assets/Game/GameData/Generated/CombatDatabase.asset";
        private const string UxmlPath = "Assets/Game/UI/MainMenu.uxml";
        private const string MenuUssPath = "Assets/Game/UI/MainMenu.uss";
        private const string ThemeUssPath = "Assets/Game/UI/LocalTanksTheme.uss";
        private const string SettingsUssPath = "Assets/Game/UI/SettingsOverlay.uss";

        [MenuItem("Local Tanks/Build Main Menu")]
        public static void Build()
        {
            EnsureFolder(BattleSceneLayout.FlowFolder);
            EnsureFolder("Assets/Game/Generated/UI");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            CombatDatabase database = AssetDatabase.LoadAssetAtPath<CombatDatabase>(DatabasePath);
            VisualTreeAsset visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            StyleSheet menuStyle = AssetDatabase.LoadAssetAtPath<StyleSheet>(MenuUssPath);
            StyleSheet themeStyle = AssetDatabase.LoadAssetAtPath<StyleSheet>(ThemeUssPath);
            StyleSheet settingsStyle = AssetDatabase.LoadAssetAtPath<StyleSheet>(SettingsUssPath);
            if (database == null || visualTree == null || menuStyle == null || themeStyle == null || settingsStyle == null)
                throw new InvalidOperationException("Main menu data or UI assets are missing.");

            PanelSettings panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panelSettings == null)
            {
                panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panelSettings, PanelSettingsPath);
            }
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            panelSettings.match = 0.5f;
            EditorUtility.SetDirty(panelSettings);

            Scene previous = SceneManager.GetActiveScene();
            bool preserve = previous.IsValid() && previous.isLoaded && !string.IsNullOrEmpty(previous.path);
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                preserve ? NewSceneMode.Additive : NewSceneMode.Single);
            scene.name = BattleSceneLayout.MainMenu;
            SceneManager.SetActiveScene(scene);
            try
            {
                GameObject root = new GameObject("MainMenuUI");
                UIDocument document = root.AddComponent<UIDocument>();
                document.panelSettings = panelSettings;
                document.visualTreeAsset = visualTree;
                document.sortingOrder = 100;
                MainMenuController menu = root.AddComponent<MainMenuController>();
                menu.Configure(database, new[] { themeStyle, menuStyle, settingsStyle });
                root.AddComponent<SettingsPanelController>().Configure(settingsStyle);

                string scenePath = BattleSceneLayout.Path(BattleSceneLayout.MainMenu);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, scenePath);
                PutFirstInBuildSettings(scenePath);
                AssetDatabase.SaveAssets();
                Debug.Log($"Local Tanks main menu built: {scenePath}");
            }
            finally
            {
                if (preserve && previous.IsValid() && previous.isLoaded)
                    SceneManager.SetActiveScene(previous);
                if (preserve && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void PutFirstInBuildSettings(string scenePath)
        {
            List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes
                .Where(item => item.path != scenePath)
                .ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent))
                throw new InvalidOperationException($"Cannot create folder '{path}'.");
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
