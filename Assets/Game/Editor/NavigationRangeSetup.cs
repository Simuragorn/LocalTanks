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

namespace LocalTanks.Editor
{
    public static class NavigationRangeSetup
    {
        private const string SourcePath = "Assets/Game/GameData/Maps/navigation_range.json";
        private const string ScenePath = "Assets/Game/Scenes/Battle_NavigationRange.unity";
        private const string GeneratedRoot = "Assets/Game/Generated/Navigation";
        private const string TilesRoot = GeneratedRoot + "/Tiles";

        private static readonly string[] TankPrefabPaths =
        {
            "Assets/Game/Prefabs/Tanks/TigerII_Player.prefab",
            "Assets/Game/Prefabs/Tanks/E100_Player.prefab",
            "Assets/Game/Prefabs/Tanks/T34_Player.prefab",
            "Assets/Game/Prefabs/Tanks/PzKpfwIV_Player.prefab"
        };

        [MenuItem("Local Tanks/Build Navigation Range")]
        public static void Build()
        {
            NavigationMapSource source = LoadAndValidateSource();
            EnsureFolder("Assets/Game/Generated");
            EnsureFolder(GeneratedRoot);
            EnsureFolder(TilesRoot);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "Battle_NavigationRange";

            TerrainLookup lookup = BuildTerrainLookup(source);
            CreateTilemap(source, lookup);
            NavigationMap map = CreateNavigationMap(source, lookup);
            DestructibleObstacle wall = CreateDestructibleWall(source, lookup, map);
            CreateTanks(scene, map);
            CreateCameraAndDiagnostics(map);
            CreateInstructions(wall);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EnsureSceneInBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"Local Tanks navigation range created from {SourcePath}: {ScenePath}");
        }

        private static NavigationMapSource LoadAndValidateSource()
        {
            string absolutePath = Path.GetFullPath(SourcePath);
            if (!File.Exists(absolutePath))
            {
                throw new FileNotFoundException("Navigation map JSON was not found.", SourcePath);
            }

            NavigationMapSource source = JsonUtility.FromJson<NavigationMapSource>(File.ReadAllText(absolutePath));
            if (source == null || source.schemaVersion != 1 || source.width <= 0 || source.height <= 0 || source.cellSize <= 0f)
            {
                throw new InvalidDataException("Navigation map header is invalid.");
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
                }
            }

