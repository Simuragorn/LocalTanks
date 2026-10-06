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
        private const string TigerTexturePath = "Assets/Game/Art/Tanks/TigerII/Source/Tiger-II_strip2.png";
        private const string TankDefinitionPath = "Assets/Game/GameData/Generated/Tanks/tiger_ii.asset";
        private const string ProjectilePrefabPath = "Assets/Game/Prefabs/Combat/PrototypeProjectile.prefab";
        private const string TankPrefabPath = "Assets/Game/Prefabs/Tanks/TigerII_Player.prefab";
        private const string ScenePath = "Assets/Game/Scenes/Battle_TestRange.unity";

        [InitializeOnLoadMethod]
        private static void ScheduleSprintTwoAssetUpgrade()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                {
                    return;
                }

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TankPrefabPath);
                if (prefab != null &&
                    prefab.GetComponent<TankHealth>() != null &&
                    prefab.GetComponent<PolygonCollider2D>() != null)
                {
                    return;
                }

                try
                {
                    BuildAll();
                }
                catch (System.Exception exception)
                {
                    Debug.LogException(exception);
                }
            };
        }

        [MenuItem("Local Tanks/Build Sprint 002 Prototype")]
        public static void BuildAll()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            ConfigureTigerTexture();
            CombatDefinitionImporter.Reimport();
            TankDefinition config = AssetDatabase.LoadAssetAtPath<TankDefinition>(TankDefinitionPath);
            if (config == null)
            {
                throw new System.InvalidOperationException("Tiger II definition was not generated.");
            }

            Projectile2D projectile = CreateProjectilePrefab();
            GameObject tankPrefab = CreateTankPrefab(config, projectile);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                CreateTestScene(tankPrefab);
            }
            else
            {
                UpgradeTestScene(tankPrefab);
                EnsureSceneInBuildSettings();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"Local Tanks Sprint 002 prototype created: {ScenePath}");
        }

        private static void ConfigureTigerTexture()
        {
            TextureImporter importer = AssetImporter.GetAtPath(TigerTexturePath) as TextureImporter;
            if (importer == null)
            {
                throw new System.IO.FileNotFoundException("Tiger II source texture was not found.", TigerTexturePath);
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

            GUID hullId = GUID.Generate();
            GUID turretId = GUID.Generate();
            foreach (SpriteRect existingRect in provider.GetSpriteRects())
            {
                if (existingRect.name == "TigerII_Hull")
                {
                    hullId = existingRect.spriteID;
                }
                else if (existingRect.name == "TigerII_Turret")
                {
                    turretId = existingRect.spriteID;
                }
            }

            SpriteRect hull = new SpriteRect
            {
                name = "TigerII_Hull",
                rect = new Rect(7f, 83f, 105f, 191f),
                alignment = SpriteAlignment.Custom,
                pivot = new Vector2(0.5f, 0.5f),
                spriteID = hullId
            };

            SpriteRect turret = new SpriteRect
            {
                name = "TigerII_Turret",
                rect = new Rect(145f, 7f, 69f, 234f),
                alignment = SpriteAlignment.Custom,
                // The pivot is at the turret ring instead of the centre of its long barrel.
                pivot = new Vector2(0.5f, 0.78f),
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

                Projectile2D projectile = root.AddComponent<Projectile2D>();
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, ProjectilePrefabPath);
                return prefab.GetComponent<Projectile2D>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject CreateTankPrefab(TankDefinition config, Projectile2D projectilePrefab)
        {
            Dictionary<string, Sprite> sprites = AssetDatabase.LoadAllAssetsAtPath(TigerTexturePath)
                .OfType<Sprite>()
                .ToDictionary(sprite => sprite.name, sprite => sprite);

            if (!sprites.TryGetValue("TigerII_Hull", out Sprite hullSprite) ||
                !sprites.TryGetValue("TigerII_Turret", out Sprite turretSprite))
            {
                throw new System.InvalidOperationException("Tiger II sprites were not sliced correctly.");
            }

            GameObject root = new GameObject("TigerII_Player");
            try
            {
                Rigidbody2D body = root.AddComponent<Rigidbody2D>();
                body.gravityScale = 0f;
                body.mass = 45f;
                body.linearDamping = 1f;
                body.angularDamping = 4f;
                body.interpolation = RigidbodyInterpolation2D.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

                PolygonCollider2D collider = root.AddComponent<PolygonCollider2D>();
                collider.points = CreateTigerHullOutline();

                GameObject hullVisual = CreateSpriteChild(
                    root.transform,
                    "HullVisual",
                    hullSprite,
                    Color.white,
                    5);
                hullVisual.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);

                GameObject turretPivot = new GameObject("TurretPivot");
                turretPivot.transform.SetParent(root.transform, false);
                TurretAiming aiming = turretPivot.AddComponent<TurretAiming>();
                aiming.Configure(config);

                GameObject turretVisual = CreateSpriteChild(
                    turretPivot.transform,
                    "TurretVisual",
                    turretSprite,
                    Color.white,
                    10);
                turretVisual.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);

                GameObject muzzle = new GameObject("Muzzle");
                muzzle.transform.SetParent(turretPivot.transform, false);
                muzzle.transform.localPosition = new Vector3(0f, 1.8f, 0f);

                TankMotor motor = root.AddComponent<TankMotor>();
                motor.Configure(config);

                WeaponController weapon = root.AddComponent<WeaponController>();
                weapon.Configure(config, projectilePrefab, muzzle.transform);

                PlayerTankInput input = root.AddComponent<PlayerTankInput>();
                input.Configure(motor, aiming, weapon);

                TankHealth health = root.AddComponent<TankHealth>();
                health.Configure(config);

                TankArmor armor = root.AddComponent<TankArmor>();
                armor.Configure(config, health);

                TankDestroyedState destroyedState = root.AddComponent<TankDestroyedState>();
                destroyedState.Configure(health, motor, aiming, weapon, input);

                return PrefabUtility.SaveAsPrefabAsset(root, TankPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Vector2[] CreateTigerHullOutline()
        {
            // Local +Y is forward. The outline follows the visible track and hull silhouette,
            // retaining the angled nose/shoulders and rounded rear instead of using a box.
            return new[]
            {
                new Vector2(-0.37f, 0.955f),
                new Vector2(0.37f, 0.955f),
                new Vector2(0.47f, 0.91f),
                new Vector2(0.515f, 0.80f),
                new Vector2(0.525f, 0.62f),
                new Vector2(0.525f, -0.70f),
                new Vector2(0.50f, -0.84f),
                new Vector2(0.43f, -0.925f),
                new Vector2(0.34f, -0.955f),
                new Vector2(-0.34f, -0.955f),
                new Vector2(-0.43f, -0.925f),
                new Vector2(-0.50f, -0.84f),
                new Vector2(-0.525f, -0.70f),
                new Vector2(-0.525f, 0.62f),
                new Vector2(-0.515f, 0.80f),
                new Vector2(-0.47f, 0.91f)
            };
        }

        private static void CreateTestScene(GameObject tankPrefab)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "Battle_TestRange";

            Sprite shapeSprite = GetBuiltinSprite();
            CreateArea("Ground", Vector2.zero, new Vector2(30f, 20f), new Color(0.16f, 0.25f, 0.15f), -20, false, shapeSprite);

            CreateArea("Wall_North", new Vector2(0f, 8.5f), new Vector2(26f, 0.8f), Color.gray, 0, true, shapeSprite);
            CreateArea("Wall_South", new Vector2(0f, -8.5f), new Vector2(26f, 0.8f), Color.gray, 0, true, shapeSprite);
            CreateArea("Wall_West", new Vector2(-12.5f, 0f), new Vector2(0.8f, 17f), Color.gray, 0, true, shapeSprite);
            CreateArea("Wall_East", new Vector2(12.5f, 0f), new Vector2(0.8f, 17f), Color.gray, 0, true, shapeSprite);
            CreateArea("Wall_Centre", new Vector2(2.5f, 1.5f), new Vector2(0.8f, 6f), new Color(0.35f, 0.35f, 0.38f), 0, true, shapeSprite);
            CreateArea("Wall_Cover", new Vector2(-4f, 3.5f), new Vector2(4f, 0.8f), new Color(0.35f, 0.35f, 0.38f), 0, true, shapeSprite);

            GameObject target = CreateArea("Target", new Vector2(0f, 6f), new Vector2(0.9f, 0.9f), new Color(0.78f, 0.16f, 0.13f), 2, false, shapeSprite);
            CircleCollider2D targetCollider = target.AddComponent<CircleCollider2D>();
            targetCollider.radius = 0.45f;

            GameObject player = (GameObject)PrefabUtility.InstantiatePrefab(tankPrefab, scene);
            player.name = "TigerII_Player";
            player.transform.position = new Vector3(0f, -5f, 0f);

            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 7f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.06f, 0.08f, 0.07f);
            cameraObject.transform.position = new Vector3(0f, -2f, -10f);
            cameraObject.AddComponent<AudioListener>();
            CameraFollow2D follow = cameraObject.AddComponent<CameraFollow2D>();
            follow.Configure(player.transform);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EnsureSceneInBuildSettings();
        }

        private static void UpgradeTestScene(GameObject tankPrefab)
        {
            Scene scene = SceneManager.GetActiveScene().path == ScenePath
                ? SceneManager.GetActiveScene()
                : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            GameObject legacyTarget = GameObject.Find("Target");
            if (legacyTarget != null)
            {
                Object.DestroyImmediate(legacyTarget);
            }

            GameObject target = GameObject.Find("TigerII_Target");
            if (target == null)
            {
                target = (GameObject)PrefabUtility.InstantiatePrefab(tankPrefab, scene);
                target.name = "TigerII_Target";
            }

            target.transform.SetPositionAndRotation(new Vector3(0f, 5f, 0f), Quaternion.Euler(0f, 0f, 180f));
            PlayerTankInput targetInput = target.GetComponent<PlayerTankInput>();
            if (targetInput != null)
            {
                targetInput.enabled = false;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static void EnsureSceneInBuildSettings()
        {
            List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
            EditorBuildSettingsScene existing = scenes.FirstOrDefault(scene => scene.path == ScenePath);
            if (existing == null)
            {
                scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            }
            else
            {
                existing.enabled = true;
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static GameObject CreateSpriteChild(
            Transform parent,
            string name,
            Sprite sprite,
            Color color,
            int sortingOrder)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            SpriteRenderer renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            return child;
        }

        private static GameObject CreateArea(
            string name,
            Vector2 position,
            Vector2 size,
            Color color,
            int sortingOrder,
            bool addCollider,
            Sprite sprite)
        {
            GameObject area = new GameObject(name);
            area.transform.position = position;

            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(area.transform, false);
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            visual.transform.localScale = new Vector3(
                size.x / sprite.bounds.size.x,
                size.y / sprite.bounds.size.y,
                1f);

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
            if (sprite == null)
            {
                throw new System.InvalidOperationException("Unity built-in UI sprite was not found.");
            }

            return sprite;
        }
    }
}
