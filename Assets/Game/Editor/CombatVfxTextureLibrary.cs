using UnityEngine;

namespace LocalTanks.Editor
{
    /// <summary>
    /// The neutral textures every combat effect is assembled from.
    /// </summary>
    /// <remarks>
    /// They are drawn, not extracted: no World of Tanks material, shader or particle format is involved,
    /// so nothing here has to be re-cut when the local client changes and nothing derived from WoT enters
    /// the repository. Eleven textures cover the whole effect set because one smoke puff serves the shot,
    /// the damage trail and the wreck with a different colour, size and lifetime.
    /// <para>
    /// Drawing is deterministic: all variation comes from a hash of the pixel coordinate and a per-texture
    /// seed. Rebuilding the library therefore produces byte-identical PNG files, which is what makes the
    /// committed result reviewable instead of a new random image every run.
    /// </para>
    /// </remarks>
    internal static class VfxTextureLibrary
    {
        public const int Size = 128;

        public const string SmokePuff = "smoke_puff";
        public const string SmokeWisp = "smoke_wisp";
        public const string DustPuff = "dust_puff";
        public const string ExhaustSmoke = "exhaust_wisp";
        public const string Spark = "spark";
        public const string FlashStar = "flash_star";
        public const string FlashCore = "flash_core";
        public const string DirtChunk = "dirt_chunk";
        public const string WaterDrop = "water_drop";
        public const string Flame = "flame";
        public const string Ember = "ember";
        public const string TrackMarkLight = "track_mark_light";
        public const string TrackMarkMedium = "track_mark_medium";
        public const string TrackMarkHeavy = "track_mark_heavy";

        public static readonly string[] All =
        {
            SmokePuff, SmokeWisp, DustPuff, ExhaustSmoke, Spark,
            FlashStar, FlashCore, DirtChunk, WaterDrop, Flame, Ember,
            TrackMarkLight, TrackMarkMedium, TrackMarkHeavy
        };

        /// <summary>True for the print patterns, which are imported as sprites rather than textures.</summary>
        public static bool IsTrackMark(string textureName)
        {
            return textureName == TrackMarkLight || textureName == TrackMarkMedium || textureName == TrackMarkHeavy;
        }

        /// <summary>Draws one texture. Every entry of <see cref="All"/> has a case here.</summary>
        public static Texture2D Create(string textureName)
        {
            VfxPixelCanvas canvas = new VfxPixelCanvas(Size);
            switch (textureName)
            {
                case SmokePuff: DrawSmokePuff(canvas); break;
                case SmokeWisp: DrawSmokeWisp(canvas); break;
                case DustPuff: DrawDustPuff(canvas); break;
                case ExhaustSmoke: DrawExhaustWisp(canvas); break;
                case Spark: DrawSpark(canvas); break;
                case FlashStar: DrawFlashStar(canvas); break;
                case FlashCore: DrawFlashCore(canvas); break;
                case DirtChunk: DrawDirtChunk(canvas); break;
                case WaterDrop: DrawWaterDrop(canvas); break;
                case Flame: DrawFlame(canvas); break;
                case Ember: DrawEmber(canvas); break;
                case TrackMarkLight: DrawTrackPrint(canvas, 5, 0.34f, 0.05f, 0.018f, 0.5f); break;
                case TrackMarkMedium: DrawTrackPrint(canvas, 6, 0.4f, 0.065f, 0.021f, 0.75f); break;
                case TrackMarkHeavy: DrawTrackPrint(canvas, 7, 0.44f, 0.075f, 0.024f, 1f); break;
                default: throw new System.InvalidOperationException($"Unknown VFX texture '{textureName}'.");
            }

            return canvas.CreateTexture();
        }

