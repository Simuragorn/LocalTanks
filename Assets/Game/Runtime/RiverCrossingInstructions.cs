using UnityEngine;

namespace LocalTanks
{
    public sealed class RiverCrossingInstructions : MonoBehaviour
    {
        private void OnGUI()
        {
            GUI.Box(
                new Rect(12f, 12f, 370f, 122f),
                "Карта «Переправа»\n" +
                "W/S/A/D — движение, мышь — башня, ЛКМ — огонь\n" +
                "Колесо — масштаб, F2 — навигация, F3 — обзор\n" +
                "F4 — состояние ближайшего боевого ИИ\n" +
                "F5 — скорость времени ×1 / ×3\n" +
                "Север: лес  •  Центр: посёлок  •  Юг: луга");
        }
    }
}
