using UnityEngine;

namespace LocalTanks
{
    public sealed class RiverCrossingInstructions : MonoBehaviour
    {
        private void OnGUI()
        {
            GUI.Box(
                new Rect(12f, 12f, 350f, 86f),
                "Карта «Переправа»\n" +
                "W/S/A/D — движение, мышь — башня, ЛКМ — огонь\n" +
                "Колесо — масштаб, F2 — навигационная сетка\n" +
                "Север: лес  •  Центр: посёлок  •  Юг: луга");
        }
    }
}
