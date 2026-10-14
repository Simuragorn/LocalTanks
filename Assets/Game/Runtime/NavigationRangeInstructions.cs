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
            Rect panel = LocalTanksGuiTheme.ClampToScreen(new Rect(12f, 12f, 360f, 108f));
            ModalUiState.SetPointerRegion(this, panel);
            LocalTanksGuiTheme.DrawPanel(panel, false);
            GUI.Label(new Rect(panel.x + 14f, panel.y + 10f, panel.width - 28f, 22f),
                "Навигационный полигон", LocalTanksGuiTheme.SectionTitle);
            GUI.Label(new Rect(panel.x + 14f, panel.y + 38f, panel.width - 28f, 60f),
                "W/S/A/D — движение   ·   ЛКМ — огонь по стене\n" +
                $"Колесо — зум   ·   F2 — сетка   ·   {wallState}", LocalTanksGuiTheme.Caption);
        }

        private void OnDisable()
        {
            ModalUiState.ClearPointerRegion(this);
        }
    }
}
