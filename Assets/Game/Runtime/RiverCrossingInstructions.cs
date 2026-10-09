using UnityEngine;

namespace LocalTanks
{
    public sealed class RiverCrossingInstructions : MonoBehaviour
    {
        private void OnGUI()
        {
            if (BattleDiagnosticsOverlay.AnyModalVisible) return;

            GUI.Box(
                new Rect(12f, 12f, 390f, 144f),
                "Карта «Переправа»\n" +
                "W/S/A/D — движение, мышь — башня, ЛКМ — огонь\n" +
                "Колесо — масштаб, F2 — навигация, F3 — обзор\n" +
                "F4 — инспектор ИИ, Tab / Shift+Tab — выбор бота\n" +
                "F5 — время ×1 / ×3, F6 — анализ карты\n" +
                "F1 — полная справка\n" +
                "Север: лес  •  Центр: посёлок  •  Юг: луга");
        }
    }
}
