using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;

namespace LocalTanks.Editor
{
    /// <summary>
    /// Builds a playable battle scene from an imported World of Tanks arena. The map asset owns all
    /// geometry; this class only turns it into Unity objects, so re-importing the arena and rebuilding
    /// the scene is the whole iteration loop.
    /// </summary>
    public static class WoTMapSceneBuilder
    {
        /// <summary>
        /// Renders the arena from above and saves it, so the ground can be judged by looking at it rather
        /// than by reading scene data. A null graphics device discards every draw call, so this is the only
        /// way to see whether the surface, the navigation wash and the art end up as intended.
        /// </summary>
        [MenuItem("Local Tanks/Capture WoT Map Preview")]
        public static void CapturePreview()
        {
            UnityEngine.SceneManagement.Scene scene =
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Camera camera = UnityEngine.Object.FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                throw new InvalidOperationException("The arena scene has no camera to render from.");
            }

            WoTMapAsset asset = AssetDatabase.LoadAssetAtPath<WoTMapAsset>(AssetPath);
            if (asset == null)
            {
                throw new InvalidOperationException($"The converted map asset is missing at {AssetPath}.");
            }

            // Seen from directly above the middle, framed to the playable area: the same view a player gets
            // when zoomed out, which is where a wrong ground colour or a stray overlay shows up first.
            camera.transform.position = new Vector3(asset.worldCenter.x, asset.worldCenter.y, -10f);
            camera.transform.rotation = Quaternion.identity;
            camera.orthographic = true;
            camera.orthographicSize = asset.playableSize.y * 0.5f;

            RenderTexture target = new RenderTexture(1600, 1200, 24, RenderTextureFormat.ARGB32);
            Texture2D image = new Texture2D(1600, 1200, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                RenderTexture.active = target;
                camera.Render();
                image.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
                image.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(PreviewPath)) ?? ".");
                File.WriteAllBytes(Path.GetFullPath(PreviewPath), image.EncodeToPNG());
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log($"WoT arena preview saved: {PreviewPath}");
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

        /// <summary>
        /// Captures the ground alone at all three meaningful gameplay zooms. The whole-arena preview can
        /// prove alignment and colour, but it downsamples the map so heavily that a texture imported at half
        /// resolution still looks acceptable there. These close views are the regression proof for detail.
        /// </summary>
        [MenuItem("Local Tanks/Diagnostics/Capture WoT Ground Detail Preview")]
        public static void CaptureGroundDetailPreview()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Camera camera = UnityEngine.Object.FindAnyObjectByType<Camera>();
            WoTMapAsset asset = AssetDatabase.LoadAssetAtPath<WoTMapAsset>(AssetPath);
            GameObject surfaceObject = GameObject.Find("Ground/Surface");
            SpriteRenderer surface = surfaceObject != null ? surfaceObject.GetComponent<SpriteRenderer>() : null;
            if (camera == null || asset == null || surface == null)
            {
                throw new InvalidOperationException("The arena needs a camera, map asset and Ground/Surface to capture detail.");
            }

            Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            bool[] states = renderers.Select(item => item.enabled).ToArray();
            for (int index = 0; index < renderers.Length; index++)
            {
                renderers[index].enabled = renderers[index] == surface;
            }