            return source;
        }

        private static TerrainLookup BuildTerrainLookup(NavigationMapSource source)
        {
            Dictionary<char, TerrainSource> definitions = source.terrains.ToDictionary(item => item.symbol[0]);
            Dictionary<char, Tile> tiles = new Dictionary<char, Tile>();
            Sprite sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            foreach (TerrainSource terrain in source.terrains)
            {
                char symbol = terrain.symbol[0];
                string safeName = symbol == '#' ? "Solid" : symbol == 'd' ? "Destructible" : terrain.kind;
                string path = $"{TilesRoot}/Tile_{safeName}.asset";
                Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
                if (tile == null)
                {
                    tile = ScriptableObject.CreateInstance<Tile>();
                    AssetDatabase.CreateAsset(tile, path);
                }

                tile.sprite = sprite;
                tile.color = new Color(terrain.red, terrain.green, terrain.blue, 1f);
                tile.colliderType = Tile.ColliderType.Grid;
                EditorUtility.SetDirty(tile);
                tiles[symbol] = tile;
            }

            return new TerrainLookup(definitions, tiles);
        }

        private static void CreateTilemap(NavigationMapSource source, TerrainLookup lookup)
        {
            GameObject gridObject = new GameObject("NavigationGrid");
            Grid grid = gridObject.AddComponent<Grid>();
            grid.cellSize = new Vector3(source.cellSize, source.cellSize, 1f);
            gridObject.transform.position = MapOrigin(source);

            Tilemap ground = CreateTilemapLayer(gridObject.transform, "Ground", -20, false);
            Tilemap solids = CreateTilemapLayer(gridObject.transform, "SolidObstacles", 0, true);
            Tile grassTile = lookup.Tiles['.'];

            for (int sourceRow = 0; sourceRow < source.height; sourceRow++)
            {
                int y = source.height - 1 - sourceRow;
                for (int x = 0; x < source.width; x++)
                {
                    char symbol = source.rows[sourceRow][x];
                    Vector3Int cell = new Vector3Int(x, y, 0);
                    if (symbol == '#')
                    {
                        ground.SetTile(cell, grassTile);
                        solids.SetTile(cell, lookup.Tiles[symbol]);
                    }
                    else if (symbol == 'd')
                    {
                        ground.SetTile(cell, grassTile);
                    }
                    else
                    {
                        ground.SetTile(cell, lookup.Tiles[symbol]);
                    }
                }
            }
        }

        private static Tilemap CreateTilemapLayer(Transform parent, string name, int sortingOrder, bool collider)
        {
            GameObject layer = new GameObject(name);
            layer.transform.SetParent(parent, false);
            Tilemap tilemap = layer.AddComponent<Tilemap>();
            TilemapRenderer renderer = layer.AddComponent<TilemapRenderer>();
            renderer.sortingOrder = sortingOrder;
            if (collider)
            {
                layer.AddComponent<TilemapCollider2D>();
            }

            return tilemap;
        }

        private static NavigationMap CreateNavigationMap(NavigationMapSource source, TerrainLookup lookup)
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

        private static DestructibleObstacle CreateDestructibleWall(
            NavigationMapSource source,
            TerrainLookup lookup,
            NavigationMap map)
        {
            List<Vector2Int> cells = FindCells(source, 'd');
            if (cells.Count == 0)
            {
                throw new InvalidDataException("Navigation range must contain a destructible wall marked with 'd'.");
            }

            int minX = cells.Min(cell => cell.x);
            int maxX = cells.Max(cell => cell.x);
            int minY = cells.Min(cell => cell.y);
            int maxY = cells.Max(cell => cell.y);
            Vector2 min = map.CellToWorld(new Vector2Int(minX, minY));
            Vector2 max = map.CellToWorld(new Vector2Int(maxX, maxY));
            Vector2 size = new Vector2(
                (maxX - minX + 1) * source.cellSize,
                (maxY - minY + 1) * source.cellSize);

            GameObject wall = new GameObject("DestructibleWall");
            wall.transform.position = (min + max) * 0.5f;
            BoxCollider2D collider = wall.AddComponent<BoxCollider2D>();
            collider.size = size;

            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(wall.transform, false);
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = lookup.Tiles['d'].sprite;
            renderer.color = lookup.Tiles['d'].color;
            renderer.sortingOrder = 1;
            visual.transform.localScale = new Vector3(
                size.x / renderer.sprite.bounds.size.x,
                size.y / renderer.sprite.bounds.size.y,
                1f);

            DestructibleObstacle obstacle = wall.AddComponent<DestructibleObstacle>();
            obstacle.Configure(map, cells.ToArray(), 400);
            return obstacle;
        }

        private static void CreateTanks(Scene scene, NavigationMap map)
        {
            GameObject[] prefabs = TankPrefabPaths
                .Select(path => AssetDatabase.LoadAssetAtPath<GameObject>(path))
                .ToArray();
            if (prefabs.Any(prefab => prefab == null))
            {
                throw new InvalidOperationException("Build the combat test range before building the navigation range.");
            }

            GameObject player = CreateTank(prefabs[0], scene, "Navigation_Player", map.CellToWorld(new Vector2Int(19, 4)));
            AddTerrainModifier(player, map);

            CreateAgent(
                prefabs[2], scene, "NavAgent_T34", map, new Vector2Int(8, 5),
                new[] { new Vector2Int(31, 18), new Vector2Int(8, 5) });
            CreateAgent(
                prefabs[3], scene, "NavAgent_PanzerIV", map, new Vector2Int(31, 5),
                new[] { new Vector2Int(8, 18), new Vector2Int(31, 5) });
            CreateAgent(
                prefabs[1], scene, "NavAgent_E100", map, new Vector2Int(12, 18),
                new[] { new Vector2Int(27, 5), new Vector2Int(12, 18) });
        }

        private static GameObject CreateTank(GameObject prefab, Scene scene, string name, Vector2 position)
        {
            GameObject tank = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            tank.name = name;
            tank.transform.SetPositionAndRotation(position, Quaternion.identity);
            return tank;
        }

        private static void CreateAgent(
            GameObject prefab,
            Scene scene,
            string name,
            NavigationMap map,
            Vector2Int spawn,
            Vector2Int[] patrolCells)
        {
            GameObject tank = CreateTank(prefab, scene, name, map.CellToWorld(spawn));
            DisableCombatControls(tank);
            TankMotor motor = tank.GetComponent<TankMotor>();
            AddTerrainModifier(tank, map);
            NavigationAgent agent = tank.AddComponent<NavigationAgent>();
            agent.Configure(map, motor, patrolCells.Select(map.CellToWorld).ToArray(), 1, true);
            TankHealthBar bar = tank.AddComponent<TankHealthBar>();
            bar.Configure(1.45f);
        }

        private static void AddTerrainModifier(GameObject tank, NavigationMap map)
        {
            TerrainMotorModifier modifier = tank.AddComponent<TerrainMotorModifier>();
            modifier.Configure(map, tank.GetComponent<TankMotor>());
        }

        private static void DisableCombatControls(GameObject tank)
        {
            PlayerTankInput input = tank.GetComponent<PlayerTankInput>();
            TurretAiming turret = tank.GetComponentInChildren<TurretAiming>();
            WeaponController weapon = tank.GetComponent<WeaponController>();
            if (input != null) input.enabled = false;
            if (turret != null) turret.enabled = false;
            if (weapon != null) weapon.enabled = false;
        }

        private static void CreateCameraAndDiagnostics(NavigationMap map)
        {
            GameObject player = GameObject.Find("Navigation_Player");
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 10f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.07f, 0.06f);
            cameraObject.transform.position = new Vector3(player.transform.position.x, player.transform.position.y, -10f);
            cameraObject.AddComponent<AudioListener>();
            CameraFollow2D follow = cameraObject.AddComponent<CameraFollow2D>();
            follow.Configure(player.transform);
            follow.SetZoom(10f, true);

            NavigationDebugOverlay overlay = cameraObject.AddComponent<NavigationDebugOverlay>();
            overlay.Configure(map);
        }

        private static void CreateInstructions(DestructibleObstacle wall)
        {
            GameObject instructions = new GameObject("NavigationRangeInstructions");
            NavigationRangeInstructions hud = instructions.AddComponent<NavigationRangeInstructions>();
            hud.Configure(wall);
        }

        private static List<Vector2Int> FindCells(NavigationMapSource source, char symbol)
        {
            List<Vector2Int> cells = new List<Vector2Int>();
            for (int sourceRow = 0; sourceRow < source.height; sourceRow++)
            {
                int y = source.height - 1 - sourceRow;
                for (int x = 0; x < source.width; x++)
                {
                    if (source.rows[sourceRow][x] == symbol)
                    {
                        cells.Add(new Vector2Int(x, y));
                    }
                }
            }

            return cells;
        }

        private static Vector2 MapOrigin(NavigationMapSource source)
        {
            return new Vector2(-source.width * source.cellSize * 0.5f, -source.height * source.cellSize * 0.5f);
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
            if (scenes.All(item => item.path != ScenePath))
            {
                scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        [Serializable]
        private sealed class NavigationMapSource
        {
            public int schemaVersion;
            public string id;
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
            public readonly Dictionary<char, TerrainSource> Definitions;
            public readonly Dictionary<char, Tile> Tiles;

            public TerrainLookup(Dictionary<char, TerrainSource> definitions, Dictionary<char, Tile> tiles)
            {
                Definitions = definitions;
                Tiles = tiles;
            }
        }
    }
}
