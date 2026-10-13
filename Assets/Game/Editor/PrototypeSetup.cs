using System.Collections.Generic;
using System.IO;
using System.Linq;
using LocalTanks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace LocalTanks.Editor
{
    public static class PrototypeSetup
    {
        private const string ProjectilePrefabPath = "Assets/Game/Prefabs/Combat/PrototypeProjectile.prefab";

        /// <summary>
        /// The test range is a service scene, so it lives under <c>Labs</c>. The path comes from the one
        /// place that knows the layout instead of being written out again here.
        /// </summary>
        private static string ScenePath => BattleSceneLayout.Path(BattleSceneLayout.TestRange);
        private const string SettingsOverlayUxmlPath = "Assets/Game/UI/SettingsOverlay.uxml";
        private const string SettingsOverlayUssPath = "Assets/Game/UI/SettingsOverlay.uss";
        private const string SettingsPanelSettingsPath = "Assets/Game/Generated/UI/SettingsPanelSettings.asset";
        private const string TrackOverlayShaderPath = "Assets/Game/Art/Shaders/TankTrackOverlay.shader";
        private const string TrackOverlayMaterialPath = "Assets/Game/Generated/Materials/TankTrackOverlay.mat";

        /// <summary>
        /// The tread overlay sits directly above the hull sprite at 5 and far below the turret at
        /// 10, the selection outline at hull + 20 and the class icon at 100, so a moving highlight
        /// is always covered by the parts that are physically above the tracks.
        /// </summary>
        private const int TrackOverlaySortingOrder = 6;

        private static readonly TankBuildSpec[] TankSpecs =
        {
            new TankBuildSpec(
                "TigerII", "Tiger II",
                "Assets/LocalOnly/WoTGenerated/G16_PzVIB_Tiger_II/G16_PzVIB_Tiger_II_strip2.png",
                "Assets/Game/GameData/Generated/Tanks/tiger_ii.asset",
                "Assets/Game/Prefabs/Tanks/TigerII_Player.prefab",
                new Rect(24f, 139f, 419f, 802f), new Rect(467f, 24f, 278f, 1031f),
                new Vector2(0.4985709f, 0.7753074f), new Vector2(0.4950269f, 0.4900504f),
                new Vector2(0.597f, 0.975f),
                7.38f, 1.41f,
                CreateTigerHullOutline()),
            new TankBuildSpec(
                "MS1", "MS-1",
                "Assets/LocalOnly/WoTGenerated/R11_MS-1/R11_MS-1_strip2.png",
                "Assets/Game/GameData/Generated/Tanks/ms_1.asset",
                "Assets/Game/Prefabs/Tanks/MS1_Player.prefab",
                new Rect(24f, 24f, 192f, 476f), new Rect(240f, 117f, 120f, 289f),
                new Vector2(0.5f, 0.6802735f), new Vector2(0.5f, 0.4236555f),
                new Vector2(0.625f, 0.979f),
                4.38f, 0.45f,
                CreateMS1HullOutline()),
            new TankBuildSpec(
                "Leichttraktor", "Leichttraktor",
                "Assets/LocalOnly/WoTGenerated/G12_Ltraktor/G12_Ltraktor_strip2.png",
                "Assets/Game/GameData/Generated/Tanks/leichttraktor.asset",
                "Assets/Game/Prefabs/Tanks/Leichttraktor_Player.prefab",
                new Rect(24f, 24f, 229f, 462f), new Rect(277f, 117f, 158f, 275f),
                new Vector2(0.4986667f, 0.7045380f), new Vector2(0.5f, 0.6946704f),
                new Vector2(0.074f, 0.805f),
                4.35f, 0.48f,
                CreateLeichttraktorHullOutline()),
            new TankBuildSpec(
                "T1Cunningham", "T1 Cunningham",
                "Assets/LocalOnly/WoTGenerated/A01_T1_Cunningham/A01_T1_Cunningham_strip2.png",
                "Assets/Game/GameData/Generated/Tanks/t1_cunningham.asset",
                "Assets/Game/Prefabs/Tanks/T1Cunningham_Player.prefab",
                new Rect(24f, 24f, 195f, 422f), new Rect(243f, 134f, 127f, 202f),
                new Vector2(0.5005221f, 0.6842200f), new Vector2(0.5025058f, 0.6974256f),
                new Vector2(0.73f, 0.10f),
                3.866f, 1.14f,
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

                if (TankSpecs.All(HasTrackOverlay))
                {
                    return;
                }

                // A newly added build spec has no prefab yet, and a prefab built before the tread
                // overlay existed has no animator, so either case rebuilds the combat assets once
                // when the project is opened. The check is derived from the prefabs themselves, so
                // there is no second list of machines to keep in sync.
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

        /// <summary>True when a tank prefab exists and already carries its tread overlay.</summary>
        private static bool HasTrackOverlay(TankBuildSpec spec)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);
            return prefab != null && prefab.GetComponentInChildren<TankTrackAnimator>(true) != null;
        }

        [MenuItem("Local Tanks/Build Test Range")]
        [MenuItem("Local Tanks/Build Sprint 002 Prototype")]
        public static void BuildAll()
        {
            BattleSceneLayout.EnsurePlaced(BattleSceneLayout.TestRange);
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
            LogTrackMaskSummary();
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
                float drawnMetres = spec.HullLengthPixels / PixelsPerMetre;
                Debug.Log($"Hull outline '{spec.DisplayName}': collider {actual.x:0.###} x {actual.y:0.###}, " +
                          $"sprite {expected.x:0.###} x {expected.y:0.###}, points {spec.HullOutline.Length}, " +
                          $"hull {spec.HullLengthMetres:0.###} m draws as {GameLengthFor(spec.HullLengthMetres):0.###} " +
                          $"units and the sheet records {drawnMetres:0.###} m.");
            }
        }

        /// <summary>
        /// Where the fleet scale comes from: the reference machine's 7.38 m hull measures 1.92 units, which
        /// the map, camera, view ranges and collision distances already assume. The value itself lives in
        /// <see cref="GameScale.MetresPerUnit"/> and is not repeated here — a second copy of this coefficient
        /// is exactly what let the editor and the arena disagree before ADR-064.
        /// </summary>

        /// <summary>
        /// Sprite pixels per model metre produced by the render pipeline. The reference machine
        /// confirms it: its 7.38 m hull is drawn 802 pixels long, and 7.38 * 108 = 797 pixels, which
        /// matches the sheet to within the rounding of one brush stroke. The value is used to measure
        /// what the sheet actually contains, so a sprite rendered at another scale is rejected instead
        /// of silently making its tank the wrong size.
        /// </summary>
        private const float PixelsPerMetre = 108f;

        /// <summary>
        /// Real hull length of the reference machine, whose sprite already defines the fleet scale.
        /// Every other tank is measured against it rather than against its own art.
        /// </summary>
        public const float ReferenceHullLengthMetres = 7.38f;

        /// <summary>Hull length in pixels of the reference machine in the canonical sheet.</summary>
        public const float ReferenceHullLengthPixels = 802f;
        public const float ReferenceVisualScale = 0.24f;

        /// <summary>
        /// Visual scale that makes a hull of the given real length and sprite size match the reference
        /// machine on screen. The rendered sprite keeps the model's true proportions, so comparing two
        /// hulls reduces to their real lengths and their drawn pixel lengths: a hull drawn in half as
        /// many pixels as the reference is scaled up by two to stay the same real size.
        /// <para>
        /// This is the single place the fleet scale is decided. Setting the scale from a tank's own
        /// sprite instead is what once made the T1 Cunningham 77% longer than a Tiger II, because both
        /// its documented length and its sheet differed from the reference.
        /// </para>
        /// </summary>
        public static float ScaleForHullLength(float hullLengthMetres, float hullLengthPixels)
        {
            if (hullLengthPixels <= 0f || hullLengthMetres <= 0f)
            {
                throw new System.InvalidOperationException(
                    "Hull length must be positive in both metres and pixels to derive the visual scale.");
            }

            return ReferenceVisualScale
                   * (hullLengthMetres / ReferenceHullLengthMetres)
                   * (ReferenceHullLengthPixels / hullLengthPixels);
        }

        /// <summary>In-game hull length of a machine with the given real length, in Unity units.</summary>
        public static float GameLengthFor(float hullLengthMetres)
        {
            return hullLengthMetres / GameScale.MetresPerUnit;
        }

        private static GameObject[] BuildCombatAssetsInternal()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (TankBuildSpec spec in TankSpecs)
            {
                spec.LoadCanonicalLayout();
                ValidateHullOutline(spec);
                ValidateTrackLayout(spec);
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
        /// Fails the build when a tank's hull contour does not describe its own sprite, or when its
        /// sprite is not drawn at the shared game scale.
        /// <para>
        /// Contour points are world units equal to half of the sprite rectangle multiplied by the
        /// visual scale, so the contour must span that rectangle. A contour entered in other units
        /// still compiles, still passes validation and still collides with things; the only symptom
        /// is a hull on screen that does not match what the player hits.
        /// </para>
        /// <para>
        /// The second check is the one that keeps machines correct relative to each other. A sprite
        /// rendered at the wrong scale still matches its own collider, so nothing else notices, and
        /// the tank simply comes out too large or too small next to its neighbours. Comparing the
        /// drawn hull against the documented real length at <see cref="GameScale.MetresPerUnit"/> catches it.
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

            float drawnLengthMetres = spec.HullLengthPixels / PixelsPerMetre;
            float deviation = Mathf.Abs(drawnLengthMetres / spec.HullLengthMetres - 1f);
            if (deviation > tolerance)
            {
                throw new System.InvalidOperationException(
                    $"Sprite for '{spec.DisplayName}' is drawn {drawnLengthMetres:0.###} m long but the machine is " +
                    $"{spec.HullLengthMetres:0.###} m ({deviation * 100f:0}% off), so it would not match the other " +
                    $"machines. At {GameScale.MetresPerUnit:0.###} m per unit the hull must measure " +
                    $"{GameLengthFor(spec.HullLengthMetres):0.###} units. Re-render the sprite or correct the " +
                    "documented hull length; do not tune the scale of a single tank.");
            }

            if (spec.HullOutline.Length < 10)
            {
                throw new System.InvalidOperationException(
                    $"Hull contour for '{spec.DisplayName}' has {spec.HullOutline.Length} points. The visible " +
                    "hull outline needs at least ten points so it does not collapse into a rectangle.");
            }
        }

        /// <summary>
        /// Fails the build when a measured track block cannot drive the tread overlay.
        /// <para>
        /// The mask is drawn as a second sprite on top of the hull, so it only lines up when it was
        /// cropped to exactly the hull rectangle. A mask of another size still imports, still
        /// renders and still animates; the only symptom is a highlight sliding across the hull in
        /// the wrong place, which no compiler and no other test notices.
        /// </para>
        /// </summary>
        private static void ValidateTrackLayout(TankBuildSpec spec)
        {
            TrackLayoutJson tracks = spec.Tracks;
            if (tracks.schemaVersion != 1)
            {
                throw new System.InvalidOperationException(
                    $"Track block of '{spec.DisplayName}' declares schema {tracks.schemaVersion}, expected 1. " +
                    "Re-run Tools\\WoTSpritePipeline for this vehicle.");
            }

            if (!Mathf.Approximately(tracks.mask.width, spec.HullRect.width) ||
                !Mathf.Approximately(tracks.mask.height, spec.HullRect.height))
            {
                throw new System.InvalidOperationException(
                    $"Track mask of '{spec.DisplayName}' is {tracks.mask.width:0} by {tracks.mask.height:0} pixels " +
                    $"but its hull sprite rectangle is {spec.HullRect.width:0} by {spec.HullRect.height:0}. The mask " +
                    "must be cropped exactly like the hull layer, otherwise the moving highlight does not follow " +
                    "the tracks.");
            }

            if (!Mathf.Approximately(tracks.outputPixelsPerMeter, PixelsPerMetre))
            {
                throw new System.InvalidOperationException(
                    $"Track block of '{spec.DisplayName}' was measured at {tracks.outputPixelsPerMeter:0.###} pixels " +
                    $"per metre but the fleet is built at {PixelsPerMetre:0.###}. Re-render the sprite.");
            }

            float hullWidthMetres = spec.HullRect.width / PixelsPerMetre;
            if (tracks.bandSeparationMetres <= 0f || tracks.bandSeparationMetres > hullWidthMetres)
            {
                throw new System.InvalidOperationException(
                    $"Track bands of '{spec.DisplayName}' are reported {tracks.bandSeparationMetres:0.###} m apart, " +
                    $"which is outside the {hullWidthMetres:0.###} m width of its hull. The measured mask is not " +
                    "usable as a differential.");
            }

            if (tracks.treadPitchMetres <= 0f || tracks.treadPitchMetres >= tracks.mask.height / PixelsPerMetre)
            {
                throw new System.InvalidOperationException(
                    $"Tread pitch of '{spec.DisplayName}' is {tracks.treadPitchMetres:0.###} m against a mask " +
                    $"{tracks.mask.height / PixelsPerMetre:0.###} m long. One feature must fit on the hull at least " +
                    "once; lower the pitch in the track override or the pipeline configuration.");
            }

            if (!Mathf.Approximately(Mathf.Abs(tracks.forwardSignV), 1f))
            {
                throw new System.InvalidOperationException(
                    $"Track block of '{spec.DisplayName}' records forwardSignV {tracks.forwardSignV}, but the scroll " +
                    "direction must be either +1 or -1.");
            }
        }

        /// <summary>
        /// Prints the measured track block of every tank. The overlay is invisible in a build log, so
        /// this is the record that makes a wrong mask diagnosable without opening a scene.
        /// </summary>
        private static void LogTrackMaskSummary()
        {
            foreach (TankBuildSpec spec in TankSpecs)
            {
                TrackLayoutJson tracks = spec.Tracks;
                Debug.Log($"Track mask '{spec.DisplayName}': source {tracks.source}, sides {tracks.sideSource}" +
                          (tracks.channelsSwapped ? " (channels swapped)" : string.Empty) +
                          $", mask {tracks.mask.width:0}x{tracks.mask.height:0}, " +
                          $"bands {tracks.bandSeparationMetres:0.###} m apart and {tracks.bandWidthMetres:0.###} m " +
                          $"wide, tread pitch {tracks.treadPitchMetres:0.###} m, forward sign {tracks.forwardSignV:0}, " +
                          $"diagnostic {tracks.diagnostic}." +
                          (string.IsNullOrEmpty(tracks.fallbackReason) ? string.Empty : $" {tracks.fallbackReason}"));
            }
        }

        /// <summary>
        /// Imports the generated track mask as one sprite that covers its whole texture.
        /// <para>
        /// The overlay is a second sprite drawn over the hull sprite, so it only lines up when its
        /// mesh spans the complete rectangle: a tight mesh would trim the transparent border and
        /// shift the mask against the hull. Centring the pivot and keeping the 100 pixels per unit
        /// of the strip keeps both sprites at the same world size and at the same position.
        /// </para>
        /// </summary>
        private static Sprite ConfigureTrackMaskTexture(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                throw new System.IO.FileNotFoundException("Track mask texture was not found.", path);
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;

            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
            {
                throw new System.InvalidOperationException($"Track mask did not import as a sprite: {path}");
            }

            return sprite;
        }

        /// <summary>
        /// Returns the one shared material every tread overlay uses, creating it on first use.
        /// Machine-specific values never reach this asset: they travel in a property block, so the
        /// fleet shares a single material, a single shader and a single draw setup.
        /// </summary>
        private static Material EnsureTrackOverlayMaterial()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(TrackOverlayShaderPath);
            if (shader == null)
            {
                throw new System.InvalidOperationException($"Track overlay shader was not found: {TrackOverlayShaderPath}");
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(TrackOverlayMaterialPath);
            if (material != null)
            {
                if (material.shader != shader)
                {
                    material.shader = shader;
                    EditorUtility.SetDirty(material);
                }

                return material;
            }

            const string materialDirectory = "Assets/Game/Generated/Materials";
            if (!AssetDatabase.IsValidFolder(materialDirectory))
            {
                AssetDatabase.CreateFolder("Assets/Game/Generated", "Materials");
            }

            material = new Material(shader) { name = Path.GetFileNameWithoutExtension(TrackOverlayMaterialPath) };
            // Defaults only: a human may tune these in the generated material and the build keeps
            // the edited values, because they are never overwritten here.
            material.SetColor("_TrackHighlight", new Color(1f, 0.99f, 0.96f, 1f));
            material.SetVector(
                "_TrackStrength",
                new Vector4(TankTrackAnimator.DefaultStrength.x, TankTrackAnimator.DefaultStrength.y, 0f, 0f));
            AssetDatabase.CreateAsset(material, TrackOverlayMaterialPath);
            return material;
        }

        /// <summary>
        /// Adds the tread overlay child and its animator to a tank root.
        /// <para>
        /// The overlay is a child of the hull visual, so it inherits the hull's uniform scale and
        /// its 180 degree source-art rotation, which is what keeps it aligned with the hull sprite.
        /// The hull renderer and its rectangle inside the source texture are recorded as well,
        /// because the shader reads those pixels to make the tread lighten what is already painted.
        /// </para>
        /// </summary>
        private static TankTrackAnimator CreateTrackOverlay(
            GameObject root, GameObject hullVisual, TankBuildSpec spec, TankHealth health, Rigidbody2D body)
        {
            Sprite mask = ConfigureTrackMaskTexture(spec.TrackMaskPath);
            Material material = EnsureTrackOverlayMaterial();

            GameObject overlay = new GameObject("TrackOverlay");
            overlay.transform.SetParent(hullVisual.transform, false);
            SpriteRenderer renderer = overlay.AddComponent<SpriteRenderer>();
            renderer.sprite = mask;
            renderer.sharedMaterial = material;
            renderer.sortingOrder = TrackOverlaySortingOrder;

            SpriteRenderer hullRenderer = hullVisual.GetComponent<SpriteRenderer>();
            Rect hullTextureRect = hullRenderer.sprite.textureRect;
            Texture2D hullTexture = hullRenderer.sprite.texture;
            Vector4 normalizedHullRect = new Vector4(
                hullTextureRect.x / hullTexture.width,
                hullTextureRect.y / hullTexture.height,
                hullTextureRect.width / hullTexture.width,
                hullTextureRect.height / hullTexture.height);

            TrackLayoutJson tracks = spec.Tracks;
            float maskHeightUnits = tracks.mask.height / 100f * spec.VisualScale.y;
            TankTrackAnimator animator = root.AddComponent<TankTrackAnimator>();
            animator.Configure(
                body,
                health,
                renderer,
                hullRenderer,
                normalizedHullRect,
                tracks.bandSeparationMetres * 0.5f / GameScale.MetresPerUnit,
                tracks.treadPitchMetres / GameScale.MetresPerUnit,
                maskHeightUnits,
                tracks.forwardSignV,
                TankTrackAnimator.DefaultStrength);
            return animator;
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
                turretPivot.transform.localPosition = AnchorPosition(spec, spec.HullTurretMount);
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
                CreateTrackOverlay(root, hullVisual, spec, health, body);

                // Where a machine breathes out is a fact of its model, not of its mass or class, so the
                // outlet is an anchor measured from the canonical sprite and carried in the prefab. The rig
                // attaches the exhaust plume to it; a prefab without one falls back to the hull's rear.
                GameObject exhaust = new GameObject(TankVfxRig.ExhaustAnchorName);
                exhaust.transform.SetParent(root.transform, false);
                exhaust.transform.localPosition = AnchorPosition(spec, spec.HullExhaust);

                // The rig carries no asset references, so it is safe in a committed prefab: it loads the
                // generated effect prefabs at play time and resolves every look from the committed
                // profile table. The test range therefore shows one machine's effects in isolation.
                TankVfxRig vfx = root.AddComponent<TankVfxRig>();
                float trackSeparation = spec.Tracks.bandSeparationMetres / GameScale.MetresPerUnit;
                if (trackSeparation <= 0f)
                {
                    throw new System.InvalidOperationException(
                        $"The measured track gauge of {spec.DisplayName} is {spec.Tracks.bandSeparationMetres} m, " +
                        "so the prints and the dust would be placed by guesswork. Re-run the sprite pipeline.");
                }

                vfx.Configure(
                    definition,
                    motor,
                    health,
                    weapon,
                    newTerrain: null,
                    newVisibility: null,
                    trackSeparation: trackSeparation,
                    newExhaustAnchor: exhaust.transform);

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

        /// <summary>
        /// Adds the settings overlay to a scene that has no battle HUD of its own. The test range only
        /// drives and shoots at targets, so it needs its own document with the same shared markup and
        /// styles that the battle scenes include as a template. Rebuilding a scene must not add a
        /// second copy.
        /// </summary>
        private static void EnsureSettingsOverlay(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.GetComponentInChildren<SettingsPanelController>(true) != null)
                {
                    return;
                }
            }

            VisualTreeAsset visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(SettingsOverlayUxmlPath);
            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(SettingsOverlayUssPath);
            if (visualTree == null || styleSheet == null)
            {
                throw new System.InvalidOperationException("Settings overlay UXML or USS could not be loaded.");
            }

            PanelSettings panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(SettingsPanelSettingsPath);
            if (panelSettings == null)
            {
                panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panelSettings, SettingsPanelSettingsPath);
            }

            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            panelSettings.match = 0.5f;
            EditorUtility.SetDirty(panelSettings);

            GameObject overlay = new GameObject("SettingsOverlay");
            SceneManager.MoveGameObjectToScene(overlay, scene);
            UIDocument document = overlay.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = visualTree;
            document.sortingOrder = 120;
            SettingsPanelController controller = overlay.AddComponent<SettingsPanelController>();
            controller.Configure(styleSheet);
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
            EnsureSettingsOverlay(scene);
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
        /// nearly constant width, because the tracks form both edges. Contour points are world units
        /// equal to half of the sprite rectangle multiplied by the visual scale, as for every other
        /// tank: the hull rectangle is 195 by 422 pixels at the scale derived from its 3.866 m hull,
        /// so the half extents are 0.234 and 0.506.
        /// </summary>
        private static Vector2[] CreateT1CunninghamHullOutline()
        {
            const float halfWidth = 0.2309f;
            const float halfLength = 0.4997f;
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

        /// <summary>
        /// Converts a point given in normalised coordinates of the hull sprite rectangle into the local
        /// space of the machine. Both axes are negated because the hull artwork is drawn facing the other
        /// way and the visual child carries a 180 degree rotation.
        /// </summary>
        private static Vector3 AnchorPosition(TankBuildSpec spec, Vector2 normalizedPoint)
        {
            Vector2 sourcePixelOffset = new Vector2(
                (normalizedPoint.x - 0.5f) * spec.HullRect.width,
                (normalizedPoint.y - 0.5f) * spec.HullRect.height);
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
                Vector2 turretPivot, Vector2 hullTurretMount, Vector2 hullExhaust, float hullLengthMetres,
                float muzzleDistance, Vector2[] hullOutline)
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
                HullExhaust = hullExhaust;
                HullLengthMetres = hullLengthMetres;
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

            /// <summary>
            /// Where the machine's exhaust leaves the hull, in normalised coordinates of the hull sprite
            /// rectangle, measured by looking at the canonical top-down artwork: the pipe on the MS-1 tail,
            /// the corrugated pipe along the Leichttraktor's left side, the outlets on the Tiger II engine
            /// deck and the mufflers under the T1 Cunningham's front deck. A single point cannot be derived
            /// from mass, class or the hull box, so it is stated once per machine, next to the other
            /// measured art facts, and the prefab carries it as the "Exhaust" anchor.
            /// </summary>
            public Vector2 HullExhaust { get; private set; }
            public float HullLengthMetres { get; }

            /// <summary>
            /// Sprite scale, derived from the documented hull length so every machine shares the one
            /// game scale. Uniform on both axes on purpose: a mixed scale would stretch the art away
            /// from the collider derived from the same rectangle.
            /// </summary>
            public Vector2 VisualScale => new Vector2(
                ScaleForHullLength(HullLengthMetres, HullLengthPixels),
                ScaleForHullLength(HullLengthMetres, HullLengthPixels));

            public float MuzzleDistance { get; private set; }
            public Vector2[] HullOutline { get; }

            /// <summary>Measured tracks block of the canonical layout.</summary>
            public TrackLayoutJson Tracks { get; private set; }

            /// <summary>Absolute path of the generated track mask texture.</summary>
            public string TrackMaskPath { get; private set; }

            public void LoadCanonicalLayout()
            {
                string directory = Path.GetDirectoryName(TexturePath)?.Replace('\\', '/');
                string vehicleId = string.IsNullOrEmpty(directory) ? string.Empty : Path.GetFileName(directory);
                string layoutPath = $"{directory}/{vehicleId}_layout.json";
                string absolutePath = Path.GetFullPath(layoutPath);
                if (!File.Exists(absolutePath))
                    throw new System.InvalidOperationException($"Canonical sprite layout was not found: {layoutPath}");

                SpriteLayoutJson layout = JsonUtility.FromJson<SpriteLayoutJson>(File.ReadAllText(absolutePath));
                if (layout == null || layout.schemaVersion != 3 || layout.hull == null || layout.turret == null ||
                    layout.tracks == null || layout.tracks.mask == null)
                {
                    throw new InvalidDataException(
                        $"Unsupported or incomplete sprite layout: {layoutPath}. Schema 3 with a measured " +
                        "tracks block is required; re-run Tools\\WoTSpritePipeline for this vehicle instead " +
                        "of editing the layout by hand.");
                }

                HullRect = layout.hull.ToRect();
                TurretRect = layout.turret.ToRect();
                TurretPivot = layout.turret.pivot.ToVector2();
                HullTurretMount = layout.hull.turretMount.ToVector2();
                MuzzleDistance = layout.turret.height * TurretPivot.y / 100f * VisualScale.y;

                Tracks = layout.tracks;
                string maskPath = $"{directory}/{layout.tracks.maskFile}";
                if (string.IsNullOrWhiteSpace(layout.tracks.maskFile) || !File.Exists(Path.GetFullPath(maskPath)))
                {
                    throw new InvalidDataException(
                        $"The track mask declared by {layoutPath} does not exist: {maskPath}. Re-run " +
                        "Tools\\WoTSpritePipeline for this vehicle.");
                }

                TrackMaskPath = maskPath;
            }

            /// <summary>
            /// Longest side of the hull rectangle in sprite pixels, which is the hull length for every
            /// machine in the fleet because each hull is longer than it is wide.
            /// </summary>
            public float HullLengthPixels => Mathf.Max(HullRect.width, HullRect.height);
        }

        [System.Serializable]
        private sealed class SpriteLayoutJson
        {
            public int schemaVersion;
            public SpriteLayoutPartJson hull;
            public SpriteLayoutPartJson turret;
            public TrackLayoutJson tracks;
        }

        /// <summary>
        /// The track block of the canonical layout. The sprite pipeline measures it from the 3D
        /// model and from the pixels it actually drew, so Unity never guesses a track position or
        /// a scroll direction from the artwork.
        /// </summary>
        [System.Serializable]
        private sealed class TrackLayoutJson
        {
            public int schemaVersion;
            public string source;
            public string maskFile;
            public SpriteLayoutSizeJson mask;
            public float outputPixelsPerMeter;
            public string sideSource;
            public bool geometryVerified;
            public bool channelsSwapped;
            public float forwardSignV;
            public float bandSeparationPixels;
            public float bandSeparationMetres;
            public float bandWidthPixels;
            public float bandWidthMetres;
            public float treadPitchMetres;
            public string diagnostic;
            public string fallbackReason;
        }

        [System.Serializable]
        private sealed class SpriteLayoutSizeJson
        {
            public float width;
            public float height;
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
