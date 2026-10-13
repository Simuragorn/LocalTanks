using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LocalTanks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
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
                Texture2D texture = VfxTextureLibrary.IsAnimated(textureName)
                ? VfxTextureLibrary.CreateSheet(textureName)
                : VfxTextureLibrary.Create(textureName);
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
            materials["dust"] = CreateMaterial(shader, "vfx_dust", textures[VfxTextureLibrary.DustPuff], false);
            materials["exhaust"] = CreateMaterial(shader, "vfx_exhaust", textures[VfxTextureLibrary.ExhaustSmoke], false);
            materials["spray"] = CreateMaterial(shader, "vfx_spray", textures[VfxTextureLibrary.WaterDrop], false);
            materials["debris"] = CreateMaterial(shader, "vfx_debris", textures[VfxTextureLibrary.DirtChunk], false);
            materials["spark"] = CreateMaterial(shader, "vfx_spark", textures[VfxTextureLibrary.Spark], true);
            materials["flash"] = CreateMaterial(shader, "vfx_flash", textures[VfxTextureLibrary.FlashStar], true);
            materials["petals"] = CreateMaterial(shader, "vfx_petals", textures[VfxTextureLibrary.FlashPetals], true);
            materials["core"] = CreateMaterial(shader, "vfx_core", textures[VfxTextureLibrary.FlashCore], true);
            materials["flame"] = CreateMaterial(shader, "vfx_flame", textures[VfxTextureLibrary.Flame], true);
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
            // Three layers instead of one symmetric star: a small white-hot core, an irregular bloom of
            // petals, and thin spikes thrown along the bore. The rig rotates and scales the whole thing a
            // little on every shot, so a burst of fire never looks like one sprite repeated.
            ParticleSystem core = AddSystem(root, "Core", materials["core"], FlashSortingOrder + 1, seconds + 0.06f, 0.04f, 0.07f, 0.16f, 0.28f);
            SetHotColor(core, 4.6f, 3.9f, 2.8f);
            SetBurst(core, 1);
            SetPlanarVelocity(core, -0.1f, 0.1f, 0.4f, 1.0f);
            SetSizeOverLifetime(core, 1.1f, 0.5f);
            SetFade(core, 0.06f);

            ParticleSystem petals = AddSystem(root, "Petals", materials["petals"], FlashSortingOrder, seconds + 0.1f, 0.06f, 0.12f, 0.55f, 0.95f);
            SetHotColor(petals, 2.6f, 2f, 1.2f);
            SetBurst(petals, 1);
            SetPlanarVelocity(petals, -0.2f, 0.2f, 0.7f, 1.6f);
            SetSizeOverLifetime(petals, 1f, 0.5f);
            SetFade(petals, 0.08f);

            ParticleSystem spikes = AddSystem(root, "Spikes", materials["spark"], SparkSortingOrder, seconds + 0.08f, 0.05f, 0.11f, 0.22f, 0.45f);
            SetHotColor(spikes, 3.4f, 2.9f, 2f);
            // Warm and short: the light follows the burst, and the pool switches the whole instance off
            // when the flash is over, so the light is brief without any code to fade it.
            AddLight(root, "FlashLight", new Color(1f, 0.74f, 0.42f), 1.1f, 0.08f, 2.2f, 0.6f);
            SetBurst(spikes, 5);
            // Spikes leave the bore, so their spread along the barrel is narrow and their side spread tiny.
            SetPlanarVelocity(spikes, -0.25f, 0.25f, 1.2f, 2.6f);
            SetSizeOverLifetime(spikes, 1f, 0.3f);
            SetFade(spikes, 0.06f);
        }

        /// <summary>
        /// The plume that keeps leaving the barrel for a moment after the shot. It lives at the muzzle and
        /// the rig opens it for a fraction of a second, which is what a gun actually does: the flash is
        /// gone in a frame or two, the propellant smoke keeps drifting out behind it.
        /// </summary>
        private static void BuildBarrelSmoke(GameObject root, Dictionary<string, Material> materials)
        {
            ParticleSystem plume = AddSystem(root, "Plume", materials["exhaust"], MuzzleSmokeSortingOrder, 2f, 0.6f, 1.2f, 0.12f, 0.22f);
            SetRate(plume, 7f);
            // Local +Y is out of the bore, because the prefab is parented to the muzzle.
            SetPlanarVelocity(plume, -0.18f, 0.18f, 0.3f, 0.9f);
            SetSizeOverLifetime(plume, 0.7f, 2.1f);
            SetFade(plume, 0.1f);
        }

        private static void BuildMuzzleSmoke(GameObject root, CombatVfxCatalog catalog, Dictionary<string, Material> materials)
        {
            float seconds = Max(catalog.weaponProfiles, profile => profile.smokeSeconds);
            int burst = Mathf.Max(1, Max(catalog.weaponProfiles, profile => profile.smokeBurst));
            ParticleSystem smoke = AddSystem(root, "Smoke", materials["smoke"], MuzzleSmokeSortingOrder, seconds, 0.5f, 1.0f, 0.24f, 0.42f, true);
            SetBurst(smoke, burst);
            SetPlanarVelocity(smoke, -0.4f, 0.4f, 0.6f, 1.7f);
            SetSizeOverLifetime(smoke, 0.7f, 1.7f);
            SetFade(smoke, 0.12f);
        }

        private static void BuildImpactSpark(GameObject root, CombatVfxCatalog catalog, Dictionary<string, Material> materials)
        {
            float speed = Max(catalog.impactProfiles, profile => profile.sparkSpeed);
            int burst = Mathf.Max(1, Max(catalog.impactProfiles, profile => profile.sparkCount));
            // Sparks were the one effect the first in-game look rejected outright: at a fifth of a tank's
            // length each they read as flying pebbles. They are now small, thin and short-lived, which is
            // what makes them read as sparks rather than as debris.
            ParticleSystem spark = AddSystem(root, "Sparks", materials["spark"], SparkSortingOrder, 0.3f, 0.1f, 0.2f, 0.04f, 0.08f);
            SetBurst(spark, burst);
            // Sparks fly outward in the plane of the map: an omnidirectional burst in three dimensions
            // would look like a static dot from a top-down camera.
            SetPlanarVelocity(spark, -speed, speed, -speed, speed);
            SetSizeOverLifetime(spark, 1f, 0.15f);
            SetFade(spark, 0.05f);
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
            ParticleSystem smoke = AddSystem(root, "Smoke", materials["smoke"], SmokeSortingOrder, 3f, 1.4f, 2.4f, 0.5f, 0.9f);
            SetRate(smoke, 3f);
            SetPlanarVelocity(smoke, -0.2f, 0.2f, 0.6f, 1.1f);
            SetSizeOverLifetime(smoke, 0.6f, 2f);
            SetFade(smoke, 0.2f);
        }

        private static void BuildDamageFire(GameObject root, Dictionary<string, Material> materials)
        {
            // A fire is a body of light with tongues licking out of it, not a pile of flame-shaped sprites.
            // Four layers: a warm glow that carries the brightness, soft tongues, a hot core and embers.
            // Lifetimes are short and sizes small, so the shape shimmers instead of showing one large
            // silhouette, and turbulence keeps every particle moving differently.
            ParticleSystem glow = AddSystem(root, "Glow", materials["fire_glow"], FireSortingOrder, 3f, 0.35f, 0.6f, 0.3f, 0.55f, 0.18f, 0.3f, true);
            SetHotColor(glow, 2.8f, 1.5f, 0.5f);
            SetRate(glow, 16f);
            SetPlanarVelocity(glow, -0.15f, 0.15f, 0.2f, 0.6f);
            SetSizeOverLifetime(glow, 0.85f, 1.2f);
            SetFade(glow, 0.22f);
            SetTurbulence(glow, 0.12f, 0.7f);

            ParticleSystem tongues = AddSystem(root, "Tongues", materials["flame"], FireSortingOrder + 1, 3f, 0.26f, 0.48f, 0.28f, 0.48f, 0.18f, 0.3f, true);
            SetHotColor(tongues, 2.4f, 1.3f, 0.35f);
            SetRate(tongues, 20f);
            SetPlanarVelocity(tongues, -0.1f, 0.1f, 0.35f, 0.95f);
            SetStartRotation(tongues, -0.55f, 0.55f);
            SetSizeOverLifetime(tongues, 1f, 0.55f);
            SetFade(tongues, 0.18f);
            SetTurbulence(tongues, 0.2f, 0.9f);

            ParticleSystem core = AddSystem(root, "Core", materials["core"], FireSortingOrder + 2, 3f, 0.18f, 0.34f, 0.16f, 0.3f, 0.1f, 0.16f);
            SetRate(core, 10f);
            SetPlanarVelocity(core, -0.1f, 0.1f, 0.15f, 0.5f);
            SetSizeOverLifetime(core, 1.1f, 0.6f);
            SetFade(core, 0.15f);

            ParticleSystem ember = AddSystem(root, "Ember", materials["ember"], EmberSortingOrder, 3f, 0.6f, 1.3f, 0.04f, 0.09f, 0.14f, 0.22f);
            SetHotColor(ember, 3.6f, 2.6f, 1.2f);
            SetRate(ember, 9f);
            SetPlanarVelocity(ember, -0.7f, 0.7f, 0.7f, 1.9f);
            SetSizeOverLifetime(ember, 1f, 0.3f);
            SetFade(ember, 0.1f);

            // A wreck lights its own surroundings for as long as it burns, which is what makes a burning
            // machine visible from across the map and what turns the fire into something in the world
            // rather than a picture drawn on top of it.
            AddLight(root, "FireLight", new Color(1f, 0.52f, 0.2f), 0.8f, 0.25f, 2.4f, 0.5f);
        }

        /// <summary>
        /// Authors a layer above white. Values over one are what the bloom pass picks up, so a flash core
        /// glows instead of clipping to a flat white spot; the tone mapper in the volume profile brings
        /// them back into range for the screen.
        /// </summary>
        private static void SetHotColor(ParticleSystem system, float red, float green, float blue)
        {
            ParticleSystem.MainModule main = system.main;
            main.startColor = new Color(red, green, blue, 1f);
        }

        /// <summary>Limits how far a particle may be rotated, so tongues point along the plume.</summary>
        private static void SetStartRotation(ParticleSystem system, float minRadians, float maxRadians)
        {
            ParticleSystem.MainModule main = system.main;
            main.startRotation = new ParticleSystem.MinMaxCurve(minRadians, maxRadians);
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
            // The exhaust is the effect a player watches to read what a machine is doing, so it is dense
            // and long lived: the rig raises the rate while the engine is loaded and multiplies it while a
            // machine pulls away from a standstill.
            ParticleSystem exhaust = AddSystem(root, "Exhaust", materials["exhaust"], DustSortingOrder, 2f, 1.0f, 1.9f, 0.26f, 0.46f);
            SetRate(exhaust, 6f);
            SetPlanarVelocity(exhaust, -0.22f, 0.22f, 0.2f, 0.65f);
            SetSizeOverLifetime(exhaust, 0.8f, 2.1f);
            SetFade(exhaust, 0.2f);
        }

        /// <summary>
        /// Index of the additive light blend style in <c>Assets/Settings/Renderer2D.asset</c>. The renderer
        /// declares four styles, two multiply and two additive; light meant to be seen as a glow has to use
        /// an additive one, otherwise it would multiply the scene towards black.
        /// </summary>
        private const int AdditiveLightBlendStyle = 1;

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
            bool animated = false)
        {
            return AddSystem(root, name, material, sortingOrder, duration, lifetimeMin, lifetimeMax, sizeMin, sizeMax, 0f, 0f, animated);
        }

        /// <summary>
        /// Adds a 2D light to an effect. A muzzle flash and a fire do not only draw, they light what is
        /// around them, and that is most of what makes a shot read as a shot: the hull and the ground
        /// brighten for a moment. Blend style one is the additive style of this project's 2D renderer,
        /// which adds light on top of the scene instead of replacing its colour.
        /// </summary>
        private static void AddLight(
            GameObject root,
            string name,
            Color color,
            float intensity,
            float innerRadius,
            float outerRadius,
            float falloff)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            Light2D light = child.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.pointLightInnerRadius = innerRadius;
            light.pointLightOuterRadius = outerRadius;
            light.pointLightInnerAngle = 360f;
            light.pointLightOuterAngle = 360f;
            light.falloffIntensity = falloff;
            light.blendStyleIndex = AdditiveLightBlendStyle;
            light.shadowsEnabled = false;
        }

        /// <summary>
        /// Plays an animated sheet over the particle's own life, so a flame or a cloud of smoke changes
        /// inside itself instead of being one still picture that drifts.
        /// </summary>
        private static void SetAnimatedSheet(ParticleSystem system)
        {
            ParticleSystem.TextureSheetAnimationModule sheet = system.textureSheetAnimation;
            sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = VfxTextureLibrary.SheetColumns;
            sheet.numTilesY = VfxTextureLibrary.SheetColumns;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.timeMode = ParticleSystemAnimationTimeMode.Lifetime;
            sheet.cycleCount = 1;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f)));
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
            float spreadY,
            bool animated = false)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            ParticleSystem system = child.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

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
            if (animated)
            {
                SetAnimatedSheet(system);
            }

            return system;
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