        private static void DrawSmokePuff(VfxPixelCanvas canvas)
        {
            // A puff is several overlapping billows, not one circle: a single soft disc reads as a spot.
            canvas.SoftCircle(0.5f, 0.47f, 0.44f, new Color(0.94f, 0.93f, 0.92f, 0.55f), 1.6f, 0.5f, 11);
            canvas.SoftCircle(0.35f, 0.58f, 0.27f, new Color(0.9f, 0.89f, 0.88f, 0.45f), 1.5f, 0.55f, 12);
            canvas.SoftCircle(0.66f, 0.55f, 0.25f, new Color(0.9f, 0.89f, 0.88f, 0.45f), 1.5f, 0.55f, 13);
            canvas.SoftCircle(0.5f, 0.32f, 0.26f, new Color(0.86f, 0.85f, 0.84f, 0.4f), 1.4f, 0.6f, 14);
            canvas.SoftCircle(0.5f, 0.5f, 0.16f, new Color(1f, 1f, 1f, 0.5f), 2f, 0.35f, 15);
        }

        private static void DrawSmokeWisp(VfxPixelCanvas canvas)
        {
            canvas.SoftEllipse(0.5f, 0.5f, 0.46f, 0.34f, new Color(0.94f, 0.93f, 0.92f, 0.5f), 1.5f, 0.5f, 21);
            canvas.SoftEllipse(0.4f, 0.62f, 0.26f, 0.2f, new Color(0.9f, 0.89f, 0.88f, 0.4f), 1.4f, 0.55f, 22);
            canvas.SoftEllipse(0.62f, 0.4f, 0.24f, 0.18f, new Color(0.9f, 0.89f, 0.88f, 0.4f), 1.4f, 0.55f, 23);
        }

        private static void DrawDustPuff(VfxPixelCanvas canvas)
        {
            canvas.SoftCircle(0.5f, 0.48f, 0.48f, new Color(0.95f, 0.94f, 0.9f, 0.34f), 2.1f, 0.6f, 31);
            canvas.SoftCircle(0.36f, 0.4f, 0.3f, new Color(0.93f, 0.92f, 0.88f, 0.3f), 2f, 0.65f, 32);
            canvas.SoftCircle(0.66f, 0.44f, 0.28f, new Color(0.93f, 0.92f, 0.88f, 0.3f), 2f, 0.65f, 33);
            canvas.SoftCircle(0.5f, 0.62f, 0.26f, new Color(0.9f, 0.89f, 0.85f, 0.25f), 1.9f, 0.7f, 34);
        }

        private static void DrawExhaustWisp(VfxPixelCanvas canvas)
        {
            canvas.SoftCircle(0.5f, 0.44f, 0.4f, new Color(0.8f, 0.79f, 0.78f, 0.3f), 2.2f, 0.7f, 41);
            canvas.SoftCircle(0.5f, 0.68f, 0.26f, new Color(0.76f, 0.75f, 0.74f, 0.22f), 2.4f, 0.8f, 42);
        }

        private static void DrawSpark(VfxPixelCanvas canvas)
        {
            // Sparks are drawn along +Y, which is the axis every effect prefab emits along.
            Color hot = new Color(1f, 0.95f, 0.72f, 1f);
            canvas.SoftEllipse(0.5f, 0.5f, 0.045f, 0.34f, hot, 0.8f, 0.25f, 51);
            canvas.SoftEllipse(0.5f, 0.5f, 0.09f, 0.2f, new Color(1f, 0.88f, 0.55f, 0.6f), 1.4f, 0.3f, 52);
            canvas.SoftCircle(0.5f, 0.5f, 0.08f, Color.white, 1f, 0f, 53);
        }

        private static void DrawFlashStar(VfxPixelCanvas canvas)
        {
            Color warm = new Color(1f, 0.92f, 0.66f, 0.85f);
            for (int index = 0; index < 6; index++)
            {
                float angle = Mathf.PI * 2f * index / 6f + 0.22f;
                float length = index % 2 == 0 ? 0.46f : 0.3f;
                canvas.SoftEllipse(
                    0.5f + Mathf.Cos(angle) * length * 0.5f,
                    0.5f + Mathf.Sin(angle) * length * 0.5f,
                    0.055f,
                    length * 0.5f,
                    warm,
                    1.1f,
                    0.2f,
                    61 + index,
                    angle - Mathf.PI * 0.5f);
            }

            canvas.SoftCircle(0.5f, 0.5f, 0.22f, new Color(1f, 0.97f, 0.85f, 0.9f), 1.2f, 0.2f, 70);
        }

