using System.Collections.Generic;
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
                "Assets/Game/Art/Tanks/TigerII/Source/Tiger-II_strip2.png",
                "Assets/Game/GameData/Generated/Tanks/tiger_ii.asset",
                "Assets/Game/Prefabs/Tanks/TigerII_Player.prefab",
                new Rect(24f, 139f, 419f, 802f), new Rect(467f, 24f, 278f, 1031f),
                new Vector2(0.4985709f, 0.7753074f), new Vector2(0.4950269f, 0.4900504f),
                new Vector2(0.24f, 0.24f), 1.41f, 69.8f,
                CreateTigerHullOutline()),
            new TankBuildSpec(
                "E100", "E-100",
                "Assets/Game/Art/Tanks/E100/Source/E-100_strip2.png",
                "Assets/Game/GameData/Generated/Tanks/e_100.asset",
                "Assets/Game/Prefabs/Tanks/E100_Player.prefab",
                new Rect(22f, 39f, 117f, 227f), new Rect(145f, 33f, 80f, 188f),
                new Vector2(0.5f, 0.75f), new Vector2(0.5f, 0.5f),
                new Vector2(1.01f, 1f), 1.40f, 140f,
                CreateE100HullOutline()),
            new TankBuildSpec(
                "T34", "T-34/76",
                "Assets/Game/Art/Tanks/T34/Source/T34_strip2.png",
                "Assets/Game/GameData/Generated/Tanks/t_34_76.asset",
                "Assets/Game/Prefabs/Tanks/T34_Player.prefab",
                new Rect(9f, 11f, 127f, 268f), new Rect(153f, 25f, 85f, 197f),
                new Vector2(0.5f, 0.75f), new Vector2(0.5f, 0.5f),
                new Vector2(0.63f, 0.65f), 0.95f, 26.5f,
                CreateT34HullOutline()),
            new TankBuildSpec(
                "PzKpfwIV", "Panzer IV",
                "Assets/Game/Art/Tanks/PzKpfwIV/Source/Pz.Kpfw.IV_strip2.png",
                "Assets/Game/GameData/Generated/Tanks/panzer_iv.asset",
                "Assets/Game/Prefabs/Tanks/PzKpfwIV_Player.prefab",
                new Rect(14f, 17f, 121f, 260f), new Rect(146f, 16f, 89f, 199f),
                new Vector2(0.5f, 0.75f), new Vector2(0.5f, 0.5f),
                new Vector2(0.63f, 0.71f), 1.05f, 20f,
                CreatePanzerIVHullOutline()),
            new TankBuildSpec(
                "BT2", "BT-2",
                "Assets/Game/Art/Tanks/BT2/Source/BT-2_strip2.png",
                "Assets/Game/GameData/Generated/Tanks/bt_2.asset",
                "Assets/Game/Prefabs/Tanks/BT2_Player.prefab",
                new Rect(12f, 16f, 122f, 261f), new Rect(156f, 31f, 74f, 189f),
                new Vector2(0.5f, 0.75f), new Vector2(0.5f, 0.5f),
                new Vector2(0.475f, 0.55f), 0.77f, 11.3f,
                CreateBT2HullOutline()),
            new TankBuildSpec(
                "MS1", "MS-1",
                "Assets/Game/Art/Tanks/MS1/Source/MS-1_strip2.png",
                "Assets/Game/GameData/Generated/Tanks/ms_1.asset",
                "Assets/Game/Prefabs/Tanks/MS1_Player.prefab",
                new Rect(24f, 24f, 192f, 476f), new Rect(240f, 117f, 120f, 289f),
                new Vector2(0.5f, 0.6802735f), new Vector2(0.5f, 0.4236555f),
                new Vector2(0.24f, 0.24f), 0.45f, 5.5f,
                CreateMS1HullOutline())
        };

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

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                CreateTestScene();
            }

            UpgradeTestScene(tankPrefabs);
            EnsureSceneInBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"Local Tanks test range created with {tankPrefabs.Length} selectable tanks: {ScenePath}");
        }

        [MenuItem("Local Tanks/Build Combat Assets")]
        public static void BuildCombatAssets()
        {
            GameObject[] tankPrefabs = BuildCombatAssetsInternal();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"Local Tanks combat assets created with {tankPrefabs.Length} tank prefabs.");
        }

        private static GameObject[] BuildCombatAssetsInternal()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (TankBuildSpec spec in TankSpecs)
            {
                ConfigureTankTexture(spec);
            }

            CombatDefinitionImporter.Reimport();
            Projectile2D projectile = CreateProjectilePrefab();
            GameObject[] tankPrefabs = TankSpecs
                .Select(spec => CreateTankPrefab(spec, LoadDefinition(spec), projectile))
                .ToArray();
            return tankPrefabs;
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
                body.mass = spec.Mass;
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
                WeaponController weapon = root.AddComponent<WeaponController>();
                weapon.Configure(definition, projectilePrefab, muzzle.transform);
                PlayerTankInput input = root.AddComponent<PlayerTankInput>();
                input.Configure(motor, aiming, weapon);
                TankHealth health = root.AddComponent<TankHealth>();
                health.Configure(definition);
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

        private static void UpgradeTestScene(GameObject[] tankPrefabs)
        {
            Scene scene = SceneManager.GetActiveScene().path == ScenePath
                ? SceneManager.GetActiveScene()
                : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            MoveObstacle("Wall_Centre", new Vector3(-9f, 0f, 0f));
            MoveObstacle("Wall_Cover", new Vector3(8f, 1f, 0f));
            DestroyNamed("Target");
            DestroyNamed("TigerII_Player");
            DestroyNamed("PlayerTankSelector");
            foreach (TankBuildSpec spec in TankSpecs)
            {
                DestroyNamed(spec.SpritePrefix + "_Target");
            }

            GameObject player = (GameObject)PrefabUtility.InstantiatePrefab(tankPrefabs[0], scene);
            player.name = "TigerII_Player";
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
            string[] targetNames = TankSpecs.Select(spec => spec.SpritePrefix + "_Target").ToArray();
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
                TankSpecs.Select(spec => spec.DisplayName).ToArray(),
                player,
                0,
                follow,
                respawner);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
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

        private static Vector2[] CreateE100HullOutline()
        {
            return new[]
            {
                new Vector2(-0.46f, 1.125f), new Vector2(0.46f, 1.125f),
                new Vector2(0.56f, 1.04f), new Vector2(0.58f, 0.86f),
                new Vector2(0.58f, -0.83f), new Vector2(0.55f, -1.02f),
                new Vector2(0.46f, -1.105f), new Vector2(0.31f, -1.125f),
                new Vector2(-0.31f, -1.125f), new Vector2(-0.46f, -1.105f),
                new Vector2(-0.55f, -1.02f), new Vector2(-0.58f, -0.83f),
                new Vector2(-0.58f, 0.86f), new Vector2(-0.56f, 1.04f)
            };
        }

        private static Vector2[] CreateT34HullOutline()
        {
            return new[]
            {
                new Vector2(-0.29f, 0.865f), new Vector2(0.29f, 0.865f),
                new Vector2(0.37f, 0.78f), new Vector2(0.39f, 0.58f),
                new Vector2(0.39f, -0.63f), new Vector2(0.35f, -0.80f),
                new Vector2(0.27f, -0.865f), new Vector2(-0.27f, -0.865f),
                new Vector2(-0.35f, -0.80f), new Vector2(-0.39f, -0.63f),
                new Vector2(-0.39f, 0.58f), new Vector2(-0.37f, 0.78f)
            };
        }

        private static Vector2[] CreatePanzerIVHullOutline()
        {
            return new[]
            {
                new Vector2(-0.31f, 0.91f), new Vector2(0.31f, 0.91f),
                new Vector2(0.37f, 0.84f), new Vector2(0.375f, -0.76f),
                new Vector2(0.34f, -0.88f), new Vector2(0.27f, -0.91f),
                new Vector2(-0.27f, -0.91f), new Vector2(-0.34f, -0.88f),
                new Vector2(-0.375f, -0.76f), new Vector2(-0.37f, 0.84f)
            };
        }

        private static Vector2[] CreateBT2HullOutline()
        {
            return new[]
            {
                new Vector2(-0.22f, 0.715f), new Vector2(0.22f, 0.715f),
                new Vector2(0.28f, 0.68f), new Vector2(0.30f, 0.54f),
                new Vector2(0.30f, -0.54f), new Vector2(0.28f, -0.67f),
                new Vector2(0.22f, -0.715f), new Vector2(-0.22f, -0.715f),
                new Vector2(-0.28f, -0.67f), new Vector2(-0.30f, -0.54f),
                new Vector2(-0.30f, 0.54f), new Vector2(-0.28f, 0.68f)
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
                float mass, Vector2[] hullOutline)
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
                Mass = mass;
                HullOutline = hullOutline;
            }

            public string SpritePrefix { get; }
            public string DisplayName { get; }
            public string TexturePath { get; }
            public string DefinitionPath { get; }
            public string PrefabPath { get; }
            public Rect HullRect { get; }
            public Rect TurretRect { get; }
            public Vector2 TurretPivot { get; }
            public Vector2 HullTurretMount { get; }
            public Vector2 VisualScale { get; }
            public float MuzzleDistance { get; }
            public float Mass { get; }
            public Vector2[] HullOutline { get; }
        }
    }
}
