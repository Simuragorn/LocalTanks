using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace LocalTanks.Editor
{
    /// <summary>
    /// Turns the local ground manifest produced by the WoT environment pipeline into one Unity material.
    /// The manifest is deliberately the hand-off: Python owns image processing, Unity owns import settings
    /// and references, and no scene stores a second hand-maintained copy of either.
    /// </summary>
    internal static class WoTGroundMaterialBuilder
    {
        internal const string ShaderName = "LocalTanks/WoTGroundDetail";
        private const string GroundRoot = "Assets/LocalOnly/WoTEnvironmentGenerated/Ground";

        internal readonly struct Result
        {
            public Result(Sprite surface, Material material, float detailStrength)
            {
                Surface = surface;
                Material = material;
                DetailStrength = detailStrength;
            }

            public Sprite Surface { get; }
            public Material Material { get; }
            public float DetailStrength { get; }
        }

        /// <summary>
        /// Applies importer settings before the map asset is loaded. Reimporting a texture can refresh the
        /// asset database and invalidate already loaded ScriptableObject references, so this is deliberately
        /// a separate first phase of the scene build.
        /// </summary>
        public static void Prepare(string mapId)
        {
            string manifestPath = ManifestPath(mapId);
            string absolute = Path.GetFullPath(manifestPath);
            if (!File.Exists(absolute))
            {
                throw new FileNotFoundException(
                    $"Runtime ground for '{mapId}' is missing. Run " +
                    "Automation/pipelines/WoTEnvironmentPipeline/Scripts/Invoke-WoTEnvironmentPipeline.ps1 " +
                    "-Mode BuildGround before rebuilding the scene.", absolute);
            }

            JObject document = JObject.Parse(File.ReadAllText(absolute));
            ValidateIdentity(document, mapId, manifestPath);
            ConfigureMacro(
                RequiredString(document, "macroTexture", manifestPath),
                Mathf.Clamp((int?)document["macroMaxTextureSize"] ?? 4096, 2048, 8192));
            ConfigureAtlas(RequiredString(document, "materialAtlas", manifestPath));
            ConfigureSelector(RequiredString(document, "materialSelector", manifestPath));
        }

        public static Result Build(string mapId, Vector2 playableSize, float metresPerUnit)
        {
            string manifestPath = ManifestPath(mapId);
            string absolute = Path.GetFullPath(manifestPath);
            if (!File.Exists(absolute))
            {
                throw new FileNotFoundException(
                    $"Runtime ground for '{mapId}' is missing. Run " +
                    "Automation/pipelines/WoTEnvironmentPipeline/Scripts/Invoke-WoTEnvironmentPipeline.ps1 " +
                    "-Mode BuildGround before rebuilding the scene.", absolute);
            }

            JObject document = JObject.Parse(File.ReadAllText(absolute));
            ValidateIdentity(document, mapId, manifestPath);

            string macroPath = RequiredString(document, "macroTexture", manifestPath);
            string atlasPath = RequiredString(document, "materialAtlas", manifestPath);
            string selectorPath = RequiredString(document, "materialSelector", manifestPath);

            Sprite surface = AssetDatabase.LoadAssetAtPath<Sprite>(macroPath);
            Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
            Texture2D selector = AssetDatabase.LoadAssetAtPath<Texture2D>(selectorPath);
            if (surface == null) throw new InvalidDataException($"Ground macro is not an imported sprite: {macroPath}");
            if (atlas == null) throw new InvalidDataException($"Ground atlas is not an imported texture: {atlasPath}");
            if (selector == null) throw new InvalidDataException($"Ground selector is not an imported texture: {selectorPath}");

            Shader shader = Shader.Find(ShaderName);
            if (shader == null) throw new InvalidOperationException($"Ground shader is missing: {ShaderName}");
            string materialPath = $"{GroundRoot}/{mapId}_ground.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(shader) { name = $"{mapId}_ground" };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            JObject atlasData = document["atlas"] as JObject
                ?? throw new InvalidDataException($"Ground manifest has no atlas block: {manifestPath}");
            int columns = Mathf.Max(1, (int?)atlasData["columns"] ?? 1);
            int rows = Mathf.Max(1, (int?)atlasData["rows"] ?? 1);
            int tilePixels = Mathf.Max(1, (int?)atlasData["tilePixels"] ?? 1);
            int paddingPixels = Mathf.Max(0, (int?)atlasData["paddingPixels"] ?? 0);
            int cellPixels = Mathf.Max(tilePixels, (int?)atlasData["cellPixels"] ?? tilePixels);
            int materialCount = Math.Max(1, document["materials"] is JArray materials ? materials.Count : 1);
            float detailTileMetres = Mathf.Max(0.01f, (float?)document["detailTileMeters"] ?? 32f);
            float detailTileUnits = detailTileMetres / Mathf.Max(0.001f, metresPerUnit);
            float strength = Mathf.Clamp01((float?)document["detailStrength"] ?? 0.55f);

            material.SetTexture("_DetailAtlas", atlas);
            material.SetTexture("_SelectorTex", selector);
            material.SetVector("_DetailRepeats", new Vector4(
                playableSize.x / detailTileUnits,
                playableSize.y / detailTileUnits,
                0f,
                0f));
            material.SetVector("_AtlasLayout", new Vector4(
                columns,
                rows,
                paddingPixels / (float)cellPixels,
                tilePixels / (float)cellPixels));
            material.SetFloat("_MaterialCount", materialCount);
            material.SetFloat("_DetailStrength", strength);
            material.SetColor("_GroundTint", Color.white);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);

            return new Result(surface, material, strength);
        }

        public static string ManifestPath(string mapId) => $"{GroundRoot}/{mapId}_ground.json";

        private static void ValidateIdentity(JObject document, string mapId, string path)
        {
            if ((int?)document["schemaVersion"] != 1 || (string)document["mapId"] != mapId)
            {
                throw new InvalidDataException($"Ground manifest identity is invalid: {path}");
            }
        }

        private static string RequiredString(JObject document, string key, string path)
        {
            string value = (string)document[key];
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException($"Ground manifest {path} has no '{key}'.");
            }
            return value.Replace('\\', '/');
        }

        private static void ConfigureMacro(string path, int maxSize)
        {
            ConfigureTexture(path, importer =>
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100f;
                TextureImporterSettings spriteSettings = new TextureImporterSettings();
                importer.ReadTextureSettings(spriteSettings);
                spriteSettings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(spriteSettings);
                importer.maxTextureSize = maxSize;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.compressionQuality = 100;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 4;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.alphaIsTransparency = false;
            });
        }

        private static void ConfigureAtlas(string path)
        {
            ConfigureTexture(path, importer =>
            {
                importer.textureType = TextureImporterType.Default;
                importer.maxTextureSize = 4096;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.compressionQuality = 100;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 4;
                importer.wrapMode = TextureWrapMode.Clamp;
                // The atlas is multiplicative data centred on byte value 128, not a colour image. Importing
                // it as sRGB would decode 0.5 to ~0.21 in linear space and darken the entire arena.
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.None;
            });
        }

        private static void ConfigureSelector(string path)
        {
            ConfigureTexture(path, importer =>
            {
                importer.textureType = TextureImporterType.Default;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Point;
                importer.anisoLevel = 1;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.None;
            });
        }

        private static void ConfigureTexture(string path, Action<TextureImporter> configure)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidDataException($"Texture importer is missing: {path}");
            configure(importer);
            // This builder only runs as an explicit content-generation operation. Always committing the
            // settings is more reliable than comparing Unity's partially serialised importer view: platform
            // overrides and a first-time sprite conversion are not represented consistently by that view.
            importer.SaveAndReimport();
        }
    }
}
