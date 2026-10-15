using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace LocalTanks.Tests
{
    public sealed class MainMenuControllerTests
    {
        private const string UxmlPath = "Assets/Game/UI/MainMenu.uxml";

        [UnityTest]
        public IEnumerator TeamEditors_ChangeCountsAndApplyOneTankToAWholeTeam()
        {
            PanelSettings panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            CombatDatabase database = ScriptableObject.CreateInstance<CombatDatabase>();
            TankDefinition first = Tank("first", "Первый");
            TankDefinition second = Tank("second", "Второй");
            database.tanks = new[] { first, second };
            GameObject host = new GameObject("MainMenuTest");
            UIDocument document = host.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            MainMenuController controller = host.AddComponent<MainMenuController>();
            controller.Configure(database, new StyleSheet[0]);
            try
            {
                yield return null;
                yield return null;

                SliderInt teamACount = document.rootVisualElement.Q<SliderInt>("team-a-count");
                SliderInt teamBCount = document.rootVisualElement.Q<SliderInt>("team-b-count");
                DropdownField teamAWhole = document.rootVisualElement.Q<DropdownField>("team-a-whole");
                DropdownField teamBWhole = document.rootVisualElement.Q<DropdownField>("team-b-whole");
                Assert.That(teamACount, Is.Not.Null);
                Assert.That(teamBCount, Is.Not.Null);
                Assert.That(teamAWhole.choices, Has.Count.EqualTo(3));

                teamACount.value = 3;
                teamBCount.value = 2;
                teamAWhole.value = teamAWhole.choices.Find(item => item.StartsWith("Второй"));
                teamBWhole.value = teamBWhole.choices.Find(item => item.StartsWith("Первый"));
                yield return null;

                Assert.That(controller.SelectedTeamATanks, Is.EqualTo(new[] { "second", "second", "second" }));
                Assert.That(controller.SelectedTeamBTanks, Is.EqualTo(new[] { "first", "first" }));
            }
            finally
            {
                Object.Destroy(host);
                Object.Destroy(panelSettings);
                Object.Destroy(database);
                Object.Destroy(first);
                Object.Destroy(second);
            }
        }

        [UnityTest]
        public IEnumerator HiddenSettingsTemplate_DoesNotBlockMenuPointerPicking()
        {
            PanelSettings panelSettings = Object.Instantiate(
                AssetDatabase.LoadAssetAtPath<PanelSettings>(
                    "Assets/Game/Generated/UI/MainMenuPanelSettings.asset"));
            panelSettings.targetTexture = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32);
            CombatDatabase database = ScriptableObject.CreateInstance<CombatDatabase>();
            TankDefinition tank = Tank("first", "Первый");
            database.tanks = new[] { tank };
            GameObject host = new GameObject("MainMenuPickingTest");
            UIDocument document = host.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            MainMenuController controller = host.AddComponent<MainMenuController>();
            controller.Configure(database, new[]
            {
                AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Game/UI/LocalTanksTheme.uss"),
                AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Game/UI/MainMenu.uss"),
                AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Game/UI/SettingsOverlay.uss")
            });
            host.AddComponent<SettingsPanelController>().Configure(
                AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Game/UI/SettingsOverlay.uss"));

            try
            {
                yield return null;
                yield return null;

                VisualElement template = document.rootVisualElement.Q<VisualElement>("settings-instance");
                Button settings = document.rootVisualElement.Q<Button>("button-settings");
                Button play = document.rootVisualElement.Q<Button>("button-play");
                DropdownField map = document.rootVisualElement.Q<DropdownField>("map-choice");
                VisualElement mapInput = map.Q<VisualElement>(className: "unity-base-field__input");
                VisualElement mapShell = map.parent;
                Label readableMapText = mapShell.Q<Label>("lt-dropdown-readable-text");
                Assert.That(template.pickingMode, Is.EqualTo(PickingMode.Ignore));
                Assert.That(settings.worldBound.width, Is.GreaterThan(0f));
                Assert.That(play.worldBound.height, Is.GreaterThan(0f));
                Assert.That(mapInput, Is.Not.Null);
                Assert.That(readableMapText, Is.Not.Null);
                Assert.That(mapShell.name, Is.EqualTo("lt-dropdown-shell"));
                Assert.That(map.resolvedStyle.opacity, Is.Zero);
                Assert.That(mapShell.resolvedStyle.backgroundColor.r, Is.LessThan(0.35f));
                Assert.That(readableMapText.text, Is.EqualTo(map.value));
                Assert.That(readableMapText.worldBound.width, Is.GreaterThan(100f));
                Assert.That(readableMapText.worldBound.height, Is.GreaterThan(10f));
                AssertPickedInside(document, settings);
                AssertPickedInside(document, play);
                AssertPickedInside(document, map);
            }
            finally
            {
                Object.Destroy(host);
                panelSettings.targetTexture.Release();
                Object.Destroy(panelSettings.targetTexture);
                Object.Destroy(panelSettings);
                Object.Destroy(database);
                Object.Destroy(tank);
            }
        }

        private static void AssertPickedInside(UIDocument document, VisualElement expected)
        {
            VisualElement picked = document.rootVisualElement.panel.Pick(expected.worldBound.center);
            Assert.That(picked == expected || expected.Contains(picked), Is.True,
                $"Expected '{expected.name}' at {expected.worldBound.center}, but picked '{picked?.name}'.");
        }

        private static TankDefinition Tank(string id, string displayName)
        {
            TankDefinition tank = ScriptableObject.CreateInstance<TankDefinition>();
            tank.id = id;
            tank.displayName = displayName;
            tank.availableInGame = true;
            tank.tier = 1;
            return tank;
        }
    }
}
