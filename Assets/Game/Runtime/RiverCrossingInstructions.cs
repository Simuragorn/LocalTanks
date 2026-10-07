using UnityEngine;

namespace LocalTanks
{
    public sealed class RiverCrossingInstructions : MonoBehaviour
    {
        private void OnGUI()
        {
            GUI.Box(
                new Rect(12f, 12f, 370f, 104f),
                "Карта «Переправа»\n" +
                "W/S/A/D — движение, мышь — башня, ЛКМ — огонь\n" +
                "Колесо — масштаб, F2 — навигация, F3 — обзор\n" +
                "F4 — состояние ближайшего боевого ИИ\n" +
                "Север: лес  •  Центр: посёлок  •  Юг: луга");
        }
    }
}
