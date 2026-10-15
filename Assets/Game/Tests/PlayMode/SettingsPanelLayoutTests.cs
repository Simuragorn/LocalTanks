using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace LocalTanks.Tests
{
    /// <summary>
    /// The settings panel is read and clicked in whatever window a player has. Its rows used to collapse
    /// into each other in a short game view, because a flex column shrinks its children when the content
    /// is taller than the screen, and the label text then overlapped the next slider. These tests build the
    /// real HUD document with the real stylesheets at several view sizes and check the geometry, which a
    /// headless run can measure without a graphics device.
    /// </summary>
    public sealed class SettingsPanelLayoutTests
    {
        private const string PanelSettingsPath = "Assets/Game/Generated/RiverCrossing/BattleHudPanelSettings.asset";
        private const string BattleHudPath = "Assets/Game/UI/BattleHud.uxml";
        private const string BattleHudUssPath = "Assets/Game/UI/BattleHud.uss";
        private const string SettingsOverlayUssPath = "Assets/Game/UI/SettingsOverlay.uss";

        private static readonly Vector2Int[] ViewSizes =
        {
            new Vector2Int(1920, 1080),
            new Vector2Int(1280, 720),
            new Vector2Int(960, 400),
            new Vector2Int(640, 260)
        };

        [UnityTest]
        public IEnumerator RowsNeverOverlapEachOtherOrTheButtons()
        {
            List<string> failures = new List<string>();
            foreach (Vector2Int size in ViewSizes)
            {
                Panel panel = null;
                try
                {
                    panel = BuildPanel(size);
                    yield return null;
                    yield return null;
                    panel.Controller.SetOpen(true);
                    yield return null;

                    failures.AddRange(Measure(size, panel));
                }
                finally
                {
                    panel?.Dispose();
                }
            }

            Assert.That(failures, Is.Empty,
                "The settings panel must stay readable at every window size:\n- " +
                string.Join("\n- ", failures));
        }

        [UnityTest]
        public IEnumerator OverlayFillsTheDocumentEvenAsATemplate()
        {
            Panel panel = null;
            try
            {
                panel = BuildPanel(new Vector2Int(1920, 1080));
                yield return null;
                yield return null;
                panel.Controller.SetOpen(true);
                yield return null;

                VisualElement root = panel.Document.rootVisualElement;
                VisualElement overlay = root.Q<VisualElement>("settings-overlay");
                VisualElement settingsPanel = root.Q<VisualElement>("settings-panel");
                Debug.Log($"[layout] root={root.worldBound} overlay={overlay.worldBound} panel={settingsPanel.worldBound} " +
                          $"row={root.Query<VisualElement>(className: "settings-row").First().worldBound}");

                Assert.That(overlay.worldBound.height, Is.EqualTo(root.worldBound.height).Within(1f),
                    "A template container does not stretch by itself. Without the .settings-instance rule the " +
                    "overlay measures its insets against a zero-height box, which squeezes every row to zero " +
                    "height and draws each label over the next slider.");
                Assert.That(overlay.worldBound.width, Is.EqualTo(root.worldBound.width).Within(1f));
            }
            finally
            {
                panel?.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator PanelOwnsThePointerOnlyWhileItIsOpen()
        {
            Panel panel = null;
            try
            {
                panel = BuildPanel(new Vector2Int(1280, 720));
                yield return null;
                yield return null;

                int before = ModalUiState.OpenCount;

                panel.Controller.SetOpen(true);
                Assert.That(ModalUiState.OpenCount, Is.EqualTo(before + 1),
                    "The tank has to know the panel is taking the mouse, or a slider drag fires the gun.");

                panel.Controller.SetOpen(false);
                Assert.That(ModalUiState.OpenCount, Is.EqualTo(before));

                // A destroyed HUD must not leave gameplay input blocked for the rest of the session.
                panel.Controller.SetOpen(true);
                Object.Destroy(panel.Host);
                yield return null;
                Assert.That(ModalUiState.OpenCount, Is.EqualTo(before));
            }
            finally
            {
                panel?.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator StylesKeepThePanelReadable()
        {
            Panel panel = null;
            try
            {
                panel = BuildPanel(new Vector2Int(1280, 720));
                yield return null;
                yield return null;
                panel.Controller.SetOpen(true);
                yield return null;

                VisualElement root = panel.Document.rootVisualElement;
                VisualElement settingsPanel = root.Q<VisualElement>("settings-panel");
                Label label = root.Q<Label>(className: "settings-label");
                Label hint = root.Q<Label>(className: "settings-hint");
                VisualElement row = root.Query<VisualElement>(className: "settings-row").First();
                VisualElement dragger = root.Q<Slider>("slider-master")
                    ?.Q<VisualElement>(className: "unity-base-slider__dragger");

                // A battlefield is a busy background: the panel has to be opaque enough to read on, and
                // the text has to be bright rather than the mid grey that disappeared into the ground.
                Assert.That(settingsPanel.resolvedStyle.backgroundColor.a, Is.GreaterThan(0.9f),
                    "The panel background must not be see-through enough to fight the text.");
                Assert.That(Luminance(label.resolvedStyle.color), Is.GreaterThan(0.75f),
                    $"The slider labels must be bright, but were {label.resolvedStyle.color}");
                Assert.That(hint.resolvedStyle.fontSize, Is.GreaterThanOrEqualTo(12f),
                    "The hint is the smallest text in the panel and was unreadable at 11 px.");
                Assert.That(row.resolvedStyle.minHeight.value, Is.GreaterThanOrEqualTo(22f),
                    "A row without a minimum height collapses when the window is short.");

                // The sub-element selectors are the fragile part of the styling: a wrong class name is
                // silently ignored, and the handle would stay a hairline nobody can grab.
                Assert.That(dragger, Is.Not.Null,
                    "The slider dragger class name changed, so the handle styling no longer applies.");
                Assert.That(dragger.resolvedStyle.width, Is.GreaterThanOrEqualTo(10f),
                    $"The slider handle must be wide enough to grab, but was {dragger.resolvedStyle.width}");
            }
            finally
            {
                panel?.Dispose();
            }
        }

        private static float Luminance(Color color)
        {
            return 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
        }

        private static List<string> Measure(Vector2Int size, Panel panel)
        {
            List<string> failures = new List<string>();
            VisualElement root = panel.Document.rootVisualElement;
            VisualElement overlay = root.Q<VisualElement>("settings-overlay");
            VisualElement settingsPanel = root.Q<VisualElement>("settings-panel");
            List<VisualElement> rows = root.Query<VisualElement>(className: "settings-row").ToList();
            VisualElement buttons = root.Q<VisualElement>(className: "settings-button-row");
            VisualElement hint = root.Q<VisualElement>(className: "settings-hint");

            string where = $"at {size.x}×{size.y}";

            Assert.That(overlay, Is.Not.Null);
            Assert.That(settingsPanel, Is.Not.Null);
            if (rows.Count != 7)
            {
                failures.Add($"{where}: expected five audio and two crew rows, found {rows.Count}");
                return failures;
            }

            for (int index = 0; index < rows.Count - 1; index++)
            {
                Rect current = rows[index].worldBound;
                Rect next = rows[index + 1].worldBound;
                // Rows must keep their own height: a squeezed row is what made the labels collide.
                if (current.height < 18f)
                {
                    failures.Add($"{where}: row {index} is only {current.height:0.#} px tall, so its text " +
                                 $"cannot fit (rows: {Describe(rows)})");
                }

                if (current.yMax > next.yMin + 0.5f)
                {
                    failures.Add($"{where}: rows {index} and {index + 1} overlap by " +
                                 $"{current.yMax - next.yMin:0.#} px (rows: {Describe(rows)})");
                }
            }

            if (hint != null && buttons != null && hint.worldBound.yMax > buttons.worldBound.yMin + 0.5f)
            {
                failures.Add($"{where}: the hint overlaps the buttons by " +
                             $"{hint.worldBound.yMax - buttons.worldBound.yMin:0.#} px");
            }

            // Every control has to stay inside the panel, and the panel inside the screen.
            if (buttons != null && buttons.worldBound.yMax > settingsPanel.worldBound.yMax + 0.5f)
            {
                failures.Add($"{where}: the buttons stick out of the panel by " +
                             $"{buttons.worldBound.yMax - settingsPanel.worldBound.yMax:0.#} px");
            }

            if (settingsPanel.worldBound.height < ContentHeight(settingsPanel))
            {
                failures.Add($"{where}: the panel is {settingsPanel.worldBound.height:0.#} px tall while its " +
                             $"content needs {ContentHeight(settingsPanel):0.#} px, so something is squeezed");
            }

            // It also has to fit: a panel taller than the overlay would push the buttons off the screen.
            if (settingsPanel.worldBound.height > overlay.worldBound.height + 0.5f)
            {
                failures.Add($"{where}: the panel is {settingsPanel.worldBound.height:0.#} px tall but the " +
                             $"overlay is only {overlay.worldBound.height:0.#} px, so its buttons are off screen");
            }

            return failures;
        }

        private static float ContentHeight(VisualElement panel)
        {
            float bottom = 0f;
            foreach (VisualElement child in panel.Children())
            {
                bottom = Mathf.Max(bottom, child.worldBound.yMax - panel.worldBound.yMin);
            }

            return bottom;
        }

        private static string Describe(IEnumerable<VisualElement> rows)
        {
            return string.Join(", ", rows.Select(row =>
                $"{row.worldBound.yMin:0.#}..{row.worldBound.yMax:0.#}"));
        }

        private static Panel BuildPanel(Vector2Int size)
        {
            PanelSettings template = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            Assert.That(template, Is.Not.Null, $"The battle HUD panel settings were not found: {PanelSettingsPath}");

            RenderTexture target = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32);
            PanelSettings settings = Object.Instantiate(template);
            settings.targetTexture = target;

            GameObject host = new GameObject("SettingsPanelLayoutHost");
            UIDocument document = host.AddComponent<UIDocument>();
            document.panelSettings = settings;
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(BattleHudPath);
            SettingsPanelController controller = host.AddComponent<SettingsPanelController>();

            VisualElement root = document.rootVisualElement;
            if (root != null)
            {
                root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(BattleHudUssPath));
                root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(SettingsOverlayUssPath));
            }

            return new Panel(host, document, controller, settings, target);
        }

        private sealed class Panel
        {
            public Panel(GameObject host, UIDocument document, SettingsPanelController controller,
                PanelSettings settings, RenderTexture target)
            {
                Host = host;
                Document = document;
                Controller = controller;
                Settings = settings;
                Target = target;
            }

            public GameObject Host { get; }
            public UIDocument Document { get; }
            public SettingsPanelController Controller { get; }
            public PanelSettings Settings { get; }
            public RenderTexture Target { get; }

            public void Dispose()
            {
                Object.Destroy(Host);
                Object.Destroy(Settings);
                Target.Release();
                Object.Destroy(Target);
            }
        }
    }
}
