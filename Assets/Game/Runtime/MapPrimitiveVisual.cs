using UnityEngine;

namespace LocalTanks
{
    public enum MapPrimitiveKind
    {
        HardObstacle,
        DestructibleObstacle,
        Concealment
    }

    public sealed class MapPrimitiveVisual : MonoBehaviour
    {
        public static readonly Color HardObstacleColor = new Color(0.30f, 0.18f, 0.09f, 1f);
        public static readonly Color DestructibleObstacleColor = new Color(0.82f, 0.68f, 0.22f, 1f);
        public static readonly Color ConcealmentColor = new Color(0.27f, 0.52f, 0.22f, 0.92f);

        [SerializeField] private MapPrimitiveKind kind;

        public MapPrimitiveKind Kind => kind;

        public void Configure(MapPrimitiveKind value)
        {
            kind = value;
        }

        public static Color GetColor(MapPrimitiveKind value)
        {
            return value switch
            {
                MapPrimitiveKind.HardObstacle => HardObstacleColor,
                MapPrimitiveKind.DestructibleObstacle => DestructibleObstacleColor,
                _ => ConcealmentColor
            };
        }
    }
}
