using System;
using System.IO;
using LocalTanks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LocalTanks.Editor
{
    /// <summary>
    /// Renders the tread overlay of a generated tank into PNG files so its look can be judged without
    /// driving a battle: the same machine with the overlay off, stationary, and at several moving
    /// phases, at the battle zoom and close up. The effect is a few pixels wide on screen, so a
    /// written parameter set is not evidence of how it reads; these images are.
    /// </summary>
    public static class TrackEffectPreview
    {
        private const string OutputDirectory = "TestResults/TrackPreview";

        private static readonly string[] PrefabPaths =
        {
            "Assets/Game/Prefabs/Tanks/MS1_Player.prefab",
            "Assets/Game/Prefabs/Tanks/TigerII_Player.prefab",
            "Assets/Game/Prefabs/Tanks/Leichttraktor_Player.prefab",
            "Assets/Game/Prefabs/Tanks/T1Cunningham_Player.prefab"
        };

        /// <summary>
        /// Zoom levels: the battle camera, two closer views and an extreme one that fills the frame
        /// with a single track band, where the links themselves can be judged.
        /// </summary>
        private static readonly float[] BattleZooms = { 13f, 4f, 1f, 0.35f };

        /// <summary>Tread feature spacing in model metres, over the plausible range.</summary>
        private static readonly float[] TreadPitches = { 0.22f };

        /// <summary>
        /// Candidate looks: lighten strength, darken strength, how much darkening survives on the
        /// darkest art, and the gain of the coarse links that replace the fine ones at a distance.
        /// </summary>
        private static readonly Vector4[] Looks =
        {
            new Vector4(0.22f, 0.34f, 0.5f, 1f),
            new Vector4(TankTrackAnimator.DefaultStrength.x, TankTrackAnimator.DefaultStrength.y, 0.45f, 1f),
            new Vector4(0.42f, 0.62f, 0.45f, 1f),
            // Gaps only, exaggerated: this must visibly darken the band, because additive blending
            // silently clamped exactly this half of the pattern away.
            new Vector4(0f, 1.2f, 0.7f, 1f)
        };

        private static readonly float[] Phases = { 0f, 0.25f, 0.5f, 0.75f };

        [MenuItem("Local Tanks/Diagnostics/Capture Track Effect Preview")]
        public static void Capture()
        {
            Directory.CreateDirectory(OutputDirectory);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Camera camera = new GameObject("PreviewCamera").AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.22f, 0.14f, 1f);
            camera.transform.position = new Vector3(0f, 0f, -10f);

            GameObject tank = null;
            try
            {
                foreach (string prefabPath in PrefabPaths)
                {
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    if (prefab == null)
                    {
                        Debug.LogWarning($"Track preview skipped a missing prefab: {prefabPath}");
                        continue;
                    }

                    string name = Path.GetFileNameWithoutExtension(prefabPath);
                    tank = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    tank.transform.position = Vector3.zero;
                    tank.transform.rotation = Quaternion.identity;
                    SpriteRenderer overlay = tank.transform.Find("HullVisual/TrackOverlay")?.GetComponent<SpriteRenderer>();
                    SpriteRenderer hull = tank.transform.Find("HullVisual")?.GetComponent<SpriteRenderer>();
                    if (overlay == null || hull == null)
                    {
                        Debug.LogWarning($"Track preview found no overlay on {prefabPath}");
                        continue;
                    }

                    foreach (float zoom in BattleZooms)
                    {
                        camera.orthographicSize = zoom;

                        // A battle view wants the whole machine, but a close view is useless centred on
                        // the hull: the track band sits at its outer edge and would leave the frame. From
                        // the middle zoom inwards the camera therefore follows the starboard band.
                        float centre = tank.transform.position.x;
                        Vector3 position = camera.transform.position;
                        position.x = zoom <= 4f ? centre - overlay.bounds.extents.x * 0.85f : centre;
                        position.y = tank.transform.position.y;
                        camera.transform.position = position;

                        CaptureWithoutOverlay(camera, overlay, name, zoom);
                        CapturePhases(camera, overlay, hull, name, zoom);
                    }

                    UnityEngine.Object.DestroyImmediate(tank);
                    tank = null;
                }
            }
            finally
            {
                if (tank != null)
                {
                    UnityEngine.Object.DestroyImmediate(tank);
                }

                EditorSceneManager.CloseScene(scene, true);
            }

            Debug.Log($"Track effect preview saved to {Path.GetFullPath(OutputDirectory)}");
        }

        private static void CaptureWithoutOverlay(Camera camera, SpriteRenderer overlay, string name, float zoom)
        {
            bool enabled = overlay.enabled;
            overlay.enabled = false;
            try
            {
                Render(camera, $"{name}_zoom{zoom:0.##}_off.png");
            }
            finally
            {
                overlay.enabled = enabled;
            }
        }

        private static void CapturePhases(
            Camera camera, SpriteRenderer overlay, SpriteRenderer hull, string name, float zoom)
        {
            MaterialPropertyBlock properties = new MaterialPropertyBlock();
            overlay.GetPropertyBlock(properties);
            float maskHeightUnits = overlay.bounds.size.y;
            TankTrackAnimator animator = overlay.GetComponentInParent<TankTrackAnimator>();

            // The animator is not running in the editor, so the preview supplies what its Awake would
            // have written, including the hull pixels the shader reads.
            Rect hullRect = hull.sprite.textureRect;
            Texture2D hullTexture = hull.sprite.texture;
            properties.SetVector(
                "_HullRect",
                new Vector4(
                    hullRect.x / hullTexture.width,
                    hullRect.y / hullTexture.height,
                    hullRect.width / hullTexture.width,
                    hullRect.height / hullTexture.height));
            properties.SetTexture("_HullTex", hullTexture);
            properties.SetVector(
                "_TrackGeometry",
                animator != null ? animator.TrackGeometry : new Vector4(0.85f, 0.15f, 0.08f, 0f));
            foreach (float pitchMetres in TreadPitches)
            {
                float links = TankTrackAnimator.LinkRepeats(
                    maskHeightUnits, pitchMetres / GameScale.MetresPerUnit);
                foreach (Vector4 look in Looks)
                {
                    properties.SetVector(
                        "_TrackParams", new Vector4(links, TankTrackAnimator.SweepRepeats, 0f, 0f));
                    properties.SetVector("_TrackStrength", new Vector4(look.x, look.y, 0f, 0f));
                    properties.SetVector("_HullLighting", new Vector4(look.z, 0f, 1.5f, look.w));
                    properties.SetVector("_TrackTravel", Vector4.zero);
                    properties.SetVector(
                        "_TrackMotion",
                        new Vector4(
                            0f,
                            0f,
                            TankTrackAnimator.DistantPatternGain,
                            TankTrackAnimator.StaticPatternStrength));
                    overlay.SetPropertyBlock(properties);
                    Render(
                        camera,
                        $"{name}_zoom{zoom:0.##}_pitch{pitchMetres:0.##}_h{look.x:0.00}s{look.y:0.00}_stationary.png");

                    properties.SetVector(
                        "_TrackMotion",
                        new Vector4(
                            1f,
                            1f,
                            TankTrackAnimator.DistantPatternGain,
                            TankTrackAnimator.StaticPatternStrength));
                    foreach (float phase in Phases)
                    {
                        // One phase is one link period, expressed as the travel the shader expects.
                        float travel = phase / links;
                        properties.SetVector("_TrackTravel", new Vector4(travel, travel, 0f, 0f));
                        overlay.SetPropertyBlock(properties);
                        Render(
                            camera,
                            $"{name}_zoom{zoom:0.##}_pitch{pitchMetres:0.##}_h{look.x:0.00}s{look.y:0.00}_phase{phase:0.00}.png");
                    }
                }
            }
        }

        private static void Render(Camera camera, string fileName)
        {
            const int width = 1280;
            const int height = 720;
            RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                RenderTexture.active = target;
                camera.Render();
                image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.GetFullPath(Path.Combine(OutputDirectory, fileName)), image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(image);
            }
        }
    }
}
