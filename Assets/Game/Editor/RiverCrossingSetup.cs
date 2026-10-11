using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LocalTanks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;

namespace LocalTanks.Editor
{
    public static class RiverCrossingSetup
    {
        private const string SourcePath = "Assets/Game/GameData/Maps/river_crossing.json";
        private const string ScenePath = "Assets/Game/Scenes/Battle_RiverCrossing.unity";
        private const string DuelScenePath = "Assets/Game/Scenes/Battle_AiDuelLab.unity";
        private const string PlayerTestScenePath = "Assets/Game/Scenes/Battle_PlayerAiTestLab.unity";
        private const string GeneratedRoot = "Assets/Game/Generated/RiverCrossing";
        private const string TilesRoot = GeneratedRoot + "/Tiles";
        private const string LineMaterialPath = GeneratedRoot + "/MapLine.mat";
        private const string ShapeTexturePath = GeneratedRoot + "/WhiteSquare.png";
        private const string CircleTexturePath = GeneratedRoot + "/WhiteCircle.png";
        private const string PreviewPath = "Docs/Media/RiverCrossing.png";
        private const string VisionRulesPath = GeneratedRoot + "/VisionRules.asset";
        private const string CaptureRulesPath = GeneratedRoot + "/CaptureRules.asset";
        private const string PanelSettingsPath = GeneratedRoot + "/BattleHudPanelSettings.asset";
        private const string BattleHudUxmlPath = "Assets/Game/UI/BattleHud.uxml";
        private const string BattleHudUssPath = "Assets/Game/UI/BattleHud.uss";
        private const string ClassIconsRoot = "Assets/Game/Generated/UI/VehicleClasses";

        private static readonly string[] TankPrefabPaths =
        {
            "Assets/Game/Prefabs/Tanks/MS1_Player.prefab",
            "Assets/Game/Prefabs/Tanks/Leichttraktor_Player.prefab"
        };

        [InitializeOnLoadMethod]
        private static void ScheduleInitialBuild()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
                {
                    return;
                }

                try
                {
                    Build();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            };
        }

        [MenuItem("Local Tanks/Build River Crossing Map")]
        public static void Build()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            MapSource source = LoadAndValidateSource();
            TacticalMapDescriptorImporter.Reimport();
            EnsureFolder("Assets/Game/Generated");
            EnsureFolder(GeneratedRoot);
            EnsureFolder(TilesRoot);
            EnsureFolder("Assets/Game/Generated/UI");
            EnsureFolder(ClassIconsRoot);
            GenerateVehicleClassIcons();
            CaptureRules captureRules = GetOrCreateCaptureRules();
            BattleScenario battleScenario = BattleScenarioImporter.Reimport();
            BattleScenarioImporter.ReimportDuelLab();
            AiDuelLabCatalogImporter.Reimport();

