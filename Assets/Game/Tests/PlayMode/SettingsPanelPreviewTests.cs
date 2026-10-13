using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEngine.Rendering;

namespace LocalTanks.Tests
{
    /// <summary>
    /// Renders the settings overlay to a PNG, because a layout test can measure a panel but only a picture
    /// shows whether a human can read it: contrast, handle size and the panel's weight against a battlefield.
    /// </summary>
    /// <remarks>
    /// This is the one Play Mode check that needs pixels, so the headless suite ignores it and the
    /// `CaptureSettingsPanel` operation runs this class alone with a real graphics device. The pictures are
    /// evidence for a person, and the assertions only guard against the empty file that a null device or a
    /// panel that never painted would leave behind.
    /// </remarks>
    public sealed class SettingsPanelPreviewTests
    {
        private const string PanelSettingsPath = "Assets/Game/Generated/RiverCrossing/BattleHudPanelSettings.asset";
        private const string SettingsOverlayPath = "Assets/Game/UI/SettingsOverlay.uxml";
        private const string SettingsOverlayUssPath = "Assets/Game/UI/SettingsOverlay.uss";
        private const string OutputFolder = "TestResults";

        // Two shapes on purpose: the reference window of the HUD, and a smaller one a player may use. Both
        // go through the real panel settings, so the picture shows the scaling the game actually applies.
        private static readonly (Vector2Int Size, string Name)[] Shapes =
        {
            (new Vector2Int(1920, 1080), "SettingsPanel.png"),
            (new Vector2Int(1280, 720), "SettingsPanel_small.png")
        };

        [UnityTest]
        public IEnumerator CaptureTheOverlayAtANormalAndAShortWindow()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("Rendering a UI Toolkit panel needs a graphics device: run the " +
                              "CaptureSettingsPanel operation instead of the headless suite.");
            }

            Directory.CreateDirectory(Path.GetFullPath(OutputFolder));
            List<string> blank = new List<string>();
            foreach ((Vector2Int size, string name) in Shapes)
            {
                Panel panel = null;
                try
                {
                    panel = BuildPanel(size);
                    // The panel needs a few frames before it has been laid out and painted into its target.
                    yield return null;
                    yield return null;
                    panel.Controller.SetOpen(true);
                    yield return null;
                    yield return null;

                    float luminance = Save(panel, Path.Combine(OutputFolder, name));
                    if (luminance > 0.75f)
                    {
                        blank.Add($"{name} ({size.x}×{size.y}) came out at {luminance:0.00} mean luminance, " +
                                  "which is the blank texture a panel that never painted leaves behind");
                    }
                }
                finally
                {
                    panel?.Dispose();
                }
            }

            Assert.That(blank, Is.Empty, "The settings overlay did not render:\n- " + string.Join("\n- ", blank));
            Debug.Log($"Settings panel previews written to {OutputFolder}: " +
                      string.Join(", ", System.Array.ConvertAll(Shapes, shape => shape.Name)));
        }

        private static float Save(Panel panel, string path)
        {
            RenderTexture target = panel.Target;
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                Texture2D image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.GetFullPath(path), image.EncodeToPNG());

                Color32[] pixels = image.GetPixels32();
                double total = 0d;
                foreach (Color32 pixel in pixels)
                {
                    total += (0.2126 * pixel.r + 0.7152 * pixel.g + 0.0722 * pixel.b) / 255d;
                }

                Object.DestroyImmediate(image);
                return (float)(total / Mathf.Max(1, pixels.Length));
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        private static Panel BuildPanel(Vector2Int size)
        {
            PanelSettings template = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            Assert.That(template, Is.Not.Null, $"The battle HUD panel settings were not found: {PanelSettingsPath}");

            RenderTexture target = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32);
            PanelSettings settings = Object.Instantiate(template);
            settings.targetTexture = target;
            settings.referenceResolution = size;

            GameObject host = new GameObject("SettingsPanelPreviewHost");
            UIDocument document = host.AddComponent<UIDocument>();
            document.panelSettings = settings;
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(SettingsOverlayPath);
            SettingsPanelController controller = host.AddComponent<SettingsPanelController>();
            controller.Configure(AssetDatabase.LoadAssetAtPath<StyleSheet>(SettingsOverlayUssPath), true);

            return new Panel(host, controller, settings, target);
        }

        private sealed class Panel
        {
            public Panel(GameObject host, SettingsPanelController controller, PanelSettings settings, RenderTexture target)
            {
                Host = host;
                Controller = controller;
                Settings = settings;
                Target = target;
            }

            public GameObject Host { get; }
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
