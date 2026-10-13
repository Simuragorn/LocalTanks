using NUnit.Framework;
using UnityEngine;

namespace LocalTanks.Tests
{
    /// <summary>
    /// F2 draws the navigation map, and it used to draw it only with `Debug.DrawLine`, which the game window
    /// never shows: the key changed a flag and nothing else. The Game view now gets a texture built from the
    /// same map, so these tests pin that texture's size, its colours and its rebuild rule without needing a
    /// graphics device.
    /// </summary>
    public sealed class NavigationDebugOverlayTests
    {
        private const int Width = 4;
        private const int Height = 3;

        [Test]
        public void GridTextureMatchesTheMapAndItsColours()
        {
            GameObject host = new GameObject("NavigationOverlayHost");
            GameObject mapObject = new GameObject("NavigationOverlayMap");
            try
            {
                NavigationMap map = BuildMap(mapObject);
                NavigationDebugOverlay overlay = host.AddComponent<NavigationDebugOverlay>();
                overlay.Configure(map);

                Assert.That(overlay.Visible, Is.True, "The navigation view starts visible.");

                Texture2D texture = overlay.BuildGridTexture();

                Assert.That(texture, Is.Not.Null);
                Assert.That(texture.width, Is.EqualTo(Width));
                Assert.That(texture.height, Is.EqualTo(Height));
                Assert.That(texture.filterMode, Is.EqualTo(FilterMode.Point),
                    "One pixel per cell has to stay crisp, otherwise the grid turns into a blur.");

                AssertPixel(texture, new Vector2Int(0, 0), new Color32(196, 54, 48, 255), "blocked cell");
                AssertPixel(texture, new Vector2Int(1, 0), new Color32(232, 230, 214, 255), "road");
                AssertPixel(texture, new Vector2Int(2, 0), new Color32(148, 96, 40, 255), "mud");
                AssertPixel(texture, new Vector2Int(3, 0), new Color32(56, 104, 196, 255), "shallow water");
                AssertPixel(texture, new Vector2Int(0, 1), new Color32(72, 148, 72, 255), "grass");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(mapObject);
            }
        }

        [Test]
        public void GridTextureIsRebuiltOnlyWhenTheMapChanges()
        {
            GameObject host = new GameObject("NavigationOverlayHost");
            GameObject mapObject = new GameObject("NavigationOverlayMap");
            try
            {
                NavigationMap map = BuildMap(mapObject);
                NavigationDebugOverlay overlay = host.AddComponent<NavigationDebugOverlay>();
                overlay.Configure(map);

                Texture2D first = overlay.BuildGridTexture();
                Assert.That(overlay.BuildGridTexture(), Is.SameAs(first),
                    "An unchanged map must not rebuild the picture every frame.");

                map.SetBlocked(new Vector2Int(2, 2), true);
                Texture2D second = overlay.BuildGridTexture();

                Assert.That(second, Is.SameAs(first), "The texture is reused, only its pixels change.");
                AssertPixel(second, new Vector2Int(2, 2), new Color32(196, 54, 48, 255), "newly blocked cell");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(mapObject);
            }
        }

        [Test]
        public void ToggleAndSetVisible_AgreeWithEachOther()
        {
            GameObject host = new GameObject("NavigationOverlayHost");
            GameObject mapObject = new GameObject("NavigationOverlayMap");
            try
            {
                NavigationDebugOverlay overlay = host.AddComponent<NavigationDebugOverlay>();
                overlay.Configure(BuildMap(mapObject));

                overlay.SetVisible(false);
                Assert.That(overlay.Visible, Is.False);
                overlay.Toggle();
                Assert.That(overlay.Visible, Is.True);
                overlay.Toggle();
                Assert.That(overlay.Visible, Is.False, "F2, the button and a test all go through the same state.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(mapObject);
            }
        }

        private static void AssertPixel(Texture2D texture, Vector2Int cell, Color32 expected, string what)
        {
            Color32 actual = texture.GetPixel(cell.x, cell.y);
            Assert.That(actual.r, Is.EqualTo(expected.r), $"{what} red channel at {cell}");
            Assert.That(actual.g, Is.EqualTo(expected.g), $"{what} green channel at {cell}");
            Assert.That(actual.b, Is.EqualTo(expected.b), $"{what} blue channel at {cell}");
        }

        private static NavigationMap BuildMap(GameObject mapObject)
        {
            int cells = Width * Height;
            TerrainKind[] terrain = new TerrainKind[cells];
            float[] costs = new float[cells];
            float[] speeds = new float[cells];
            float[] accelerations = new float[cells];
            bool[] blocked = new bool[cells];
            for (int index = 0; index < cells; index++)
            {
                terrain[index] = TerrainKind.Grass;
                costs[index] = 1f;
                speeds[index] = 1f;
                accelerations[index] = 1f;
            }

            terrain[1] = TerrainKind.Road;
            terrain[2] = TerrainKind.Mud;
            terrain[3] = TerrainKind.ShallowWater;
            blocked[0] = true;

            NavigationMap map = mapObject.AddComponent<NavigationMap>();
            map.Configure(Width, Height, 1f, Vector2.zero, terrain, costs, speeds, accelerations, blocked);
            return map;
        }
    }
}
