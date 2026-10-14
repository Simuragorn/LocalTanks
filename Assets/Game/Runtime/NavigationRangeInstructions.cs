using UnityEngine;

namespace LocalTanks
{
    public sealed class NavigationRangeInstructions : MonoBehaviour
    {
        [SerializeField] private DestructibleObstacle wall;

        public void Configure(DestructibleObstacle destructibleWall)
        {
            wall = destructibleWall;
        }

        private void Start()
        {
            // This scene exists specifically to inspect pathfinding, so it opts back into the diagnostic
            // that normal battle maps now keep hidden until F2 is pressed.
            FindAnyObjectByType<NavigationDebugOverlay>()?.SetVisible(true);
        }

        private void OnGUI()
        {
            string wallState = wall == null
                ? "нет данных"
                : wall.IsDestroyed
                    ? "проход открыт"
                    : $"HP стены: {wall.CurrentHitPoints}";
            GUI.Box(
                new Rect(12f, 12f, 330f, 82f),
                "Навигационный полигон\nW/S/A/D — игрок, ЛКМ — огонь по стене\nКолесо — зум, F2 — сетка\n" + wallState);
        }
    }
}