        private static void DrawFlashCore(VfxPixelCanvas canvas)
        {
            canvas.SoftCircle(0.5f, 0.5f, 0.46f, new Color(1f, 0.85f, 0.5f, 0.55f), 2.2f, 0.25f, 81);
            canvas.SoftCircle(0.5f, 0.5f, 0.26f, new Color(1f, 0.95f, 0.78f, 0.85f), 1.4f, 0.15f, 82);
            canvas.SoftCircle(0.5f, 0.5f, 0.12f, Color.white, 1f, 0f, 83);
        }

        private static void DrawDirtChunk(VfxPixelCanvas canvas)
        {
            // A clod has a hard edge and a slightly lit top, because it is looked at from above.
            canvas.SoftCircle(0.5f, 0.5f, 0.4f, new Color(0.42f, 0.36f, 0.29f, 0.95f), 0.55f, 0.9f, 91);
            canvas.SoftCircle(0.46f, 0.58f, 0.24f, new Color(0.56f, 0.49f, 0.4f, 0.9f), 0.7f, 0.8f, 92);
            canvas.SoftCircle(0.6f, 0.42f, 0.14f, new Color(0.3f, 0.26f, 0.21f, 0.8f), 0.8f, 0.7f, 93);
        }

        private static void DrawWaterDrop(VfxPixelCanvas canvas)
        {
            canvas.SoftEllipse(0.5f, 0.5f, 0.3f, 0.42f, new Color(0.86f, 0.93f, 0.96f, 0.75f), 1.5f, 0.3f, 101);
            canvas.SoftEllipse(0.5f, 0.55f, 0.14f, 0.2f, new Color(1f, 1f, 1f, 0.85f), 1.2f, 0.15f, 102);
        }

        private static void DrawFlame(VfxPixelCanvas canvas)
        {
            // A tongue of flame: wide at the base, pointed at the top, with a hot core.
            for (int step = 0; step < 26; step++)
            {
                float t = step / 25f;
                float y = 0.12f + t * 0.76f;
                float width = Mathf.Lerp(0.3f, 0.03f, Mathf.Pow(t, 0.75f));
                Color colour = Color.Lerp(
                    new Color(1f, 0.55f, 0.16f, 0.85f),
                    new Color(1f, 0.85f, 0.4f, 0.5f),
                    t);
                canvas.SoftEllipse(0.5f, y, width * 0.5f, 0.075f, colour, 1.2f, 0.35f, 111 + step);
            }

            canvas.SoftEllipse(0.5f, 0.3f, 0.11f, 0.2f, new Color(1f, 0.93f, 0.62f, 0.8f), 1.3f, 0.2f, 131);
        }

        private static void DrawEmber(VfxPixelCanvas canvas)
        {
            canvas.SoftCircle(0.5f, 0.5f, 0.3f, new Color(1f, 0.72f, 0.28f, 0.35f), 2.2f, 0.2f, 141);
            canvas.SoftCircle(0.5f, 0.5f, 0.12f, new Color(1f, 0.88f, 0.55f, 0.95f), 1.2f, 0.1f, 142);
            canvas.SoftCircle(0.5f, 0.5f, 0.05f, Color.white, 1f, 0f, 143);
        }