            const int side = 768;
            float[] zooms = { 4f, 8f, 12f };
            string folder = Path.GetFullPath("TestResults/WoTGroundDetail");
            Directory.CreateDirectory(folder);
            try
            {
                camera.transform.position = new Vector3(asset.worldCenter.x, asset.worldCenter.y, -10f);
                camera.transform.rotation = Quaternion.identity;
                camera.orthographic = true;
                foreach (float zoom in zooms)
                {
                    camera.orthographicSize = zoom;
                    RenderTexture target = new RenderTexture(side, side, 24, RenderTextureFormat.ARGB32);
                    Texture2D image = new Texture2D(side, side, TextureFormat.RGB24, false);
                    RenderTexture previous = RenderTexture.active;
                    try
                    {
                        camera.targetTexture = target;
                        RenderTexture.active = target;
                        camera.Render();
                        image.ReadPixels(new Rect(0f, 0f, side, side), 0, 0);
                        image.Apply();
                        string path = Path.Combine(folder, $"Zoom_{zoom:0}.png");
                        File.WriteAllBytes(path, image.EncodeToPNG());
                    }
                    finally
                    {
                        camera.targetTexture = null;
                        RenderTexture.active = previous;
                        UnityEngine.Object.DestroyImmediate(target);
                        UnityEngine.Object.DestroyImmediate(image);
                    }
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log($"WoT ground detail previews saved: {folder}");
            }
            finally
            {
                for (int index = 0; index < renderers.Length; index++) renderers[index].enabled = states[index];
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static string ScenePath => BattleSceneLayout.EnsurePlaced(BattleSceneLayout.MapSceneName(DefaultMapId));

        private static string AssetPath => WoTMapImporter.AssetPath(DefaultMapId);

        /// <summary>The arena these menu entries build and preview.</summary>
        private const string DefaultMapId = "10_hills";

        /// <summary>Where a rendered preview of the arena is written for inspection.</summary>
        private const string PreviewPath = "Docs/Media/WoTMapArena.png";

        private const string GeneratedRoot = "Assets/Game/Generated/WoTMap";
        private const string ClassIconsRoot = "Assets/Game/Generated/UI/VehicleClasses";
        private const float GroundSortingOrder = -20f;

        /// <summary>
        /// The navigation wash sits just above the surface image and below every object, so it can be read
        /// without hiding the ground it describes.
        /// </summary>
        private const float NavigationOverlaySortingOrder = -19f;

        /// <summary>
        /// The sprite-block ground that the baked texture replaced. Left switched off rather than deleted:
        /// it is the working comparison if the bake ever needs revisiting, and a reference to dead code is
        /// cheaper than losing the experiment.
        /// </summary>
        private const bool UseSpriteBlockGround = false;

        /// <summary>
        /// Whether the navigation grid is drawn as a colour wash over the ground.
        /// <para>
        /// Off, because it is a diagnostic and not scenery. The cells are four metres across, which is about
        /// fifteen pixels of the baked ground, and drawn over it they read as a field of small dark squares -
        /// which is exactly how they were reported. Switch this on when the routing grid is what needs
        /// looking at.
        /// </para>
        /// </summary>
        private const bool ShowNavigationOverlay = false;

        /// <summary>Where the detail materials live; one file per material id.</summary>
        private const string GroundMaterialFolder = "Assets/LocalOnly/WoTEnvironmentGenerated/Ground";

        /// <summary>
        /// Side of one detail block, in metres. The materials are seamless tiles, so the ground is covered
        /// by repeating them rather than by stretching a single image: repetition at this size reads as
        /// ground, where one stretched texture reads as a photograph of ground seen from orbit.
        /// </summary>
        private const float DetailBlockMetres = 4f;

        /// <summary>Draw order of the detail materials: above the surface image, below everything else.</summary>
        private const float DetailSortingOrder = -18f;

        /// <summary>
        /// How strongly the detail materials show over the surface image.
        /// <para>
        /// They are deliberately not opaque. Drawn at full strength they replace the arena's own picture,
        /// which carries the roads, fields, water and settlement that make the map recognisable, and leave
        /// only a repeating tile. At part strength the tiles supply grain under a close camera while the
        /// picture still supplies the shapes - which is the point of having both.
        /// </para>
        /// </summary>
        private const float DetailStrength = 0.45f;

        /// <summary>
        /// How much larger than its cell a ground block is drawn. The overlap hides the slivers that the
        /// per-block nudge would otherwise open, and a sliver shows the brighter layer beneath as a line.
        /// </summary>
        private const float BlockOverlap = 1.08f;

        /// <summary>
        /// One ground material and the range of terrain height it covers.
        /// <para>
        /// The ranges come from the arena's own height distribution rather than from taste. On Mines the
        /// lowest ground sits at about sea level, the median is sixteen metres and the cliffs reach a
        /// hundred and sixty, so sand and cracked flats belong in the bottoms, ground and rubble on the
        /// flats, boulders on the slopes and bedrock above. The materials themselves are the ones the map
        /// is actually paved with according to its own data, where Rock_4 is referenced 395 times,
        /// PBS_Ground_37 238 times, and the rest fall in between.
        /// </para>
        /// </summary>
        private readonly struct GroundMaterial
        {
            public GroundMaterial(string spriteId, float fromMetres, float toMetres)
            {
                SpriteId = spriteId;
                FromMetres = fromMetres;
                ToMetres = toMetres;
            }

            public string SpriteId { get; }
            public float FromMetres { get; }
            public float ToMetres { get; }
        }

        private static readonly GroundMaterial[] GroundMaterials =
        {
            new GroundMaterial("mines_sand", float.NegativeInfinity, 1.5f),
            new GroundMaterial("mines_cracked_earth", 1.5f, 8f),
            new GroundMaterial("mines_ground", 8f, 24f),
            new GroundMaterial("mines_rubble", 24f, 50f),
            new GroundMaterial("mines_boulders", 50f, 78f),
            new GroundMaterial("mines_rock", 78f, float.PositiveInfinity)
        };

        private const float ObstacleSortingOrder = -5f;

        [MenuItem("Local Tanks/Build WoT Map Scene (Mines)")]
        public static void BuildDefaultMapScene() => Build(DefaultMapId);

        public static void Build(string mapId)
        {
            // Texture import can refresh the asset database, so it must finish before the map ScriptableObject
            // is loaded and passed through the rest of the scene build.
            WoTGroundMaterialBuilder.Prepare(mapId);
            WoTMapAsset asset = WoTMapImporter.ImportAsset(mapId);
            // The arena is a playable map, so the scene belongs under Maps; placing it first means a scene
            // that has not been moved yet is never rebuilt as a second copy next to the old one.
            string sceneName = BattleSceneLayout.MapSceneName(mapId);
            string scenePath = BattleSceneLayout.EnsurePlaced(sceneName);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = sceneName;
            SceneManager.SetActiveScene(scene);

            NavigationMap navigation = CreateNavigationMap(asset);
            CreateGround(asset);
            CreateContentRoots(asset);
            CaptureBase[] bases = CreateBases(asset);
            CameraFollow2D camera = CreateCamera(navigation, asset);
            TeamVisionSystem vision = CreateBattleSystems(bases);
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
            GameObject mapObject = new GameObject("NavigationMap");
            SceneManager.MoveGameObjectToScene(mapObject, SceneManager.GetActiveScene());
            NavigationMap map = mapObject.AddComponent<NavigationMap>();
            mapObject.AddComponent<WoTNavigationMapSource>().Configure(asset);
            return map;
        }

        /// <summary>
        /// Ground is one tinted sheet per navigation terrain kind rather than a tile instance per cell:
        /// the arena is 208 units across at a four-unit grid, and a tile per cell would add thousands of
        /// objects for a single flat colour. Real surface art arrives later as textures over this sheet.
        /// </summary>
        /// <summary>
        /// The arena floor. It is the map's own surface image, taken from the game client, drawn as one
        /// sprite across the whole playable area, with its own material grain sampled by a shared shader.
        /// <para>
        /// The alternative was a tilemap of flat colours keyed to the navigation grid, which is a
        /// debugging view rather than a place to fight: it showed the routing cells and told a player
        /// nothing about the ground. The surface image is the real thing - fields, treelines, stony
        /// shores and the road network of Mines - and covering the map with a single sprite means there
        /// are no tile seams at all.
        /// </para>
        /// <para>
        /// The source is 4096 square over an 800-metre arena, about five pixels per metre: enough for the
        /// large shapes but not for the closest camera. The generated selector and material atlas remain
        /// separate, so the shader can repeat their grain at a physical scale instead of stretching it with
        /// the macro image. It is still one renderer and has no tile boundaries.
        /// </para>
        /// </summary>
        private static void CreateGround(WoTMapAsset asset)
        {
            GameObject root = new GameObject("Ground");
            SceneManager.MoveGameObjectToScene(root, SceneManager.GetActiveScene());

            WoTGroundMaterialBuilder.Result ground = WoTGroundMaterialBuilder.Build(
                asset.mapId,
                asset.playableSize,
                asset.metresPerUnit);
            Sprite surface = ground.Surface;

            GameObject floor = new GameObject("Surface");
            floor.transform.SetParent(root.transform, false);
            floor.transform.position = asset.worldCenter;
            SpriteRenderer renderer = floor.AddComponent<SpriteRenderer>();
            renderer.sprite = surface;
            renderer.sharedMaterial = ground.Material;
            renderer.sortingOrder = Mathf.RoundToInt(GroundSortingOrder);
            GroundAppearanceTuner tuner = floor.AddComponent<GroundAppearanceTuner>();
            tuner.Configure(ground.DetailStrength);

            // Scaled to the playable area rather than left at its pixel size, so the picture and the
            // collision world describe the same ground. Uniform, because the source is square and the
            // arena is square: a mismatch here would stretch the map itself.
            Vector2 drawn = surface.bounds.size;
            if (drawn.x > 0.0001f && drawn.y > 0.0001f)
            {
                floor.transform.localScale = new Vector3(
                    asset.playableSize.x / drawn.x,
                    asset.playableSize.y / drawn.y,
                    1f);
            }

            if (UseSpriteBlockGround) CreateDetailGround(root.transform, asset);
            if (ShowNavigationOverlay) CreateNavigationOverlay(root.transform, asset);
        }

        /// <summary>
        /// Covers the arena with the map's own ground materials, chosen by terrain height.
        /// <para>
        /// The reason this exists is that the surface image carries about five pixels per metre. That is
        /// plenty from above and soft under a tank, so each material is repeated as a seamless tile across
        /// blocks of a few metres and drawn over the image. The picture supplies the large shapes - fields,
        /// roads, water, the settlement - and the tiles supply the grain that a camera close to the ground
        /// needs.
        /// </para>
        /// <para>
        /// Each block also gets a small offset into its tile, taken from its own coordinates so the result
        /// is identical on every rebuild. Without it the repetition lines up into a visible grid, which is
        /// the artefact this whole exercise is meant to avoid.
        /// </para>
        /// </summary>
        private static void CreateDetailGround(Transform parent, WoTMapAsset asset)
        {
            List<(string Id, Sprite Sprite)> materials = new List<(string, Sprite)>();
            foreach (GroundMaterial material in GroundMaterials)
            {
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                    $"{GroundMaterialFolder}/{material.SpriteId}.png");
                if (sprite == null)
                {
                    Debug.LogWarning($"Ground material '{material.SpriteId}' is missing; its band will show " +
                                     "the surface image only. Rebuild the environment library to restore it.");
                    continue;
                }

                materials.Add((material.SpriteId, sprite));
            }

            if (materials.Count == 0) return;

            GameObject root = new GameObject("GroundDetail");
            root.transform.SetParent(parent, false);

            float cell = asset.navigationCellSize;
            Vector2 origin = asset.worldCenter - asset.playableSize * 0.5f;
            int side = Mathf.Max(1, Mathf.RoundToInt(DetailBlockMetres / cell));
            int placed = 0;

            for (int row = 0; row < asset.navigationRows; row += side)
            {
                // Every other row of blocks is shifted half a block sideways and a little along, so block
                // edges do not stack into continuous lines. Aligned edges are what makes a tiled ground
                // read as a grid of squares rather than as ground.
                int stagger = ((row / side) & 1) == 1 ? side / 2 : 0;
                for (int column = -stagger; column < asset.navigationColumns; column += side)
                {
                    int sampleColumn = Mathf.Clamp(column, 0, asset.navigationColumns - side);
                    float height = BlockHeight(asset, row, sampleColumn, side);
                    GroundMaterial band = GroundMaterials[BandFor(height)];
                    Sprite sprite = materials.FirstOrDefault(item => item.Id == band.SpriteId).Sprite;
                    if (sprite == null) continue;

                    Vector2 centre = origin + new Vector2(
                        (column + side * 0.5f) * cell,
                        (row + side * 0.5f) * cell);
                    // A small deterministic nudge so neighbouring blocks of the same material do not line up
                    // into a lattice. Seeded from the coordinates, so a rebuild reproduces the arena exactly.
                    centre += new Vector2(
                        (Hash01(column, row, 1) - 0.5f) * cell * 0.5f,
                        (Hash01(column, row, 2) - 0.5f) * cell * 0.5f);

                    GameObject block = new GameObject($"Ground_{column:00}_{row:00}");
                    block.transform.SetParent(root.transform, false);
                    block.transform.position = centre;
                    SpriteRenderer renderer = block.AddComponent<SpriteRenderer>();
                    renderer.sprite = sprite;
                    renderer.sortingOrder = Mathf.RoundToInt(DetailSortingOrder);
                    // Opacity varies only slightly. A wide range was tried and made the grid worse rather
                    // than better: neighbouring blocks of one material became visible as patches, and the
                    // eye finds regular brightness steps far more readily than it finds seams.
                    float strength = DetailStrength * (0.94f + Hash01(column, row, 4) * 0.12f);
                    renderer.color = new Color(1f, 1f, 1f, Mathf.Clamp01(strength));
                    // Mirrored per block, and the tile's own UV origin is not moved: the materials are
                    // seamless, so their edges already agree and shifting them would create the very seam
                    // this is trying to avoid.
                    renderer.flipX = ((column / side) & 1) == 1;
                    renderer.flipY = ((row / side) & 1) == 1;

                    Vector2 drawn = sprite.bounds.size;
                    // Every block is drawn slightly larger than its cell so neighbours overlap. Without the
                    // overlap the nudge above opens slivers between blocks, and through a sliver the brighter
                    // layer underneath shows as a bright line - which is exactly the grid this fixes.
                    float size = side * cell * BlockOverlap;
                    if (drawn.x > 0.0001f && drawn.y > 0.0001f)
                    {
                        block.transform.localScale = new Vector3(size / drawn.x, size / drawn.y, 1f);
                    }

                    placed++;
                }
            }

            Debug.Log($"Ground detail: {placed} blocks of {DetailBlockMetres} m from {materials.Count} materials.");
        }

        /// <summary>
        /// A stable value in [0,1) from a block's coordinates. Used for the small position, size and opacity
        /// variations that break up repetition. Deliberately not <c>Random</c>: the same arena must look the
        /// same after every rebuild, and a random source would also make a scene diff unreadable.
        /// </summary>
        private static float Hash01(int column, int row, int salt)
        {
            unchecked
            {
                uint hash = (uint)(column * 73856093) ^ (uint)(row * 19349663) ^ (uint)(salt * 83492791);
                hash ^= hash >> 13;
                hash *= 0x5bd1e995;
                hash ^= hash >> 15;
                return (hash & 0xffffff) / (float)0x1000000;
            }
        }

        /// <summary>Average terrain height over a block, so a single cliff does not recolour its neighbours.</summary>
        private static float BlockHeight(WoTMapAsset asset, int row, int column, int side)
        {
            float sum = 0f;
            int count = 0;
            for (int y = row; y < Mathf.Min(row + side, asset.navigationRows); y++)
            {
                for (int x = column; x < Mathf.Min(column + side, asset.navigationColumns); x++)
                {
                    int index = y * asset.navigationColumns + x;
                    if (index < 0 || index >= asset.terrainHeightsMetres.Length) continue;
                    sum += asset.terrainHeightsMetres[index];
                    count++;
                }
            }

            return count > 0 ? sum / count : 0f;
        }

        /// <summary>Which height band a terrain height falls in.</summary>
        private static int BandFor(float height)
        {
            for (int index = 0; index < GroundMaterials.Length; index++)
            {
                if (height <= GroundMaterials[index].ToMetres) return index;
            }

            return GroundMaterials.Length - 1;
        }

        /// <summary>
        /// A faint colour wash over the ground showing the navigation grid. It is a diagnostic layer, not
        /// the arena floor: the cells are four metres across and routing only, so drawn at full strength
        /// they hid the map behind a checkerboard. At low alpha they are readable when looked for and
        /// invisible when not.
        /// </summary>
        private static void CreateNavigationOverlay(Transform parent, WoTMapAsset asset)
        {
            GameObject grid = new GameObject("NavigationOverlay");
            grid.transform.SetParent(parent, false);
            Grid gridComponent = grid.AddComponent<Grid>();
            gridComponent.cellSize = new Vector3(asset.navigationCellSize, asset.navigationCellSize, 1f);
            gridComponent.transform.position = asset.worldCenter - asset.playableSize * 0.5f;

            Tilemap overlay = CreateTilemapLayer(grid.transform, "Cells", NavigationOverlaySortingOrder, false);
            Tile tile = CreateGroundTile(Color.white);
            const float wash = 0.18f;
            for (int row = 0; row < asset.navigationRows; row++)
            {
                for (int column = 0; column < asset.navigationColumns; column++)
                {
                    int index = row * asset.navigationColumns + column;
                    Color colour = GroundColor((TerrainKind)asset.navigationTerrain[index]);
                    tile.color = new Color(colour.r, colour.g, colour.b, wash);
                    overlay.SetTile(new Vector3Int(column, row, 0), tile);
                }
            }
        }

        /// <summary>
        /// Terrain colour used by the navigation wash. Cover is drawn as dry ground rather than as a wall,
        /// because a tank drives through it and is only slowed.
        /// </summary>
        private static Color GroundColor(TerrainKind kind)
        {
            return kind switch
            {
                TerrainKind.Road => new Color(0.42f, 0.34f, 0.22f),
                TerrainKind.Mud => new Color(0.29f, 0.21f, 0.12f),
                TerrainKind.ShallowWater => new Color(0.13f, 0.34f, 0.45f),
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
            builder.ConfigureSprites(LoadSpriteBindings(asset));
        }

        /// <summary>
        /// Every id the library is known to hold. The family table below classifies each one, so a sprite
        /// is either usable on a wall or on a plant and never both by accident.
        /// </summary>
        private static readonly string[] LibrarySpriteIds =
        {
            "village_cowshed", "village_wood_shed", "village_windmill", "rock_sandstone",
            "wood_fence", "haystack", "log_pile", "dry_bush", "fallen_tree_root", "burnt_tree"
        };

        /// <summary>
        /// Binds the art the environment library offers for this arena. Only the sprites this map actually
        /// uses are bound, so the scene carries a handful of references rather than the whole library, and
        /// a missing library degrades to collision geometry instead of failing the build.
        /// </summary>
        private static WoTMapContentBuilder.SpriteBinding[] LoadSpriteBindings(WoTMapAsset asset)
        {
            const string manifestPath = "Assets/LocalOnly/WoTEnvironmentGenerated/library_manifest.json";
            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning($"Environment library manifest was not found at {manifestPath}; " +
                                 "the arena will be built as collision geometry without art.");
                return Array.Empty<WoTMapContentBuilder.SpriteBinding>();
            }

            JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
            Dictionary<string, WoTMapContentBuilder.SpriteBinding> bindings =
                new Dictionary<string, WoTMapContentBuilder.SpriteBinding>();
            foreach (string spriteId in NeededSpriteIds(asset))
            {
                JToken entry = (manifest["objects"] as JArray)?
                    .FirstOrDefault(item => (string)item["id"] == spriteId);
                if (entry == null)
                {
                    Debug.LogWarning($"Library has no object '{spriteId}'; those instances stay untextured.");
                    continue;
                }

                string path = $"Assets/LocalOnly/WoTEnvironmentGenerated/{(string)entry["sprite"]}";
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null)
                {
                    Debug.LogWarning($"Library sprite was not imported: {path}");
                    continue;
                }

                float[] size = entry["worldSizeMeters"]?.Select(value => (float)value).ToArray();
                bindings[spriteId] = new WoTMapContentBuilder.SpriteBinding
                {
                    spriteId = spriteId,
                    sprite = sprite,
                    // The library's own category is the authority on what a sprite depicts, so obstacles
                    // and plants draw from separate families instead of sharing one pool.
                    family = (string)entry["category"],
                    worldSizeMeters = size != null && size.Length >= 2
                        ? new Vector2(size[0], size[1])
                        : Vector2.zero
                };
            }

            return bindings.Values.ToArray();
        }

