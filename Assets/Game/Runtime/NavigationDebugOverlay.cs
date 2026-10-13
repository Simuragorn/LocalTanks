using UnityEngine;
using UnityEngine.InputSystem;

namespace LocalTanks
{
    /// <summary>
    /// Developer view of the navigation map: which cells the pathfinder may use, what surface they are and
    /// where the agents are going.
    /// </summary>
    /// <remarks>
    /// The grid used to be drawn only with `Debug.DrawLine`, which appears in the Scene view and nowhere
    /// else: in the game window F2 changed a flag and nothing else, so the key looked broken. The Game view
    /// therefore gets a translucent picture of the same map (one texture pixel per cell, rebuilt only when
    /// the map version changes), while the Scene view keeps the grid lines and the live agent paths.
    /// </remarks>
    public sealed class NavigationDebugOverlay : MonoBehaviour
    {
        [SerializeField] private NavigationMap map;
        [SerializeField] private bool visible = true;

        private Texture2D gridTexture;
        private int gridMapVersion = -1;

        public bool Visible => visible;

        public void Configure(NavigationMap navigationMap)
        {
            map = navigationMap;
            gridMapVersion = -1;
        }

        /// <summary>Shows or hides the overlay. The key, the on-screen button and a test all use this.</summary>
        public void SetVisible(bool value)
        {
            visible = value;
        }

        public void Toggle()
        {
            visible = !visible;
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame)
            {
                Toggle();
            }

            if (!visible || map == null)
            {
                return;
            }

            DrawSceneGrid();
            foreach (NavigationAgent agent in FindObjectsByType<NavigationAgent>())
            {
                var path = agent.CurrentPath;
                for (int index = 1; index < path.Count; index++)
                {
                    Debug.DrawLine(path[index - 1], path[index], Color.cyan);
                }

                Debug.DrawLine(agent.transform.position, agent.Destination, Color.magenta);
            }
        }

        private void OnGUI()
        {
            if (BattleDiagnosticsOverlay.AnyModalVisible) return;

            const float width = 250f;
            Rect button = new Rect(Screen.width - width - 12f, 12f, width, 28f);
            if (GUI.Button(button, visible ? "Скрыть навигацию (F2)" : "Показать навигацию (F2)"))
            {
                Toggle();
            }

            if (!visible || map == null)
            {
                return;
            }

            DrawGameViewGrid();
        }

        /// <summary>
        /// Paints the map over the world it describes, so F2 is visible in the game window and not only in
        /// the Scene view. The picture is semi-transparent: it is a diagnostic layer, not a replacement for
        /// the battlefield.
        /// </summary>
        private void DrawGameViewGrid()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Texture2D texture = BuildGridTexture();
            if (texture == null)
            {
                return;
            }

            Vector2 worldMin = map.CellToWorld(Vector2Int.zero) - Vector2.one * (map.CellSize * 0.5f);
            Vector2 worldMax = map.CellToWorld(new Vector2Int(map.Width - 1, map.Height - 1)) + Vector2.one * (map.CellSize * 0.5f);
            Vector3 min = camera.WorldToScreenPoint(new Vector3(worldMin.x, worldMin.y, 0f));
            Vector3 max = camera.WorldToScreenPoint(new Vector3(worldMax.x, worldMax.y, 0f));
            if (min.z < 0f || max.z < 0f)
            {
                return;
            }

            Rect rect = new Rect(min.x, Screen.height - max.y, max.x - min.x, max.y - min.y);
            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.42f);
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true);
            GUI.color = previous;
        }

        /// <summary>
        /// One pixel per cell, rebuilt only when the map changes. Exposed so the colours are checked by a
        /// test instead of by looking at the screen.
        /// </summary>
        public Texture2D BuildGridTexture()
        {
            if (map == null)
            {
                return null;
            }

            if (gridTexture != null && gridMapVersion == map.Version)
            {
                return gridTexture;
            }

            if (gridTexture == null || gridTexture.width != map.Width || gridTexture.height != map.Height)
            {
                if (gridTexture != null)
                {
                    Destroy(gridTexture);
                }

                gridTexture = new Texture2D(map.Width, map.Height, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "NavigationGrid"
                };
            }

            Color32[] pixels = new Color32[map.Width * map.Height];
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    pixels[y * map.Width + x] = CellColor(new Vector2Int(x, y));
                }
            }

            gridTexture.SetPixels32(pixels);
            gridTexture.Apply(false, false);
            gridMapVersion = map.Version;
            return gridTexture;
        }

        private Color32 CellColor(Vector2Int cell)
        {
            if (map.IsBlocked(cell)) return new Color32(196, 54, 48, 255);
            switch (map.GetTerrain(cell))
            {
                case TerrainKind.Road: return new Color32(232, 230, 214, 255);
                case TerrainKind.Mud: return new Color32(148, 96, 40, 255);
                case TerrainKind.ShallowWater: return new Color32(56, 104, 196, 255);
                default: return new Color32(72, 148, 72, 255);
            }
        }

        /// <summary>The Scene view keeps its own drawing: an IMGUI layer cannot show anything there.</summary>
        private void DrawSceneGrid()
        {
            float size = map.CellSize;
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    Vector2Int cell = new Vector2Int(x, y);
                    Vector2 center = map.CellToWorld(cell);
                    Color color = GetCellColor(cell);
                    float half = size * 0.42f;
                    Debug.DrawLine(center + Vector2.left * half, center + Vector2.right * half, color);
                    Debug.DrawLine(center + Vector2.down * half, center + Vector2.up * half, color);
                }
            }
        }

        private Color GetCellColor(Vector2Int cell)
        {
            if (map.IsBlocked(cell)) return Color.red;
            switch (map.GetTerrain(cell))
            {
                case TerrainKind.Road: return Color.white;
                case TerrainKind.Mud: return new Color(0.65f, 0.35f, 0.1f);
                case TerrainKind.ShallowWater: return Color.blue;
                default: return new Color(0.25f, 0.65f, 0.25f);
            }
        }
    }
}