        /// <summary>
        /// One print pattern, drawn along +Y so the emitter's Y scale is the length of the print.
        /// </summary>
        /// <remarks>
        /// A print is a groove, not a smear, and it reads as one only if it has a lit edge and a shaded
        /// one: the first version was a flat dark bar and looked like painted stripes on the ground. The
        /// lit rim sits on one side of the groove, the shadow on the other, and the tread bars are pressed
        /// into the middle. Three of these exist so a fleet does not share one stamped tread; the width on
        /// the ground comes from the machine's measured track gauge, not from the pattern.
        /// </remarks>
        private static void DrawTrackPrint(
            VfxPixelCanvas canvas,
            int bars,
            float bodyHalfWidth,
            float barHalfWidth,
            float barThickness,
            float relief)
        {
            Color groove = new Color(0.1f, 0.1f, 0.1f, 0.9f);
            Color litRim = new Color(0.62f, 0.6f, 0.56f, 0.55f * relief);
            Color shadowRim = new Color(0.02f, 0.02f, 0.02f, 0.7f * relief);

            // The groove itself, running the whole length of the sprite so consecutive prints join into a
            // continuous line instead of a row of beads, with a slightly wider soft edge around it.
            canvas.SoftEllipse(0.5f, 0.5f, bodyHalfWidth, 0.52f, groove, 0.4f, 0.3f, 151);
            canvas.SoftEllipse(0.5f, 0.5f, bodyHalfWidth * 1.12f, 0.5f, new Color(0.08f, 0.08f, 0.08f, 0.35f), 1.2f, 0.4f, 152);

            // Relief: a lit rim on the left of the groove and a shadow on the right, so the print reads as
            // pressed into the ground rather than painted on it.
            canvas.SoftEllipse(0.5f - bodyHalfWidth * 0.92f, 0.5f, bodyHalfWidth * 0.16f, 0.46f, litRim, 0.8f, 0.3f, 153);
            canvas.SoftEllipse(0.5f + bodyHalfWidth * 0.94f, 0.5f, bodyHalfWidth * 0.14f, 0.46f, shadowRim, 0.8f, 0.3f, 154);

            // Tread bars, slightly wider than the groove so the pattern bites into the edges.
            for (int bar = 0; bar < bars; bar++)
            {
                float spacing = 0.94f / bars;
                float y = 0.03f + spacing * (bar + 0.5f);
                canvas.SoftEllipse(0.5f, y, barHalfWidth * 1.06f, barThickness, new Color(0.03f, 0.03f, 0.03f, 0.85f), 0.6f, 0.25f, 160 + bar);
                canvas.SoftEllipse(0.5f, y + barThickness * 1.1f, barHalfWidth, barThickness * 0.5f,
                    new Color(0.5f, 0.48f, 0.45f, 0.28f * relief), 0.9f, 0.3f, 180 + bar);
            }
        }
    }

    /// <summary>
    /// A small alpha-blended pixel canvas with deterministic value noise.
    /// </summary>
    /// <remarks>
    /// The environment atlas painter writes opaque pixels and has no use for soft edges; particle
    /// textures are almost entirely gradient, so this canvas blends source-over and shapes every stamp
    /// with a noise-modulated falloff. Alpha blending is the reason it is a separate helper rather than a
    /// second caller of the atlas one.
    /// </remarks>
    internal sealed class VfxPixelCanvas
    {
        private readonly int size;
        private readonly Color[] pixels;

        public VfxPixelCanvas(int size)
        {
            this.size = Mathf.Max(1, size);
            pixels = new Color[this.size * this.size];
        }

        /// <summary>Soft radial stamp. <paramref name="power"/> shapes the falloff, noise breaks the circle.</summary>
        public void SoftCircle(
            float cx,
            float cy,
            float radius,
            Color color,
            float power,
            float noiseAmount,
            int seed)
        {
            SoftEllipse(cx, cy, radius, radius, color, power, noiseAmount, seed, 0f);
        }

