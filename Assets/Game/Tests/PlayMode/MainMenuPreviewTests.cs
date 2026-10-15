using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace LocalTanks.Tests
{
    public sealed class MainMenuPreviewTests
    {
        private const string OutputPath = "Docs/Media/MainMenu.png";
        private const string ShortOutputPath = "TestResults/MainMenu_1280x720.png";

        [UnityTest]
        public IEnumerator CaptureMainMenu()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Run the CaptureMainMenu operation with a graphics device.");

            yield return CaptureAt(1920, 1080, OutputPath);
            yield return CaptureAt(1280, 720, ShortOutputPath);
        }

        private static IEnumerator CaptureAt(int width, int height, string outputPath)
        {
            RenderTexture target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            PanelSettings settings = Object.Instantiate(
                AssetDatabase.LoadAssetAtPath<PanelSettings>(
                    "Assets/Game/Generated/UI/MainMenuPanelSettings.asset"));
            settings.targetTexture = target;
            GameObject host = new GameObject("MainMenuPreview");
            UIDocument document = host.AddComponent<UIDocument>();
            document.panelSettings = settings;
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Game/UI/MainMenu.uxml");
            MainMenuController controller = host.AddComponent<MainMenuController>();
            controller.Configure(
                AssetDatabase.LoadAssetAtPath<CombatDatabase>("Assets/Game/GameData/Generated/CombatDatabase.asset"),
                new[]
                {
                    AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Game/UI/LocalTanksTheme.uss"),
                    AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Game/UI/MainMenu.uss"),
                    AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Game/UI/SettingsOverlay.uss")
                });
            try
            {
                yield return null;
                yield return null;
                yield return null;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                Texture2D image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
                image.Apply();
                byte[] png = image.EncodeToPNG();
                File.WriteAllBytes(Path.GetFullPath(outputPath), png);
                Assert.That(png.Length, Is.GreaterThan(1000));
                Object.DestroyImmediate(image);
                RenderTexture.active = previous;
            }
            finally
            {
                Object.Destroy(host);
                Object.Destroy(settings);
                target.Release();
                Object.Destroy(target);
            }
        }
    }
}
