using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace LocalTanks.Editor
{
    /// <summary>
    /// Builds a playable battle scene from an imported World of Tanks arena. The map asset owns all
    /// geometry; this class only turns it into Unity objects, so re-importing the arena and rebuilding
    /// the scene is the whole iteration loop.
    /// </summary>
    public static class WoTMapSceneBuilder
    {
        private const string GeneratedRoot = "Assets/Game/Generated/WoTMap";
        private const string ClassIconsRoot = "Assets/Game/Generated/UI/VehicleClasses";
        private const float GroundSortingOrder = -20f;
        private const float ObstacleSortingOrder = -5f;

        [MenuItem("Local Tanks/Build WoT Map Scene (Mines)")]
        public static void BuildDefaultMapScene() => Build("10_hills");

        public static void Build(string mapId)
        {
            WoTMapAsset asset = WoTMapImporter.ImportAsset(mapId);
            string scenePath = WoTMapImporter.ScenePath(mapId);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = $"Battle_{mapId}";
            SceneManager.SetActiveScene(scene);

            NavigationMap navigation = CreateNavigationMap(asset);
            CreateGround(asset);
            CreateContentRoots(asset);
            CaptureBase[] bases = CreateBases(asset);
            CameraFollow2D camera = CreateCamera(navigation, asset);
            TeamVisionSystem vision = CreateBattleSystems();
            CreateBattleDirector(asset, navigation, vision, camera, bases);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, scenePath);
            EnsureSceneInBuildSettings(scenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"WoT battle scene built from '{asset.mapId}': {scenePath}");
        }

        /// <summary>
        /// Navigation grid of the arena. Cells, costs and speed multipliers come from the map asset, so
        /// the scene never recomputes what the pipeline already decided.
        /// </summary>
        private static NavigationMap CreateNavigationMap(WoTMapAsset asset)
        {
            int count = asset.navigationColumns * asset.navigationRows;
            TerrainKind[] terrain = asset.navigationTerrain.Select(kind => (TerrainKind)kind).ToArray();
            float[] costs = asset.navigationCosts ?? Enumerable.Repeat(1f, count).ToArray();
            float[] speeds = asset.navigationSpeedMultipliers ?? Enumerable.Repeat(1f, count).ToArray();
            // Cover steers a route through its movement cost, which is the part that matters. Its speed
            // penalty is dropped here because a navigation cell is four units across while a hull is about
            // one, so a per-cell penalty slows a tank almost everywhere on a dense arena: measured speeds
            // fell to 0.05 against a limit of 2.59 and the battle never started.
            for (int index = 0; index < count; index++)
            {
                if (terrain[index] == TerrainKind.Obstacle) speeds[index] = 1f;
            }

            float[] accelerations = speeds;
            bool[] blocked = asset.navigationBlocked;

            GameObject mapObject = new GameObject("NavigationMap");
            SceneManager.MoveGameObjectToScene(mapObject, SceneManager.GetActiveScene());
            NavigationMap map = mapObject.AddComponent<NavigationMap>();
            map.Configure(
                asset.navigationColumns,
                asset.navigationRows,
                asset.navigationCellSize,
                asset.worldCenter - asset.playableSize * 0.5f,
                terrain,
                costs,
                speeds,
                accelerations,
                blocked);
            return map;
        }

        /// <summary>
        /// Ground is one tinted sheet per navigation terrain kind rather than a tile instance per cell:
        /// the arena is 208 units across at a four-unit grid, and a tile per cell would add thousands of
        /// objects for a single flat colour. Real surface art arrives later as textures over this sheet.
        /// </summary>
        private static void CreateGround(WoTMapAsset asset)
        {
            GameObject grid = new GameObject("Ground");
            SceneManager.MoveGameObjectToScene(grid, SceneManager.GetActiveScene());
            Grid gridComponent = grid.AddComponent<Grid>();
            gridComponent.cellSize = new Vector3(asset.navigationCellSize, asset.navigationCellSize, 1f);
            gridComponent.transform.position = asset.worldCenter - asset.playableSize * 0.5f;

            Tilemap ground = CreateTilemapLayer(grid.transform, "Ground", GroundSortingOrder, false);
            Tile tile = CreateGroundTile(new Color(0.24f, 0.34f, 0.21f));
            for (int row = 0; row < asset.navigationRows; row++)
            {
                for (int column = 0; column < asset.navigationColumns; column++)
                {
                    int index = row * asset.navigationColumns + column;
                    TerrainKind kind = (TerrainKind)asset.navigationTerrain[index];
                    tile.color = GroundColor(kind);
                    ground.SetTile(new Vector3Int(column, row, 0), tile);
                }
            }
        }

        private static Color GroundColor(TerrainKind kind)
        {
            return kind switch
            {
                TerrainKind.Road => new Color(0.42f, 0.34f, 0.22f),
                TerrainKind.Mud => new Color(0.29f, 0.21f, 0.12f),
                TerrainKind.ShallowWater => new Color(0.13f, 0.34f, 0.45f),
                // Cover is drawn as dry ground, not as a wall: a tank drives through it and is slowed.
                TerrainKind.Obstacle => new Color(0.31f, 0.32f, 0.20f),
                TerrainKind.Blocked => new Color(0.16f, 0.16f, 0.15f),
                _ => new Color(0.24f, 0.34f, 0.21f)
            };
        }

        /// <summary>
        /// Creates the empty parents and the runtime builder that fills them from the map asset. The
        /// scene keeps the map reference and the object hierarchy; the thousands of colliders are built
        /// on load, because serialising them added twenty megabytes to one scene file.
        /// </summary>
        private static void CreateContentRoots(WoTMapAsset asset)
        {
            GameObject root = new GameObject("WoTMapContent");
            SceneManager.MoveGameObjectToScene(root, SceneManager.GetActiveScene());
            WoTMapContentBuilder builder = root.AddComponent<WoTMapContentBuilder>();
            builder.Configure(asset);
        }

        /// <summary>
        /// Two capture bases, one per team, with a ring of spawn slots each. The source arena provides a
        /// single spawn per side, while a battle needs a slot per tank, so the slots are placed on rings
        /// around the base and spaced by the widest hull the game fields.
        /// </summary>
        private static CaptureBase[] CreateBases(WoTMapAsset asset)
        {
            GameObject root = new GameObject("Bases");
            SceneManager.MoveGameObjectToScene(root, SceneManager.GetActiveScene());
            List<CaptureBase> bases = new List<CaptureBase>();

            for (int index = 0; index < asset.basePositions.Length; index++)
            {
                Vector2 position = asset.basePositions[index];
                TeamId owner = index == 0 ? TeamId.TeamA : TeamId.TeamB;
                string baseId = index == 0 ? "A" : "B";
                GameObject baseObject = new GameObject($"Base_{baseId}");
                baseObject.transform.SetParent(root.transform, false);
                baseObject.transform.position = new Vector3(position.x, position.y, 0f);

                List<SpriteRenderer> slots = CreateSpawnSlots(baseObject.transform, asset, position);
                int blockedSlots = slots.Count(slot => IsBlocked(asset, slot.transform.position));
                if (blockedSlots > 0)
                {
                    Debug.LogWarning($"Base {baseId}: {blockedSlots} of {slots.Count} spawn slots sit on ground " +
                                     "that routing cannot cross, so a tank spawned there may not be able to leave.");
                }
                LineRenderer ring = baseObject.AddComponent<LineRenderer>();
                ring.useWorldSpace = false;
                ring.loop = true;
                ring.widthMultiplier = 0.4f;
                ring.positionCount = 24;
                for (int step = 0; step < 24; step++)
                {
                    float angle = step * Mathf.PI * 2f / 24f;
                    ring.SetPosition(step, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 9f);
                }

                CaptureBase capture = baseObject.AddComponent<CaptureBase>();
                capture.Configure(baseId, $"ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚ÂÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¹Ãƒâ€¦Ã¢â‚¬Å“ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚ÂÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â°ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚ÂÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â·ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚ÂÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â° {baseId}", owner, new CaptureRules(), ring);
                bases.Add(capture);
            }

            return bases.ToArray();
        }

        /// <summary>
        /// Spawn slots on a ring around the base. A slot that lands on ground routing cannot cross is
        /// moved outward along its own direction until it finds open ground, because a tank spawned
        /// inside geometry cannot drive out and looks exactly like a broken AI: one converted base had
        /// five of its seven tanks trapped this way.
        /// </summary>
        private static List<SpriteRenderer> CreateSpawnSlots(Transform parent, WoTMapAsset asset, Vector2 center)
        {
            const int slotCount = 16;
            const float firstRadius = 6f;
            List<SpriteRenderer> slots = new List<SpriteRenderer>(slotCount);
            for (int index = 0; index < slotCount; index++)
            {
                float angle = index * Mathf.PI * 2f / slotCount;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 position = center + direction * firstRadius;
                for (float radius = firstRadius; radius <= firstRadius + 24f; radius += 2f)
                {
                    position = center + direction * radius;
                    if (!IsBlocked(asset, position)) break;
                }

                GameObject slot = new GameObject($"Spawn_{index:00}");
                slot.transform.SetParent(parent, false);
                slot.transform.localPosition = new Vector3(position.x - center.x, position.y - center.y, 0f);
                SpriteRenderer renderer = slot.AddComponent<SpriteRenderer>();
                slots.Add(renderer);
            }

            return slots;
        }

        /// <summary>
        /// Returns the path running away from <paramref name="fromWorld"/>. The tactical analysis draws
        /// every lane from one base to the other without recording which, so the direction is decided by
        /// geometry: the endpoint nearest the team's own base becomes the start. Assuming the direction
        /// instead sent one team across the map from the enemy's side.
        /// </summary>
        private static Vector2Int[] OrientFrom(Vector2Int[] path, Vector2 fromWorld, float cellSize, Vector2 origin)
        {
            if (path == null || path.Length < 2) return path;

            // Compared in world units, the space both the base and the cell centres live in.
            Vector2 firstWorld = origin + new Vector2(
                (path[0].x + 0.5f) * cellSize, (path[0].y + 0.5f) * cellSize);
            Vector2 lastWorld = origin + new Vector2(
                (path[path.Length - 1].x + 0.5f) * cellSize,
                (path[path.Length - 1].y + 0.5f) * cellSize);
            return Vector2.Distance(firstWorld, fromWorld) <= Vector2.Distance(lastWorld, fromWorld)
                ? path
                : path.Reverse().ToArray();
        }

        /// <summary>
        /// Whether a world position falls on ground the navigation grid marks as closed. A tank spawned
        /// there is boxed in by geometry, which looks exactly like a broken AI.
        /// </summary>
        private static bool IsBlocked(WoTMapAsset asset, Vector2 world)
        {
            Vector2 origin = asset.worldCenter - asset.playableSize * 0.5f;
            int column = Mathf.FloorToInt((world.x - origin.x) / asset.navigationCellSize);
            int row = Mathf.FloorToInt((world.y - origin.y) / asset.navigationCellSize);
            if (column < 0 || row < 0 || column >= asset.navigationColumns || row >= asset.navigationRows)
            {
                return true;
            }

            return asset.navigationBlocked[row * asset.navigationColumns + column];
        }

        private static CameraFollow2D CreateCamera(NavigationMap map, WoTMapAsset asset)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            SceneManager.MoveGameObjectToScene(cameraObject, SceneManager.GetActiveScene());
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            // The arena is eight times wider than the current map, so the camera can pull back much
            // further before the field stops being readable.
            camera.orthographicSize = 26f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.075f, 0.055f);
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            cameraObject.AddComponent<AudioListener>();

            CameraFollow2D follow = cameraObject.AddComponent<CameraFollow2D>();
            follow.ConfigureZoomRange(8f, 60f, 4f);
            follow.SetZoom(26f, true);
            cameraObject.AddComponent<NavigationDebugOverlay>().Configure(map);
            cameraObject.AddComponent<PlayerAimReticle>();
            return follow;
        }

        private static TeamVisionSystem CreateBattleSystems()
        {
            GameObject systems = new GameObject("BattleSystems");
            SceneManager.MoveGameObjectToScene(systems, SceneManager.GetActiveScene());
            VisionRules rules = AssetDatabase.LoadAssetAtPath<VisionRules>(
                "Assets/Game/Generated/RiverCrossing/VisionRules.asset");
            if (rules == null)
            {
                throw new InvalidOperationException(
                    "Vision rules were not generated. Build the River Crossing map once to create them.");
            }

            TeamVisionSystem vision = systems.AddComponent<TeamVisionSystem>();
            vision.Configure(rules, TeamId.TeamA);
            systems.AddComponent<BattleVisibilitySettings>().Configure(false, 0.28f);
            return vision;
        }

        /// <summary>
        /// Wires the director so the scene is a complete, playable battle rather than a half-built one.
        /// The arena supplies two bases and a spawn field, not a hand-authored roster, so a symmetric
        /// scenario is generated from the playable tanks: one nation per team, the same vehicle on both
        /// sides, which keeps nationality rules that the rest of the game enforces.
        /// </summary>
        private static void CreateBattleDirector(
            WoTMapAsset asset,
            NavigationMap map,
            TeamVisionSystem vision,
            CameraFollow2D camera,
            CaptureBase[] bases)
        {
            CombatDatabase database = AssetDatabase.LoadAssetAtPath<CombatDatabase>(
                CombatDefinitionImporter.DatabasePath);
            if (database == null)
            {
                throw new InvalidOperationException("Combat database was not generated.");
            }

            // Each arena needs its own analysis: the shared descriptor describes another map, and the
            // director rejects a mismatch rather than letting the AI reason about the wrong battlefield.
            TacticalMapDescriptorAsset tacticalMap = WoTMapTacticalDescriptorBuilder.Build(asset.mapId);

            VehicleClassIconBinding[] icons = Enum.GetValues(typeof(VehicleClass))
                .Cast<VehicleClass>()
                .Select(vehicleClass => new VehicleClassIconBinding
                {
                    vehicleClass = vehicleClass,
                    icon = AssetDatabase.LoadAssetAtPath<Sprite>($"{ClassIconsRoot}/{vehicleClass}.png")
                })
                .ToArray();
            if (icons.Any(item => item.icon == null))
            {
                throw new InvalidOperationException("One or more vehicle class icons were not generated.");
            }

            TankPrefabBinding[] prefabs = CreatePrefabBindings(asset);
            BattleScenario scenario = CreateScenario(database, prefabs, tacticalMap, asset, asset.basePositions, $"Battle_{asset.mapId}");
            GameObject root = new GameObject("BattleDirector");
            SceneManager.MoveGameObjectToScene(root, SceneManager.GetActiveScene());
            root.AddComponent<BattleDirector>().Configure(
                scenario,
                database,
                map,
                vision,
                camera,
                bases,
                prefabs,
                icons,
                tacticalMap);
        }

        /// <summary>Binds every playable tank, exactly as the other arena scenes do.</summary>
        private static TankPrefabBinding[] CreatePrefabBindings(WoTMapAsset asset)
        {
            return WoTMapImporter.PlayableTankIds()
                .Select(tankId =>
                {
                    string path = PrototypeSetup.FindTankPrefabPath(tankId);
                    GameObject prefab = string.IsNullOrWhiteSpace(path)
                        ? null
                        : AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null)
                    {
                        throw new InvalidOperationException(
                            $"Prefab was not generated for playable tank '{tankId}'. Build the combat assets first.");
                    }

                    return new TankPrefabBinding { tankId = tankId, prefab = prefab };
                })
                .ToArray();
        }

        /// <summary>
        /// A symmetric roster on the arena's two bases. Each side fields the same tank, chosen as the
        /// lowest-tier playable machine so both teams are matched, and each slot uses one of the base's
        /// own spawn markers.
        /// <para>
        /// Every AI entry carries the route of a real analysed lane. The tactical analysis produces paths
        /// from base B to base A, so a Team B entry follows the path as given and a Team A entry follows
        /// it reversed. Bots are spread across the lanes in turn, which is what makes them use the whole
        /// arena instead of walking into the same wall.
        /// </para>
        /// </summary>
        private static BattleScenario CreateScenario(
            CombatDatabase database,
            TankPrefabBinding[] prefabs,
            TacticalMapDescriptorAsset tacticalMap,
            WoTMapAsset mapAsset,
            Vector2[] baseSpawnPositions,
            string sceneId)
        {
            TankDefinition chosen = database.tanks
                .Where(tank => tank != null && tank.availableInGame)
                .OrderBy(tank => tank.tier)
                .ThenBy(tank => tank.id, StringComparer.Ordinal)
                .First();
            int teamSize = BattleScenario.TeamSize;

            Vector2Int[][] paths = tacticalMap.descriptor.lanes
                .Where(lane => lane != null && lane.pathCells != null && lane.pathCells.Length >= 2)
                .OrderByDescending(lane => lane.lengthWorld)
                .Select(lane => lane.pathCells)
                .ToArray();
            if (paths.Length == 0)
            {
                throw new InvalidOperationException(
                    "The tactical analysis produced no lane with a usable path, so bots would have no route.");
            }

            // The game enforces a fixed shape: two bots per flank lane and three down the middle. The
            // analysed lanes are assigned to those three roles so the arena's real routes drive the
            // formation instead of the bots sharing one path.
            BattleLane[] formation = { BattleLane.Center, BattleLane.North, BattleLane.South };
            int[] perLane = { 3, 2, 2 };

            // The route must leave the tank's own base, so every lane is oriented from that team's spawn.
            // The tactical source is also transformed by the grid origin and cell size, so those travel
            // with the paths to keep one definition of where a cell sits.
            Vector2 worldOrigin = mapAsset.worldCenter - mapAsset.playableSize * 0.5f;
            Vector2 spawnA = baseSpawnPositions[0];
            Vector2 spawnB = baseSpawnPositions[1];
            Vector2[] spawnOrigins = { spawnA, spawnA, spawnA, spawnB, spawnB, spawnB };

            List<BattleScenarioEntry> entries = new List<BattleScenarioEntry>();
            for (int laneIndex = 0; laneIndex < formation.Length; laneIndex++)
            {
                Vector2Int[] forward = paths[laneIndex % paths.Length];
                for (int within = 0; within < perLane[laneIndex]; within++)
                {
                    bool player = laneIndex == 1 && within == 0;
                    entries.Add(new BattleScenarioEntry
                    {
                        id = $"a_{formation[laneIndex]}_{within:00}",
                        tankId = chosen.id,
                        team = TeamId.TeamA,
                        playerControlled = player,
                        baseId = "A",
                        spawnSlot = entries.Count(entry => entry.team == TeamId.TeamA),
                        lane = formation[laneIndex],
                        role = laneIndex == 0 ? CombatAiRole.Assault : CombatAiRole.Flank,
                        // The route must leave the tank's own base. Orienting it by geometry rather than by
                        // assuming the analysis direction cannot silently send a team across the map from
                        // the enemy's side, which is what happened when the assumption was wrong.
                        routeCells = player ? null : OrientFrom(forward, spawnOrigins[laneIndex], mapAsset.navigationCellSize, worldOrigin)
                    });
                    entries.Add(new BattleScenarioEntry
                    {
                        id = $"b_{formation[laneIndex]}_{within:00}",
                        tankId = chosen.id,
                        team = TeamId.TeamB,
                        playerControlled = false,
                        baseId = "B",
                        spawnSlot = entries.Count(entry => entry.team == TeamId.TeamB),
                        lane = formation[laneIndex],
                        role = laneIndex == 0 ? CombatAiRole.Assault : CombatAiRole.Flank,
                        routeCells = OrientFrom(forward, spawnOrigins[laneIndex + 3], mapAsset.navigationCellSize, worldOrigin)
                    });
                }
            }

            BattleScenario scenario = ScriptableObject.CreateInstance<BattleScenario>();
            scenario.id = "generated_10_hills";
            scenario.sceneId = sceneId;
            scenario.kind = BattleScenarioKind.StandardBattle;
            scenario.entries = entries.ToArray();
            return scenario;
        }

        private static Tilemap CreateTilemapLayer(Transform parent, string name, float sortingOrder, bool collider)
        {
            GameObject layer = new GameObject(name);
            layer.transform.SetParent(parent, false);
            Tilemap tilemap = layer.AddComponent<Tilemap>();
            TilemapRenderer renderer = layer.AddComponent<TilemapRenderer>();
            renderer.sortingOrder = Mathf.RoundToInt(sortingOrder);
            if (collider)
            {
                layer.AddComponent<TilemapCollider2D>();
            }

            return tilemap;
        }

        private static Tile CreateGroundTile(Color color)
        {
            Directory.CreateDirectory(GeneratedRoot);
            string path = $"{GeneratedRoot}/WoTGroundTile.asset";
            Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, path);
            }

            if (tile.sprite == null)
            {
                tile.sprite = GetShapeSprite();
            }

            tile.colliderType = Tile.ColliderType.None;
            tile.color = color;
            EditorUtility.SetDirty(tile);
            return tile;
        }

        /// <summary>Flat white sprite used for the ground sheet, tinted per tile.</summary>
        private static Sprite GetShapeSprite()
        {
            string path = "Assets/Game/Generated/RiverCrossing/WhiteSquare.png";
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) return sprite;

            // Fall back to Unity's built-in square so the builder works on a fresh checkout.
            return AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        }

        private static void EnsureSceneInBuildSettings(string scenePath)
        {
            List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(item => item.path == scenePath)) return;
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
