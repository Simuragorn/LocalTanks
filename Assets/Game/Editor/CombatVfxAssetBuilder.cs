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

            Debug.Log($"Local Tanks: built {CombatVfxEffects.All().Length} combat VFX prefabs, " +
                      $"{materials.Count} materials and {textures.Count} textures " +
                      $"({catalog.weaponProfiles.Length} caliber bands, {catalog.surfaceProfiles.Length} surfaces).");
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

            Sprite markSprite = AssetDatabase.LoadAssetAtPath<Sprite>(MarkSpritePath());
            if (markSprite != null && !Mathf.Approximately(markSprite.bounds.size.x, 1f))
            {
                errors.Add($"Track mark sprite is {markSprite.bounds.size.x:0.###} world units wide. " +
                           "Import it at " + VfxTextureLibrary.Size + " pixels per unit so the emitter's " +
                           "scale is the print size in world units.");
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
        /// the generated art. The track print is the exception: the emitter loads it by name at run time,
        /// so it has to sit inside the <c>Resources</c> folder next to the effect prefabs. Writing it to
        /// the art folder would generate a perfectly good texture that the game could never find.
        /// </remarks>
        public static string TextureAssetPath(string textureName)
        {
            return textureName == VfxTextureLibrary.TrackMark
                ? $"{PrefabFolder}/{textureName}.png"
                : $"{TextureFolder}/{textureName}.png";
        }

        /// <summary>The track print sprite, at the path the runtime provider asks for.</summary>
        public static string MarkSpritePath()
        {
            return $"{PrefabFolder}/{VfxTextureLibrary.TrackMark}.png";
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
                ConfigureTexture(assetPath, textureName == VfxTextureLibrary.TrackMark);
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
            materials["core"] = CreateMaterial(shader, "vfx_core", textures[VfxTextureLibrary.FlashCore], true);
            materials["flame"] = CreateMaterial(shader, "vfx_flame", textures[VfxTextureLibrary.Flame], true);
            materials["ember"] = CreateMaterial(shader, "vfx_ember", textures[VfxTextureLibrary.Ember], true);
            return materials;
        }

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
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", additive ? 2f : 0f);
            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)(additive ? BlendMode.One : BlendMode.SrcAlpha));
            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
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
            ParticleSystem core = AddSystem(root, "Core", materials["core"], FlashSortingOrder, seconds + 0.05f, 0.05f, 0.09f, 0.22f, 0.34f);
            SetBurst(core, 1);
            SetPlanarVelocity(core, -0.2f, 0.2f, 1.0f, 2.0f);
            SetSizeOverLifetime(core, 1.1f, 0.5f);
            SetFade(core, 0.05f);

            ParticleSystem star = AddSystem(root, "Star", materials["flash"], SparkSortingOrder, seconds + 0.1f, 0.04f, 0.07f, 0.5f, 0.8f);
            SetBurst(star, 1);
            SetPlanarVelocity(star, -0.3f, 0.3f, 0.6f, 1.4f);
            SetSizeOverLifetime(star, 1f, 0.35f);
            SetFade(star, 0.05f);
        }

        private static void BuildMuzzleSmoke(GameObject root, CombatVfxCatalog catalog, Dictionary<string, Material> materials)
        {
            float seconds = Max(catalog.weaponProfiles, profile => profile.smokeSeconds);
            int burst = Mathf.Max(1, Max(catalog.weaponProfiles, profile => profile.smokeBurst));
            ParticleSystem smoke = AddSystem(root, "Smoke", materials["smoke"], MuzzleSmokeSortingOrder, seconds, 0.5f, 1.0f, 0.35f, 0.6f);
            SetBurst(smoke, burst);
            SetPlanarVelocity(smoke, -0.4f, 0.4f, 0.6f, 1.7f);
            SetSizeOverLifetime(smoke, 0.7f, 1.7f);
            SetFade(smoke, 0.12f);
        }

        private static void BuildImpactSpark(GameObject root, CombatVfxCatalog catalog, Dictionary<string, Material> materials)
        {
            float speed = Max(catalog.impactProfiles, profile => profile.sparkSpeed);
            int burst = Mathf.Max(1, Max(catalog.impactProfiles, profile => profile.sparkCount));
            ParticleSystem spark = AddSystem(root, "Sparks", materials["spark"], SparkSortingOrder, 0.4f, 0.18f, 0.34f, 0.18f, 0.3f);
            SetBurst(spark, burst);
            // Sparks fly outward in the plane of the map: an omnidirectional burst in three dimensions
            // would look like a static dot from a top-down camera.
            SetPlanarVelocity(spark, -speed, speed, -speed, speed);
            SetSizeOverLifetime(spark, 1f, 0.25f);
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
            ParticleSystem flame = AddSystem(root, "Flame", materials["flame"], FireSortingOrder, 3f, 0.35f, 0.6f, 0.35f, 0.6f);
            SetRate(flame, 5f);
            SetPlanarVelocity(flame, -0.15f, 0.15f, 0.2f, 0.7f);
            SetSizeOverLifetime(flame, 1.1f, 0.7f);
            SetFade(flame, 0.15f);

            ParticleSystem ember = AddSystem(root, "Ember", materials["ember"], EmberSortingOrder, 3f, 0.5f, 1.1f, 0.06f, 0.13f);
            SetRate(ember, 2f);
            SetPlanarVelocity(ember, -0.5f, 0.5f, 0.5f, 1.4f);
            SetSizeOverLifetime(ember, 1f, 0.3f);
            SetFade(ember, 0.1f);
        }

        private static void BuildExhaustSmoke(GameObject root, Dictionary<string, Material> materials)
        {
            ParticleSystem exhaust = AddSystem(root, "Exhaust", materials["exhaust"], DustSortingOrder, 2f, 0.8f, 1.5f, 0.2f, 0.35f);
            SetRate(exhaust, 1.5f);
            SetPlanarVelocity(exhaust, -0.2f, 0.2f, 0.15f, 0.5f);
            SetSizeOverLifetime(exhaust, 0.8f, 1.9f);
            SetFade(exhaust, 0.2f);
        }

        /// <summary>
        /// Creates one particle system of an effect with the modules the runtime relies on already set:
        /// no shape (so every particle starts at the transform), planar velocity, and billboard rendering
        /// with an explicit draw order.
        /// </summary>
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
            main.maxParticles = 128;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;

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
