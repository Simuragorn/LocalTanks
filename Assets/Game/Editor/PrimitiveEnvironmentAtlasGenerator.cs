using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LocalTanks.Editor
{
    public static class PrimitiveEnvironmentAtlasGenerator
    {
        private const int AtlasSize = 2048;
        private const int Columns = 4;
        private const int Rows = 4;

        public static void Generate(string assetPath, int expectedSpriteCount)
        {
            if (expectedSpriteCount != Columns * Rows)
            {
                throw new ArgumentException("Primitive environment atlas requires exactly sixteen sprites.");
            }

            PixelCanvas canvas = new PixelCanvas(AtlasSize, AtlasSize);
            int cellSize = AtlasSize / Columns;
            for (int index = 0; index < expectedSpriteCount; index++)
            {
                int column = index % Columns;
                int rowFromTop = index / Columns;
                int bottom = (Rows - 1 - rowFromTop) * cellSize;
                DrawPrimitive(index, new TilePainter(canvas, column * cellSize, bottom, cellSize));
            }

            Texture2D texture = canvas.CreateTexture();
            byte[] png = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);
            string absolutePath = Path.GetFullPath(assetPath);
            bool changed = !File.Exists(absolutePath) || !File.ReadAllBytes(absolutePath).SequenceEqual(png);
            if (changed)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? string.Empty);
                File.WriteAllBytes(absolutePath, png);
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static void DrawPrimitive(int index, TilePainter painter)
        {
            switch (index)
            {
                case 0: DrawBroadleafTrees(painter); break;
                case 1: DrawPineTrees(painter); break;
                case 2: DrawBushCluster(painter); break;
                case 3: DrawHedge(painter); break;
                case 4: DrawFarmhouse(painter); break;
                case 5: DrawWorkshop(painter); break;
                case 6: DrawRuinedHouse(painter); break;
                case 7: DrawBarn(painter); break;
                case 8: DrawStoneWall(painter); break;
                case 9: DrawWoodenFence(painter); break;
                case 10: DrawRubble(painter); break;
                case 11: DrawCrater(painter); break;
                case 12: DrawDirtPatch(painter); break;
                case 13: DrawReeds(painter); break;
                case 14: DrawMeadowGrass(painter); break;
                case 15: DrawBoulders(painter); break;
            }
        }

        private static void DrawBroadleafTrees(TilePainter p)
        {
            p.Ellipse(0.51f, 0.43f, 0.74f, 0.52f, C(10, 20, 13, 90));
            DrawCrown(p, 0.28f, 0.52f, 0.18f, C(38, 74, 38), C(72, 119, 55));
            DrawCrown(p, 0.48f, 0.64f, 0.22f, C(34, 68, 34), C(76, 128, 58));
            DrawCrown(p, 0.70f, 0.53f, 0.18f, C(31, 64, 33), C(66, 111, 50));
            DrawCrown(p, 0.42f, 0.37f, 0.19f, C(35, 70, 34), C(69, 116, 52));
            DrawCrown(p, 0.63f, 0.34f, 0.17f, C(30, 61, 31), C(60, 105, 47));
            p.Circle(0.50f, 0.48f, 0.055f, C(91, 67, 39));
            p.Circle(0.43f, 0.70f, 0.035f, C(119, 158, 78));
            p.Circle(0.68f, 0.58f, 0.030f, C(112, 151, 71));
        }

        private static void DrawPineTrees(TilePainter p)
        {
            p.Ellipse(0.52f, 0.40f, 0.70f, 0.47f, C(9, 18, 14, 90));
            DrawPine(p, 0.32f, 0.47f, 0.22f);
            DrawPine(p, 0.56f, 0.59f, 0.27f);
            DrawPine(p, 0.70f, 0.36f, 0.20f);
        }

        private static void DrawBushCluster(TilePainter p)
        {
            p.Ellipse(0.50f, 0.44f, 0.70f, 0.42f, C(9, 20, 11, 80));
            Vector2[] centers =
            {
                new Vector2(0.25f, 0.48f), new Vector2(0.38f, 0.58f),
                new Vector2(0.52f, 0.50f), new Vector2(0.67f, 0.57f),
                new Vector2(0.75f, 0.43f), new Vector2(0.57f, 0.36f),
                new Vector2(0.36f, 0.35f)
            };
            for (int i = 0; i < centers.Length; i++)
            {
                float radius = 0.105f + (i % 3) * 0.012f;
                p.Circle(centers[i].x, centers[i].y, radius + 0.018f, C(35, 70, 32));
                p.Circle(centers[i].x, centers[i].y, radius, i % 2 == 0 ? C(82, 132, 57) : C(69, 116, 49));
                p.Circle(centers[i].x - 0.025f, centers[i].y + 0.025f, 0.022f, C(132, 166, 78));
            }
        }

        private static void DrawHedge(TilePainter p)
        {
            p.Ellipse(0.50f, 0.43f, 0.83f, 0.34f, C(9, 19, 10, 80));
            p.Rect(0.50f, 0.53f, 0.70f, 0.25f, C(35, 72, 31));
            for (int i = 0; i < 7; i++)
            {
                float x = 0.20f + i * 0.10f;
                p.Circle(x, 0.53f + (i % 2) * 0.025f, 0.105f, C(65, 115, 45));
                p.Circle(x - 0.02f, 0.57f, 0.025f, C(120, 156, 70));
            }
        }

        private static void DrawFarmhouse(TilePainter p)
        {
            DrawBuildingShadow(p, 0.52f, 0.45f, 0.76f, 0.58f);
            p.Rect(0.50f, 0.53f, 0.76f, 0.58f, C(54, 45, 39));
            p.Rect(0.50f, 0.53f, 0.70f, 0.52f, C(150, 76, 54));
            p.Line(0.15f, 0.53f, 0.85f, 0.53f, 0.020f, C(92, 45, 36));
            p.Line(0.50f, 0.28f, 0.50f, 0.78f, 0.014f, C(188, 104, 68));
            p.Rect(0.70f, 0.68f, 0.09f, 0.09f, C(57, 51, 47));
            p.Rect(0.70f, 0.68f, 0.045f, 0.045f, C(23, 25, 24));
        }

        private static void DrawWorkshop(TilePainter p)
        {
            DrawBuildingShadow(p, 0.52f, 0.44f, 0.80f, 0.50f);
            p.Rect(0.50f, 0.54f, 0.80f, 0.50f, C(47, 51, 52));
            p.Rect(0.50f, 0.54f, 0.74f, 0.44f, C(103, 116, 112));
            for (int i = 0; i < 6; i++)
            {
                float x = 0.21f + i * 0.115f;
                p.Line(x, 0.33f, x, 0.75f, 0.012f, C(70, 82, 80));
            }
            p.Rect(0.50f, 0.54f, 0.25f, 0.08f, C(144, 169, 164));
            p.Rect(0.50f, 0.54f, 0.20f, 0.035f, C(61, 78, 79));
        }

        private static void DrawRuinedHouse(TilePainter p)
        {
            p.Polygon(new[]
            {
                new Vector2(0.15f, 0.29f), new Vector2(0.25f, 0.20f), new Vector2(0.78f, 0.24f),
                new Vector2(0.87f, 0.46f), new Vector2(0.77f, 0.79f), new Vector2(0.56f, 0.75f),
                new Vector2(0.48f, 0.86f), new Vector2(0.19f, 0.73f)
            }, C(20, 19, 18, 90));
            p.Polygon(new[]
            {
                new Vector2(0.17f, 0.34f), new Vector2(0.28f, 0.24f), new Vector2(0.75f, 0.27f),
                new Vector2(0.82f, 0.48f), new Vector2(0.73f, 0.75f), new Vector2(0.53f, 0.70f),
                new Vector2(0.45f, 0.80f), new Vector2(0.22f, 0.69f)
            }, C(113, 91, 70));
            p.Polygon(new[]
            {
                new Vector2(0.46f, 0.32f), new Vector2(0.70f, 0.31f),
                new Vector2(0.73f, 0.50f), new Vector2(0.55f, 0.58f)
            }, C(0, 0, 0, 0));
            p.Line(0.22f, 0.35f, 0.65f, 0.70f, 0.026f, C(62, 48, 38));
            p.Line(0.24f, 0.70f, 0.60f, 0.30f, 0.022f, C(68, 51, 39));
            for (int i = 0; i < 7; i++)
            {
                p.Rect(0.18f + i * 0.10f, 0.22f + (i % 2) * 0.05f, 0.065f, 0.045f,
                    i % 2 == 0 ? C(151, 91, 62) : C(139, 128, 107));
            }
        }

        private static void DrawBarn(TilePainter p)
        {
            DrawBuildingShadow(p, 0.52f, 0.44f, 0.80f, 0.56f);
            p.Rect(0.50f, 0.54f, 0.80f, 0.56f, C(48, 36, 29));
            p.Rect(0.50f, 0.54f, 0.74f, 0.50f, C(126, 75, 47));
            p.Line(0.13f, 0.54f, 0.87f, 0.54f, 0.020f, C(70, 42, 30));
            p.Line(0.50f, 0.29f, 0.50f, 0.79f, 0.020f, C(172, 105, 62));
            p.Line(0.18f, 0.34f, 0.42f, 0.74f, 0.018f, C(84, 50, 34));
            p.Line(0.18f, 0.74f, 0.42f, 0.34f, 0.018f, C(84, 50, 34));
            p.Line(0.58f, 0.34f, 0.82f, 0.74f, 0.018f, C(84, 50, 34));
            p.Line(0.58f, 0.74f, 0.82f, 0.34f, 0.018f, C(84, 50, 34));
        }

        private static void DrawStoneWall(TilePainter p)
        {
            p.Rect(0.51f, 0.43f, 0.88f, 0.25f, C(16, 18, 18, 90));
            p.Rect(0.50f, 0.54f, 0.88f, 0.25f, C(52, 57, 56));
            p.Rect(0.50f, 0.54f, 0.84f, 0.20f, C(130, 137, 128));
            for (int i = 1; i < 6; i++)
            {
                float x = 0.08f + i * 0.14f;
                p.Line(x, 0.44f, x, 0.64f, 0.010f, C(77, 82, 79));
            }
            p.Line(0.08f, 0.54f, 0.92f, 0.54f, 0.010f, C(88, 94, 89));
        }

        private static void DrawWoodenFence(TilePainter p)
        {
            p.Line(0.10f, 0.43f, 0.90f, 0.43f, 0.07f, C(24, 20, 15, 80));
            p.Line(0.10f, 0.48f, 0.90f, 0.48f, 0.034f, C(116, 76, 43));
            p.Line(0.10f, 0.61f, 0.90f, 0.61f, 0.034f, C(137, 91, 50));
            for (int i = 0; i < 6; i++)
            {
                float x = 0.12f + i * 0.15f;
                p.Rect(x, 0.54f, 0.055f, 0.30f, C(75, 48, 30));
                p.Rect(x, 0.56f, 0.035f, 0.27f, C(155, 101, 55));
            }
        }

        private static void DrawRubble(TilePainter p)
        {
            p.Polygon(new[]
            {
                new Vector2(0.13f, 0.36f), new Vector2(0.26f, 0.23f), new Vector2(0.69f, 0.20f),
                new Vector2(0.88f, 0.39f), new Vector2(0.80f, 0.68f), new Vector2(0.55f, 0.79f),
                new Vector2(0.27f, 0.71f)
            }, C(28, 23, 19, 90));
            Color32[] rubbleColors = { C(142, 76, 50), C(166, 101, 65), C(128, 124, 112), C(91, 88, 81) };
            for (int i = 0; i < 12; i++)
            {
                float x = 0.20f + ((i * 37) % 61) * 0.010f;
                float y = 0.29f + ((i * 23) % 39) * 0.010f;
                float size = 0.065f + (i % 3) * 0.018f;
                p.Rect(x, y, size, size * 0.72f, rubbleColors[i % rubbleColors.Length]);
            }
            p.Line(0.23f, 0.29f, 0.78f, 0.70f, 0.035f, C(211, 153, 62));
            p.Line(0.26f, 0.70f, 0.77f, 0.28f, 0.035f, C(211, 153, 62));
        }

        private static void DrawCrater(TilePainter p)
        {
            p.Ellipse(0.50f, 0.47f, 0.82f, 0.62f, C(20, 17, 14, 85));
            p.Ellipse(0.50f, 0.52f, 0.82f, 0.62f, C(105, 76, 50));
            p.Ellipse(0.50f, 0.52f, 0.62f, 0.44f, C(63, 48, 37));
            p.Ellipse(0.50f, 0.52f, 0.40f, 0.25f, C(38, 33, 29));
            for (int i = 0; i < 10; i++)
            {
                float angle = i * Mathf.PI * 2f / 10f;
                p.Circle(0.50f + Mathf.Cos(angle) * 0.34f, 0.52f + Mathf.Sin(angle) * 0.25f,
                    0.025f + (i % 2) * 0.009f, C(139, 105, 69));
            }
        }

        private static void DrawDirtPatch(TilePainter p)
        {
            p.Polygon(new[]
            {
                new Vector2(0.10f, 0.43f), new Vector2(0.24f, 0.23f), new Vector2(0.62f, 0.18f),
                new Vector2(0.88f, 0.36f), new Vector2(0.82f, 0.66f), new Vector2(0.57f, 0.79f),
                new Vector2(0.25f, 0.72f)
            }, C(130, 100, 64, 185));
            for (int i = 0; i < 9; i++)
            {
                float x = 0.20f + ((i * 29) % 59) * 0.010f;
                float y = 0.30f + ((i * 17) % 37) * 0.010f;
                p.Circle(x, y, 0.018f + (i % 3) * 0.007f, C(80, 62, 44, 205));
            }
        }

        private static void DrawReeds(TilePainter p)
        {
            p.Ellipse(0.50f, 0.37f, 0.70f, 0.22f, C(21, 46, 29, 90));
            for (int i = 0; i < 13; i++)
            {
                float x = 0.18f + i * 0.052f;
                float top = 0.52f + (i % 4) * 0.075f;
                p.Line(x, 0.30f, x + ((i % 2 == 0) ? -0.035f : 0.035f), top, 0.014f, C(90, 125, 55));
                if (i % 3 == 0)
                {
                    p.Line(x - 0.02f, top, x + 0.02f, top, 0.026f, C(116, 79, 39));
                }
            }
        }

        private static void DrawMeadowGrass(TilePainter p)
        {
            for (int cluster = 0; cluster < 6; cluster++)
            {
                float x = 0.18f + (cluster % 3) * 0.31f;
                float y = 0.31f + (cluster / 3) * 0.31f;
                p.Line(x, y, x - 0.06f, y + 0.18f, 0.016f, C(118, 145, 67, 210));
                p.Line(x, y, x, y + 0.21f, 0.016f, C(140, 158, 74, 220));
                p.Line(x, y, x + 0.07f, y + 0.16f, 0.016f, C(101, 130, 59, 210));
            }
        }

        private static void DrawBoulders(TilePainter p)
        {
            p.Ellipse(0.51f, 0.39f, 0.72f, 0.36f, C(14, 17, 16, 90));
            DrawBoulder(p, 0.32f, 0.48f, 0.18f);
            DrawBoulder(p, 0.55f, 0.57f, 0.23f);
            DrawBoulder(p, 0.72f, 0.38f, 0.16f);
            DrawBoulder(p, 0.43f, 0.32f, 0.13f);
        }

        private static void DrawCrown(TilePainter p, float x, float y, float radius, Color32 outline, Color32 fill)
        {
            p.Circle(x, y, radius, outline);
            p.Circle(x - radius * 0.06f, y + radius * 0.07f, radius * 0.82f, fill);
        }

        private static void DrawPine(TilePainter p, float x, float y, float radius)
        {
            p.Star(x, y, radius, radius * 0.55f, 12, C(24, 52, 38));
            p.Star(x - 0.01f, y + 0.015f, radius * 0.78f, radius * 0.39f, 12, C(47, 91, 57));
            p.Circle(x, y, radius * 0.16f, C(100, 76, 43));
        }

        private static void DrawBuildingShadow(TilePainter p, float x, float y, float width, float height)
        {
            p.Rect(x + 0.025f, y - 0.07f, width + 0.04f, height + 0.04f, C(10, 12, 11, 95));
        }

        private static void DrawBoulder(TilePainter p, float x, float y, float radius)
        {
            p.Circle(x, y, radius, C(57, 62, 59));
            p.Circle(x - radius * 0.08f, y + radius * 0.10f, radius * 0.82f, C(126, 132, 123));
            p.Circle(x - radius * 0.28f, y + radius * 0.31f, radius * 0.17f, C(178, 181, 165));
        }

        private static Color32 C(byte red, byte green, byte blue, byte alpha = 255)
        {
            return new Color32(red, green, blue, alpha);
        }

        private sealed class TilePainter
        {
            private readonly PixelCanvas canvas;
            private readonly int left;
            private readonly int bottom;
            private readonly int size;

            public TilePainter(PixelCanvas canvas, int left, int bottom, int size)
            {
                this.canvas = canvas;
                this.left = left;
                this.bottom = bottom;
                this.size = size;
            }

            public void Circle(float x, float y, float radius, Color32 color)
            {
                canvas.FillEllipse(X(x), Y(y), radius * size, radius * size, color);
            }

            public void Ellipse(float x, float y, float width, float height, Color32 color)
            {
                canvas.FillEllipse(X(x), Y(y), width * size * 0.5f, height * size * 0.5f, color);
            }

            public void Rect(float x, float y, float width, float height, Color32 color)
            {
                canvas.FillRect(
                    Mathf.RoundToInt(X(x - width * 0.5f)),
                    Mathf.RoundToInt(Y(y - height * 0.5f)),
                    Mathf.RoundToInt(X(x + width * 0.5f)),
                    Mathf.RoundToInt(Y(y + height * 0.5f)),
                    color);
            }

            public void Line(float x0, float y0, float x1, float y1, float thickness, Color32 color)
            {
                canvas.DrawLine(X(x0), Y(y0), X(x1), Y(y1), thickness * size, color);
            }

            public void Polygon(Vector2[] points, Color32 color)
            {
                canvas.FillPolygon(points.Select(point => new Vector2(X(point.x), Y(point.y))).ToArray(), color);
            }

            public void Star(float x, float y, float outerRadius, float innerRadius, int points, Color32 color)
            {
                Vector2[] vertices = new Vector2[points * 2];
                for (int index = 0; index < vertices.Length; index++)
                {
                    float angle = Mathf.PI * 2f * index / vertices.Length + Mathf.PI * 0.5f;
                    float radius = index % 2 == 0 ? outerRadius : innerRadius;
                    vertices[index] = new Vector2(x + Mathf.Cos(angle) * radius, y + Mathf.Sin(angle) * radius);
                }
                Polygon(vertices, color);
            }

            private float X(float normalized) => left + normalized * size;
            private float Y(float normalized) => bottom + normalized * size;
        }

        private sealed class PixelCanvas
        {
            private readonly int width;
            private readonly int height;
            private readonly Color32[] pixels;

            public PixelCanvas(int width, int height)
            {
                this.width = width;
                this.height = height;
                pixels = new Color32[width * height];
            }

            public void FillRect(int minX, int minY, int maxX, int maxY, Color32 color)
            {
                minX = Mathf.Clamp(minX, 0, width - 1);
                maxX = Mathf.Clamp(maxX, 0, width - 1);
                minY = Mathf.Clamp(minY, 0, height - 1);
                maxY = Mathf.Clamp(maxY, 0, height - 1);
                for (int y = minY; y <= maxY; y++)
                {
                    int row = y * width;
                    for (int x = minX; x <= maxX; x++)
                    {
                        pixels[row + x] = color;
                    }
                }
            }

            public void FillEllipse(float centerX, float centerY, float radiusX, float radiusY, Color32 color)
            {
                int minX = Mathf.Max(0, Mathf.FloorToInt(centerX - radiusX));
                int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(centerX + radiusX));
                int minY = Mathf.Max(0, Mathf.FloorToInt(centerY - radiusY));
                int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(centerY + radiusY));
                float inverseX = 1f / Mathf.Max(0.001f, radiusX * radiusX);
                float inverseY = 1f / Mathf.Max(0.001f, radiusY * radiusY);
                for (int y = minY; y <= maxY; y++)
                {
                    float dy = y + 0.5f - centerY;
                    int row = y * width;
                    for (int x = minX; x <= maxX; x++)
                    {
                        float dx = x + 0.5f - centerX;
                        if (dx * dx * inverseX + dy * dy * inverseY <= 1f)
                        {
                            pixels[row + x] = color;
                        }
                    }
                }
            }

            public void DrawLine(float x0, float y0, float x1, float y1, float thickness, Color32 color)
            {
                float distance = Vector2.Distance(new Vector2(x0, y0), new Vector2(x1, y1));
                int steps = Mathf.Max(1, Mathf.CeilToInt(distance));
                float radius = Mathf.Max(0.75f, thickness * 0.5f);
                for (int step = 0; step <= steps; step++)
                {
                    float amount = (float)step / steps;
                    FillEllipse(Mathf.Lerp(x0, x1, amount), Mathf.Lerp(y0, y1, amount), radius, radius, color);
                }
            }

            public void FillPolygon(Vector2[] vertices, Color32 color)
            {
                if (vertices == null || vertices.Length < 3)
                {
                    return;
                }

                int minX = Mathf.Max(0, Mathf.FloorToInt(vertices.Min(point => point.x)));
                int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(vertices.Max(point => point.x)));
                int minY = Mathf.Max(0, Mathf.FloorToInt(vertices.Min(point => point.y)));
                int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(vertices.Max(point => point.y)));
                for (int y = minY; y <= maxY; y++)
                {
                    int row = y * width;
                    for (int x = minX; x <= maxX; x++)
                    {
                        if (Contains(vertices, x + 0.5f, y + 0.5f))
                        {
                            pixels[row + x] = color;
                        }
                    }
                }
            }

            public Texture2D CreateTexture()
            {
                Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                return texture;
            }

            private static bool Contains(Vector2[] vertices, float x, float y)
            {
                bool inside = false;
                int previous = vertices.Length - 1;
                for (int current = 0; current < vertices.Length; current++)
                {
                    Vector2 a = vertices[current];
                    Vector2 b = vertices[previous];
                    bool crosses = (a.y > y) != (b.y > y) &&
                                   x < (b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x;
                    if (crosses)
                    {
                        inside = !inside;
                    }
                    previous = current;
                }
                return inside;
            }
        }
    }
}
