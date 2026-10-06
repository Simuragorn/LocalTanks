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
        private const string ConfigPath = "Assets/Game/Data/TigerII_Prototype.asset";
        private const string ProjectilePrefabPath = "Assets/Game/Prefabs/Combat/PrototypeProjectile.prefab";
        private const string TankPrefabPath = "Assets/Game/Prefabs/Tanks/TigerII_Player.prefab";
        private const string ScenePath = "Assets/Game/Scenes/Battle_TestRange.unity";

        [MenuItem("Local Tanks/Build First Sprint Prototype")]
        public static void BuildAll()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            ConfigureTigerTexture();
            TankPrototypeConfig config = CreateOrUpdateConfig();
            Projectile2D projectile = CreateProjectilePrefab();
            GameObject tankPrefab = CreateTankPrefab(config, projectile);
            CreateTestScene(tankPrefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"Local Tanks first sprint prototype created: {ScenePath}");
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

            SpriteRect hull = new SpriteRect
            {
                name = "TigerII_Hull",
                rect = new Rect(7f, 83f, 105f, 191f),
                alignment = SpriteAlignment.Custom,
                pivot = new Vector2(0.5f, 0.5f),
                spriteID = GUID.Generate()
            };

            SpriteRect turret = new SpriteRect
            {
                name = "TigerII_Turret",
                rect = new Rect(145f, 7f, 69f, 234f),
                alignment = SpriteAlignment.Custom,
                // The pivot is at the turret ring instead of the centre of its long barrel.
                pivot = new Vector2(0.5f, 0.78f),
                spriteID = GUID.Generate()
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

        private static TankPrototypeConfig CreateOrUpdateConfig()
        {
            TankPrototypeConfig config = AssetDatabase.LoadAssetAtPath<TankPrototypeConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<TankPrototypeConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }

            config.maxForwardSpeed = 5f;
            config.maxReverseSpeed = 2.25f;
            config.acceleration = 3.5f;
            config.braking = 6f;
            config.hullTurnSpeed = 75f;
            config.turretTurnSpeed = 110f;
            config.reloadSeconds = 0.8f;
            config.projectileSpeed = 18f;
            config.projectileRadius = 0.06f;
            config.projectileLifetime = 3f;
            config.projectileRange = 45f;
            EditorUtility.SetDirty(config);
            return config;
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

        private static GameObject CreateTankPrefab(TankPrototypeConfig config, Projectile2D projectilePrefab)
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

                BoxCollider2D collider = root.AddComponent<BoxCollider2D>();
                collider.size = new Vector2(0.9f, 1.7f);

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

                return PrefabUtility.SaveAsPrefabAsset(root, TankPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
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
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
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
