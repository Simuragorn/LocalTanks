using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace LocalTanks.Tests
{
    /// <summary>
    /// Drives the real settings panel: the sliders must reach <see cref="PlayerSettingsStore"/> and the
    /// panel must be able to hide itself, because a panel that renders but stores nothing would still
    /// look correct on screen.
    /// </summary>
    public sealed class SettingsPanelControllerTests
    {
        private const string SettingsOverlayPath = "Assets/Game/UI/SettingsOverlay.uxml";

        private float previousMaster;
        private float previousMusic;
        private CrewLanguageMode previousCrewLanguage;
        private CrewVoiceGender previousCrewGender;

        [SetUp]
        public void CapturePreviousValue()
        {
            previousMaster = PlayerSettingsStore.MasterVolume;
            previousMusic = PlayerSettingsStore.MusicVolume;
            previousCrewLanguage = PlayerSettingsStore.CrewLanguage;
            previousCrewGender = PlayerSettingsStore.CrewGender;
        }

        [TearDown]
        public void RestorePreviousValue()
        {
            PlayerSettingsStore.MasterVolume = previousMaster;
            PlayerSettingsStore.MusicVolume = previousMusic;
            PlayerSettingsStore.CrewLanguage = previousCrewLanguage;
            PlayerSettingsStore.CrewGender = previousCrewGender;
        }

        [UnityTest]
        public IEnumerator Slider_ChangesAreStored()
        {
            PanelSettings panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            GameObject host = new GameObject("SettingsPanelHost");
            UIDocument document = host.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(SettingsOverlayPath);
            SettingsPanelController controller = host.AddComponent<SettingsPanelController>();

            try
            {
                PlayerSettingsStore.MasterVolume = 1f;
                VisualElement root = null;
                for (int frame = 0; frame < 5 && root == null; frame++)
                {
                    yield return null;
                    root = document.rootVisualElement;
                }

                Assert.That(root, Is.Not.Null, "The settings document did not produce a visual tree.");

                Slider master = root.Q<Slider>("slider-master");
                Assert.That(master, Is.Not.Null, "The master slider is missing from the document.");

                // Read the stored value through the slider, which is what a player changing it does.
                master.value = 40f;
                yield return null;

                Assert.That(PlayerSettingsStore.MasterVolume, Is.EqualTo(0.4f).Within(0.001f),
                    "Moving the master slider must reach the settings store.");

                Label value = root.Q<Label>("value-master");
                Assert.That(value, Is.Not.Null);
                Assert.That(value.text, Is.EqualTo("40%"));

                // The music channel is a second bus with its own slider, and it must not be a decoration.
                Slider music = root.Q<Slider>("slider-music");
                Assert.That(music, Is.Not.Null, "The music slider is missing from the document.");
                music.value = 25f;
                yield return null;
                Assert.That(PlayerSettingsStore.MusicVolume, Is.EqualTo(0.25f).Within(0.001f),
                    "Moving the music slider must reach the settings store.");
                Label musicValue = root.Q<Label>("value-music");
                Assert.That(musicValue, Is.Not.Null);
                Assert.That(musicValue.text, Is.EqualTo("25%"));

                Slider voice = root.Q<Slider>("slider-voice");
                Assert.That(voice, Is.Not.Null, "The voice slider is missing from the document.");

                DropdownField language = root.Q<DropdownField>("crew-language");
                DropdownField gender = root.Q<DropdownField>("crew-gender");
                Assert.That(language, Is.Not.Null);
                Assert.That(gender, Is.Not.Null);
                language.value = "Русская";
                gender.value = "Женская";
                yield return null;
                Assert.That(PlayerSettingsStore.CrewLanguage, Is.EqualTo(CrewLanguageMode.Russian));
                Assert.That(PlayerSettingsStore.CrewGender, Is.EqualTo(CrewVoiceGender.Female));

                controller.SetOpen(true);
                Assert.That(controller.IsOpen, Is.True);
                controller.OpenGraphicsPlaceholder();
                Assert.That(controller.IsGraphicsPlaceholderOpen, Is.True,
                    "The graphics button leads to a deliberately empty placeholder window.");
                controller.SetOpen(false);
                Assert.That(controller.IsOpen, Is.False,
                    "Closing the panel must hide the overlay.");
            }
            finally
            {
                Object.Destroy(host);
                Object.Destroy(panelSettings);
            }

            yield return null;
        }
    }
}