        /// <summary>
        /// Soft ellipse, optionally rotated. A rotated stamp is what makes a spark a streak along the
        /// direction the prefab emits and keeps the whole effect in the plane of the map.
        /// </summary>
        public void SoftEllipse(
            float cx,
            float cy,
            float radiusX,
            float radiusY,
            Color color,
            float power,
            float noiseAmount,
            int seed,
            float rotationRadians = 0f)
        {
            int centerX = Mathf.RoundToInt(cx * size);
            int centerY = Mathf.RoundToInt(cy * size);
            int extent = Mathf.CeilToInt(Mathf.Max(radiusX, radiusY) * size) + 2;
            float cos = Mathf.Cos(-rotationRadians);
            float sin = Mathf.Sin(-rotationRadians);
            float rx = Mathf.Max(0.5f, radiusX * size);
            float ry = Mathf.Max(0.5f, radiusY * size);

            for (int y = centerY - extent; y <= centerY + extent; y++)
            {
                for (int x = centerX - extent; x <= centerX + extent; x++)
                {
                    if (x < 0 || y < 0 || x >= size || y >= size)
                    {
                        continue;
                    }

                    float dx = x + 0.5f - centerX;
                    float dy = y + 0.5f - centerY;
                    float localX = (dx * cos - dy * sin) / rx;
                    float localY = (dx * sin + dy * cos) / ry;
                    float distance = Mathf.Sqrt(localX * localX + localY * localY);
                    if (distance >= 1f)
                    {
                        continue;
                    }

                    float falloff = Mathf.Pow(1f - distance, Mathf.Max(0.05f, power));
                    if (noiseAmount > 0f)
                    {
                        float noise = VfxNoise.Fbm((x + 0.5f) / size * 6f, (y + 0.5f) / size * 6f, seed, 3);
                        falloff *= Mathf.Lerp(1f, noise, Mathf.Clamp01(noiseAmount));
                    }

                    if (falloff <= 0.002f)
                    {
                        continue;
                    }

                    Color source = color;
                    source.a = color.a * falloff;
                    Blend(x, y, source);
                }
            }
        }

        public Texture2D CreateTexture()
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        /// <summary>Source-over compositing, so overlapping stamps build up the way paint does.</summary>
        private void Blend(int x, int y, Color source)
        {
            int index = y * size + x;
            Color destination = pixels[index];
            float alpha = source.a + destination.a * (1f - source.a);
            if (alpha <= 0.0001f)
            {
                pixels[index] = new Color(0f, 0f, 0f, 0f);
                return;
            }

            float red = (source.r * source.a + destination.r * destination.a * (1f - source.a)) / alpha;
            float green = (source.g * source.a + destination.g * destination.a * (1f - source.a)) / alpha;
            float blue = (source.b * source.a + destination.b * destination.a * (1f - source.a)) / alpha;
            pixels[index] = new Color(red, green, blue, alpha);
        }
    }

    /// <summary>Deterministic value noise; the only source of variation in the drawn textures.</summary>
    internal static class VfxNoise
    {
        public static float Fbm(float x, float y, int seed, int octaves)
        {
            float sum = 0f;
            float amplitude = 1f;
            float total = 0f;
            for (int octave = 0; octave < Mathf.Max(1, octaves); octave++)
            {
                sum += Value(x, y, seed + octave * 977) * amplitude;
                total += amplitude;
                x *= 2f;
                y *= 2f;
                amplitude *= 0.5f;
            }

            return total <= 0f ? 0f : sum / total;
        }

        public static float Value(float x, float y, int seed)
        {
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            float fx = x - x0;
            float fy = y - y0;
            float sx = fx * fx * (3f - 2f * fx);
            float sy = fy * fy * (3f - 2f * fy);
            float top = Mathf.Lerp(Hash(x0, y0, seed), Hash(x0 + 1, y0, seed), sx);
            float bottom = Mathf.Lerp(Hash(x0, y0 + 1, seed), Hash(x0 + 1, y0 + 1, seed), sx);
            return Mathf.Lerp(top, bottom, sy);
        }

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int hash = seed * 374761393 + x * 668265263 + y * 1442695041;
                hash = (hash ^ (hash >> 13)) * 1274126177;
                hash ^= hash >> 16;
                return (hash & 0x7fffffff) / (float)0x7fffffff;
            }
        }
    }
}
