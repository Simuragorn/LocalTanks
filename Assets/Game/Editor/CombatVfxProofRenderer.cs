using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LocalTanks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LocalTanks.Editor
{
    /// <summary>
    /// Renders every generated combat effect into one reviewable image.
    /// </summary>
    /// <remarks>
    /// A particle effect cannot be judged from its numbers: a flash of the wrong size or an impact that
    /// finishes before the first frame looks exactly like a correct one in the profile file. This command
    /// builds a throwaway scene, advances every prefab to a representative moment of its own life, renders
    /// one frame and writes it to <c>Docs/Media</c>, so the library can be compared with its intent
    /// without flying a battle.
    /// <para>
    /// It needs a graphics device, so it runs in <c>-batchmode</c> but never with <c>-nographics</c>:
    /// the null device discards every draw call and would produce an empty image.
    /// </para>
    /// </remarks>
    public static class CombatVfxProofRenderer
    {
        public const string OutputPath = "Docs/Media/CombatVfx.png";

        private const int ImageWidth = 1280;
        private const int ImageHeight = 960;
        private const int Columns = 4;
        private const int Rows = 3;
        private const float CellSize = 1.6f;

        /// <summary>
        /// How long each effect is scanned for its busiest moment. A muzzle flash lives for a few frames
        /// and a smoke trail builds up for a second, so one fixed moment cannot serve both; the scan finds
        /// the moment each effect actually has something to show.
        /// </summary>
        private const float BurstScanSeconds = 1.5f;

        private const float LoopScanSeconds = 2.5f;

        private const float ScanStepSeconds = 0.02f;

        [MenuItem("Local Tanks/Diagnostics/Render Combat Vfx Proof")]
        public static void Render()
        {
            string[] effects = CombatVfxEffects.All();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Camera camera = CreateCamera();
            List<GameObject> instances = new List<GameObject>();
            try
            {
                for (int index = 0; index < effects.Length; index++)
                {
                    GameObject prefab = Resources.Load<GameObject>($"{CombatVfxEffects.ResourceFolder}/{effects[index]}");
                    if (prefab == null)
                    {
                        throw new FileNotFoundException(
                            $"Effect '{effects[index]}' has no prefab; run 'Local Tanks/Build Combat Vfx Assets' first.");
                    }

                    GameObject instance = UnityEngine.Object.Instantiate(prefab);
                    instance.transform.position = CellPosition(index);
                    instances.Add(instance);
                }

                Prepare(instances);
                RenderToFile(camera, Path.GetFullPath(OutputPath));

                Debug.Log($"Combat VFX proof rendered to {OutputPath}: {effects.Length} effects in " +
                          $"{Columns}×{Rows} cells, each at the moment it covers the most of the frame.");
            }
            finally
            {
                foreach (GameObject instance in instances)
                {
                    if (instance != null)
                    {
                        UnityEngine.Object.DestroyImmediate(instance);
                    }
                }

                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }

            EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>
        /// Positions effects in a grid, one cell each, ordered as <see cref="CombatVfxEffects.All"/> so the
        /// image can be read without labels.
        /// </summary>
        private static Vector3 CellPosition(int index)
        {
            int column = index % Columns;
            int row = index / Columns;
            return new Vector3(
                (column - (Columns - 1) * 0.5f) * CellSize,
                ((Rows - 1) * 0.5f - row) * CellSize,
                0f);
        }

        private static Camera CreateCamera()
        {
            GameObject cameraObject = new GameObject("VfxProofCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = Rows * CellSize * 0.5f + 0.3f;
            camera.aspect = (float)ImageWidth / ImageHeight;
            camera.clearFlags = CameraClearFlags.SolidColor;
            // Mid grey: dust is light and sparks are additive, so both have to stay visible.
            camera.backgroundColor = new Color(0.19f, 0.2f, 0.21f);
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.transform.rotation = Quaternion.identity;
            camera.cullingMask = ~0;
            return camera;
        }

        /// <summary>
        /// Shows every effect at the moment it covers the most of the frame, then leaves it there.
        /// </summary>
        /// <remarks>
        /// The systems are restarted explicitly: a stopped particle system ignores a plain simulation
        /// step, so an effect that was merely created would render an empty cell.
        /// <para>
        /// The chosen moment is the one with the largest total visible area, not the largest number of
        /// particles. A burst is densest at the instant it is born, which is also when every particle sits
        /// on top of every other one: the first proof sheet showed the destruction as a single bright dot
        /// with its smoke cloud still collapsed inside it. Weighting each particle by its current size and
        /// opacity shows the moment a viewer would actually recognise.
        /// </para>
        /// </remarks>
        private static void Prepare(List<GameObject> instances)
        {
            string[] effects = CombatVfxEffects.All();
            for (int index = 0; index < instances.Count; index++)
            {
                string effectId = effects[index];
                ParticleSystem[] systems = instances[index].GetComponentsInChildren<ParticleSystem>(true);
                float window = CombatVfxEffects.IsLoop(effectId) ? LoopScanSeconds : BurstScanSeconds;

                Restart(systems);
                float busiestMoment = ScanStepSeconds;
                float peakArea = -1f;
                int peakCount = 0;
                for (float elapsed = 0f; elapsed < window; elapsed += ScanStepSeconds)
                {
                    Advance(systems, ScanStepSeconds);
                    float area = MeasureVisibleArea(systems);
                    if (area > peakArea)
                    {
                        peakArea = area;
                        peakCount = CountParticles(systems);
                        busiestMoment = elapsed + ScanStepSeconds;
                    }
                }

                Restart(systems);
                Advance(systems, busiestMoment);

                // The second pass re-rolls the particles, so its area differs a little from the scan peak;
                // both numbers are logged because the difference is randomness, not a defect.
                Debug.Log($"[vfxproof] {effectId}: shown at {busiestMoment:0.##}s with " +
                          $"{CountParticles(systems)} particle(s), visible area " +
                          $"{MeasureVisibleArea(systems):0.###}; scan peak {peakArea:0.###} " +
                          $"at {peakCount} particle(s).");
            }
        }

        /// <summary>
        /// Total size of the live particles weighted by their opacity: a rough measure of how much of the
        /// frame an effect is actually painting, which is what a proof image is for.
        /// </summary>
        private static float MeasureVisibleArea(ParticleSystem[] systems)
        {
            float total = 0f;
            foreach (ParticleSystem system in systems)
            {
                int count = system.particleCount;
                if (count == 0)
                {
                    continue;
                }

                ParticleSystem.Particle[] particles = new ParticleSystem.Particle[count];
                int read = system.GetParticles(particles);
                for (int index = 0; index < read; index++)
                {
                    total += particles[index].GetCurrentSize(system) * particles[index].GetCurrentColor(system).a;
                }
            }

            return total;
        }

        private static void Restart(ParticleSystem[] systems)
        {
            foreach (ParticleSystem system in systems)
            {
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                system.Clear(true);
                system.Simulate(0.001f, true, true);
            }
        }

        private static void Advance(ParticleSystem[] systems, float seconds)
        {
            foreach (ParticleSystem system in systems)
            {
                system.Simulate(seconds, true, false);
            }
        }

        private static int CountParticles(ParticleSystem[] systems)
        {
            int total = 0;
            foreach (ParticleSystem system in systems)
            {
                total += system.particleCount;
            }

            return total;
        }

        private static void RenderToFile(Camera camera, string absolutePath)
        {
            RenderTexture target = new RenderTexture(ImageWidth, ImageHeight, 24, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();

                RenderTexture.active = target;
                Texture2D image = new Texture2D(ImageWidth, ImageHeight, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0f, 0f, ImageWidth, ImageHeight), 0, 0);
                image.Apply(false, false);
                byte[] png = image.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(image);

                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? string.Empty);
                File.WriteAllBytes(absolutePath, png);
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
