using UnityEngine;
using UnityEngine.InputSystem;

namespace LocalTanks
{
    public sealed class NavigationDebugOverlay : MonoBehaviour
    {
        [SerializeField] private NavigationMap map;
        [SerializeField] private bool visible = true;

        public bool Visible => visible;

        public void Configure(NavigationMap navigationMap)
        {
            map = navigationMap;
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame)
            {
                visible = !visible;
            }

            if (!visible || map == null)
            {
                return;
            }

            DrawGrid();
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
            const float width = 250f;
            Rect button = new Rect(Screen.width - width - 12f, 12f, width, 28f);
            if (GUI.Button(button, visible ? "Скрыть навигацию (F2)" : "Показать навигацию (F2)"))
            {
                visible = !visible;
            }
        }

        private void DrawGrid()
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
