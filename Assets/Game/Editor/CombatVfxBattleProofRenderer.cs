using System;
using System.Collections.Generic;
using System.IO;
using LocalTanks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LocalTanks.Editor
{
    /// <summary>
    /// Renders the combat effects attached to real machines at battle scale.
    /// </summary>
    /// <remarks>
    /// The isolated proof sheet answers "does this effect exist and roughly how big is it". It cannot
    /// answer the question a player actually asks — whether the fire, the flash and the dust look right
    /// next to a tank of a known length — because an effect alone has no scale reference. This composition
    /// puts a driving machine, a firing machine and a burning wreck into one frame with their effects on
    /// their real anchors, so the sizes can be judged against the hulls.
    /// <para>
    /// It runs in <c>-batchmode</c> without <c>-nographics</c>: the null graphics device discards every
    /// draw call and would produce an empty image.
    /// </para>
    /// </remarks>
    public static class CombatVfxBattleProofRenderer
    {
        public const string OutputPath = "Docs/Media/CombatVfxBattle.png";

        private const int ImageWidth = 1600;
        private const int ImageHeight = 900;
        private const float LoopSettleSeconds = 1.1f;
        private const float StepSeconds = 0.02f;

        private const string LightTankPrefab = "Assets/Game/Prefabs/Tanks/MS1_Player.prefab";
        private const string HeavyTankPrefab = "Assets/Game/Prefabs/Tanks/TigerII_Player.prefab";

        [MenuItem("Local Tanks/Diagnostics/Render Combat Vfx Battle Proof")]
        public static void Render()
        {
            CombatVfxCatalog catalog = CombatVfxCatalog.Load();
            string[] errors = catalog.Validate();
            if (errors.Length > 0)
            {
                throw new InvalidDataException(
                    "Combat VFX profiles are invalid, so nothing can be composed:\n- " + string.Join("\n- ", errors));
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Camera camera = CreateCamera();
            List<GameObject> spawned = new List<GameObject>();
            try
            {
                CreateGround(spawned);
                GameObject target = ComposeDrivingMachine(catalog, spawned);
                ComposeFiringMachine(catalog, target, spawned);
                ComposeBurningWreck(catalog, spawned);

                Prepare(spawned);
                RenderToFile(camera, Path.GetFullPath(OutputPath));
                Debug.Log($"Combat VFX battle proof rendered to {OutputPath}: a driving machine, a firing " +
                          "machine with an impact on the first one, and a burning wreck, all with their " +
                          "effects on their own anchors.");
            }
            finally
            {
                foreach (GameObject item in spawned)
                {
                    if (item != null)
                    {
                        UnityEngine.Object.DestroyImmediate(item);
                    }
                }

                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        /// <summary>
        /// A patch of ground under the whole composition.
        /// </summary>
        /// <remarks>
        /// Prints, dust and smoke are all things that happen to the ground, and judging them against a
        /// dark void is how the first version of this frame made a print look like a painted rail: nothing
        /// in the image said what it was lying on.
        /// </remarks>
        private static void CreateGround(List<GameObject> spawned)
        {
            GameObject ground = new GameObject("Ground");
            spawned.Add(ground);
            Sprite plate = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            SpriteRenderer renderer = ground.AddComponent<SpriteRenderer>();
            renderer.sprite = plate;
            renderer.color = new Color(0.45f, 0.4f, 0.31f, 1f);
            renderer.sortingOrder = -10;
            Vector2 size = plate.bounds.size;
            ground.transform.position = new Vector3(0f, -0.35f, 0f);
            ground.transform.localScale = new Vector3(9f / size.x, 5f / size.y, 1f);
        }

        /// <summary>A machine on the move: prints behind it, dust under both tracks, exhaust from its pipe.</summary>
        private static GameObject ComposeDrivingMachine(CombatVfxCatalog catalog, List<GameObject> spawned)
        {
            GameObject tank = Spawn(LightTankPrefab, new Vector3(-1.55f, 0.55f, 0f), 22f, spawned);
            TankGeometry geometry = Measure(tank);
            MovementVfxProfile movement = catalog.ResolveMovement(7.6f);
            SurfaceVfxProfile surface = catalog.ResolveSurface(TerrainKind.Grass);

            // Prints: the same geometry the rig uses, laid along the path the machine came from.
            GameObject marks = new GameObject("PrintDemo");
            spawned.Add(marks);
            TankTrackMarkEmitter emitter = marks.AddComponent<TankTrackMarkEmitter>();
            Sprite sprite = Resources.Load<Sprite>(CombatVfxEffects.MarkSpritePath(
                CombatVfxEffects.MarkVariantFor(movement.id)));
            emitter.Configure(sprite, 64);

            float spacing = movement.markSpacingFraction * geometry.Length;
            float width = movement.markWidthFraction * geometry.TrackSeparation;
            float length = movement.markLengthFraction * geometry.Length;
            Color markColor = surface.markTint.ToColor();
            markColor.a = movement.markOpacity;
            Vector3 back = -geometry.Forward;
            for (int step = 0; step < 14; step++)
            {
                Vector3 centre = geometry.Centre + (Vector3)(back * (step * spacing));
                Quaternion rotation = CombatVfxSystem.PlaneRotation(back);
                emitter.Emit(centre + geometry.Right * (-geometry.TrackSeparation * 0.5f), rotation, width, length, markColor, 600f);
                emitter.Emit(centre + geometry.Right * (geometry.TrackSeparation * 0.5f), rotation, width, length, markColor, 600f);
            }

            // Dust under both tracks and the exhaust plume at the machine's own outlet.
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 position = geometry.Centre + geometry.Right * (side * geometry.TrackSeparation * 0.5f);
                AddLoop(spawned, tank.transform, CombatVfxEffects.TrackDust, $"Dust{side}", position,
                    movement.dustRate * 0.85f, movement.dustSizeFraction * geometry.Width, surface.dustTint.ToColor());
            }

            AddLoop(spawned, tank.transform, CombatVfxEffects.ExhaustSmoke, "Exhaust",
                geometry.ExhaustPosition, movement.exhaustStartPuffMultiplier * movement.exhaustLoadedRate,
                0.34f, new Color(0.42f, 0.41f, 0.4f, 1f));

            return tank;
        }

        /// <summary>A machine firing, and the round landing on the machine that is driving.</summary>
        private static void ComposeFiringMachine(CombatVfxCatalog catalog, GameObject target, List<GameObject> spawned)
        {
            GameObject tank = Spawn(HeavyTankPrefab, new Vector3(1.9f, -0.35f, 0f), -28f, spawned);
            TankGeometry geometry = Measure(tank);
            WeaponVfxProfile weapon = catalog.ResolveWeapon(88f);
            MovementVfxProfile movement = catalog.ResolveMovement(69.8f);

            Transform muzzle = FindDeep(tank.transform, "Muzzle");
            if (muzzle == null)
            {
                throw new InvalidOperationException($"{HeavyTankPrefab} has no Muzzle anchor to fire from.");
            }

            Quaternion aim = CombatVfxSystem.PlaneRotation(muzzle.up);
            AddOneShot(spawned, tank.transform, CombatVfxEffects.MuzzleFlash, "Flash",
                muzzle.position, aim, weapon.flashScale, 0.045f);
            AddOneShot(spawned, tank.transform, CombatVfxEffects.MuzzleSmoke, "MuzzleSmoke",
                muzzle.position, aim, weapon.smokeScale, 0.3f, weapon.smokeBurst);
            AddLoop(spawned, tank.transform, CombatVfxEffects.BarrelSmoke, "BarrelSmoke",
                muzzle.position, 7f, 0f, null);
            AddLoop(spawned, tank.transform, CombatVfxEffects.ExhaustSmoke, "Exhaust",
                geometry.ExhaustPosition, movement.exhaustIdleRate, 0f, null);

            // The hit sits on the light machine's hull, where a blocked round would land.
            TankGeometry targetGeometry = Measure(target);
            ImpactVfxProfile impact = catalog.ResolveImpact(ImpactOutcome.Blocked);
            Vector3 hitPoint = targetGeometry.Centre + targetGeometry.Right * (targetGeometry.Width * 0.45f);
            Vector2 hitNormal = targetGeometry.Right;
            Quaternion hitRotation = CombatVfxSystem.PlaneRotation(hitNormal);
            SurfaceVfxProfile surface = catalog.ResolveSurface(TerrainKind.Grass);

            AddOneShot(spawned, target.transform, CombatVfxEffects.ImpactSpark, "Sparks",
                hitPoint, hitRotation, impact.flashScale, 0.1f, impact.sparkCount);
            AddOneShot(spawned, target.transform, CombatVfxEffects.ImpactDust, "ImpactDust",
                hitPoint, hitRotation, impact.dustScale, 0.4f);
            AddOneShot(spawned, target.transform, CombatVfxEffects.ImpactDebris, "ImpactDebris",
                hitPoint, hitRotation, impact.flashScale, 0.25f, impact.debrisCount);
        }

        /// <summary>A wreck: darkened hulls, fire over the engine deck and the smoke of the wreck band.</summary>
        private static void ComposeBurningWreck(CombatVfxCatalog catalog, List<GameObject> spawned)
        {
            GameObject tank = Spawn(LightTankPrefab, new Vector3(-0.75f, -1.55f, 0f), 58f, spawned);
            TankGeometry geometry = Measure(tank);
            DamageVfxProfile wreck = catalog.ResolveDamage(0f, true);
            MovementVfxProfile movement = catalog.ResolveMovement(7.6f);

            // The same grey the destroyed state applies, so the wreck reads as one.
            foreach (SpriteRenderer renderer in tank.GetComponentsInChildren<SpriteRenderer>(true))
            {
                renderer.color = new Color(0.25f, 0.25f, 0.25f, renderer.color.a);
            }

            // Emission over the engine deck, which is the rear quarter of the hull. A negative rate, zero
            // size and null tint mean "keep what the prefab was generated with": the fire's soot, glow,
            // low and tall tongues, hot core and embers retain their own rates, sizes and colours.
            Vector3 deck = geometry.Centre + geometry.Forward * (-geometry.Length * 0.3f);
            AddLoop(spawned, tank.transform, CombatVfxEffects.DamageFire, "Fire",
                deck, -1f, 0f, null);

            AddLoop(spawned, tank.transform, CombatVfxEffects.DamageSmoke, "WreckSmoke", deck,
                wreck.smokeRate, wreck.smokeSizeFraction * geometry.Width, new Color(0.28f, 0.27f, 0.26f, wreck.smokeOpacity));
        }

        private static GameObject Spawn(string prefabPath, Vector3 position, float rotationDegrees, List<GameObject> spawned)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                throw new FileNotFoundException($"Tank prefab was not found: {prefabPath}");
            }

            GameObject tank = UnityEngine.Object.Instantiate(prefab, position, Quaternion.Euler(0f, 0f, rotationDegrees));
            spawned.Add(tank);
            return tank;
        }

        /// <summary>
        /// The machine's own geometry, read from the collider and the anchors the prefab builder measured,
        /// so the effects land where the rig would put them in a battle.
        /// </summary>
        private static TankGeometry Measure(GameObject tank)
        {
            Vector2 size = new Vector2(1f, 1f);
            PolygonCollider2D collider = tank.GetComponent<PolygonCollider2D>();
            if (collider != null && collider.points != null && collider.points.Length >= 3)
            {
                Vector2 minimum = collider.points[0] + collider.offset;
                Vector2 maximum = minimum;
                foreach (Vector2 point in collider.points)
                {
                    Vector2 shifted = point + collider.offset;
                    minimum = Vector2.Min(minimum, shifted);
                    maximum = Vector2.Max(maximum, shifted);
                }

                size = maximum - minimum;
            }

            TankVfxRig rig = tank.GetComponent<TankVfxRig>();
            SerializedObject serialized = new SerializedObject(rig);
            float separation = serialized.FindProperty("measuredTrackSeparation")?.floatValue ?? 0f;
            if (separation <= 0.01f)
            {
                separation = size.x * 0.78f;
            }

            Transform exhaust = tank.transform.Find(TankVfxRig.ExhaustAnchorName);
            Vector3 exhaustPosition = exhaust != null
                ? exhaust.position
                : tank.transform.TransformPoint(new Vector3(size.x * 0.3f, -size.y * 0.4f, 0f));

            return new TankGeometry(tank.transform.position, tank.transform.up, tank.transform.right,
                size, separation, exhaustPosition);
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name == name)
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Attaches a persistent effect at a world position, as a child of the machine so it follows the
        /// same rotation the game would give it, and returns the instance for later simulation. A negative
        /// rate and a zero size leave the values the prefab was generated with.
        /// </summary>
        private static GameObject AddLoop(
            List<GameObject> spawned,
            Transform parent,
            string effectId,
            string name,
            Vector3 worldPosition,
            float rate,
            float size,
            Color? tint)
        {
            GameObject instance = UnityEngine.Object.Instantiate(
                LoadEffect(effectId), worldPosition, parent.rotation, parent);
            instance.name = name;
            instance.transform.position = worldPosition;
            spawned.Add(instance);

            foreach (ParticleSystem system in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                system.Play(true);
                ParticleSystem.MainModule main = system.main;
                if (size > 0.01f)
                {
                    main.startSize = size;
                }

                if (tint.HasValue)
                {
                    main.startColor = tint.Value;
                }
            }

            SetEmission(instance, rate);
            return instance;
        }

        private static GameObject AddOneShot(
            List<GameObject> spawned,
            Transform parent,
            string effectId,
            string name,
            Vector3 worldPosition,
            Quaternion rotation,
            float scale,
            float simulateSeconds,
            int count = 0,
            Color? tint = null)
        {
            GameObject instance = UnityEngine.Object.Instantiate(LoadEffect(effectId), worldPosition, rotation, parent);
            instance.name = name;
            instance.transform.position = worldPosition;
            instance.transform.localScale = Vector3.one * Mathf.Max(0.05f, scale);
            spawned.Add(instance);

            foreach (ParticleSystem system in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                system.Clear(true);
                if (count > 0)
                {
                    ParticleSystem.EmissionModule emission = system.emission;
                    if (emission.burstCount > 0)
                    {
                        ParticleSystem.Burst[] bursts = new ParticleSystem.Burst[emission.burstCount];
                        emission.GetBursts(bursts);
                        bursts[0].count = count;
                        emission.SetBursts(bursts);
                    }
                }

                if (tint.HasValue)
                {
                    ParticleSystem.MainModule main = system.main;
                    main.startColor = tint.Value;
                }

                system.Simulate(0.001f, true, true);
                system.Simulate(simulateSeconds, true, false);
            }

            return instance;
        }

        /// <summary>
        /// Advanced every persistent effect to a steady state. A negative rate leaves the rates the prefab
        /// was generated with, which is what the layered fire needs.
        /// </summary>
        private static void Prepare(List<GameObject> spawned)
        {
            foreach (GameObject instance in spawned)
            {
                if (instance == null)
                {
                    continue;
                }

                foreach (ParticleSystem system in instance.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (system.emission.burstCount > 0 && system.main.loop == false)
                    {
                        continue;   // a one-shot was already simulated to its own moment
                    }

                    system.Simulate(0.001f, true, true);
                    for (float elapsed = 0f; elapsed < LoopSettleSeconds; elapsed += StepSeconds)
                    {
                        system.Simulate(StepSeconds, true, false);
                    }
                }
            }
        }

        private static void SetEmission(GameObject instance, float rate)
        {
            foreach (ParticleSystem system in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (rate < 0f)
                {
                    ParticleSystem.EmissionModule enabled = system.emission;
                    enabled.enabled = true;
                    continue;
                }

                ParticleSystem.EmissionModule emission = system.emission;
                emission.rateOverTime = Mathf.Max(0.01f, rate);
                emission.enabled = true;
            }
        }

        private static GameObject LoadEffect(string effectId)
        {
            GameObject prefab = Resources.Load<GameObject>($"{CombatVfxEffects.ResourceFolder}/{effectId}");
            if (prefab == null)
            {
                throw new FileNotFoundException(
                    $"Effect '{effectId}' has no prefab; run 'Local Tanks/Build Combat Vfx Assets' first.");
            }

            return prefab;
        }

        private static Camera CreateCamera()
        {
            GameObject cameraObject = new GameObject("VfxBattleCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 2.45f;
            camera.aspect = (float)ImageWidth / ImageHeight;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.24f, 0.23f, 0.2f);
            camera.transform.position = new Vector3(0f, -0.35f, -10f);
            camera.transform.rotation = Quaternion.identity;
            camera.cullingMask = ~0;
            return camera;
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

        private readonly struct TankGeometry
        {
            public TankGeometry(
                Vector3 centre,
                Vector3 forward,
                Vector3 right,
                Vector2 size,
                float trackSeparation,
                Vector3 exhaustPosition)
            {
                Centre = centre;
                Forward = forward;
                Right = right;
                Size = size;
                TrackSeparation = trackSeparation;
                ExhaustPosition = exhaustPosition;
            }

            public Vector3 Centre { get; }
            public Vector3 Forward { get; }
            public Vector3 Right { get; }
            public Vector2 Size { get; }
            public float TrackSeparation { get; }
            public Vector3 ExhaustPosition { get; }

            public float Width => Size.x;
            public float Length => Size.y;
        }
    }
}
