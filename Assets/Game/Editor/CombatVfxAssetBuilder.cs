using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LocalTanks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LocalTanks.Editor
{
    /// <summary>
    /// Builds the whole combat effect library from the committed profile file: neutral textures, materials
    /// and one particle prefab per named effect.
    /// </summary>
    /// <remarks>
    /// The particle systems are generated rather than hand-authored for the same reason the tank prefabs
    /// are: an effect that exists only inside a <c>.prefab</c> cannot be reviewed, diffed or regenerated,
    /// and eleven prefabs tuned by hand drift apart within a month. Everything here comes from
    /// <see cref="CombatVfxCatalog"/>, so the numbers in the profile file are the single source of truth
    /// for what a shot, an impact and a wreck look like.
    /// <para>
    /// Only what the runtime cannot vary per event is baked in: lifetime, speed and the shape of the fade.
    /// Size, colour, particle count and rate stay runtime parameters, because they depend on the machine
    /// or the surface the effect happens on.
    /// </para>
    /// </remarks>
    public static class CombatVfxAssetBuilder
    {
        public const string TextureFolder = "Assets/Game/Art/Vfx/Generated";
        public const string MaterialFolder = "Assets/Game/Generated/Vfx";
        public const string PrefabFolder = "Assets/Game/Resources/" + CombatVfxEffects.ResourceFolder;
        private const int DustSortingOrder = 4;
        private const int SpraySortingOrder = 6;
        private const int SmokeSortingOrder = 6;
        private const int FireSortingOrder = 7;
        private const int EmberSortingOrder = 8;
        private const int MuzzleSmokeSortingOrder = 12;
        private const int ImpactDustSortingOrder = 12;
        private const int DebrisSortingOrder = 13;
        private const int BurstSortingOrder = 15;
        private const int SparkSortingOrder = 16;
        private const int FlashSortingOrder = 17;

        [MenuItem("Local Tanks/Build Combat Vfx Assets")]
        public static void Build()
        {
            CombatVfxCatalog catalog = CombatVfxProfileValidator.LoadSource();
            string[] profileErrors = CombatVfxProfileValidator.CollectErrors();
            if (profileErrors.Length > 0)
            {
                throw new InvalidDataException(
                    "Combat VFX profiles are invalid, so no effect can be built:\n- " +
                    string.Join("\n- ", profileErrors));
            }

            EnsureFolder(TextureFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder(PrefabFolder);

            Dictionary<string, Texture2D> textures = BuildTextures();
            Dictionary<string, Material> materials = BuildMaterials(textures);
            foreach (string effectId in CombatVfxEffects.All())
            {
                BuildEffect(effectId, catalog, materials);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            string[] errors = CollectAssetErrors();
            if (errors.Length > 0)
            {
                throw new InvalidDataException(
                    "Combat VFX assets were built but are incomplete:\n- " + string.Join("\n- ", errors));
            }

            int removed = DeleteStaleAssets();
            Debug.Log($"Local Tanks: built {CombatVfxEffects.All().Length} combat VFX prefabs, " +
                      $"{materials.Count} materials and {textures.Count} textures " +
                      $"({catalog.weaponProfiles.Length} caliber bands, {catalog.surfaceProfiles.Length} surfaces)" +
                      (removed > 0 ? $", removed {removed} stale file(s)." : "."));
        }

        /// <summary>
        /// Removes generated files that the current effect list no longer produces.
        /// </summary>
        /// <remarks>
        /// A renamed effect leaves its old prefab or texture behind, and a stale asset that still loads is
        /// worse than a missing one: the emitter keeps finding the old print pattern, or the pool keeps a
        /// prefab nobody generates any more, and both look like the new work had no effect.
        /// </remarks>
        private static int DeleteStaleAssets()
        {
            HashSet<string> expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string effectId in CombatVfxEffects.All())
            {
                expected.Add(PrefabPath(effectId));
            }

            foreach (string variant in CombatVfxEffects.MarkVariants)
            {
                expected.Add(MarkSpritePath(variant));
            }

            foreach (string textureName in VfxTextureLibrary.All)
            {
                if (!VfxTextureLibrary.IsTrackMark(textureName))
                {
                    expected.Add(TextureAssetPath(textureName));
                }
            }

            int removed = 0;
            foreach (string folder in new[] { PrefabFolder, TextureFolder })
            {
                string absolute = Path.GetFullPath(folder);
                if (!Directory.Exists(absolute))
                {
                    continue;
                }

                foreach (string file in Directory.GetFiles(absolute, "*.png", SearchOption.TopDirectoryOnly)
                             .Concat(Directory.GetFiles(absolute, "*.prefab", SearchOption.TopDirectoryOnly)))
                {
                    string assetPath = $"{folder}/{Path.GetFileName(file)}";
                    if (expected.Contains(assetPath))
                    {
                        continue;
                    }

                    AssetDatabase.DeleteAsset(assetPath);
                    removed++;
                }
            }

            return removed;
        }

        [MenuItem("Local Tanks/Data/Validate Combat Vfx Assets")]
        public static void ValidateMenu()
        {
            string[] errors = CollectAssetErrors();
            if (errors.Length > 0)
            {
                throw new InvalidDataException(
                    "Combat VFX assets are missing or incomplete; run 'Local Tanks/Build Combat Vfx Assets':\n- " +
                    string.Join("\n- ", errors));
            }

            Debug.Log("Local Tanks: combat VFX assets are present and match the generated effect list.");
        }

        /// <summary>
        /// Everything that would make the effect layer silently do nothing. Read by the menu commands and
        /// by an Edit Mode test, so a half-built library cannot pass a green run.
        /// </summary>
        public static string[] CollectAssetErrors()
        {
            List<string> errors = new List<string>();
            errors.AddRange(CombatVfxProfileValidator.CollectErrors());

            foreach (string textureName in VfxTextureLibrary.All)
            {
                string path = TextureAssetPath(textureName);
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
                {
                    errors.Add($"VFX texture '{path}' is missing.");
                }
            }

            foreach (string effectId in CombatVfxEffects.All())
            {
                string path = PrefabPath(effectId);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    errors.Add($"VFX prefab '{path}' is missing.");
                    continue;
                }

                ParticleSystem[] systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
                if (systems.Length == 0)
                {
                    errors.Add($"VFX prefab '{path}' contains no particle system.");
                    continue;
                }

                bool expectedLoop = CombatVfxEffects.IsLoop(effectId);
                foreach (ParticleSystem system in systems)
                {
                    if (system.main.loop != expectedLoop)
                    {
                        errors.Add($"VFX prefab '{path}' has a system whose loop setting contradicts " +
                                   $"'{effectId}' being a {(expectedLoop ? "persistent" : "one-shot")} effect.");
                        break;
                    }

                    if (system.GetComponent<ParticleSystemRenderer>().sharedMaterial == null)
                    {
                        errors.Add($"VFX prefab '{path}' has a system without a material.");
                        break;
                    }
                }
            }

            foreach (string variant in CombatVfxEffects.MarkVariants)
            {
                Sprite markSprite = AssetDatabase.LoadAssetAtPath<Sprite>(MarkSpritePath(variant));
                if (markSprite == null)
                {
                    errors.Add($"Track print sprite '{MarkSpritePath(variant)}' is missing.");
                    continue;
                }

                if (!Mathf.Approximately(markSprite.bounds.size.x, 1f) ||
                    !Mathf.Approximately(markSprite.bounds.size.y, 1f))
                {
                    errors.Add($"Track print sprite '{variant}' is {markSprite.bounds.size.x:0.###} by " +
                               $"{markSprite.bounds.size.y:0.###} world units. Import it at " +
                               VfxTextureLibrary.Size + " pixels per unit so the emitter's scale is the " +
                               "print size in world units.");
                }
            }

            return errors.ToArray();
        }

        public static string PrefabPath(string effectId)
        {
            return $"{PrefabFolder}/{effectId}.prefab";
        }

        /// <summary>
        /// Where one neutral texture is written.
        /// </summary>
        /// <remarks>
        /// Particle textures are referenced by the generated materials, so they belong with the rest of
        /// the generated art. The print patterns are the exception: the emitter loads one of them by name
        /// at run time, so they have to sit inside the <c>Resources</c> folder next to the effect prefabs.
        /// Writing them to the art folder would generate perfectly good sprites that the game could never
        /// find.
        /// </remarks>
        public static string TextureAssetPath(string textureName)
        {
            return VfxTextureLibrary.IsTrackMark(textureName)
                ? $"{PrefabFolder}/{textureName}.png"
                : $"{TextureFolder}/{textureName}.png";
        }

        /// <summary>One print pattern, at the path the runtime provider asks for.</summary>
        public static string MarkSpritePath(string variant)
        {
            return $"{PrefabFolder}/{variant}.png";
        }

        private static Dictionary<string, Texture2D> BuildTextures()
        {
            Dictionary<string, Texture2D> result = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            foreach (string textureName in VfxTextureLibrary.All)
            {
                string assetPath = TextureAssetPath(textureName);
                Texture2D texture = VfxTextureLibrary.Create(textureName);
                byte[] png = texture.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(texture);

                string absolutePath = Path.GetFullPath(assetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? string.Empty);
                bool changed = !File.Exists(absolutePath) || !File.ReadAllBytes(absolutePath).SequenceEqual(png);
                if (changed)
                {
                    File.WriteAllBytes(absolutePath, png);
                }

                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                ConfigureTexture(assetPath, VfxTextureLibrary.IsTrackMark(textureName));
                result[textureName] = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            }

            return result;
        }

        /// <summary>
        /// The track mark is a sprite of exactly one world unit so that the emitter's scale is the print
        /// size in world units. Everything else is a plain particle texture, clamped and uncompressed.
        /// </summary>
        private static void ConfigureTexture(string assetPath, bool asSprite)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                throw new FileNotFoundException("VFX texture could not be imported.", assetPath);
            }

            importer.textureType = asSprite ? TextureImporterType.Sprite : TextureImporterType.Default;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = asSprite ? VfxTextureLibrary.Size : 100f;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private static Dictionary<string, Material> BuildMaterials(Dictionary<string, Texture2D> textures)
        {
            Shader shader = FindParticleShader();
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "No particle shader was found. Expected 'Universal Render Pipeline/Particles/Unlit'.");
            }

            Dictionary<string, Material> materials = new Dictionary<string, Material>(StringComparer.Ordinal);
            materials["smoke"] = CreateMaterial(shader, "vfx_smoke", textures[VfxTextureLibrary.SmokePuff], false);
            materials["smoke_wisp"] = CreateMaterial(shader, "vfx_smoke_wisp", textures[VfxTextureLibrary.SmokeWisp], false);
            materials["dust"] = CreateMaterial(shader, "vfx_dust", textures[VfxTextureLibrary.DustPuff], false);
            materials["exhaust"] = CreateMaterial(shader, "vfx_exhaust", textures[VfxTextureLibrary.ExhaustSmoke], false);
            materials["spray"] = CreateMaterial(shader, "vfx_spray", textures[VfxTextureLibrary.WaterDrop], false);
            materials["debris"] = CreateMaterial(shader, "vfx_debris", textures[VfxTextureLibrary.DirtChunk], false);
            materials["spark"] = CreateMaterial(shader, "vfx_spark", textures[VfxTextureLibrary.Spark], true);
            materials["flash"] = CreateMaterial(shader, "vfx_flash", textures[VfxTextureLibrary.FlashStar], true);
            materials["petals"] = CreateMaterial(shader, "vfx_petals", textures[VfxTextureLibrary.FlashPetals], true);
            materials["core"] = CreateMaterial(shader, "vfx_core", textures[VfxTextureLibrary.FlashCore], true);
            // The flame keeps its orange body through normal alpha blending. Only the glow, core and
            // embers are additive; making every layer additive turned a fire into separate bright beads.
            materials["flame"] = CreateMaterial(shader, "vfx_flame", textures[VfxTextureLibrary.Flame], false);
            materials["fire_glow"] = CreateMaterial(shader, "vfx_fire_glow", textures[VfxTextureLibrary.FireGlow], true);
            materials["ember"] = CreateMaterial(shader, "vfx_ember", textures[VfxTextureLibrary.Ember], true);
            return materials;
        }

        /// <summary>
        /// Creates one particle material in the exact state the URP particle shader needs.
        /// </summary>
        /// <remarks>
        /// Setting blend factors is not enough on its own. The first build ran headless, and the materials
        /// it produced were still opaque variants: `_SURFACE_TYPE_TRANSPARENT` was missing, so the shader
        /// compiled its opaque pass, ignored blending and left a depth-only and shadow-caster pass enabled.
        /// Smoke and dust drew as hard white blobs. Rendering the proof on a real device let Unity resolve
        /// the keywords and passes, and those resolved values are written here so a headless rebuild
        /// produces the same material instead of quietly regressing to opaque.
        /// </remarks>
        private static Material CreateMaterial(Shader shader, string materialName, Texture2D texture, bool additive)
        {
            string path = $"{MaterialFolder}/{materialName}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.mainTexture = texture;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);

            // Transparent surface, whichever way the effect blends.
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", additive ? 2f : 0f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);

            // Additive keeps the source alpha so a spark fades out instead of blowing out to white.
            Vector2 sourceBlend = new Vector2((float)BlendMode.SrcAlpha, (float)BlendMode.One);
            Vector2 destinationBlend = additive
                ? new Vector2((float)BlendMode.One, (float)BlendMode.One)
                : new Vector2((float)BlendMode.OneMinusSrcAlpha, (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", sourceBlend.x);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", destinationBlend.x);
            if (material.HasProperty("_SrcBlendAlpha")) material.SetFloat("_SrcBlendAlpha", sourceBlend.y);
            if (material.HasProperty("_DstBlendAlpha")) material.SetFloat("_DstBlendAlpha", destinationBlend.y);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetShaderPassEnabled("DepthOnly", false);
            material.SetShaderPassEnabled("SHADOWCASTER", false);
            material.renderQueue = (int)RenderQueue.Transparent;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Shader FindParticleShader()
        {
            foreach (string name in new[]
                     {
                         "Universal Render Pipeline/Particles/Unlit",
                         "Particles/Standard Unlit",
                         "Universal Render Pipeline/2D/Sprite-Unlit-Default",
                         "Sprites/Default"
                     })
            {
                Shader shader = Shader.Find(name);
                if (shader != null)
                {
                    return shader;
                }
            }

            return null;
        }

        private static void BuildEffect(string effectId, CombatVfxCatalog catalog, Dictionary<string, Material> materials)
        {
            GameObject root = new GameObject(effectId);
            try
            {
                switch (effectId)
                {
                    case CombatVfxEffects.MuzzleFlash: BuildMuzzleFlash(root, catalog, materials); break;
                    case CombatVfxEffects.MuzzleSmoke: BuildMuzzleSmoke(root, catalog, materials); break;
                    case CombatVfxEffects.ImpactSpark: BuildImpactSpark(root, catalog, materials); break;
                    case CombatVfxEffects.ImpactDebris: BuildImpactDebris(root, catalog, materials); break;
                    case CombatVfxEffects.ImpactDust: BuildImpactDust(root, catalog, materials); break;
                    case CombatVfxEffects.DestructionBurst: BuildDestructionBurst(root, materials); break;
                    case CombatVfxEffects.TrackDust: BuildTrackDust(root, materials); break;
                    case CombatVfxEffects.WaterSpray: BuildWaterSpray(root, materials); break;
                    case CombatVfxEffects.DamageSmoke: BuildDamageSmoke(root, materials); break;
                    case CombatVfxEffects.DamageFire: BuildDamageFire(root, materials); break;
                    case CombatVfxEffects.ExhaustSmoke: BuildExhaustSmoke(root, materials); break;
                    case CombatVfxEffects.BarrelSmoke: BuildBarrelSmoke(root, materials); break;
                    default: throw new InvalidOperationException($"No builder is defined for effect '{effectId}'.");
                }

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(effectId));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void BuildMuzzleFlash(GameObject root, CombatVfxCatalog catalog, Dictionary<string, Material> materials)
        {
            float seconds = Max(catalog.weaponProfiles, profile => profile.flashSeconds);
            // A gun throws a short cone of propellant flame along the bore, not a radial star. Four layers
            // keep the event readable in one or two frames: white core, directional plume, two short side
            // flames and a few narrow incandescent fragments.
            ParticleSystem core = AddSystem(root, "Core", materials["core"], FlashSortingOrder + 2, seconds + 0.05f, 0.04f, 0.075f, 0.18f, 0.29f);
            SetBurst(core, 1);
            SetPlanarVelocity(core, -0.05f, 0.05f, 0.2f, 0.65f);
            SetSizeOverLifetime(core, 1f, 0.45f);
            SetFade(core, 0.035f);
            OffsetSystem(core, 0f, 0.08f);

            ParticleSystem plume = AddSystem(root, "Plume", materials["petals"], FlashSortingOrder + 1, seconds + 0.09f, 0.055f, 0.11f, 0.82f, 1.18f);
            SetBurst(plume, 1);
            SetPlanarVelocity(plume, -0.08f, 0.08f, 0.35f, 1.0f);
            SetStartRotation(plume, -0.12f, 0.12f);
            SetSizeOverLifetime(plume, 1f, 0.58f);
            SetFade(plume, 0.04f);
            OffsetSystem(plume, 0f, 0.22f);

            ParticleSystem sideFlames = AddSystem(root, "SideFlames", materials["flame"], FlashSortingOrder, seconds + 0.08f, 0.05f, 0.11f, 0.32f, 0.52f);
            SetBurst(sideFlames, 3);
            SetPlanarVelocity(sideFlames, -0.42f, 0.42f, 0.32f, 0.92f);
            SetStartRotation(sideFlames, -0.45f, 0.45f);
            SetSizeOverLifetime(sideFlames, 0.9f, 0.25f);
            SetFade(sideFlames, 0.035f);
            OffsetSystem(sideFlames, 0f, 0.1f);

            ParticleSystem sparks = AddSystem(root, "Sparks", materials["spark"], SparkSortingOrder, seconds + 0.08f, 0.055f, 0.12f, 0.05f, 0.11f);
            SetBurst(sparks, 4);
            SetPlanarVelocity(sparks, -0.16f, 0.16f, 1.3f, 2.9f);
            SetStartRotation(sparks, -0.14f, 0.14f);
            SetSizeOverLifetime(sparks, 1f, 0.15f);
            SetFade(sparks, 0.045f);
            OffsetSystem(sparks, 0f, 0.16f);
        }

        /// <summary>
        /// The plume that keeps leaving the barrel for a moment after the shot. It lives at the muzzle and
        /// the rig opens it for a fraction of a second, which is what a gun actually does: the flash is
        /// gone in a frame or two, the propellant smoke keeps drifting out behind it.
        /// </summary>
        private static void BuildBarrelSmoke(GameObject root, Dictionary<string, Material> materials)
        {
            // A narrow jet leaves the bore first; a slower translucent cloud hangs at the muzzle after it.
            // Both are opened by the rig for the weapon profile's barrelSmokeSeconds.
            ParticleSystem jet = AddSystem(root, "Jet", materials["smoke_wisp"], MuzzleSmokeSortingOrder, 2f, 0.42f, 0.78f, 0.07f, 0.14f);
            SetRate(jet, 7f);
            SetPlanarVelocity(jet, -0.08f, 0.08f, 0.42f, 1.1f);
            SetStartRotation(jet, -0.24f, 0.24f);
            SetSizeOverLifetime(jet, 0.65f, 1.85f);
            SetFade(jet, 0.08f);
            SetTurbulence(jet, 0.12f, 0.9f);
            SetStartColor(jet, new Color(0.7f, 0.69f, 0.67f, 0.52f));
            OffsetSystem(jet, 0f, 0.07f);

            ParticleSystem residual = AddSystem(root, "Residual", materials["smoke"], MuzzleSmokeSortingOrder - 1, 2f, 0.72f, 1.35f, 0.12f, 0.23f);
            SetRate(residual, 7f);
            SetPlanarVelocity(residual, -0.2f, 0.2f, 0.12f, 0.52f);
            SetStartRotation(residual, -1.2f, 1.2f);
            SetSizeOverLifetime(residual, 0.55f, 2.2f);
            SetFade(residual, 0.12f);
            SetTurbulence(residual, 0.2f, 0.62f);
            SetStartColor(residual, new Color(0.78f, 0.77f, 0.75f, 0.36f));
        }

        private static void BuildMuzzleSmoke(GameObject root, CombatVfxCatalog catalog, Dictionary<string, Material> materials)
        {
            float seconds = Max(catalog.weaponProfiles, profile => profile.smokeSeconds);
            int burst = Mathf.Max(1, Max(catalog.weaponProfiles, profile => profile.smokeBurst));
            ParticleSystem smoke = AddSystem(root, "PropellantCloud", materials["smoke"], MuzzleSmokeSortingOrder, seconds, 0.42f, 1.05f, 0.2f, 0.38f);
            SetBurst(smoke, burst);
            SetPlanarVelocity(smoke, -0.26f, 0.26f, 0.7f, 1.85f);
            SetStartRotation(smoke, -1.1f, 1.1f);
            SetSizeOverLifetime(smoke, 0.55f, 2f);
            SetFade(smoke, 0.09f);
            SetTurbulence(smoke, 0.18f, 0.72f);
            SetStartColor(smoke, new Color(0.76f, 0.75f, 0.73f, 0.62f));
            OffsetSystem(smoke, 0f, 0.12f);
        }

        private static void BuildImpactSpark(GameObject root, CombatVfxCatalog catalog, Dictionary<string, Material> materials)
        {
            float speed = Max(catalog.impactProfiles, profile => profile.sparkSpeed);
            int burst = Mathf.Max(1, Max(catalog.impactProfiles, profile => profile.sparkCount));
            // A WoT-like armour hit is a shallow fan of incandescent streaks reflected from the plate,
            // not a circular cloud of glowing dots. Local +Y is aligned with the collision normal by the
            // runtime; the width below gives the fan its spread while every particle still leaves the hull.
            ParticleSystem spark = AddSystem(root, "Sparks", materials["spark"], SparkSortingOrder,
                0.46f, 0.16f, 0.4f, 0.035f, 0.075f);
            SetBurst(spark, burst);
            SetPlanarVelocity(spark, -speed * 0.58f, speed * 0.58f, speed * 0.34f, speed);
            SetSizeOverLifetime(spark, 1f, 0.08f);
            SetSparkColourOverLifetime(spark);
            SetStretchedBillboard(spark, 8.5f, 0.09f);
        }

        private static void BuildImpactDebris(GameObject root, CombatVfxCatalog catalog, Dictionary<string, Material> materials)
        {
            float speed = Mathf.Max(0.5f, Max(catalog.impactProfiles, profile => profile.sparkSpeed) * 0.6f);
            int burst = Mathf.Max(1, Max(catalog.impactProfiles, profile => profile.debrisCount));
            ParticleSystem debris = AddSystem(root, "Debris", materials["debris"], DebrisSortingOrder, 0.8f, 0.35f, 0.6f, 0.1f, 0.2f);
            SetBurst(debris, burst);
            SetPlanarVelocity(debris, -speed, speed, -speed, speed);
            SetSizeOverLifetime(debris, 1f, 0.6f);
            SetFade(debris, 0.1f);
        }

        private static void BuildImpactDust(GameObject root, CombatVfxCatalog catalog, Dictionary<string, Material> materials)
        {
            float seconds = Max(catalog.impactProfiles, profile => profile.seconds);
            ParticleSystem dust = AddSystem(root, "Dust", materials["dust"], ImpactDustSortingOrder, seconds, 0.6f, 1.1f, 0.3f, 0.55f);
            SetBurst(dust, 12);
            SetPlanarVelocity(dust, -0.9f, 0.9f, -0.2f, 1.0f);
            SetSizeOverLifetime(dust, 0.7f, 1.8f);
            SetFade(dust, 0.12f);
        }

        private static void BuildDestructionBurst(GameObject root, Dictionary<string, Material> materials)
        {
            ParticleSystem core = AddSystem(root, "Core", materials["core"], BurstSortingOrder, 0.4f, 0.12f, 0.22f, 0.5f, 0.85f);
            SetBurst(core, 1);
            SetPlanarVelocity(core, -0.2f, 0.2f, -0.2f, 0.2f);
            SetSizeOverLifetime(core, 1.2f, 0.4f);
            SetFade(core, 0.05f);

            ParticleSystem smoke = AddSystem(root, "Smoke", materials["smoke"], MuzzleSmokeSortingOrder, 2.4f, 1.2f, 2.2f, 0.5f, 0.95f);
            SetBurst(smoke, 26);
            SetPlanarVelocity(smoke, -1.2f, 1.2f, -0.4f, 1.4f);
            SetSizeOverLifetime(smoke, 0.6f, 2.2f);
            SetFade(smoke, 0.1f);

            ParticleSystem debris = AddSystem(root, "Debris", materials["debris"], DebrisSortingOrder, 1.4f, 0.6f, 1.2f, 0.12f, 0.26f);
            SetBurst(debris, 14);
            SetPlanarVelocity(debris, -2.4f, 2.4f, -2.4f, 2.4f);
            SetSizeOverLifetime(debris, 1f, 0.7f);
            SetFade(debris, 0.1f);
        }

        private static void BuildTrackDust(GameObject root, Dictionary<string, Material> materials)
        {
            ParticleSystem dust = AddSystem(root, "Dust", materials["dust"], DustSortingOrder, 2f, 0.7f, 1.3f, 0.35f, 0.6f);
            SetRate(dust, 6f);
            // Dust is left behind: in the machine's own frame the ground drifts backwards under it.
            SetPlanarVelocity(dust, -0.5f, 0.5f, -1.2f, -0.3f);
            SetSizeOverLifetime(dust, 0.7f, 1.9f);
            SetFade(dust, 0.15f);
        }

        private static void BuildWaterSpray(GameObject root, Dictionary<string, Material> materials)
        {
            ParticleSystem spray = AddSystem(root, "Spray", materials["spray"], SpraySortingOrder, 2f, 0.3f, 0.6f, 0.12f, 0.24f);
            SetRate(spray, 9f);
            SetPlanarVelocity(spray, -1.6f, 1.6f, -0.6f, 1.4f);
            SetSizeOverLifetime(spray, 1f, 0.4f);
            SetFade(spray, 0.08f);
        }

        private static void BuildDamageSmoke(GameObject root, Dictionary<string, Material> materials)
        {
            ParticleSystem smoke = AddSystem(root, "Smoke", materials["smoke"], SmokeSortingOrder, 3.5f, 1.5f, 2.8f, 0.42f, 0.78f, 0.12f, 0.16f);
            SetRate(smoke, 3.5f);
            SetPlanarVelocity(smoke, -0.28f, 0.28f, 0.45f, 1.05f);
            SetSizeOverLifetime(smoke, 0.55f, 2.25f);
            SetFade(smoke, 0.16f);
            SetTurbulence(smoke, 0.22f, 0.48f);
        }

        private static void BuildDamageFire(GameObject root, Dictionary<string, Material> materials)
        {
            // Six layers form one continuous combustion column: soot behind the source, a broken bed of
            // orange light, broad low flames, sparse tall licks, a white-hot core and rare embers. The
            // visible flame is alpha blended; only light and incandescent particles add energy.
            ParticleSystem soot = AddSystem(root, "Soot", materials["smoke"], SmokeSortingOrder, 3f, 1.0f, 2.0f, 0.2f, 0.34f, 0.14f, 0.12f);
            SetRate(soot, 2.8f);
            SetPlanarVelocity(soot, -0.24f, 0.24f, 0.5f, 1.1f);
            SetSizeOverLifetime(soot, 0.55f, 2.1f);
            SetFade(soot, 0.14f);
            SetTurbulence(soot, 0.24f, 0.5f);
            SetStartColor(soot, new Color(0.18f, 0.16f, 0.14f, 0.48f));

            ParticleSystem glow = AddSystem(root, "Glow", materials["fire_glow"], FireSortingOrder, 3f, 0.24f, 0.46f, 0.52f, 0.86f, 0.26f, 0.2f);
            SetRate(glow, 7f);
            SetPlanarVelocity(glow, -0.16f, 0.16f, 0.12f, 0.42f);
            SetSizeOverLifetime(glow, 0.78f, 1.05f);
            SetFade(glow, 0.18f);
            SetTurbulence(glow, 0.12f, 0.7f);

            ParticleSystem tongues = AddSystem(root, "Tongues", materials["flame"], FireSortingOrder + 1, 3f, 0.3f, 0.55f, 0.4f, 0.7f, 0.28f, 0.18f);
            SetRate(tongues, 16f);
            SetPlanarVelocity(tongues, -0.16f, 0.16f, 0.08f, 0.36f);
            SetStartRotation(tongues, -0.38f, 0.38f);
            SetSizeOverLifetime(tongues, 0.95f, 0.24f);
            SetFade(tongues, 0.12f);
            SetTurbulence(tongues, 0.24f, 0.86f);

            ParticleSystem tallTongues = AddSystem(root, "TallTongues", materials["flame"], FireSortingOrder + 1, 3f, 0.38f, 0.7f, 0.32f, 0.52f, 0.18f, 0.12f);
            SetRate(tallTongues, 8f);
            SetPlanarVelocity(tallTongues, -0.13f, 0.13f, 0.15f, 0.55f);
            SetStartRotation(tallTongues, -0.26f, 0.26f);
            SetSizeOverLifetime(tallTongues, 0.86f, 0.12f);
            SetFade(tallTongues, 0.1f);
            SetTurbulence(tallTongues, 0.28f, 0.72f);

            ParticleSystem core = AddSystem(root, "Core", materials["core"], FireSortingOrder + 2, 3f, 0.13f, 0.27f, 0.18f, 0.34f, 0.16f, 0.12f);
            SetRate(core, 14f);
            SetPlanarVelocity(core, -0.12f, 0.12f, 0.12f, 0.46f);
            SetSizeOverLifetime(core, 1f, 0.38f);
            SetFade(core, 0.1f);
            SetStartColor(core, new Color(1f, 0.9f, 0.62f, 0.9f));

            ParticleSystem ember = AddSystem(root, "Ember", materials["ember"], EmberSortingOrder, 3f, 0.65f, 1.4f, 0.02f, 0.05f, 0.12f, 0.12f);
            SetRate(ember, 3.5f);
            SetPlanarVelocity(ember, -0.5f, 0.5f, 0.75f, 1.8f);
            SetSizeOverLifetime(ember, 1f, 0.12f);
            SetFade(ember, 0.08f);
        }

        /// <summary>Limits how far a particle may be rotated, so tongues point along the plume.</summary>
        private static void SetStartRotation(ParticleSystem system, float minRadians, float maxRadians)
        {
            ParticleSystem.MainModule main = system.main;
            main.startRotation = new ParticleSystem.MinMaxCurve(minRadians, maxRadians);
        }

        private static void SetStartColor(ParticleSystem system, Color color)
        {
            ParticleSystem.MainModule main = system.main;
            main.startColor = color;
        }

        private static void OffsetSystem(ParticleSystem system, float x, float y)
        {
            system.transform.localPosition = new Vector3(x, y, 0f);
        }

        /// <summary>
        /// Adds turbulence. Fire and smoke that only drift in straight lines look like sprites being moved;
        /// the noise module is what makes them curl.
        /// </summary>
        private static void SetTurbulence(ParticleSystem system, float strength, float frequency)
        {
            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(strength);
            noise.frequency = frequency;
            noise.scrollSpeed = 0.35f;
            noise.damping = true;
            noise.octaveCount = 2;
        }

        private static void BuildExhaustSmoke(GameObject root, Dictionary<string, Material> materials)
        {
            // A short dark pulse reads as combustion at the pipe; the larger pale layer carries it away
            // and dissolves it. The rig still controls both rates from engine load and the start-up puff.
            ParticleSystem dense = AddSystem(root, "DensePuffs", materials["exhaust"], DustSortingOrder, 2f, 0.55f, 1.05f, 0.16f, 0.3f);
            SetRate(dense, 6f);
            SetPlanarVelocity(dense, -0.1f, 0.1f, 0.18f, 0.48f);
            SetStartRotation(dense, -0.42f, 0.42f);
            SetSizeOverLifetime(dense, 0.72f, 1.75f);
            SetFade(dense, 0.09f);
            SetTurbulence(dense, 0.13f, 0.92f);
            SetStartColor(dense, new Color(0.72f, 0.69f, 0.65f, 0.74f));

            ParticleSystem drift = AddSystem(root, "Drift", materials["smoke_wisp"], DustSortingOrder - 1, 2f, 0.95f, 1.85f, 0.21f, 0.42f);
            SetRate(drift, 6f);
            SetPlanarVelocity(drift, -0.24f, 0.24f, 0.16f, 0.62f);
            SetStartRotation(drift, -0.75f, 0.75f);
            SetSizeOverLifetime(drift, 0.62f, 2.35f);
            SetFade(drift, 0.14f);
            SetTurbulence(drift, 0.2f, 0.58f);
            SetStartColor(drift, new Color(0.52f, 0.5f, 0.47f, 0.3f));
        }

        private static ParticleSystem AddSystem(
            GameObject root,
            string name,
            Material material,
            int sortingOrder,
            float duration,
            float lifetimeMin,
            float lifetimeMax,
            float sizeMin,
            float sizeMax)
        {
            return AddSystem(root, name, material, sortingOrder, duration, lifetimeMin, lifetimeMax, sizeMin, sizeMax, 0f, 0f);
        }

        /// <summary>
        /// Creates one particle system of an effect with the modules the runtime relies on already set:
        /// no shape (so every particle starts at the transform), planar velocity, and billboard rendering
        /// with an explicit draw order.
        /// </summary>
        /// <param name="spreadX">
        /// Half width of the patch particles are born over. A fire spreads across the engine deck instead
        /// of rising from a single point, and the shape is a plain box in the plane of the map.
        /// </param>
        private static ParticleSystem AddSystem(
            GameObject root,
            string name,
            Material material,
            int sortingOrder,
            float duration,
            float lifetimeMin,
            float lifetimeMax,
            float sizeMin,
            float sizeMax,
            float spreadX,
            float spreadY)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            ParticleSystem system = child.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.useAutoRandomSeed = false;
            system.randomSeed = StableSeed(name);

            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = Mathf.Max(0.1f, duration);
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetimeMin, lifetimeMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startSpeed = 0f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = Color.white;
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 256;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = spreadX > 0f && spreadY > 0f;
            if (shape.enabled)
            {
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(spreadX * 2f, spreadY * 2f, 0.001f);
            }

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            ParticleSystemRenderer renderer = child.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sortingOrder = sortingOrder;
            renderer.receiveShadows = false;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return system;
        }

        private static uint StableSeed(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char character in value)
                {
                    hash = (hash ^ character) * 16777619u;
                }

                return hash == 0u ? 1u : hash;
            }
        }

        private static void SetPlanarVelocity(ParticleSystem system, float minX, float maxX, float minY, float maxY)
        {
            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(minX, maxX);
            velocity.y = new ParticleSystem.MinMaxCurve(minY, maxY);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        }

        private static void SetSizeOverLifetime(ParticleSystem system, float from, float to)
        {
            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(new Keyframe(0f, from), new Keyframe(1f, to)));
        }

        private static void SetFade(ParticleSystem system, float peak)
        {
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, Mathf.Clamp(peak, 0.01f, 0.9f)),
                    new GradientAlphaKey(0f, 1f)
                });
            ParticleSystem.ColorOverLifetimeModule colour = system.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        private static void SetSparkColourOverLifetime(ParticleSystem system)
        {
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(1f, 0.93f, 0.58f), 0.22f),
                    new GradientColorKey(new Color(1f, 0.38f, 0.05f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.035f),
                    new GradientAlphaKey(0.88f, 0.58f),
                    new GradientAlphaKey(0f, 1f)
                });
            ParticleSystem.ColorOverLifetimeModule colour = system.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        private static void SetStretchedBillboard(ParticleSystem system, float lengthScale, float velocityScale)
        {
            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = Mathf.Max(0f, lengthScale);
            renderer.velocityScale = Mathf.Max(0f, velocityScale);
            renderer.cameraVelocityScale = 0f;
        }

        private static void SetBurst(ParticleSystem system, int count)
        {
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0.02f, (short)Mathf.Clamp(count, 1, 200)) });
        }

        private static void SetRate(ParticleSystem system, float rate)
        {
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = Mathf.Max(0.01f, rate);
        }

        private static float Max<T>(T[] items, Func<T, float> selector)
        {
            return items == null || items.Length == 0 ? 0f : items.Max(selector);
        }

        private static int Max<T>(T[] items, Func<T, int> selector)
        {
            return items == null || items.Length == 0 ? 0 : items.Max(selector);
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            string parent = assetPath.Substring(0, assetPath.LastIndexOf('/'));
            string name = assetPath.Substring(assetPath.LastIndexOf('/') + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
