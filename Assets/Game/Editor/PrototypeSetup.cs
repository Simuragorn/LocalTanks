using System.Collections.Generic;
using System.IO;
using System.Linq;
using LocalTanks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LocalTanks.Editor
{
    public static class PrototypeSetup
    {
        private const string ProjectilePrefabPath = "Assets/Game/Prefabs/Combat/PrototypeProjectile.prefab";
        private const string ScenePath = "Assets/Game/Scenes/Battle_TestRange.unity";

        private static readonly TankBuildSpec[] TankSpecs =
        {
            new TankBuildSpec(
                "TigerII", "Tiger II",
                "Assets/LocalOnly/WoTGenerated/G16_PzVIB_Tiger_II/G16_PzVIB_Tiger_II_strip2.png",
                "Assets/Game/GameData/Generated/Tanks/tiger_ii.asset",
                "Assets/Game/Prefabs/Tanks/TigerII_Player.prefab",
                new Rect(24f, 139f, 419f, 802f), new Rect(467f, 24f, 278f, 1031f),
                new Vector2(0.4985709f, 0.7753074f), new Vector2(0.4950269f, 0.4900504f),
                new Vector2(0.24f, 0.24f), 1.41f,
                CreateTigerHullOutline()),
            new TankBuildSpec(
                "MS1", "MS-1",
                "Assets/LocalOnly/WoTGenerated/R11_MS-1/R11_MS-1_strip2.png",
                "Assets/Game/GameData/Generated/Tanks/ms_1.asset",
                "Assets/Game/Prefabs/Tanks/MS1_Player.prefab",
                new Rect(24f, 24f, 192f, 476f), new Rect(240f, 117f, 120f, 289f),
                new Vector2(0.5f, 0.6802735f), new Vector2(0.5f, 0.4236555f),
                new Vector2(0.24f, 0.24f), 0.45f,
                CreateMS1HullOutline()),
            new TankBuildSpec(
                "Leichttraktor", "Leichttraktor",
                "Assets/LocalOnly/WoTGenerated/G12_Ltraktor/G12_Ltraktor_strip2.png",
                "Assets/Game/GameData/Generated/Tanks/leichttraktor.asset",
                "Assets/Game/Prefabs/Tanks/Leichttraktor_Player.prefab",
                new Rect(24f, 24f, 229f, 462f), new Rect(277f, 117f, 158f, 275f),
                new Vector2(0.4986667f, 0.7045380f), new Vector2(0.5f, 0.6946704f),
                new Vector2(0.24f, 0.24f), 0.48f,
                CreateLeichttraktorHullOutline()),
            new TankBuildSpec(
                "T1Cunningham", "T1 Cunningham",
                "Assets/LocalOnly/WoTGenerated/A01_T1_Cunningham/A01_T1_Cunningham_strip2.png",
                "Assets/Game/GameData/Generated/Tanks/t1_cunningham.asset",
                "Assets/Game/Prefabs/Tanks/T1Cunningham_Player.prefab",
                new Rect(24f, 24f, 195f, 422f), new Rect(243f, 134f, 127f, 202f),
                new Vector2(0.5005221f, 0.6842200f), new Vector2(0.5025058f, 0.6974256f),
                new Vector2(0.42f, 0.42f), 1.14f,
                CreateT1CunninghamHullOutline())        };

        [InitializeOnLoadMethod]
        private static void SchedulePrototypeAssetUpgrade()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                {
                    return;
                }

                if (TankSpecs.All(spec => AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath) != null))
                {
                    return;
                }

                // A newly added build spec has no prefab yet, so this guard only skips the build
                // when every configured tank is already present.
                try
                {
                    BuildCombatAssets();
                }
                catch (System.Exception exception)
                {
                    Debug.LogException(exception);
                }
            };
        }

        [MenuItem("Local Tanks/Build Test Range")]
        [MenuItem("Local Tanks/Build Sprint 002 Prototype")]
        public static void BuildAll()
        {
            GameObject[] tankPrefabs = BuildCombatAssetsInternal();
            TankBuildSpec[] availableSpecs = TankSpecs
                .Where(spec => LoadDefinition(spec).availableInGame)
                .ToArray();
            GameObject[] availablePrefabs = availableSpecs
                .Select(spec => tankPrefabs.Single(prefab => AssetDatabase.GetAssetPath(prefab) == spec.PrefabPath))
                .ToArray();

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                CreateTestScene();
            }

            UpgradeTestScene(availableSpecs, availablePrefabs);
            EnsureSceneInBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"Local Tanks test range created with {availablePrefabs.Length} selectable tanks: {ScenePath}");
        }

        [MenuItem("Local Tanks/Build Combat Assets")]
        public static void BuildCombatAssets()
        {
            GameObject[] tankPrefabs = BuildCombatAssetsInternal();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"Local Tanks combat assets created with {tankPrefabs.Length} tank prefabs.");
            LogHullOutlineSummary();
        }

        /// <summary>
        /// Prints the hull size against its sprite for every tank. The numbers are already validated,
        /// so this is the record that makes a wrong-looking hull diagnosable from the build log alone
        /// without opening the scene.
        /// </summary>
        private static void LogHullOutlineSummary()
        {
            foreach (TankBuildSpec spec in TankSpecs)
            {
                Vector2 expected = ExpectedHullSize(spec);
                Vector2 actual = MeasuredHullSize(spec);
                Debug.Log($"Hull outline '{spec.DisplayName}': collider {actual.x:0.###} x {actual.y:0.###}, " +
                          $"sprite {expected.x:0.###} x {expected.y:0.###}, points {spec.HullOutline.Length}.");
            }
        }

        private static GameObject[] BuildCombatAssetsInternal()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (TankBuildSpec spec in TankSpecs)
            {
                spec.LoadCanonicalLayout();
                ValidateHullOutline(spec);
                ConfigureTankTexture(spec);
            }

            CombatDefinitionImporter.Reimport();
            Projectile2D projectile = CreateProjectilePrefab();
            GameObject[] tankPrefabs = TankSpecs
                .Select(spec => CreateTankPrefab(spec, LoadDefinition(spec), projectile))
                .ToArray();
            return tankPrefabs;
        }

        /// <summary>
        /// Prefab path for a generated tank, or null when no build spec produces that tank.
        /// Scene builders derive their <see cref="TankPrefabBinding"/> arrays from the combat
        /// database through this lookup, so adding a tank needs one build spec entry and nothing
        /// else: no second prefab list and no second tank-id list to keep in sync.
        /// </summary>
        public static string FindTankPrefabPath(string tankId)
        {
            return TankSpecs
                .FirstOrDefault(spec => string.Equals(
                    Path.GetFileNameWithoutExtension(spec.DefinitionPath),
                    tankId,
                    System.StringComparison.OrdinalIgnoreCase))
                ?.PrefabPath;
        }

        /// <summary>
        /// Expected world size of a tank's hull collider: the canonical sprite rectangle multiplied
        /// by the tank's visual scale. Outline points use the same units as that rectangle, so this
        /// is what the generated <c>PolygonCollider2D</c> must span. A contour written in other
        /// units produces a collider that no longer matches the visible hull.
        /// </summary>
        public static bool TryGetExpectedHullSize(string tankId, out Vector2 size)
        {
            size = Vector2.zero;
            TankBuildSpec spec = TankSpecs.FirstOrDefault(item => string.Equals(
                Path.GetFileNameWithoutExtension(item.DefinitionPath),
                tankId,
                System.StringComparison.OrdinalIgnoreCase));
            if (spec == null)
            {
                return false;
            }

            spec.LoadCanonicalLayout();
            size = ExpectedHullSize(spec);
            return true;
        }

        /// <summary>World size the hull contour must span: the sprite rectangle at its visual scale.</summary>
        private static Vector2 ExpectedHullSize(TankBuildSpec spec)
        {
            return new Vector2(
                spec.HullRect.width / 100f * spec.VisualScale.x,
                spec.HullRect.height / 100f * spec.VisualScale.y);
        }

        /// <summary>World size the hull contour actually spans.</summary>
        private static Vector2 MeasuredHullSize(TankBuildSpec spec)
        {
            return new Vector2(
                spec.HullOutline.Max(point => point.x) - spec.HullOutline.Min(point => point.x),
                spec.HullOutline.Max(point => point.y) - spec.HullOutline.Min(point => point.y));
        }

        /// <summary>
        /// Fails the build when a tank's hull contour does not describe its own sprite.
        /// <para>
        /// Contour points are world units equal to half of the sprite rectangle multiplied by the
        /// visual scale, so the contour must span that rectangle. A contour entered in other units
        /// still compiles, still passes validation and still collides with things; the only symptom
        /// is a hull on screen that does not match what the player hits. Checking it here turns that
        /// silent mistake into a build error that names the tank.
        /// </para>
        /// </summary>
        private static void ValidateHullOutline(TankBuildSpec spec)
        {
            Vector2 expected = ExpectedHullSize(spec);
            Vector2 actual = MeasuredHullSize(spec);

            const float tolerance = 0.08f;
            if (Mathf.Abs(actual.x - expected.x) > expected.x * tolerance ||
                Mathf.Abs(actual.y - expected.y) > expected.y * tolerance)
            {
                throw new System.InvalidOperationException(
                    $"Hull contour for '{spec.DisplayName}' is {actual.x:0.###} by {actual.y:0.###} but its " +
                    $"sprite rectangle at the configured visual scale is {expected.x:0.###} by {expected.y:0.###}. " +
                    "Outline points must be world units equal to half of the sprite rectangle multiplied by the " +
                    "visual scale; a contour written in other units produces a collider that does not match the hull.");
            }

            if (!Mathf.Approximately(spec.VisualScale.x, spec.VisualScale.y))
            {
                throw new System.InvalidOperationException(
                    $"Hull visual scale for '{spec.DisplayName}' is {spec.VisualScale.x} by {spec.VisualScale.y}. " +
                    "Use one uniform scale: a mixed scale stretches the sprite away from the collider that is " +
                    "derived from the same rectangle.");
            }

            if (spec.HullOutline.Length < 10)
            {
                throw new System.InvalidOperationException(
                    $"Hull contour for '{spec.DisplayName}' has {spec.HullOutline.Length} points. The visible " +
                    "hull outline needs at least ten points so it does not collapse into a rectangle.");
            }
        }

        private static TankDefinition LoadDefinition(TankBuildSpec spec)
        {
            TankDefinition definition = AssetDatabase.LoadAssetAtPath<TankDefinition>(spec.DefinitionPath);
            if (definition == null)
            {
                throw new System.InvalidOperationException($"Tank definition was not generated: {spec.DefinitionPath}");
            }

            return definition;
        }

        private static void ConfigureTankTexture(TankBuildSpec spec)
        {
            TextureImporter importer = AssetImporter.GetAtPath(spec.TexturePath) as TextureImporter;
            if (importer == null)
            {
                throw new System.IO.FileNotFoundException("Tank source texture was not found.", spec.TexturePath);
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = 100f;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            SpriteDataProviderFactories factories = new SpriteDataProviderFactories();
            factories.Init();
            ISpriteEditorDataProvider provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();

            string hullName = spec.SpritePrefix + "_Hull";
            string turretName = spec.SpritePrefix + "_Turret";
            GUID hullId = GUID.Generate();
            GUID turretId = GUID.Generate();
            foreach (SpriteRect existingRect in provider.GetSpriteRects())
            {
                if (existingRect.name == hullName) hullId = existingRect.spriteID;
                else if (existingRect.name == turretName) turretId = existingRect.spriteID;
            }

            SpriteRect hull = new SpriteRect
            {
                name = hullName,
                rect = spec.HullRect,
                alignment = SpriteAlignment.Custom,
                pivot = new Vector2(0.5f, 0.5f),
                spriteID = hullId
            };
            SpriteRect turret = new SpriteRect
            {
                name = turretName,
                rect = spec.TurretRect,
                alignment = SpriteAlignment.Custom,
                pivot = spec.TurretPivot,
                spriteID = turretId
            };

            SpriteRect[] spriteRects = { hull, turret };
            provider.SetSpriteRects(spriteRects);
            ISpriteNameFileIdDataProvider nameProvider = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            if (nameProvider != null)
            {
                nameProvider.SetNameFileIdPairs(spriteRects
                    .Select(rect => new SpriteNameFileIdPair(rect.name, rect.spriteID))
                    .ToList());
            }

            provider.Apply();
            importer.SaveAndReimport();
        }

        private static Projectile2D CreateProjectilePrefab()
        {
            GameObject root = new GameObject("PrototypeProjectile");
            try
            {
                SpriteRenderer renderer = root.AddComponent<SpriteRenderer>();
                renderer.sprite = GetBuiltinSprite();
                renderer.color = new Color(1f, 0.8f, 0.22f, 1f);
                renderer.sortingOrder = 20;
                root.transform.localScale = Vector3.one * (0.14f / renderer.sprite.bounds.size.x);
                root.AddComponent<Projectile2D>();
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, ProjectilePrefabPath);
                return prefab.GetComponent<Projectile2D>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject CreateTankPrefab(
            TankBuildSpec spec,
            TankDefinition definition,
            Projectile2D projectilePrefab)
        {
            Dictionary<string, Sprite> sprites = AssetDatabase.LoadAllAssetsAtPath(spec.TexturePath)
                .OfType<Sprite>()
                .ToDictionary(sprite => sprite.name, sprite => sprite);
            if (!sprites.TryGetValue(spec.SpritePrefix + "_Hull", out Sprite hullSprite) ||
                !sprites.TryGetValue(spec.SpritePrefix + "_Turret", out Sprite turretSprite))
            {
                throw new System.InvalidOperationException($"{spec.DisplayName} sprites were not sliced correctly.");
            }

            GameObject root = new GameObject(spec.SpritePrefix + "_Player");
            try
            {
                Rigidbody2D body = root.AddComponent<Rigidbody2D>();
                body.gravityScale = 0f;
                body.mass = definition.massTonnes;
                body.linearDamping = 1f;
                body.angularDamping = 4f;
                body.interpolation = RigidbodyInterpolation2D.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

                PolygonCollider2D collider = root.AddComponent<PolygonCollider2D>();
                collider.points = spec.HullOutline;

                GameObject hullVisual = CreateSpriteChild(root.transform, "HullVisual", hullSprite, 5, spec.VisualScale);
                hullVisual.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);

                GameObject turretPivot = new GameObject("TurretPivot");
                turretPivot.transform.SetParent(root.transform, false);
                turretPivot.transform.localPosition = CalculateTurretMountPosition(spec);
                TurretAiming aiming = turretPivot.AddComponent<TurretAiming>();
                aiming.Configure(definition);

                GameObject turretVisual = CreateSpriteChild(turretPivot.transform, "TurretVisual", turretSprite, 10, spec.VisualScale);
                turretVisual.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);

                GameObject muzzle = new GameObject("Muzzle");
                muzzle.transform.SetParent(turretPivot.transform, false);
                muzzle.transform.localPosition = new Vector3(0f, spec.MuzzleDistance, 0f);

                TankMotor motor = root.AddComponent<TankMotor>();
                motor.Configure(definition);
                GunDispersionController dispersion = root.AddComponent<GunDispersionController>();
                dispersion.Configure(definition, motor, aiming);
                WeaponController weapon = root.AddComponent<WeaponController>();
                weapon.Configure(definition, projectilePrefab, muzzle.transform, dispersion);
                PlayerTankInput input = root.AddComponent<PlayerTankInput>();
                input.Configure(motor, aiming, weapon);
                TankHealth health = root.AddComponent<TankHealth>();
                health.Configure(definition);
                if (HasConfiguredAudio(definition.audio))
                {
                    TankAudioController audio = root.AddComponent<TankAudioController>();
                    audio.Configure(definition, motor, aiming, weapon, health);
                }
                TankArmor armor = root.AddComponent<TankArmor>();
                armor.Configure(definition, health);
                TankDestroyedState destroyedState = root.AddComponent<TankDestroyedState>();
                destroyedState.Configure(health, motor, aiming, weapon, input);

                return PrefabUtility.SaveAsPrefabAsset(root, spec.PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static bool HasConfiguredAudio(TankAudioProfile profile)
        {
            return profile != null &&
                   !string.IsNullOrWhiteSpace(profile.engineClipId) &&
                   !string.IsNullOrWhiteSpace(profile.shotClipId) &&
                   !string.IsNullOrWhiteSpace(profile.turretClipId);
        }

        private static void CreateTestScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "Battle_TestRange";
            Sprite shapeSprite = GetBuiltinSprite();
            CreateArea("Ground", Vector2.zero, new Vector2(30f, 20f), new Color(0.16f, 0.25f, 0.15f), -20, false, shapeSprite);
            CreateArea("Wall_North", new Vector2(0f, 8.5f), new Vector2(26f, 0.8f), Color.gray, 0, true, shapeSprite);
            CreateArea("Wall_South", new Vector2(0f, -8.5f), new Vector2(26f, 0.8f), Color.gray, 0, true, shapeSprite);
            CreateArea("Wall_West", new Vector2(-12.5f, 0f), new Vector2(0.8f, 17f), Color.gray, 0, true, shapeSprite);
            CreateArea("Wall_East", new Vector2(12.5f, 0f), new Vector2(0.8f, 17f), Color.gray, 0, true, shapeSprite);
            CreateArea("Wall_Centre", new Vector2(-9f, 0f), new Vector2(0.8f, 5f), new Color(0.35f, 0.35f, 0.38f), 0, true, shapeSprite);
            CreateArea("Wall_Cover", new Vector2(8f, 1f), new Vector2(3f, 0.8f), new Color(0.35f, 0.35f, 0.38f), 0, true, shapeSprite);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static void UpgradeTestScene(TankBuildSpec[] tankSpecs, GameObject[] tankPrefabs)
        {
            Scene scene = SceneManager.GetActiveScene().path == ScenePath
                ? SceneManager.GetActiveScene()
                : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            MoveObstacle("Wall_Centre", new Vector3(-9f, 0f, 0f));
            MoveObstacle("Wall_Cover", new Vector3(8f, 1f, 0f));
            DestroyNamed("Target");
            DestroyNamed("TigerII_Player");
            DestroyNamed("MS1_Player");
            DestroyNamed("Leichttraktor_Player");
            DestroyNamed("PlayerTankSelector");
            DestroyAllTankTargets(scene);

            GameObject player = (GameObject)PrefabUtility.InstantiatePrefab(tankPrefabs[0], scene);
            player.name = tankSpecs[0].SpritePrefix + "_Player";
            player.transform.SetPositionAndRotation(new Vector3(0f, -5.5f, 0f), Quaternion.identity);

            CameraFollow2D follow = EnsureCamera(player.transform);
            GameObject selectorObject = new GameObject("PlayerTankSelector");

            Vector3[] positions = Enumerable.Range(0, tankPrefabs.Length)
                .Select(index => new Vector3(
                    tankPrefabs.Length == 1
                        ? 0f
                        : Mathf.Lerp(-8.4f, 8.4f, index / (tankPrefabs.Length - 1f)),
                    5.7f,
                    0f))
                .ToArray();
            float[] rotations = Enumerable.Repeat(180f, tankPrefabs.Length).ToArray();
            float[] rotationSpeeds = Enumerable.Range(0, tankPrefabs.Length)
                .Select(index => 7f + index * 1.5f)
                .ToArray();
            string[] targetNames = tankSpecs.Select(spec => spec.SpritePrefix + "_Target").ToArray();
            GameObject[] targets = new GameObject[tankPrefabs.Length];
            for (int index = 0; index < tankPrefabs.Length; index++)
            {
                GameObject target = (GameObject)PrefabUtility.InstantiatePrefab(tankPrefabs[index], scene);
                target.name = targetNames[index];
                target.transform.SetPositionAndRotation(positions[index], Quaternion.Euler(0f, 0f, rotations[index]));
                DisablePlayerControls(target);
                RotatingTankDisplay display = target.AddComponent<RotatingTankDisplay>();
                display.Configure(rotationSpeeds[index]);
                targets[index] = target;
            }

            TestRangeTargetRespawner respawner = selectorObject.AddComponent<TestRangeTargetRespawner>();
            respawner.Configure(tankPrefabs, targetNames, targets, positions, rotations, rotationSpeeds);
            PlayerTankSelector selector = selectorObject.AddComponent<PlayerTankSelector>();
            selector.Configure(
                tankPrefabs,
                tankSpecs.Select(spec => spec.DisplayName).ToArray(),
                player,
                0,
                follow,
                respawner);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static void DestroyAllTankTargets(Scene scene)
        {
            foreach (GameObject missingPrefab in scene.GetRootGameObjects()
                         .Where(PrefabUtility.IsPrefabAssetMissing)
                         .ToArray())
            {
                Object.DestroyImmediate(missingPrefab);
            }

            Transform[] targets = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Where(item => item.name.EndsWith("_Target", System.StringComparison.Ordinal))
                .ToArray();
            foreach (Transform target in targets)
            {
                if (target != null)
                    Object.DestroyImmediate(target.gameObject);
            }
        }

        private static CameraFollow2D EnsureCamera(Transform target)
        {
            Camera camera = Object.FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 7f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.06f, 0.08f, 0.07f);
                cameraObject.transform.position = new Vector3(0f, -2f, -10f);
                cameraObject.AddComponent<AudioListener>();
            }

            CameraFollow2D follow = camera.GetComponent<CameraFollow2D>();
            if (follow == null) follow = camera.gameObject.AddComponent<CameraFollow2D>();
            follow.Configure(target);
            PlayerAimReticle reticle = camera.GetComponent<PlayerAimReticle>();
            if (reticle == null) reticle = camera.gameObject.AddComponent<PlayerAimReticle>();
            reticle.Configure(target);
            return follow;
        }

        private static void DisablePlayerControls(GameObject target)
        {
            PlayerTankInput input = target.GetComponent<PlayerTankInput>();
            TankMotor motor = target.GetComponent<TankMotor>();
            TurretAiming turret = target.GetComponentInChildren<TurretAiming>();
            WeaponController weapon = target.GetComponent<WeaponController>();
            if (input != null) input.enabled = false;
            if (motor != null) motor.enabled = false;
            if (turret != null) turret.enabled = false;
            if (weapon != null) weapon.enabled = false;
        }

        private static void MoveObstacle(string name, Vector3 position)
        {
            GameObject obstacle = GameObject.Find(name);
            if (obstacle != null) obstacle.transform.position = position;
        }

        private static void DestroyNamed(string name)
        {
            GameObject existing = GameObject.Find(name);
            if (existing != null) Object.DestroyImmediate(existing);
        }

        private static void EnsureSceneInBuildSettings()
        {
            List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
            EditorBuildSettingsScene existing = scenes.FirstOrDefault(scene => scene.path == ScenePath);
            if (existing == null) scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            else existing.enabled = true;
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static GameObject CreateSpriteChild(
            Transform parent, string name, Sprite sprite, int sortingOrder, Vector2 scale)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.transform.localScale = new Vector3(scale.x, scale.y, 1f);
            SpriteRenderer renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = sortingOrder;
            return child;
        }

        private static GameObject CreateArea(
            string name, Vector2 position, Vector2 size, Color color,
            int sortingOrder, bool addCollider, Sprite sprite)
        {
            GameObject area = new GameObject(name);
            area.transform.position = position;
            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(area.transform, false);
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            visual.transform.localScale = new Vector3(size.x / sprite.bounds.size.x, size.y / sprite.bounds.size.y, 1f);
            if (addCollider)
            {
                BoxCollider2D collider = area.AddComponent<BoxCollider2D>();
                collider.size = size;
            }

            return area;
        }

        private static Sprite GetBuiltinSprite()
        {
            Sprite sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            if (sprite == null) throw new System.InvalidOperationException("Unity built-in UI sprite was not found.");
            return sprite;
        }

        private static Vector2[] CreateTigerHullOutline()
        {
            return new[]
            {
                new Vector2(-0.37f, 0.955f), new Vector2(0.37f, 0.955f),
                new Vector2(0.47f, 0.91f), new Vector2(0.515f, 0.80f),
                new Vector2(0.525f, 0.62f), new Vector2(0.525f, -0.70f),
                new Vector2(0.50f, -0.84f), new Vector2(0.43f, -0.925f),
                new Vector2(0.34f, -0.955f), new Vector2(-0.34f, -0.955f),
                new Vector2(-0.43f, -0.925f), new Vector2(-0.50f, -0.84f),
                new Vector2(-0.525f, -0.70f), new Vector2(-0.525f, 0.62f),
                new Vector2(-0.515f, 0.80f), new Vector2(-0.47f, 0.91f)
            };
        }

        private static Vector2[] CreateMS1HullOutline()
        {
            return new[]
            {
                new Vector2(-0.20f, 0.57f), new Vector2(0.20f, 0.57f),
                new Vector2(0.23f, 0.52f), new Vector2(0.23f, 0.22f),
                new Vector2(0.22f, 0.02f), new Vector2(0.21f, -0.20f),
                new Vector2(0.18f, -0.32f), new Vector2(0.10f, -0.36f),
                new Vector2(0.10f, -0.56f), new Vector2(-0.10f, -0.56f),
                new Vector2(-0.10f, -0.36f), new Vector2(-0.18f, -0.32f),
                new Vector2(-0.21f, -0.20f), new Vector2(-0.22f, 0.02f),
                new Vector2(-0.23f, 0.22f), new Vector2(-0.23f, 0.52f)
            };
        }

        private static Vector2[] CreateLeichttraktorHullOutline()
        {
            return new[]
            {
                new Vector2(-0.22f, 0.55f), new Vector2(0.22f, 0.55f),
                new Vector2(0.26f, 0.50f), new Vector2(0.27f, 0.34f),
                new Vector2(0.27f, -0.34f), new Vector2(0.25f, -0.50f),
                new Vector2(0.21f, -0.55f), new Vector2(-0.21f, -0.55f),
                new Vector2(-0.25f, -0.50f), new Vector2(-0.27f, -0.34f),
                new Vector2(-0.27f, 0.34f), new Vector2(-0.26f, 0.50f)
            };
        }

        /// <summary>
        /// The T1E1 hull is a straight-sided box: its silhouette in the rendered sprite keeps a
        /// nearly constant width, because the tracks form both edges. The measured half width is
        /// 0.995 of the hull rectangle, so the contour adds only the chamfers a box collider would
        /// lose rather than collapsing the hull into a rectangle.
        /// <para>
        /// Contour points are world units equal to half of the sprite rectangle multiplied by the
        /// visual scale, exactly as the other tanks express them: the hull rectangle is 195 by 422
        /// pixels at 0.42 units per hundred pixels, so the half extents are 0.4095 and 0.8862.
        /// The visual scale stays uniform, because a mixed scale would stretch the art away from
        /// the collider that is derived from it.
        /// </para>
        /// </summary>
        private static Vector2[] CreateT1CunninghamHullOutline()
        {
            const float halfWidth = 0.4095f;
            const float halfLength = 0.8862f;
            return new[]
            {
                new Vector2(-halfWidth * 0.91f, halfLength), new Vector2(halfWidth * 0.91f, halfLength),
                new Vector2(halfWidth * 0.975f, halfLength * 0.954f), new Vector2(halfWidth, halfLength * 0.872f),
                new Vector2(halfWidth, -halfLength * 0.872f), new Vector2(halfWidth * 0.975f, -halfLength * 0.954f),
                new Vector2(halfWidth * 0.91f, -halfLength), new Vector2(-halfWidth * 0.91f, -halfLength),
                new Vector2(-halfWidth * 0.975f, -halfLength * 0.954f), new Vector2(-halfWidth, -halfLength * 0.872f),
                new Vector2(-halfWidth, halfLength * 0.872f), new Vector2(-halfWidth * 0.975f, halfLength * 0.954f)
            };
        }

        private static Vector3 CalculateTurretMountPosition(TankBuildSpec spec)
        {
            Vector2 sourcePixelOffset = new Vector2(
                (spec.HullTurretMount.x - 0.5f) * spec.HullRect.width,
                (spec.HullTurretMount.y - 0.5f) * spec.HullRect.height);
            Vector2 scaledOffset = Vector2.Scale(sourcePixelOffset / 100f, spec.VisualScale);
            float localX = Mathf.Approximately(scaledOffset.x, 0f) ? 0f : -scaledOffset.x;
            float localY = Mathf.Approximately(scaledOffset.y, 0f) ? 0f : -scaledOffset.y;
            return new Vector3(localX, localY, 0f);
        }

        private sealed class TankBuildSpec
        {
            public TankBuildSpec(
                string spritePrefix, string displayName, string texturePath,
                string definitionPath, string prefabPath, Rect hullRect, Rect turretRect,
                Vector2 turretPivot, Vector2 hullTurretMount, Vector2 visualScale, float muzzleDistance,
                Vector2[] hullOutline)
            {
                SpritePrefix = spritePrefix;
                DisplayName = displayName;
                TexturePath = texturePath;
                DefinitionPath = definitionPath;
                PrefabPath = prefabPath;
                HullRect = hullRect;
                TurretRect = turretRect;
                TurretPivot = turretPivot;
                HullTurretMount = hullTurretMount;
                VisualScale = visualScale;
                MuzzleDistance = muzzleDistance;
                HullOutline = hullOutline;
            }

            public string SpritePrefix { get; }
            public string DisplayName { get; }
            public string TexturePath { get; }
            public string DefinitionPath { get; }
            public string PrefabPath { get; }
            public Rect HullRect { get; private set; }
            public Rect TurretRect { get; private set; }
            public Vector2 TurretPivot { get; private set; }
            public Vector2 HullTurretMount { get; private set; }
            public Vector2 VisualScale { get; }
            public float MuzzleDistance { get; private set; }
            public Vector2[] HullOutline { get; }

            public void LoadCanonicalLayout()
            {
                string directory = Path.GetDirectoryName(TexturePath)?.Replace('\\', '/');
                string vehicleId = string.IsNullOrEmpty(directory) ? string.Empty : Path.GetFileName(directory);
                string layoutPath = $"{directory}/{vehicleId}_layout.json";
                string absolutePath = Path.GetFullPath(layoutPath);
                if (!File.Exists(absolutePath))
                    throw new System.InvalidOperationException($"Canonical sprite layout was not found: {layoutPath}");

                SpriteLayoutJson layout = JsonUtility.FromJson<SpriteLayoutJson>(File.ReadAllText(absolutePath));
                if (layout == null || layout.schemaVersion != 2 || layout.hull == null || layout.turret == null)
                    throw new InvalidDataException($"Unsupported or incomplete sprite layout: {layoutPath}");

                HullRect = layout.hull.ToRect();
                TurretRect = layout.turret.ToRect();
                TurretPivot = layout.turret.pivot.ToVector2();
                HullTurretMount = layout.hull.turretMount.ToVector2();
                MuzzleDistance = layout.turret.height * TurretPivot.y / 100f * VisualScale.y;
            }
        }

        [System.Serializable]
        private sealed class SpriteLayoutJson
        {
            public int schemaVersion;
            public SpriteLayoutPartJson hull;
            public SpriteLayoutPartJson turret;
        }

        [System.Serializable]
        private sealed class SpriteLayoutPartJson
        {
            public float x;
            public float y;
            public float width;
            public float height;
            public SpriteLayoutPointJson pivot;
            public SpriteLayoutPointJson turretMount;

            public Rect ToRect() => new Rect(x, y, width, height);
        }

        [System.Serializable]
        private sealed class SpriteLayoutPointJson
        {
            public float x;
            public float y;

            public Vector2 ToVector2() => new Vector2(x, y);
        }
    }
}
