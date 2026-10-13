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
    /// driving a battle: the same machine with the overlay off, and with the pattern at several
    /// phases, at the battle zoom and close up. The effect is a few pixels wide on screen, so a
    /// written parameter set is not evidence of how it reads; these images are.
    /// </summary>
    public static class TrackEffectPreview
    {
        private const string OutputDirectory = "TestResults/TrackPreview";

        private static readonly string[] PrefabPaths =
        {
            "Assets/Game/Prefabs/Tanks/MS1_Player.prefab",
            "Assets/Game/Prefabs/Tanks/TigerII_Player.prefab"
        };

        /// <summary>Zoom levels: the battle camera and a close look at the pixels.</summary>
        private static readonly float[] BattleZooms = { 13f, 2f };

        /// <summary>Tread feature spacing in model metres, over the plausible range.</summary>
        private static readonly float[] TreadPitches = { 0.22f };

        /// <summary>Highlight and seam strength, from the restrained end to a visible one.</summary>
        private static readonly Vector2[] Strengths =
        {
            new Vector2(0.1f, 0.07f),
            new Vector2(0.14f, 0.1f),
            new Vector2(0.18f, 0.12f)
        };

        private static readonly float[] Phases = { 0f, 0.5f };

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
                    if (overlay == null)
                    {
                        Debug.LogWarning($"Track preview found no overlay on {prefabPath}");
                        continue;
                    }

                    foreach (float zoom in BattleZooms)
                    {
                        camera.orthographicSize = zoom;
                        CaptureWithoutOverlay(camera, overlay, name, zoom);
                        CapturePhases(camera, overlay, name, zoom);
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

        private static void CapturePhases(Camera camera, SpriteRenderer overlay, string name, float zoom)
        {
            MaterialPropertyBlock properties = new MaterialPropertyBlock();
            overlay.GetPropertyBlock(properties);
            float maskHeightUnits = overlay.bounds.size.y;

            foreach (float pitchMetres in TreadPitches)
            {
                float links = TankTrackAnimator.LinkRepeats(
                    maskHeightUnits, pitchMetres / PrototypeSetup.MetresPerUnit);
                foreach (Vector2 strength in Strengths)
                {
                    foreach (float phase in Phases)
                    {
                        // One phase is one link period, expressed as the travel the shader expects.
                        float travel = phase / links;
                        properties.SetVector(
                            "_TrackParams", new Vector4(links, TankTrackAnimator.SweepRepeats, 0f, 0f));
                        properties.SetVector("_TrackStrength", new Vector4(strength.x, strength.y, 0f, 0f));
                        properties.SetVector("_TrackTravel", new Vector4(travel, travel, 0f, 0f));
                        overlay.SetPropertyBlock(properties);
                        Render(
                            camera,
                            $"{name}_zoom{zoom:0.##}_pitch{pitchMetres:0.##}_s{strength.x:0.00}_phase{phase:0.00}.png");
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
