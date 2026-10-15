using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LocalTanks.Tests
{
    /// <summary>
    /// The settings overlay appears in two places: the battle HUD document includes it so it shares
    /// that panel and draws above the roster, and the test range loads the overlay document directly
    /// because that scene has no HUD. Two entry points can drift apart, so both documents are
    /// instantiated here and checked for exactly the same controls.
    /// </summary>
    public sealed class SettingsPanelDocumentTests
    {
        private const string BattleHudPath = "Assets/Game/UI/BattleHud.uxml";
        private const string SettingsOverlayPath = "Assets/Game/UI/SettingsOverlay.uxml";
        private const string SettingsOverlayUssPath = "Assets/Game/UI/SettingsOverlay.uss";

        private static readonly string[] RequiredControls =
        {
            "settings-overlay", "slider-master", "slider-tank", "slider-effects", "slider-music", "slider-voice",
            "value-master", "value-tank", "value-effects", "value-music", "value-voice",
            "button-reset", "button-close"
        };

        [Test]
        public void BattleHud_ContainsTheFullSettingsOverlay()
        {
            VisualElement root = Instantiate(BattleHudPath);

            AssertControls(root, BattleHudPath);
            Assert.That(root.Q<VisualElement>("ally-list"), Is.Not.Null,
                "The battle HUD must keep its roster after gaining the settings overlay.");
            Assert.That(root.Q<VisualElement>("enemy-list"), Is.Not.Null);
        }

        [Test]
        public void SettingsOverlayDocument_ContainsTheSameControls()
        {
            VisualElement root = Instantiate(SettingsOverlayPath);

            AssertControls(root, SettingsOverlayPath);
        }

        [Test]
        public void SettingsOverlay_StartsHidden()
        {
            VisualElement overlay = Instantiate(SettingsOverlayPath).Q<VisualElement>("settings-overlay");

            Assert.That(overlay, Is.Not.Null);
            Assert.That(overlay.ClassListContains("hidden"), Is.True,
                "The panel must start hidden; it is opened by F7 or by the test range key.");
        }

        [Test]
        public void BothDocuments_ExposeTheSameSliderNames()
        {
            string[] hudSliders = SliderNames(Instantiate(BattleHudPath));
            string[] overlaySliders = SliderNames(Instantiate(SettingsOverlayPath));

            Assert.That(hudSliders, Is.EqualTo(overlaySliders),
                "The battle HUD and the standalone overlay must expose the same sliders.");
        }

        [Test]
        public void SettingsAssets_Exist()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(SettingsOverlayPath), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<StyleSheet>(SettingsOverlayUssPath), Is.Not.Null);
        }

        private static string[] SliderNames(VisualElement root)
        {
            return root.Query<Slider>().ToList()
                .Select(slider => slider.name)
                .Where(name => !string.IsNullOrEmpty(name) && name.StartsWith("slider-"))
                .OrderBy(name => name)
                .ToArray();
        }

        private static void AssertControls(VisualElement root, string path)
        {
            string[] missing = RequiredControls
                .Where(name => root.Q<VisualElement>(name) == null)
                .ToArray();
            Assert.That(missing, Is.Empty,
                $"'{path}' is missing settings controls: {string.Join(", ", missing)}");
        }

        private static VisualElement Instantiate(string uxmlPath)
        {
            VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            Assert.That(asset, Is.Not.Null, $"Document was not found: {uxmlPath}");
            return asset.Instantiate();
        }
    }
}
