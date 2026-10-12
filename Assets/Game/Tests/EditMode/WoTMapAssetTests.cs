using System.IO;
using System.Linq;
using LocalTanks.Editor;
using NUnit.Framework;

namespace LocalTanks.Tests
{
    /// <summary>
    /// Guards the projection of the converted arena JSON into the map asset. The source file is
    /// local-only, so the checks are skipped on a checkout that has not run the map pipeline; when it
    /// is present, a silently empty or misplaced import fails here instead of in the scene.
    /// </summary>
    public sealed class WoTMapAssetTests
    {
        private static string SourcePath => WoTMapImporter.PrimitivesPath("10_hills");
        private static bool HasSource => File.Exists(SourcePath);

        [Test]
        public void ConvertedMap_CarriesEverySection()
        {
            if (!HasSource) Assert.Ignore("Primitive map JSON is not present; run the WoT map pipeline first.");

            WoTMapAsset asset = WoTMapAssetConverter.Convert(SourcePath, WoTMapImporter.NavigationCellMetres);
            try
            {
                Assert.That(asset.Validate(), Is.Empty);
                Assert.That(asset.mapId, Is.EqualTo("10_hills"));
                Assert.That(asset.ObstacleCount, Is.GreaterThan(1000));
                Assert.That(asset.VegetationCount, Is.GreaterThan(1000));
                Assert.That(asset.WaterCellCount, Is.GreaterThan(100));
                Assert.That(asset.basePositions, Has.Length.EqualTo(2));
                Assert.That(asset.terrainHeightsMetres.Length,
                    Is.EqualTo(asset.terrainColumns * asset.terrainRows));
                Assert.That(asset.navigationBlocked.Length,
                    Is.EqualTo(asset.navigationColumns * asset.navigationRows));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        /// <summary>
        /// The map keeps its true size. Compressing it would make a median house smaller than a tank
        /// hull, which is the mistake ADR-053 exists to prevent.
        /// </summary>
        [Test]
        public void ConvertedMap_KeepsSourceScaleAndCentresOnTheOrigin()
        {
            if (!HasSource) Assert.Ignore("Primitive map JSON is not present; run the WoT map pipeline first.");

            WoTMapAsset asset = WoTMapAssetConverter.Convert(SourcePath, WoTMapImporter.NavigationCellMetres);
            try
            {
                Assert.That(asset.playableSize.x * asset.metresPerUnit, Is.EqualTo(800f).Within(1f));
                Assert.That(asset.playableSize.y * asset.metresPerUnit, Is.EqualTo(800f).Within(1f));
                Assert.That(asset.worldCenter, Is.EqualTo(UnityEngine.Vector2.zero));

                // Every obstacle must land inside the playable area, give or take the rocks and cliffs
                // that line its very edge in the source data. A double conversion, which once moved
                // every obstacle by half the field, would fail this by tens of units.
                float limit = asset.playableSize.x * 0.5f + 10f;
                float furthest = asset.obstacles.Max(item =>
                    UnityEngine.Mathf.Max(UnityEngine.Mathf.Abs(item.center.x), UnityEngine.Mathf.Abs(item.center.y)));
                Assert.That(furthest, Is.LessThan(limit),
                    "Obstacles must be centred on the map origin, not offset by the source bounds.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }
    }
}
