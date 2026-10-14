using UnityEngine;

namespace LocalTanks
{
    public readonly struct EdgeIndicatorPlacement
    {
        public EdgeIndicatorPlacement(Vector2 position, float angleDegrees)
        {
            Position = position;
            AngleDegrees = angleDegrees;
        }

        public Vector2 Position { get; }
        public float AngleDegrees { get; }
    }

    public static class HudProjectionMath
    {
        public static bool TryPlaceAtEdge(
            Vector2 projectedPosition,
            Rect safeArea,
            out EdgeIndicatorPlacement placement)
        {
            placement = default;
            if (safeArea.width <= 0f || safeArea.height <= 0f || safeArea.Contains(projectedPosition))
            {
                return false;
            }

            Vector2 direction = projectedPosition - safeArea.center;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = Vector2.up;
            }

            float horizontalScale = Mathf.Abs(direction.x) <= 0.0001f
                ? float.PositiveInfinity
                : safeArea.width * 0.5f / Mathf.Abs(direction.x);
            float verticalScale = Mathf.Abs(direction.y) <= 0.0001f
                ? float.PositiveInfinity
                : safeArea.height * 0.5f / Mathf.Abs(direction.y);
            float scale = Mathf.Min(horizontalScale, verticalScale);
            Vector2 position = safeArea.center + direction * scale;
            position.x = Mathf.Clamp(position.x, safeArea.xMin, safeArea.xMax);
            position.y = Mathf.Clamp(position.y, safeArea.yMin, safeArea.yMax);
            placement = new EdgeIndicatorPlacement(
                position,
                Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
            return true;
        }
    }
}
