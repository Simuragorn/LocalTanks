using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace LocalTanks.Tests
{
    public sealed class BattleChatAndResultTests
    {
        private const string UxmlPath = "Assets/Game/UI/BattleHud.uxml";

        [UnityTest]
        public IEnumerator ChatSwitchesAudienceAndOnlyAddsPlayerMessages()
        {
            using PanelFixture panel = CreatePanel();
            BattleChatController chat = panel.Host.AddComponent<BattleChatController>();
            yield return null;
            chat.Configure(panel.Document.rootVisualElement);

            Assert.That(chat.Messages, Is.Empty);
            Assert.That(chat.SendText("Держим центр"), Is.True);
            Assert.That(chat.Messages[0], Is.EqualTo("[Команда] Игрок: Держим центр"));
            chat.SetChannel(BattleChatChannel.All);
            Assert.That(chat.SendText("gg"), Is.True);
            Assert.That(chat.Messages[1], Is.EqualTo("[Всем] Игрок: gg"));
            Assert.That(chat.SendText("   "), Is.False);
            Assert.That(chat.Messages, Has.Count.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator ResultPanelShowsVictoryDefeatAndDraw()
        {
            using PanelFixture panel = CreatePanel();
            BattleResultPanelController result = panel.Host.AddComponent<BattleResultPanelController>();
            yield return null;
            result.Configure(null, null, panel.Document.rootVisualElement);

            result.Show(new MatchResult(MatchOutcome.TeamWipedOut, TeamId.TeamA, 10f), TeamId.TeamA);
            Assert.That(result.IsVisible, Is.True);
            Assert.That(result.Title, Is.EqualTo("ПОБЕДА"));

            result.Show(new MatchResult(MatchOutcome.BaseCaptured, TeamId.TeamB, 20f), TeamId.TeamA);
            Assert.That(result.Title, Is.EqualTo("ПОРАЖЕНИЕ"));

            result.Show(new MatchResult(MatchOutcome.TimeLimit, TeamId.Neutral, 30f), TeamId.TeamA);
            Assert.That(result.Title, Is.EqualTo("НИЧЬЯ"));
        }

        [Test]
        public void ResultPanelMapsEnterAndEscapeToVisibleActions()
        {
            Assert.That(BattleResultPanelController.ResolveShortcut(true, false),
                Is.EqualTo(BattleResultCommand.Repeat));
            Assert.That(BattleResultPanelController.ResolveShortcut(false, true),
                Is.EqualTo(BattleResultCommand.MainMenu));
            Assert.That(BattleResultPanelController.ResolveShortcut(false, false),
                Is.EqualTo(BattleResultCommand.None));
        }

        [UnityTest]
        public IEnumerator HiddenSettingsContainerDoesNotCoverBattleButtons()
        {
            using PanelFixture panel = CreatePanel();
            panel.Host.AddComponent<BattleHudController>();
            yield return null;
            yield return null;

            VisualElement settingsContainer = panel.Document.rootVisualElement.Q<VisualElement>("settings-instance");
            Assert.That(settingsContainer, Is.Not.Null);
            Assert.That(settingsContainer.pickingMode, Is.EqualTo(PickingMode.Ignore),
                "The full-screen settings template must not intercept clicks while its overlay is hidden.");
        }

        private static PanelFixture CreatePanel()
        {
            PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            GameObject host = new GameObject("BattleUiTest");
            UIDocument document = host.AddComponent<UIDocument>();
            document.panelSettings = settings;
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            return new PanelFixture(host, document, settings);
        }

        private sealed class PanelFixture : System.IDisposable
        {
            public PanelFixture(GameObject host, UIDocument document, PanelSettings settings)
            {
                Host = host;
                Document = document;
                Settings = settings;
            }

            public GameObject Host { get; }
            public UIDocument Document { get; }
            private PanelSettings Settings { get; }

            public void Dispose()
            {
                Object.DestroyImmediate(Host);
                Object.DestroyImmediate(Settings);
            }
        }
    }
}