            Scene previousScene = SceneManager.GetActiveScene();
            bool preservePreviousScene = previousScene.IsValid() && previousScene.isLoaded &&
                                         !string.IsNullOrEmpty(previousScene.path);
            if (!preservePreviousScene && previousScene.IsValid() && previousScene.isDirty)
            {
                throw new InvalidOperationException("Save the current untitled scene before building River Crossing.");
            }

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                preservePreviousScene ? NewSceneMode.Additive : NewSceneMode.Single);
            scene.name = "Battle_RiverCrossing";
            SceneManager.SetActiveScene(scene);
            try
            {
                TerrainLookup lookup = BuildTerrainLookup(source);
                CreateTilemaps(source, lookup);
                NavigationMap map = CreateNavigationMap(source, lookup);
                CreateEnvironment(source, map, captureRules);
                CameraFollow2D cameraFollow = CreateCameraAndDiagnostics(map);
                TeamVisionSystem visionSystem = CreateBattleSystems();
                CreateBattleDirector(scene, map, battleScenario, visionSystem, cameraFollow);
                new GameObject("RiverCrossingInstructions").AddComponent<RiverCrossingInstructions>();

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, ScenePath);
                BuildAiDuelLabScene(scene);
                BuildPlayerAiTestLabScene(scene);
                EnsureSceneInBuildSettings();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log($"Local Tanks map created from {SourcePath}: {ScenePath}");
            }
            finally
            {
                if (preservePreviousScene && previousScene.IsValid() && previousScene.isLoaded)
                {
                    SceneManager.SetActiveScene(previousScene);
                }

                if (preservePreviousScene && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static void BuildAiDuelLabScene(Scene sourceScene)
        {
            EditorSceneManager.SaveScene(sourceScene, DuelScenePath, true);
            Scene duelScene = EditorSceneManager.OpenScene(DuelScenePath, OpenSceneMode.Additive);
            try
            {
                BattleScenario duelScenario = AssetDatabase.LoadAssetAtPath<BattleScenario>(
                    BattleScenarioImporter.DuelAssetPath);
                AiDuelLabCatalog duelCatalog = AssetDatabase.LoadAssetAtPath<AiDuelLabCatalog>(
                    AiDuelLabCatalogImporter.AssetPath);
                if (duelScenario == null || duelCatalog == null)
                    throw new InvalidOperationException("AI Duel Lab data assets were not generated.");
                BattleDirector director = FindInScene<BattleDirector>(duelScene);
                if (director == null) throw new InvalidOperationException("Copied AI Duel Lab scene has no BattleDirector.");
                director.OverrideScenario(duelScenario);

                RiverCrossingInstructions instructions = FindInScene<RiverCrossingInstructions>(duelScene);
                if (instructions != null) UnityEngine.Object.DestroyImmediate(instructions.gameObject);
                GameObject controllerObject = new GameObject("AiDuelLabController");
                SceneManager.MoveGameObjectToScene(controllerObject, duelScene);
                controllerObject.AddComponent<AiDuelLabController>().Configure(director, duelCatalog);

                EditorSceneManager.MarkSceneDirty(duelScene);
                EditorSceneManager.SaveScene(duelScene, DuelScenePath);
            }
            finally
            {
                if (duelScene.IsValid() && duelScene.isLoaded)
                    EditorSceneManager.CloseScene(duelScene, true);
            }
        }

        private static void BuildPlayerAiTestLabScene(Scene sourceScene)
        {
            EditorSceneManager.SaveScene(sourceScene, PlayerTestScenePath, true);
            Scene testScene = EditorSceneManager.OpenScene(PlayerTestScenePath, OpenSceneMode.Additive);
            try
            {
                BattleDirector director = FindInScene<BattleDirector>(testScene);
                if (director == null)
                    throw new InvalidOperationException("Copied Player AI Test Lab scene has no BattleDirector.");

                RiverCrossingInstructions instructions = FindInScene<RiverCrossingInstructions>(testScene);
                if (instructions != null) UnityEngine.Object.DestroyImmediate(instructions.gameObject);
                GameObject controllerObject = new GameObject("PlayerAiTestLabController");
                SceneManager.MoveGameObjectToScene(controllerObject, testScene);
                controllerObject.AddComponent<PlayerAiTestLabController>().Configure(director);

                EditorSceneManager.MarkSceneDirty(testScene);
                EditorSceneManager.SaveScene(testScene, PlayerTestScenePath);
            }
            finally
            {
                if (testScene.IsValid() && testScene.isLoaded)
                    EditorSceneManager.CloseScene(testScene, true);
            }
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .FirstOrDefault();
        }

        [MenuItem("Local Tanks/Capture River Crossing Preview")]
        public static void CapturePreview()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Camera camera = UnityEngine.Object.FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                throw new InvalidOperationException("River Crossing camera was not found.");
            }

            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.orthographicSize = 17.5f;
            RenderTexture target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            Texture2D image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                RenderTexture.active = target;
                camera.Render();
                image.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.GetFullPath(PreviewPath), image.EncodeToPNG());
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log($"River Crossing preview saved: {PreviewPath}");
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(image);
            }

            EditorSceneManager.CloseScene(scene, true);
        }

        private static MapSource LoadAndValidateSource()
        {
            string absolutePath = Path.GetFullPath(SourcePath);
            if (!File.Exists(absolutePath))
            {
                throw new FileNotFoundException("River Crossing map JSON was not found.", SourcePath);
            }

            MapSource source = JsonUtility.FromJson<MapSource>(File.ReadAllText(absolutePath));
            if (source == null || source.schemaVersion != 1 || source.width <= 0 ||
                source.height <= 0 || source.cellSize <= 0f)
            {
                throw new InvalidDataException("River Crossing map header is invalid.");
            }

            if (source.rows == null || source.rows.Length != source.height)
            {
                throw new InvalidDataException($"Map must contain exactly {source.height} rows.");
            }

            if (source.terrains == null || source.terrains.Length == 0)
            {
                throw new InvalidDataException("Map does not define terrain symbols.");
            }

            HashSet<char> symbols = new HashSet<char>();
            foreach (TerrainSource terrain in source.terrains)
            {
                if (terrain.symbol == null || terrain.symbol.Length != 1 || !symbols.Add(terrain.symbol[0]))
                {
                    throw new InvalidDataException("Every terrain symbol must be one unique character.");
                }

                if (!Enum.TryParse(terrain.kind, out TerrainKind _))
                {
                    throw new InvalidDataException($"Unknown terrain kind '{terrain.kind}'.");
                }
            }

            int basesA = 0;
            int basesB = 0;
            for (int row = 0; row < source.rows.Length; row++)
            {
                if (source.rows[row] == null || source.rows[row].Length != source.width)
                {
                    throw new InvalidDataException($"Row {row} must contain exactly {source.width} characters.");
                }

                foreach (char symbol in source.rows[row])
                {
                    if (!symbols.Contains(symbol))
                    {
                        throw new InvalidDataException($"Row {row} contains undefined symbol '{symbol}'.");
                    }

                    if (symbol == 'A') basesA++;
                    if (symbol == 'Z') basesB++;
                }
            }

            if (basesA != 1 || basesB != 1)
            {
                throw new InvalidDataException("Map must contain exactly one A base and one Z base.");
            }

            return source;
        }

        private static TerrainLookup BuildTerrainLookup(MapSource source)
        {
            Dictionary<char, TerrainSource> definitions = source.terrains.ToDictionary(item => item.symbol[0]);
            Dictionary<string, Tile> groundTiles = new Dictionary<string, Tile>
            {
                ["Grass"] = CreateOrUpdateTile("Grass", definitions['.'], Tile.ColliderType.None),
                ["Road"] = CreateOrUpdateTile("Road", definitions['='], Tile.ColliderType.None),
                ["Mud"] = CreateOrUpdateTile("Mud", definitions['m'], Tile.ColliderType.None),
                ["ShallowWater"] = CreateOrUpdateTile("ShallowWater", definitions['w'], Tile.ColliderType.None),
                ["DeepWater"] = CreateOrUpdateTile("DeepWater", definitions['~'], Tile.ColliderType.None)
            };
            Tile blocker = CreateOrUpdateTile("InvisibleBlocker", definitions['#'], Tile.ColliderType.Grid);
            blocker.color = new Color(1f, 1f, 1f, 0f);
            EditorUtility.SetDirty(blocker);
            return new TerrainLookup(definitions, groundTiles, blocker);
        }

        private static Tile CreateOrUpdateTile(string name, TerrainSource terrain, Tile.ColliderType colliderType)
        {
            string path = $"{TilesRoot}/Tile_{name}.asset";
            Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, path);
            }

            tile.sprite = GetShapeSprite();
            tile.color = new Color(terrain.red, terrain.green, terrain.blue, 1f);
            tile.colliderType = colliderType;
            EditorUtility.SetDirty(tile);
            return tile;
        }

        private static void CreateTilemaps(MapSource source, TerrainLookup lookup)
        {
            GameObject gridObject = new GameObject("RiverCrossingGrid");
            Grid grid = gridObject.AddComponent<Grid>();
            grid.cellSize = new Vector3(source.cellSize, source.cellSize, 1f);
            gridObject.transform.position = MapOrigin(source);

            Tilemap ground = CreateTilemapLayer(gridObject.transform, "Ground", -20, false, true);
            Tilemap hardBlockers = CreateTilemapLayer(gridObject.transform, "HardObstacleColliders", -19, true, false);
            Tilemap navigationOnlyBlockers = CreateTilemapLayer(gridObject.transform, "NavigationOnlyBlockers", -19, true, false);
            navigationOnlyBlockers.gameObject.AddComponent<FirePassThroughObstacle>();
            Tilemap visionBlockers = CreateTilemapLayer(gridObject.transform, "VisionBlockers", -18, true, false);
            TilemapCollider2D visionCollider = visionBlockers.GetComponent<TilemapCollider2D>();
            visionCollider.isTrigger = true;
            visionBlockers.gameObject.AddComponent<VisionBlocker>();
            for (int sourceRow = 0; sourceRow < source.height; sourceRow++)
            {
                int y = source.height - 1 - sourceRow;
                for (int x = 0; x < source.width; x++)
                {
                    char symbol = source.rows[sourceRow][x];
                    Vector3Int cell = new Vector3Int(x, y, 0);
                    ground.SetTile(cell, lookup.GroundTiles[GroundTileName(symbol)]);
                    if (lookup.Definitions[symbol].blocked)
                    {
                        if (IsHardObstacleSymbol(symbol)) hardBlockers.SetTile(cell, lookup.BlockerTile);
                        else navigationOnlyBlockers.SetTile(cell, lookup.BlockerTile);
                    }

                    if (IsVisionBlockingSymbol(symbol))
                    {
                        visionBlockers.SetTile(cell, lookup.BlockerTile);
                    }
                }
            }
        }

        private static Tilemap CreateTilemapLayer(
            Transform parent, string name, int sortingOrder, bool collider, bool render)
        {
            GameObject layer = new GameObject(name);
            layer.transform.SetParent(parent, false);
            Tilemap tilemap = layer.AddComponent<Tilemap>();
            TilemapRenderer renderer = layer.AddComponent<TilemapRenderer>();
            renderer.sortingOrder = sortingOrder;
            renderer.enabled = render;
            if (collider)
            {
                layer.AddComponent<TilemapCollider2D>();
            }

            return tilemap;
        }

        private static NavigationMap CreateNavigationMap(MapSource source, TerrainLookup lookup)
        {
            int count = source.width * source.height;
            TerrainKind[] terrain = new TerrainKind[count];
            float[] costs = new float[count];
            float[] speeds = new float[count];
            float[] accelerations = new float[count];
            bool[] blocked = new bool[count];
            for (int sourceRow = 0; sourceRow < source.height; sourceRow++)
            {
                int y = source.height - 1 - sourceRow;
                for (int x = 0; x < source.width; x++)
                {
                    TerrainSource definition = lookup.Definitions[source.rows[sourceRow][x]];
                    int index = y * source.width + x;
                    terrain[index] = Enum.Parse<TerrainKind>(definition.kind);
                    costs[index] = definition.navigationCost;
                    speeds[index] = definition.speedMultiplier;
                    accelerations[index] = definition.accelerationMultiplier;
                    blocked[index] = definition.blocked;
                }
            }

            GameObject mapObject = new GameObject("NavigationMap");
            NavigationMap map = mapObject.AddComponent<NavigationMap>();
            map.Configure(
                source.width,
                source.height,
                source.cellSize,
                MapOrigin(source),
                terrain,
                costs,
                speeds,
                accelerations,
                blocked);
            return map;
        }

        private static void CreateEnvironment(MapSource source, NavigationMap map, CaptureRules captureRules)
        {
            GameObject environment = new GameObject("Environment");
            CreateHardObstaclePrimitives(source, map, environment.transform);
            for (int sourceRow = 0; sourceRow < source.height; sourceRow++)
            {
                int y = source.height - 1 - sourceRow;
                for (int x = 0; x < source.width; x++)
                {
                    char symbol = source.rows[sourceRow][x];
                    Vector2 position = map.CellToWorld(new Vector2Int(x, y));
                    switch (symbol)
                    {
                        case 'b': CreateConcealmentPrimitive(environment.transform, $"Bush_{x}_{y}", position, source.cellSize, x, y, 0.18f); break;
                        case 'R': CreateConcealmentPrimitive(environment.transform, $"Bush_{x}_{y}", position, source.cellSize, x, y, 0.08f); break;
                    }
                }
            }

            CreateBridge(environment.transform, map.CellToWorld(new Vector2Int(35, 35)), "NorthStoneBridge", new Color(0.38f, 0.34f, 0.28f), 2.1f);
            CreateBridge(environment.transform, map.CellToWorld(new Vector2Int(35, 22)), "CentralStoneBridge", new Color(0.40f, 0.36f, 0.31f), 2.4f);
            CreateBridge(environment.transform, map.CellToWorld(new Vector2Int(35, 9)), "SouthWoodBridge", new Color(0.43f, 0.29f, 0.16f), 2.1f);

            Vector2 baseA = map.CellToWorld(FindCell(source, 'A'));
            Vector2 baseB = map.CellToWorld(FindCell(source, 'Z'));
            CreateBase("Base_A", "A", "База A", TeamId.TeamA, baseA, TeamPalette.Ally, captureRules);
            CreateBase("Base_B", "B", "База B", TeamId.TeamB, baseB, TeamPalette.Enemy, captureRules);
        }

        private static void CreateHardObstaclePrimitives(MapSource source, NavigationMap map, Transform parent)
        {
            Sprite sprite = GetShapeSprite();
            for (int sourceRow = 0; sourceRow < source.height; sourceRow++)
            {
                int y = source.height - 1 - sourceRow;
                int runStart = -1;
                for (int x = 0; x <= source.width; x++)
                {
                    bool hardObstacle = x < source.width && IsHardObstacleSymbol(source.rows[sourceRow][x]);
                    if (hardObstacle && runStart < 0)
                    {
                        runStart = x;
                    }
                    else if (!hardObstacle && runStart >= 0)
                    {
                        int runLength = x - runStart;
                        Vector2 first = map.CellToWorld(new Vector2Int(runStart, y));
                        Vector2 last = map.CellToWorld(new Vector2Int(x - 1, y));
                        GameObject visual = CreateShape(
                            parent,
                            $"HardObstacle_{runStart}_{y}_{runLength}",
                            (first + last) * 0.5f,
                            new Vector2(runLength * source.cellSize * 0.96f, source.cellSize * 0.90f),
                            MapPrimitiveVisual.HardObstacleColor,
                            2,
                            sprite);
                        visual.AddComponent<MapPrimitiveVisual>().Configure(MapPrimitiveKind.HardObstacle);
                        runStart = -1;
                    }
                }
            }
        }

        private static void CreateConcealmentPrimitive(
            Transform parent,
            string name,
            Vector2 position,
            float cellSize,
            int seedX,
            int seedY,
            float bonus)
        {
            int hash = Math.Abs(seedX * 73856093 ^ seedY * 19349663);
            float diameter = cellSize * Mathf.Lerp(1.05f, 1.45f, (hash % 1000) / 999f);
            Sprite sprite = GetCircleSprite();
            GameObject item = CreateShape(
                parent,
                name,
                position,
                Vector2.one * diameter,
                MapPrimitiveVisual.ConcealmentColor,
                3,
                sprite);
            CircleCollider2D collider = item.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = sprite.bounds.extents.x * 0.92f;
            item.AddComponent<ConcealmentZone>().Configure(bonus);
            item.AddComponent<MapPrimitiveVisual>().Configure(MapPrimitiveKind.Concealment);
        }

        private static void CreateBridge(Transform parent, Vector2 position, string name, Color color, float height)
        {
            Sprite shape = GetShapeSprite();
            GameObject bridge = new GameObject(name);
            bridge.transform.SetParent(parent, false);
            bridge.transform.position = position;
            CreateShape(bridge.transform, "Deck", Vector2.zero, new Vector2(4.8f, height), color, -8, shape);
            Color railColor = Color.Lerp(color, Color.black, 0.34f);
            CreateShape(bridge.transform, "NorthRail", new Vector2(0f, height * 0.46f), new Vector2(5.0f, 0.11f), railColor, -7, shape);
            CreateShape(bridge.transform, "SouthRail", new Vector2(0f, -height * 0.46f), new Vector2(5.0f, 0.11f), railColor, -7, shape);
        }

        private static void CreateBase(
            string name,
            string id,
            string title,
            TeamId owner,
            Vector2 position,
            Color color,
            CaptureRules captureRules)
        {
            GameObject root = new GameObject(name);
            root.transform.position = position;
            LineRenderer ring = root.AddComponent<LineRenderer>();
            ring.loop = true;
            ring.useWorldSpace = false;
            ring.positionCount = 64;
            ring.widthMultiplier = 0.10f;
            ring.startColor = color;
            ring.endColor = color;
            ring.sortingOrder = -4;
            ring.sharedMaterial = GetOrCreateLineMaterial();
            for (int index = 0; index < ring.positionCount; index++)
            {
                float angle = index * Mathf.PI * 2f / ring.positionCount;
                ring.SetPosition(index, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 5.9f);
            }

            Sprite markerSprite = GetShapeSprite();
            int spawnIndex = 0;
            for (int row = -2; row <= 2; row++)
            {
                for (int column = -1; column <= 1; column++)
                {
                    spawnIndex++;
                    GameObject marker = new GameObject($"Spawn_{spawnIndex:00}");
                    marker.transform.SetParent(root.transform, false);
                    marker.transform.localPosition = new Vector3(column * 2.8f, row * 2.4f, 0f);
                    SpriteRenderer renderer = marker.AddComponent<SpriteRenderer>();
                    renderer.sprite = markerSprite;
                    renderer.color = new Color(color.r, color.g, color.b, 0.58f);
                    renderer.sortingOrder = -3;
                    marker.transform.localScale = Vector3.one * (0.12f / markerSprite.bounds.size.x);
                }
            }

            CircleCollider2D captureZone = root.AddComponent<CircleCollider2D>();
            captureZone.radius = 5.9f;
            captureZone.isTrigger = true;
            root.AddComponent<CaptureBase>().Configure(id, title, owner, captureRules, ring);
        }

        private static CaptureRules GetOrCreateCaptureRules()
        {
            CaptureRules captureRules = AssetDatabase.LoadAssetAtPath<CaptureRules>(CaptureRulesPath);
            if (captureRules == null)
            {
                captureRules = ScriptableObject.CreateInstance<CaptureRules>();
                AssetDatabase.CreateAsset(captureRules, CaptureRulesPath);
            }

            captureRules.baseCaptureSeconds = 180f;
            captureRules.maximumContributingTanks = 3;
            captureRules.fullRecoverySeconds = 60f;
            EditorUtility.SetDirty(captureRules);
            return captureRules;
        }

        private static Material GetOrCreateLineMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(LineMaterialPath);
            if (material != null)
            {
                return material;
            }

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                throw new InvalidOperationException("Sprites/Default shader was not found.");
            }

            material = new Material(shader) { color = Color.white };
            AssetDatabase.CreateAsset(material, LineMaterialPath);
            return material;
        }

        private static GameObject CreateTanks(Scene scene, NavigationMap map)
        {
            GameObject[] prefabs = TankPrefabPaths.Select(AssetDatabase.LoadAssetAtPath<GameObject>).ToArray();
            if (prefabs.Any(prefab => prefab == null))
            {
                throw new InvalidOperationException("Build the combat test range before building River Crossing.");
            }

            CaptureBase[] bases = UnityEngine.Object.FindObjectsByType<CaptureBase>();
            CaptureBase teamABase = bases.Single(item => item.Owner == TeamId.TeamA);
            CaptureBase teamBBase = bases.Single(item => item.Owner == TeamId.TeamB);
            Quaternion teamARotation = Quaternion.Euler(0f, 0f, -90f);
            Quaternion teamBRotation = Quaternion.Euler(0f, 0f, 90f);

            GameObject player = CreateTank(
                prefabs[0],
                scene,
                "RiverCrossing_Player",
                teamABase.GetSpawnPosition(7),
                teamARotation);
            AddTerrainModifier(player, map);
            AddTeamMember(player, TeamId.TeamA, true);

            GameObject north = CreateAgent(prefabs[1], scene, "Route_North_Leichttraktor", map, teamBBase.GetSpawnPosition(11), teamBRotation, new[]
            {
                new Vector2Int(61, 24), new Vector2Int(53, 31), new Vector2Int(35, 35), new Vector2Int(21, 33),
                new Vector2Int(11, 25), new Vector2Int(21, 33), new Vector2Int(35, 35), new Vector2Int(53, 31)
            });
            AddTeamMember(north, TeamId.TeamB, false);
            GameObject center = CreateAgent(prefabs[1], scene, "Route_Center_Leichttraktor", map, teamBBase.GetSpawnPosition(7), teamBRotation, new[]
            {
                new Vector2Int(60, 22), new Vector2Int(48, 22), new Vector2Int(35, 22), new Vector2Int(22, 22),
                new Vector2Int(11, 22), new Vector2Int(22, 22), new Vector2Int(35, 22), new Vector2Int(48, 22)
            });
            AddTeamMember(center, TeamId.TeamB, false);
            GameObject south = CreateAgent(prefabs[1], scene, "Route_South_Leichttraktor", map, teamBBase.GetSpawnPosition(1), teamBRotation, new[]
            {
                new Vector2Int(61, 20), new Vector2Int(54, 14), new Vector2Int(35, 9), new Vector2Int(22, 10),
                new Vector2Int(11, 19), new Vector2Int(22, 10), new Vector2Int(35, 9), new Vector2Int(54, 14)
            });
            AddTeamMember(south, TeamId.TeamB, false);
            return player;
        }

        private static GameObject CreateTank(
            GameObject prefab,
            Scene scene,
            string name,
            Vector2 position,
            Quaternion rotation)
        {
            GameObject tank = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            tank.name = name;
            tank.transform.SetPositionAndRotation(position, rotation);
            return tank;
        }

        private static GameObject CreateAgent(
            GameObject prefab,
            Scene scene,
            string name,
            NavigationMap map,
            Vector2 spawn,
            Quaternion rotation,
            Vector2Int[] patrolCells)
        {
            GameObject tank = CreateTank(prefab, scene, name, spawn, rotation);
            PlayerTankInput input = tank.GetComponent<PlayerTankInput>();
            TurretAiming turret = tank.GetComponentInChildren<TurretAiming>();
            WeaponController weapon = tank.GetComponent<WeaponController>();
            if (input != null) input.enabled = false;
            if (turret != null) turret.enabled = false;
            if (weapon != null) weapon.enabled = false;

            TankMotor motor = tank.GetComponent<TankMotor>();
            AddTerrainModifier(tank, map);
            NavigationAgent agent = tank.AddComponent<NavigationAgent>();
            agent.Configure(map, motor, patrolCells.Select(map.CellToWorld).ToArray(), 1, true);
            tank.AddComponent<TankHealthBar>().Configure(1.45f);
            return tank;
        }

        private static void AddTeamMember(GameObject tank, TeamId team, bool playerControlled)
        {
            TankDefinition definition = tank.GetComponent<TankHealth>()?.Definition;
            Sprite classIcon = definition != null
                ? AssetDatabase.LoadAssetAtPath<Sprite>($"{ClassIconsRoot}/{definition.vehicleClass}.png")
                : null;
            if (classIcon == null)
            {
                throw new InvalidOperationException($"Class icon for '{definition?.vehicleClass}' was not generated.");
            }

            TankClassIconPresenter classPresenter = tank.GetComponent<TankClassIconPresenter>();
            if (classPresenter == null)
            {
                classPresenter = tank.AddComponent<TankClassIconPresenter>();
            }

            classPresenter.Configure(classIcon, team == TeamId.TeamA, 1.7f);
            TankVisibilityPresenter presenter = tank.GetComponent<TankVisibilityPresenter>();
            if (presenter == null)
            {
                presenter = tank.AddComponent<TankVisibilityPresenter>();
            }

            TeamMember member = tank.GetComponent<TeamMember>();
            if (member == null)
            {
                member = tank.AddComponent<TeamMember>();
            }

            member.Configure(team, playerControlled);
        }

        private static void GenerateVehicleClassIcons()
        {
            foreach (VehicleClass vehicleClass in Enum.GetValues(typeof(VehicleClass)))
            {
                const int size = 32;
                Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                texture.SetPixels(Enumerable.Repeat(Color.clear, size * size).ToArray());
                switch (vehicleClass)
                {
                    case VehicleClass.HeavyTank:
                        DrawSlantedBars(texture, 3, 5, 7);
                        break;
                    case VehicleClass.MediumTank:
                        DrawSlantedBars(texture, 2, 9, 9);
                        break;
                    case VehicleClass.LightTank:
                        FillPolygon(texture, new[]
                        {
                            new Vector2(16f, 5f), new Vector2(27f, 16f),
                            new Vector2(16f, 27f), new Vector2(5f, 16f)
                        });
                        break;
                    case VehicleClass.TankDestroyer:
                        FillPolygon(texture, new[]
                        {
                            new Vector2(4f, 25f), new Vector2(28f, 25f), new Vector2(16f, 5f)
                        });
                        break;
                    case VehicleClass.Artillery:
                        FillPolygon(texture, new[]
                        {
                            new Vector2(7f, 7f), new Vector2(25f, 7f),
                            new Vector2(25f, 25f), new Vector2(7f, 25f)
                        });
                        break;
                }

                texture.Apply();
                string path = $"{ClassIconsRoot}/{vehicleClass}.png";
                File.WriteAllBytes(Path.GetFullPath(path), texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    throw new InvalidOperationException($"Could not import vehicle class icon '{path}'.");
                }

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = size;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
        }

        private static void DrawSlantedBars(Texture2D texture, int count, int startX, int spacing)
        {
            for (int index = 0; index < count; index++)
            {
                float x = startX + index * spacing;
                FillPolygon(texture, new[]
                {
                    new Vector2(x, 9f), new Vector2(x + 3f, 7f),
                    new Vector2(x + 11f, 22f), new Vector2(x + 8f, 24f)
                });
            }
        }

        private static void FillPolygon(Texture2D texture, Vector2[] vertices)
        {
            for (int y = 0; y < texture.height; y++)
            {
                for (int x = 0; x < texture.width; x++)
                {
                    Vector2 point = new Vector2(x + 0.5f, y + 0.5f);
                    bool inside = false;
                    for (int current = 0, previous = vertices.Length - 1;
                         current < vertices.Length;
                         previous = current++)
                    {
                        Vector2 a = vertices[current];
                        Vector2 b = vertices[previous];
                        bool crosses = (a.y > point.y) != (b.y > point.y) &&
                                       point.x < (b.x - a.x) * (point.y - a.y) /
                                       (b.y - a.y) + a.x;
                        if (crosses)
                        {
                            inside = !inside;
                        }
                    }

                    if (inside)
                    {
                        texture.SetPixel(x, y, Color.white);
                    }
                }
            }
        }

        private static TeamVisionSystem CreateBattleSystems()
        {
            VisionRules visionRules = AssetDatabase.LoadAssetAtPath<VisionRules>(VisionRulesPath);
            if (visionRules == null)
            {
                visionRules = ScriptableObject.CreateInstance<VisionRules>();
                AssetDatabase.CreateAsset(visionRules, VisionRulesPath);
            }

            visionRules.maximumConcealment = 0.8f;
            visionRules.maximumBushBonus = 0.45f;
            visionRules.minimumVisibilityFactor = 0.2f;
            visionRules.checkInterval = 0.2f;
            visionRules.checksPerFrame = 24;
            visionRules.contactMemorySeconds = 8f;
            visionRules.lightTankViewAngle = 160f;
            visionRules.mediumTankViewAngle = 140f;
            visionRules.heavyTankViewAngle = 120f;
            visionRules.tankDestroyerViewAngle = 100f;
            visionRules.artilleryViewAngle = 80f;
            visionRules.viewArcVisualRadius = 60f;
            visionRules.outsideArcOverlayAlpha = 0.14f;
            visionRules.viewBoundaryAlpha = 0.2f;
            visionRules.viewBoundaryWidth = 0.04f;
            EditorUtility.SetDirty(visionRules);

            GameObject systems = new GameObject("BattleSystems");
            TeamVisionSystem vision = systems.AddComponent<TeamVisionSystem>();
            vision.Configure(visionRules, TeamId.TeamA);
            BattleRoster roster = systems.AddComponent<BattleRoster>();
            roster.Configure(TeamId.TeamA, vision);
            systems.AddComponent<VisionDebugOverlay>().Configure(vision);
            systems.AddComponent<VisionArcPresenter>().Configure(vision, GetOrCreateLineMaterial());
            systems.AddComponent<CombatAiDebugOverlay>();
            systems.AddComponent<DeveloperTimeScaleController>();

            PanelSettings panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panelSettings == null)
            {
                panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panelSettings, PanelSettingsPath);
            }

            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            panelSettings.match = 0.5f;
            EditorUtility.SetDirty(panelSettings);

            VisualTreeAsset visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(BattleHudUxmlPath);
            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(BattleHudUssPath);
            if (visualTree == null || styleSheet == null)
            {
                throw new InvalidOperationException("Battle HUD UXML or USS could not be loaded.");
            }

            GameObject hud = new GameObject("BattleHUD");
            UIDocument document = hud.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = visualTree;
            document.sortingOrder = 100;
            CaptureBase[] captureBases = UnityEngine.Object.FindObjectsByType<CaptureBase>()
                .OrderBy(item => item.BaseId, StringComparer.Ordinal)
                .ToArray();
            hud.AddComponent<BattleHudController>().Configure(roster, styleSheet, captureBases);
            return vision;
        }

        private static void AddTerrainModifier(GameObject tank, NavigationMap map)
        {
            tank.AddComponent<TerrainMotorModifier>().Configure(map, tank.GetComponent<TankMotor>());
        }

        private static CameraFollow2D CreateCameraAndDiagnostics(NavigationMap map)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 13f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.075f, 0.055f);
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            cameraObject.AddComponent<AudioListener>();
            CameraFollow2D follow = cameraObject.AddComponent<CameraFollow2D>();
            follow.ConfigureZoomRange(5f, 22f, 1.5f);
            follow.SetZoom(13f, true);
            cameraObject.AddComponent<NavigationDebugOverlay>().Configure(map);
            cameraObject.AddComponent<PlayerAimReticle>();
            return follow;
        }

        private static void CreateBattleDirector(
            Scene scene,
            NavigationMap map,
            BattleScenario scenario,
            TeamVisionSystem visionSystem,
            CameraFollow2D cameraFollow)
        {
            CombatDatabase database = AssetDatabase.LoadAssetAtPath<CombatDatabase>(CombatDefinitionImporter.DatabasePath);
            if (database == null)
            {
                throw new InvalidOperationException("Combat database was not generated.");
            }

            TacticalMapDescriptorAsset tacticalMap = AssetDatabase.LoadAssetAtPath<TacticalMapDescriptorAsset>(
                TacticalMapDescriptorImporter.AssetPath);
            if (tacticalMap == null || tacticalMap.Validate().Length > 0)
            {
                throw new InvalidOperationException("Tactical map descriptor was not generated or is invalid.");
            }

            GameObject[] prefabs = TankPrefabPaths.Select(AssetDatabase.LoadAssetAtPath<GameObject>).ToArray();
            if (prefabs.Any(prefab => prefab == null))
            {
                throw new InvalidOperationException("Build the combat test range before building River Crossing.");
            }

            string[] tankIds = { "ms_1", "leichttraktor" };
            TankPrefabBinding[] prefabBindings = tankIds.Select((tankId, index) => new TankPrefabBinding
            {
                tankId = tankId,
                prefab = prefabs[index]
            }).ToArray();
            VehicleClassIconBinding[] iconBindings = Enum.GetValues(typeof(VehicleClass))
                .Cast<VehicleClass>()
                .Select(vehicleClass => new VehicleClassIconBinding
                {
                    vehicleClass = vehicleClass,
                    icon = AssetDatabase.LoadAssetAtPath<Sprite>($"{ClassIconsRoot}/{vehicleClass}.png")
                })
                .ToArray();
            if (iconBindings.Any(item => item.icon == null))
            {
                throw new InvalidOperationException("One or more vehicle class icons were not generated.");
            }

            CaptureBase[] captureBases = UnityEngine.Object.FindObjectsByType<CaptureBase>()
                .OrderBy(item => item.BaseId, StringComparer.Ordinal)
                .ToArray();
            GameObject root = new GameObject("BattleDirector");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<BattleDirector>().Configure(
                scenario,
                database,
                map,
                visionSystem,
                cameraFollow,
                captureBases,
                prefabBindings,
                iconBindings,
                tacticalMap);
        }

        private static GameObject CreateShape(
            Transform parent,
            string name,
            Vector2 localPosition,
            Vector2 size,
            Color color,
            int sortingOrder,
            Sprite sprite)
        {
            GameObject shape = new GameObject(name);
            shape.transform.SetParent(parent, false);
            shape.transform.localPosition = localPosition;
            SpriteRenderer renderer = shape.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            shape.transform.localScale = new Vector3(size.x / sprite.bounds.size.x, size.y / sprite.bounds.size.y, 1f);
            return shape;
        }

        private static string GroundTileName(char symbol)
        {
            switch (symbol)
            {
                case '=': return "Road";
                case 'm':
                case 'c': return "Mud";
                case 'w': return "ShallowWater";
                case '~':
                case 'D': return "DeepWater";
                default: return "Grass";
            }
        }

        private static bool IsVisionBlockingSymbol(char symbol)
        {
            return symbol == '#' || symbol == 'x' || symbol == 'T' || symbol == 'P' ||
                   symbol == '1' || symbol == '2' || symbol == '3' || symbol == '4' ||
                   symbol == 's' || symbol == 'r';
        }

        private static bool IsHardObstacleSymbol(char symbol)
        {
            return symbol == '#' || symbol == 'x' || symbol == 'T' || symbol == 'P' ||
                   symbol == '1' || symbol == '2' || symbol == '3' || symbol == '4' ||
                   symbol == 's' || symbol == 'r';
        }

        private static Vector2Int FindCell(MapSource source, char symbol)
        {
            for (int sourceRow = 0; sourceRow < source.height; sourceRow++)
            {
                int column = source.rows[sourceRow].IndexOf(symbol);
                if (column >= 0)
                {
                    return new Vector2Int(column, source.height - 1 - sourceRow);
                }
            }

            throw new InvalidDataException($"Map marker '{symbol}' was not found.");
        }

        private static Vector2 MapOrigin(MapSource source)
        {
            return new Vector2(-source.width * source.cellSize * 0.5f, -source.height * source.cellSize * 0.5f);
        }

        private static Sprite GetShapeSprite()
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ShapeTexturePath);
            if (sprite != null)
            {
                return sprite;
            }

            Texture2D texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            Color32[] pixels = Enumerable.Repeat(new Color32(255, 255, 255, 255), 16 * 16).ToArray();
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(Path.GetFullPath(ShapeTexturePath), texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(ShapeTexturePath, ImportAssetOptions.ForceSynchronousImport);

            TextureImporter importer = AssetImporter.GetAtPath(ShapeTexturePath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException("Generated square texture could not be imported.");
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 16f;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ShapeTexturePath);
            if (sprite == null)
            {
                throw new InvalidOperationException("Generated square sprite could not be loaded.");
            }

            return sprite;
        }

        private static Sprite GetCircleSprite()
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(CircleTexturePath);
            if (sprite != null) return sprite;

            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[size * size];
            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            float radius = size * 0.48f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    pixels[y * size + x] = Vector2.Distance(new Vector2(x, y), center) <= radius
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(255, 255, 255, 0);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(Path.GetFullPath(CircleTexturePath), texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(CircleTexturePath, ImportAssetOptions.ForceSynchronousImport);

            TextureImporter importer = AssetImporter.GetAtPath(CircleTexturePath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Generated circle texture could not be imported.");
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = size;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            sprite = AssetDatabase.LoadAssetAtPath<Sprite>(CircleTexturePath);
            if (sprite == null) throw new InvalidOperationException("Generated circle sprite could not be loaded.");
            return sprite;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static void EnsureSceneInBuildSettings()
        {
            List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
            foreach (string path in new[] { ScenePath, DuelScenePath, PlayerTestScenePath })
            {
                if (scenes.All(item => item.path != path))
                    scenes.Add(new EditorBuildSettingsScene(path, true));
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        [Serializable]
        private sealed class MapSource
        {
            public int schemaVersion;
            public string id;
            public string displayName;
            public int width;
            public int height;
            public float cellSize;
            public TerrainSource[] terrains;
            public string[] rows;
        }

        [Serializable]
        private sealed class TerrainSource
        {
            public string symbol;
            public string kind;
            public float navigationCost;
            public float speedMultiplier;
            public float accelerationMultiplier;
            public bool blocked;
            public float red;
            public float green;
            public float blue;
        }

        private sealed class TerrainLookup
        {
            public TerrainLookup(
                Dictionary<char, TerrainSource> definitions,
                Dictionary<string, Tile> groundTiles,
                Tile blockerTile)
            {
                Definitions = definitions;
                GroundTiles = groundTiles;
                BlockerTile = blockerTile;
            }

            public Dictionary<char, TerrainSource> Definitions { get; }
            public Dictionary<string, Tile> GroundTiles { get; }
            public Tile BlockerTile { get; }
        }
    }
}
