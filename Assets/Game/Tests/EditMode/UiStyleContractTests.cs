using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace LocalTanks.Tests
{
    /// <summary>
    /// Keeps player-facing UI on the same visual foundation. A new component stylesheet may add layout
    /// and domain states, but it must import the shared theme instead of cloning panel and button colours.
    /// </summary>
    public sealed class UiStyleContractTests
    {
        private const string UiFolder = "Assets/Game/UI";
        private const string ThemePath = UiFolder + "/LocalTanksTheme.uss";
        private const string ThemeImport = "@import url(\"LocalTanksTheme.uss\");";

        private static readonly string[] RequiredThemeSelectors =
        {
            ".lt-panel", ".lt-panel--solid", ".lt-panel--quiet", ".lt-title", ".lt-label",
            ".lt-caption", ".lt-value", ".lt-meter-track", ".lt-button", ".lt-button--primary"
        };

        [Test]
        public void SharedTheme_DefinesTheSemanticFoundation()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<StyleSheet>(ThemePath), Is.Not.Null,
                "The shared player-facing UI theme must remain importable by Unity.");

            string source = File.ReadAllText(ThemePath);
            string[] missing = RequiredThemeSelectors.Where(selector => !source.Contains(selector)).ToArray();
            Assert.That(missing, Is.Empty,
                "The shared UI theme is missing contract selectors: " + string.Join(", ", missing));
        }

        [Test]
        public void ComponentStyleSheets_ImportTheSharedTheme()
        {
            List<string> missingImports = AssetDatabase.FindAssets("t:StyleSheet", new[] { UiFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => Path.GetExtension(path) == ".uss")
                .Where(path => path != ThemePath)
                .Where(path => !File.ReadAllText(path).Contains(ThemeImport))
                .ToList();

            Assert.That(missingImports, Is.Empty,
                "Every component stylesheet in Assets/Game/UI must import LocalTanksTheme.uss: " +
                string.Join(", ", missingImports));
        }

        [Test]
        public void ExistingScreens_ComposeSemanticThemeClasses()
        {
            VisualElement settings = Instantiate(UiFolder + "/SettingsOverlay.uxml");
            VisualElement hud = Instantiate(UiFolder + "/BattleHud.uxml");

            AssertClasses(settings.Q<VisualElement>("settings-panel"), "lt-panel", "lt-panel--solid");
            AssertClasses(settings.Q<Button>("button-reset"), "lt-button");
            AssertClasses(settings.Q<Button>("button-close"), "lt-button", "lt-button--primary");
            AssertClasses(hud.Q<VisualElement>("ally-list"), "lt-panel", "lt-panel--quiet");
            AssertClasses(hud.Q<VisualElement>("enemy-list"), "lt-panel", "lt-panel--quiet");
            AssertClasses(hud.Q<VisualElement>("score"), "lt-panel", "lt-panel--quiet");
            AssertClasses(hud.Q<VisualElement>("battle-clock"), "lt-panel", "lt-panel--quiet");
            AssertClasses(hud.Q<VisualElement>("weapon-status"), "lt-panel", "lt-panel--quiet");
        }

        private static VisualElement Instantiate(string path)
        {
            VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            Assert.That(asset, Is.Not.Null, $"UI document was not found: {path}");
            return asset.Instantiate();
        }

        private static void AssertClasses(VisualElement element, params string[] classes)
        {
            Assert.That(element, Is.Not.Null);
            foreach (string className in classes)
            {
                Assert.That(element.ClassListContains(className), Is.True,
                    $"'{element.name}' must compose the shared '{className}' style.");
            }
        }
    }
}
