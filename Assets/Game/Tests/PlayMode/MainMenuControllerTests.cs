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
