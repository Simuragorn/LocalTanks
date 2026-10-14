using UnityEngine;

namespace LocalTanks
{
    public sealed class RiverCrossingInstructions : MonoBehaviour
    {
        private void OnGUI()
        {
            if (BattleDiagnosticsOverlay.AnyModalVisible)
            {
                ModalUiState.ClearPointerRegion(this);
                return;
            }

            Rect panel = LocalTanksGuiTheme.ClampToScreen(new Rect(12f, Screen.height - 70f, 420f, 58f));
            ModalUiState.SetPointerRegion(this, panel);
            LocalTanksGuiTheme.DrawPanel(panel, false);
            GUI.Label(new Rect(panel.x + 12f, panel.y + 8f, panel.width - 24f, 18f),
                "Карта «Переправа»", LocalTanksGuiTheme.SectionTitle);
            GUI.Label(new Rect(panel.x + 12f, panel.y + 29f, panel.width - 24f, 20f),
                "F1 — управление   ·   F7 — звук   ·   F2–F6 — диагностика",
                LocalTanksGuiTheme.Caption);
        }

        private void OnDisable()
        {
            ModalUiState.ClearPointerRegion(this);
        }
    }
}
