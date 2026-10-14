using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LocalTanks.Tests
{
    /// <summary>
    /// Guards the generated side of the map-ground hand-off. The source textures are intentionally local-only,
    /// so these checks skip cleanly on a checkout that has not built them yet.
    /// </summary>
    public sealed class WoTGroundPipelineTests
    {
        private const string Root = "Assets/LocalOnly/WoTEnvironmentGenerated/Ground";
        private const string ManifestPath = Root + "/10_hills_ground.json";
        private const string MaterialPath = Root + "/10_hills_ground.mat";
        private const string MacroPath = Root + "/mines_aerial.png";
        private const string AtlasPath = Root + "/10_hills_ground_atlas.png";
        private const string SelectorPath = Root + "/10_hills_ground_selector.png";

        private static void RequireGeneratedGround()
        {
            if (!File.Exists(ManifestPath))
            {
                Assert.Ignore("Runtime ground is local-only; run the environment pipeline in BuildGround mode first.");
            }
        }

        [Test]
        public void GeneratedGround_UsesOneMacroAndOnePhysicalScaleDetailMaterial()
        {
            RequireGeneratedGround();
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Assert.That(material, Is.Not.Null, "Rebuild the WoT map scene after BuildGround.");
            Assert.That(material.shader.name, Is.EqualTo("LocalTanks/WoTGroundDetail"));
            Assert.That(material.GetTexture("_DetailAtlas"), Is.EqualTo(AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath)));
            Assert.That(material.GetTexture("_SelectorTex"), Is.EqualTo(AssetDatabase.LoadAssetAtPath<Texture2D>(SelectorPath)));
            Assert.That(material.GetVector("_DetailRepeats").x, Is.GreaterThan(10f));
            Assert.That(material.GetVector("_DetailRepeats").y, Is.GreaterThan(10f));
            Assert.That(material.GetFloat("_MaterialCount"), Is.GreaterThan(1f));
        }

        [Test]
        public void GeneratedGround_ImportsMacroAtSourceResolutionAndSelectorWithoutFiltering()
        {
            RequireGeneratedGround();
            TextureImporter macro = AssetImporter.GetAtPath(MacroPath) as TextureImporter;
            TextureImporter atlas = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
            TextureImporter selector = AssetImporter.GetAtPath(SelectorPath) as TextureImporter;
            Assert.That(macro, Is.Not.Null);
            Assert.That(macro.maxTextureSize, Is.GreaterThanOrEqualTo(4096));
            Assert.That(macro.mipmapEnabled, Is.True);
            Assert.That(macro.filterMode, Is.EqualTo(FilterMode.Trilinear));
            Assert.That(atlas, Is.Not.Null);
            Assert.That(atlas.sRGBTexture, Is.False, "The normalised atlas is multiplier data, not sRGB colour.");
            Assert.That(selector, Is.Not.Null);
            Assert.That(selector.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(selector.mipmapEnabled, Is.False);
            Assert.That(selector.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(selector.sRGBTexture, Is.False);
        }
    }
}
