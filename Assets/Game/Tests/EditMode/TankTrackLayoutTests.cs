using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace LocalTanks.Tests
{
    /// <summary>
    /// The tread overlay is a second sprite drawn on top of the hull sprite, so the only thing that
    /// keeps it on the tracks is the measured track block of the canonical layout. These tests read
    /// the generated layouts and the generated mask pixels together: a mask measured wrongly, a mask
    /// cropped to another rectangle, or a metric that no longer matches its own pixels all render
    /// perfectly and simply animate the wrong part of the machine.
    /// </summary>
    public sealed class TankTrackLayoutTests
    {
        private const string GeneratedRoot = "Assets/LocalOnly/WoTGenerated";

        /// <summary>Colour floor that mirrors the mask writer in Tools/WoTSpritePipeline.</summary>
        private const byte MaskAlphaCutoff = 8;

        private static string[] VehicleDirectories()
        {
            if (!Directory.Exists(GeneratedRoot))
            {
                return new string[0];
            }

            return Directory.GetDirectories(GeneratedRoot)
                .Where(directory =>
                    File.Exists(Path.Combine(directory, Path.GetFileName(directory) + "_layout.json")) &&
                    File.Exists(Path.Combine(directory, Path.GetFileName(directory) + "_tracks_mask.png")))
                .OrderBy(directory => directory)
                .ToArray();
        }

        [Test]
        public void EveryGeneratedVehicle_DeclaresItsMeasuredTracks()
        {
            string[] vehicles = VehicleDirectories();
            Assert.That(vehicles, Is.Not.Empty,
                "No generated vehicle with a track mask was found. The local sprite bundle is missing; " +
                "run Tools/WoTSpritePipeline for at least one vehicle.");

            foreach (string directory in vehicles)
            {
                string vehicleId = Path.GetFileName(directory);
                TrackLayoutJsonProbe layout = ReadLayout(directory, vehicleId);
                Assert.That(layout.schemaVersion, Is.EqualTo(3), $"{vehicleId} layout schema");
                Assert.That(layout.tracks, Is.Not.Null, $"{vehicleId} must declare a tracks block");
                Assert.That(layout.tracks.schemaVersion, Is.EqualTo(1), $"{vehicleId} tracks schema");
                Assert.That(layout.tracks.maskFile, Is.Not.Empty, $"{vehicleId} mask file");
                Assert.That(Mathf.Abs(layout.tracks.forwardSignV), Is.EqualTo(1f),
                    $"{vehicleId} scroll direction must be exactly +1 or -1");
                Assert.That(layout.tracks.treadPitchMetres, Is.GreaterThan(0f), $"{vehicleId} tread pitch");
                Assert.That(layout.tracks.bandSeparationMetres, Is.GreaterThan(0f),
                    $"{vehicleId} band separation must be measurable");
                Assert.That(layout.tracks.bandWidthMetres, Is.GreaterThan(0f), $"{vehicleId} band width");
                Assert.That(layout.tracks.sideSource, Is.Not.Empty, $"{vehicleId} side source");
            }
        }

        /// <summary>
        /// The mask must be cropped exactly like the hull layer, because both sprites are drawn with
        /// the same centred pivot: a mask of another size covers a different part of the machine even
        /// though it imports and animates without a single error.
        /// </summary>
        [Test]
        public void EveryTrackMask_MatchesTheHullRectangleOfItsLayout()
        {
            foreach (string directory in VehicleDirectories())
            {
                string vehicleId = Path.GetFileName(directory);
                TrackLayoutJsonProbe layout = ReadLayout(directory, vehicleId);
                Texture2D mask = ReadTexture(Path.Combine(directory, layout.tracks.maskFile));
                try
                {
                    Assert.That((float)mask.width, Is.EqualTo(layout.hull.width).Within(0.51f),
                        $"{vehicleId} mask width must equal its hull sprite width");
                    Assert.That((float)mask.height, Is.EqualTo(layout.hull.height).Within(0.51f),
                        $"{vehicleId} mask height must equal its hull sprite height");
                }
                finally
                {
                    Object.DestroyImmediate(mask);
                }
            }
        }

        /// <summary>
        /// The measured band separation and width are what the runtime turns into a differential and
        /// into a pattern scale, so they are re-measured here from the pixels the pipeline wrote.
        /// </summary>
        [Test]
        public void EveryTrackMask_AgreesWithTheMetricsItsLayoutReports()
        {
            foreach (string directory in VehicleDirectories())
            {
                string vehicleId = Path.GetFileName(directory);
                TrackLayoutJsonProbe layout = ReadLayout(directory, vehicleId);
                Texture2D mask = ReadTexture(Path.Combine(directory, layout.tracks.maskFile));
                try
                {
                    Color32[] pixels = mask.GetPixels32();
                    int redCount = 0;
                    int greenCount = 0;
                    double redSumX = 0;
                    double greenSumX = 0;
                    for (int index = 0; index < pixels.Length; index++)
                    {
                        Color32 pixel = pixels[index];
                        if (pixel.a < MaskAlphaCutoff)
                        {
                            continue;
                        }

                        int x = index % mask.width;
                        if (pixel.r > pixel.g)
                        {
                            redCount++;
                            redSumX += x;
                        }
                        else
                        {
                            greenCount++;
                            greenSumX += x;
                        }
                    }

                    Assert.That(redCount, Is.GreaterThan(0), $"{vehicleId} mask must mark a left track");
                    Assert.That(greenCount, Is.GreaterThan(0), $"{vehicleId} mask must mark a right track");

                    double redCentroid = redSumX / redCount;
                    double greenCentroid = greenSumX / greenCount;
                    double separation = System.Math.Abs(redCentroid - greenCentroid);
                    Assert.That(separation, Is.EqualTo(layout.tracks.bandSeparationPixels).Within(2.0),
                        $"{vehicleId} band separation must match the mask pixels");

                    // The green channel must carry the machine's right track, and the layout records
                    // where the model's own starboard direction projected to. A mirrored mask passes
                    // every other check and only shows up as tracks scrolling the wrong way in a turn.
                    bool starboardIsImageRight = layout.tracks.starboardImageXPixels >= 0f;
                    Assert.That(greenCentroid < redCentroid, Is.EqualTo(!starboardIsImageRight),
                        $"{vehicleId} green channel must lie on the side the model's starboard direction " +
                        "projects to");

                    Assert.That(separation, Is.GreaterThan(layout.hull.width * 0.2),
                        $"{vehicleId} tracks must be separated across the hull");
                    Assert.That(layout.tracks.bandSeparationMetres,
                        Is.EqualTo(separation / layout.tracks.outputPixelsPerMeter).Within(0.01),
                        $"{vehicleId} recorded metres must follow its own pixel measurement");
                }
                finally
                {
                    Object.DestroyImmediate(mask);
                }
            }
        }

        /// <summary>
        /// The blue channel carries how far a masked pixel sits from its own band edge, and the shader
        /// multiplies the moving pattern by it. Without that weight a wide fallback band ends in a
        /// straight edge and reads as a painted rectangle instead of a track.
        /// </summary>
        [Test]
        public void EveryTrackMask_WeightsItsBandsLaterally()
        {
            foreach (string directory in VehicleDirectories())
            {
                string vehicleId = Path.GetFileName(directory);
                TrackLayoutJsonProbe layout = ReadLayout(directory, vehicleId);
                Texture2D mask = ReadTexture(Path.Combine(directory, layout.tracks.maskFile));
                try
                {
                    Color32[] pixels = mask.GetPixels32();
                    int masked = 0;
                    int brightest = 0;
                    int dimmest = 255;
                    double sum = 0.0;
                    for (int index = 0; index < pixels.Length; index++)
                    {
                        Color32 pixel = pixels[index];
                        if (pixel.a < MaskAlphaCutoff)
                        {
                            continue;
                        }

                        masked++;
                        sum += pixel.b;
                        brightest = Mathf.Max(brightest, pixel.b);
                        dimmest = Mathf.Min(dimmest, pixel.b);
                    }

                    Assert.That(masked, Is.GreaterThan(0), $"{vehicleId} mask must mark something");
                    Assert.That(brightest, Is.EqualTo(255), $"{vehicleId} band must have a solid core");
                    Assert.That(dimmest, Is.LessThanOrEqualTo(200),
                        $"{vehicleId} band must fade towards its own edge");
                    Assert.That(sum / masked, Is.GreaterThan(0.4 * 255.0),
                        $"{vehicleId} lateral weight must not swallow the band");
                }
                finally
                {
                    Object.DestroyImmediate(mask);
                }
            }
        }

        private static Texture2D ReadTexture(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.That(ImageConversion.LoadImage(texture, bytes), Is.True, $"Could not decode {path}");
            return texture;
        }

        private static TrackLayoutJsonProbe ReadLayout(string directory, string vehicleId)
        {
            string path = Path.Combine(directory, vehicleId + "_layout.json");
            return JsonUtility.FromJson<TrackLayoutJsonProbe>(File.ReadAllText(path));
        }

        [System.Serializable]
        private sealed class TrackLayoutJsonProbe
        {
            public int schemaVersion;
            public HullJson hull;
            public TracksJson tracks;
        }

        [System.Serializable]
        private sealed class HullJson
        {
            public float width;
            public float height;
        }

        [System.Serializable]
        private sealed class TracksJson
        {
            public int schemaVersion;
            public string source;
            public string maskFile;
            public float outputPixelsPerMeter;
            public string sideSource;
            public float forwardSignV;
            public float bandSeparationPixels;
            public float bandSeparationMetres;
            public float bandWidthPixels;
            public float bandWidthMetres;
            public float starboardImageXPixels;
            public float treadPitchMetres;
            public string fallbackReason;
        }
    }
}