        /// <summary>Sprite ids this arena asks for, so the library is scanned once per build.</summary>
        private static IEnumerable<string> NeededSpriteIds(WoTMapAsset asset)
        {
            HashSet<string> ids = new HashSet<string>();
            List<string> structures = MapSpriteMatcher.OfFamily(Families, vegetation: false);
            List<string> vegetation = MapSpriteMatcher.OfFamily(Families, vegetation: true);
            foreach (WoTMapObstacle obstacle in asset.obstacles)
            {
                string id = MapSpriteMatcher.Resolve(obstacle.sourceResource, obstacle.category, structures);
                if (id != null) ids.Add(id);
            }

            foreach (WoTMapVegetation plant in asset.vegetation)
            {
                string id = MapSpriteMatcher.Resolve(
                    plant.sourceResource, plant.kind == MapCoverKind.Tree ? "tree" : "bush", vegetation);
                if (id != null) ids.Add(id);
            }

            return ids;
        }

        /// <summary>
        /// Every library id this arena may use, paired with what it depicts. The library's family is the
        /// authority on whether a sprite belongs on a wall or on a plant; that one fact is what keeps a
        /// burnt tree off a rock.
        /// </summary>
        private static readonly KeyValuePair<string, string>[] Families =
        {
            new KeyValuePair<string, string>("village_cowshed", "building"),
            new KeyValuePair<string, string>("village_wood_shed", "building"),
            new KeyValuePair<string, string>("village_windmill", "building"),
            new KeyValuePair<string, string>("rock_sandstone", "rock"),
            new KeyValuePair<string, string>("wood_fence", "destructible_cover"),
            new KeyValuePair<string, string>("haystack", "destructible_cover"),
            new KeyValuePair<string, string>("log_pile", "destructible_cover"),
            new KeyValuePair<string, string>("dry_bush", "vegetation"),
            new KeyValuePair<string, string>("fallen_tree_root", "vegetation"),
            new KeyValuePair<string, string>("burnt_tree", "vegetation")
        };

