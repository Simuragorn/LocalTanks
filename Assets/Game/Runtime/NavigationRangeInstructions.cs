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
