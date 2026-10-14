using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace LocalTanks.Tests
{
    /// <summary>
    /// Produces the human-readable proof for the battle HUD. The normal Play Mode suite skips it because
    /// ScreenCapture needs a real graphics device; the launcher exposes it as CaptureBattleHudProof.
    /// </summary>
    public sealed class CombatHudPreviewTests
    {
        private const string OutputPath = "Docs/Media/BattleHud.png";
        private const int ImageWidth = 1600;
        private const int ImageHeight = 900;

        [UnityTest]
        public IEnumerator CaptureBattleHudWithDamageAndAnOffscreenContact()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("The battle HUD proof needs a graphics device; run CaptureBattleHudProof.");
            }

            SceneManager.LoadScene("Battle_RiverCrossing");
            yield return null;
            yield return null;

            BattleDirector director = Object.FindAnyObjectByType<BattleDirector>();
            BattleRoster roster = Object.FindAnyObjectByType<BattleRoster>();
            BattleHudController hud = Object.FindAnyObjectByType<BattleHudController>();
            Camera camera = Camera.main;
            Assert.That(director, Is.Not.Null);
            Assert.That(roster, Is.Not.Null);
            Assert.That(hud, Is.Not.Null);
            Assert.That(camera, Is.Not.Null);

            UIDocument document = hud.GetComponent<UIDocument>();
            Assert.That(document, Is.Not.Null);
            PanelSettings originalPanelSettings = document.panelSettings;
            PanelSettings proofPanelSettings = Object.Instantiate(originalPanelSettings);
            RenderTexture hudTarget = new RenderTexture(ImageWidth, ImageHeight, 0, RenderTextureFormat.ARGB32);
            RenderTexture sceneTarget = new RenderTexture(ImageWidth, ImageHeight, 24, RenderTextureFormat.ARGB32);
            hudTarget.Create();
            sceneTarget.Create();
            proofPanelSettings.targetTexture = hudTarget;
            proofPanelSettings.referenceResolution = new Vector2Int(ImageWidth, ImageHeight);
            proofPanelSettings.clearColor = true;
            proofPanelSettings.colorClearValue = Color.clear;
            document.panelSettings = proofPanelSettings;
            camera.targetTexture = sceneTarget;
            yield return null;
            yield return null;

            foreach (CombatTankAI ai in Object.FindObjectsByType<CombatTankAI>())
            {
                ai.enabled = false;
            }

            TeamMember player = director.PlayerMember;
            TeamMember[] proofEnemies = roster.Enemies.Where(entry => entry.IsAlive)
                .Take(2)
                .Select(entry => entry.Member)
                .ToArray();
            Assert.That(proofEnemies, Has.Length.EqualTo(2));
            TeamMember offscreenEnemy = proofEnemies[0];
            TeamMember hitEnemy = proofEnemies[1];
            foreach (BattleRosterEntry other in roster.Enemies.Where(entry =>
                         entry.Member != offscreenEnemy && entry.Member != hitEnemy))
            {
                other.Member.transform.position = player.transform.position + new Vector3(1000f, 1000f, 0f);
            }

            TeamMember ally = roster.Allies.First(entry => entry.IsAlive && !entry.Member.IsPlayerControlled).Member;
            foreach (BattleRosterEntry other in roster.Allies.Where(entry =>
                         entry.Member != player && entry.Member != ally))
            {
                other.Member.transform.position = player.transform.position + new Vector3(-1000f, -1000f, 0f);
            }

            Vector2 visionForward = player.VisionForward.normalized;
            Vector2 visionRight = new Vector2(visionForward.y, -visionForward.x);
            ally.transform.position = player.transform.position + (Vector3)(visionRight * 2.5f);
            hitEnemy.transform.position = player.transform.position + (Vector3)(visionForward * 2.5f);
            camera.orthographicSize = 3.5f;
            camera.transform.position = new Vector3(player.transform.position.x, player.transform.position.y, -10f);
            offscreenEnemy.transform.position = player.transform.position + (Vector3)(visionForward * 8f);
            Physics2D.SyncTransforms();
            director.VisionSystem.ForceEvaluateAll();
            Assert.That(roster.Find(offscreenEnemy).IsCurrentlyVisible, Is.True,
                "The proof target must be spotted so the offscreen indicator is honest.");
            Assert.That(roster.Find(hitEnemy).IsCurrentlyVisible, Is.True,
                "The damaged enemy must be visible so its red popup does not leak hidden information.");

            TankArmor armor = ally.GetComponent<TankArmor>();
            Vector2 impactDirection = Vector2.up;
            armor.ResolveImpact(
                impactDirection,
                ally.transform.position,
                -impactDirection,
                100000f,
                10f,
                17,
                70f,
                0.7f,
                0.65f,
                0);

            TankArmor enemyArmor = hitEnemy.GetComponent<TankArmor>();
            enemyArmor.ResolveImpact(
                impactDirection,
                hitEnemy.transform.position,
                -impactDirection,
                100000f,
                10f,
                23,
                70f,
                0.7f,
                0.65f,
                0);

            yield return null;
            yield return null;
            Assert.That(document.rootVisualElement.Query<Label>(className: "world-health-number").ToList(),
                Is.Not.Empty);
            Assert.That(document.rootVisualElement.Query<Label>(className: "damage-popup").ToList(),
                Has.Count.EqualTo(2));
            Assert.That(document.rootVisualElement.Query<Label>(className: DamagePopupLayer.AllyDamageClass).ToList(),
                Has.Count.EqualTo(1));
            Assert.That(document.rootVisualElement.Query<Label>(className: DamagePopupLayer.EnemyDamageClass).ToList(),
                Has.Count.EqualTo(1));
            Assert.That(document.rootVisualElement.Query<VisualElement>(className: "offscreen-indicator").ToList(),
                Has.Count.GreaterThanOrEqualTo(1));
            camera.Render();
            yield return null;

            try
            {
                SaveComposite(sceneTarget, hudTarget, OutputPath);
            }
            finally
            {
                camera.targetTexture = null;
                document.panelSettings = originalPanelSettings;
                hudTarget.Release();
                sceneTarget.Release();
                Object.Destroy(hudTarget);
                Object.Destroy(sceneTarget);
                Object.Destroy(proofPanelSettings);
            }

            string absolutePath = Path.GetFullPath(OutputPath);
            Assert.That(File.Exists(absolutePath), Is.True, $"ScreenCapture did not write {OutputPath}.");
            Assert.That(new FileInfo(absolutePath).Length, Is.GreaterThan(10000),
                "The battle HUD proof is too small to contain a rendered frame.");
            Debug.Log($"Battle HUD proof written to {OutputPath}");
        }

        private static void SaveComposite(RenderTexture scene, RenderTexture hud, string path)
        {
            Texture2D sceneImage = Read(scene);
            Texture2D hudImage = Read(hud);
            Color32[] scenePixels = sceneImage.GetPixels32();
            Color32[] hudPixels = hudImage.GetPixels32();
            Color32[] outputPixels = new Color32[scenePixels.Length];
            for (int index = 0; index < outputPixels.Length; index++)
            {
                Color background = scenePixels[index];
                Color foreground = hudPixels[index];
                outputPixels[index] = Color.Lerp(background, foreground, foreground.a);
            }

            Texture2D output = new Texture2D(ImageWidth, ImageHeight, TextureFormat.RGBA32, false);
            output.SetPixels32(outputPixels);
            output.Apply();
            string absolutePath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
            File.WriteAllBytes(absolutePath, output.EncodeToPNG());
            Object.Destroy(sceneImage);
            Object.Destroy(hudImage);
            Object.Destroy(output);
        }

        private static Texture2D Read(RenderTexture source)
        {
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = source;
                Texture2D image = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
                image.Apply();
                return image;
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }
    }
}
