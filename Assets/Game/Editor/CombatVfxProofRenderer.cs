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
        private const int Rows = 4;
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

                // The prints are sprites in a world container rather than particles, so they would never
                // appear in a sheet built only from effect prefabs. Their cell shows the two track lines and
                // the relief of each pattern, which is exactly what the in-game look is judged on.
                foreach (string variant in CombatVfxEffects.MarkVariants)
                {
                    instances.Add(CreatePrintDemo(variant, instances.Count));
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
            // Bloom is what turns the HDR colours of a flash and a fire into a glow, and a camera only
            // runs the post-processing stack when its URP data says so. Without this the proof
            // images would show flat white where the game shows light.
            Camera camera = cameraObject.AddComponent<Camera>();
            CombatVfxGlowSetup.EnsurePostProcessing(cameraObject);
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
        /// A cell that shows one print pattern the way a machine presses it: a light patch of ground with
        /// two track lines curving gently, so the relief of the pattern can be judged at all.
        /// </summary>
        private static GameObject CreatePrintDemo(string variant, int index)
        {
            GameObject owner = new GameObject($"PrintDemo_{variant}");
            owner.transform.position = CellPosition(index);

            // Prints are dark grooves with a lit rim: on the sheet's dark background they would be
            // invisible, so each cell gets a patch of ground to lie on.
            GameObject ground = new GameObject("Ground");
            ground.transform.SetParent(owner.transform, false);
            Sprite plate = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            SpriteRenderer plateRenderer = ground.AddComponent<SpriteRenderer>();
            plateRenderer.sprite = plate;
            plateRenderer.color = new Color(0.62f, 0.56f, 0.44f, 1f);
            plateRenderer.sortingOrder = 0;
            Vector2 plateSize = plate.bounds.size;
            ground.transform.localScale = new Vector3(CellSize / plateSize.x, CellSize / plateSize.y, 1f);
            Debug.Log($"[vfxprint] {variant}: plate sprite={plate.name} texture={plate.texture.name} " +
                      $"shader={plateRenderer.sharedMaterial?.shader?.name} bounds={plate.bounds.size}");

            TankTrackMarkEmitter emitter = owner.AddComponent<TankTrackMarkEmitter>();
            Sprite markSprite = Resources.Load<Sprite>(CombatVfxEffects.MarkSpritePath(variant));
            emitter.Configure(markSprite, 32);

            // A print that renders as a light blob instead of a dark groove means the imported texture lost
            // its colour, and the format is the only thing that says so from inside Unity.
            Debug.Log($"[vfxprint] {variant}: sprite={markSprite?.name} " +
                      $"texture={markSprite?.texture?.name} format={markSprite?.texture?.format}");

            const float separation = 0.42f;
            const float width = 0.17f;
            const float length = 0.21f;
            const int prints = 9;
            // The same dark tint the rig uses: a print is a groove, and the colour comes from data because
            // the sprite pipeline did not reproduce the texture's own dark pixels.
            Color color = new Color(0.17f, 0.15f, 0.12f, 0.85f);
            for (int step = 0; step < prints; step++)
            {
                float amount = step / (prints - 1f);
                float y = Mathf.Lerp(-0.45f, 0.45f, amount);
                float bend = Mathf.Lerp(-0.14f, 0.14f, amount * amount);
                Quaternion rotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-9f, 19f, amount));
                Vector3 centre = owner.transform.position + new Vector3(bend, y, 0f);
                emitter.Emit(centre + new Vector3(-separation * 0.5f, 0f, 0f), rotation, width, length, color, 600f);
                emitter.Emit(centre + new Vector3(separation * 0.5f, 0f, 0f), rotation, width, length, color, 600f);
            }

            GameObject container = GameObject.Find($"TrackMarks_{owner.name}");
            SpriteRenderer[] drawn = container != null
                ? container.GetComponentsInChildren<SpriteRenderer>()
                : new SpriteRenderer[0];
            SpriteRenderer firstRenderer = drawn.Length > 0 ? drawn[0] : null;
            Debug.Log($"[vfxprint] {variant}: emitted={emitter.EmittedCount} skipped={emitter.SkippedCount} " +
                      $"container={(container != null ? container.name : "missing")} renderers={drawn.Length} " +
                      $"drawnSprite={firstRenderer?.sprite?.name} texture={firstRenderer?.sprite?.texture?.name} " +
                      $"color={firstRenderer?.color} shader={firstRenderer?.sharedMaterial?.shader?.name} " +
                      $"order={firstRenderer?.sortingOrder} scale={firstRenderer?.transform.localScale}");

            return owner;
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
                // The print demonstrations place their own sprites and have no particle system to advance.
                if (index >= effects.Length)
                {
                    continue;
                }

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