        /// <summary>
        /// Radius of a capture base's zone, in world units, shared by the drawn ring and the trigger
        /// collider so a player can see exactly where capture applies.
        /// </summary>
        private const float BaseRingRadius = 9f;

        /// <summary>Shared line material, produced by the River Crossing arena and reused here.</summary>
        private const string LineMaterialPath = "Assets/Game/Generated/RiverCrossing/MapLine.mat";

        /// <summary>Panel settings and stylesheets shared with the other arenas.</summary>
        private const string PanelSettingsPath = "Assets/Game/Generated/RiverCrossing/BattleHudPanelSettings.asset";
        private const string BattleHudUxmlPath = "Assets/Game/UI/BattleHud.uxml";
        private const string BattleHudUssPath = "Assets/Game/UI/BattleHud.uss";
        private const string SettingsOverlayUssPath = "Assets/Game/UI/SettingsOverlay.uss";

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
                    ring.SetPosition(step, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * BaseRingRadius);
                }

                // A capture zone is a trigger that reports occupancy; it must never stop a tank.
                // It is added here rather than left to CaptureBase's RequireComponent, because Unity
                // creates a required collider with default settings - solid, and as large as the base,
                // which trapped every tank inside its own spawn on the converted arena.
                CircleCollider2D captureZone = baseObject.AddComponent<CircleCollider2D>();
                captureZone.radius = BaseRingRadius;
                captureZone.isTrigger = true;
                CaptureBase capture = baseObject.AddComponent<CaptureBase>();
                capture.Configure(baseId, $"Ð‘Ð°Ð·Ð° {baseId}", owner, new CaptureRules(), ring);
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
            cameraObject.AddComponent<NavigationDebugOverlay>().Configure(map, false);
            cameraObject.AddComponent<PlayerAimReticle>();
            return follow;
        }

        /// <summary>Shared line material for the vision arcs, created once and reused by every arena.</summary>
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

        /// <summary>
        /// Vision, HUD and the developer overlays. The converted arena originally carried only vision and
        /// the hidden-unit setting, so none of the developer keys worked on it: no time acceleration, no
        /// AI inspector, no roster and no HUD. The overlays are the same components every other arena adds,
        /// so the keys documented in Docs/AI_TESTING.md behave identically here.
        /// </summary>
        private static TeamVisionSystem CreateBattleSystems(CaptureBase[] bases)
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
            BattleRoster roster = systems.AddComponent<BattleRoster>();
            roster.Configure(TeamId.TeamA, vision);
            systems.AddComponent<VisionDebugOverlay>().Configure(vision);
            systems.AddComponent<VisionArcPresenter>().Configure(vision, GetOrCreateLineMaterial());
            systems.AddComponent<CombatAiDebugOverlay>();
            systems.AddComponent<DeveloperTimeScaleController>();
            CreateBattleHud(roster, bases);
            return vision;
        }

        /// <summary>
        /// The battle interface and its settings overlay. It is built here rather than saved into the
        /// scene so the panel settings and stylesheets stay one editable source shared by every arena.
        /// </summary>
        private static void CreateBattleHud(BattleRoster roster, CaptureBase[] bases)
        {
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
            StyleSheet settingsStyleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(SettingsOverlayUssPath);
            if (visualTree == null || styleSheet == null || settingsStyleSheet == null)
            {
                throw new InvalidOperationException("Battle HUD or settings UXML/USS could not be loaded.");
            }

            GameObject hud = new GameObject("BattleHUD");
            UIDocument document = hud.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = visualTree;
            document.sortingOrder = 100;
            hud.AddComponent<BattleHudController>().Configure(
                roster,
                new[] { styleSheet, settingsStyleSheet },
                bases.OrderBy(item => item.BaseId, StringComparer.Ordinal).ToArray());
            hud.AddComponent<SettingsPanelController>();
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
            BattleScenario scenario = CreateScenario(database, BattleSceneLayout.MapSceneName(asset.mapId));
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
        /// Routes are deliberately absent: the loaded scene is analysed before every battle, so a rebuilt
        /// arena cannot preserve stale tactical geometry in its scenario.
        /// </summary>
        private static BattleScenario CreateScenario(CombatDatabase database, string sceneId)
        {
            TankDefinition chosen = database.tanks
                .Where(tank => tank != null && tank.availableInGame)
                .OrderBy(tank => tank.tier)
                .ThenBy(tank => tank.id, StringComparer.Ordinal)
                .First();
            // The game enforces a fixed formation shape. Which concrete paths satisfy these roles is
            // resolved from the scene's runtime tactical descriptor.
            BattleLane[] formation = { BattleLane.Center, BattleLane.North, BattleLane.South };
            int[] perLane = { 3, 2, 2 };

            List<BattleScenarioEntry> entries = new List<BattleScenarioEntry>();
            for (int laneIndex = 0; laneIndex < formation.Length; laneIndex++)
            {
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
                        role = laneIndex == 0 ? CombatAiRole.Assault : CombatAiRole.Flank
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
                        role = laneIndex == 0 ? CombatAiRole.Assault : CombatAiRole.Flank
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
