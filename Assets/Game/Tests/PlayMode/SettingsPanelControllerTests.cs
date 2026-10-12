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

        [SetUp]
        public void CapturePreviousValue()
        {
            previousMaster = PlayerSettingsStore.MasterVolume;
        }

        [TearDown]
        public void RestorePreviousValue()
        {
            PlayerSettingsStore.MasterVolume = previousMaster;
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

                controller.SetOpen(true);
                Assert.That(controller.IsOpen, Is.True);
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
