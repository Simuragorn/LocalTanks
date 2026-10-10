using UnityEngine;

namespace LocalTanks
{
    public sealed class TankDebugSelectionOutline : MonoBehaviour
    {
        private static Material sharedMaterial;

        [SerializeField] private Color color = new Color(0.95f, 0.78f, 0.28f, 0.95f);
        [SerializeField, Min(0.01f)] private float width = 0.07f;
        [SerializeField, Min(0f)] private float margin = 0.12f;

        private PolygonCollider2D hullCollider;
        private LineRenderer line;

        public bool IsVisible => line != null && line.enabled;

        public void SetVisible(bool value)
        {
            EnsureInitialized();
            if (line != null) line.enabled = value;
            if (value) RefreshGeometry();
        }

        private void Awake()
        {
            EnsureInitialized();
            SetVisible(false);
        }

        private void LateUpdate()
        {
            if (IsVisible) RefreshGeometry();
        }

        private void OnDisable()
        {
            if (line != null) line.enabled = false;
        }

        private void EnsureInitialized()
        {
            if (hullCollider == null) hullCollider = GetComponent<PolygonCollider2D>();
            if (line != null || hullCollider == null) return;

            GameObject visual = new GameObject("DebugSelectionOutline");
            visual.transform.SetParent(transform, false);
            line = visual.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = true;
            line.widthMultiplier = width;
            line.numCornerVertices = 3;
            line.numCapVertices = 3;
            line.startColor = color;
            line.endColor = color;
            line.sharedMaterial = GetSharedMaterial();

            SpriteRenderer reference = GetComponentInChildren<SpriteRenderer>();
            if (reference != null)
            {
                line.sortingLayerID = reference.sortingLayerID;
                line.sortingOrder = reference.sortingOrder + 20;
            }
        }

        private void RefreshGeometry()
        {
            if (hullCollider == null || line == null || hullCollider.pathCount == 0) return;
            Vector2[] points = hullCollider.GetPath(0);
            if (points.Length < 3) return;

            Vector2 center = Vector2.zero;
            foreach (Vector2 point in points) center += point;
            center /= points.Length;
            float maximumRadius = 0f;
            foreach (Vector2 point in points)
                maximumRadius = Mathf.Max(maximumRadius, Vector2.Distance(center, point));
            float scale = maximumRadius > 0.001f ? 1f + margin / maximumRadius : 1f;

            line.positionCount = points.Length;
            for (int index = 0; index < points.Length; index++)
            {
                Vector2 expanded = center + (points[index] - center) * scale;
                line.SetPosition(index, hullCollider.transform.TransformPoint(expanded));
            }
        }

        private static Material GetSharedMaterial()
        {
            if (sharedMaterial != null) return sharedMaterial;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return null;
            sharedMaterial = new Material(shader)
            {
                name = "Runtime AI Selection Outline",
                hideFlags = HideFlags.HideAndDontSave
            };
            return sharedMaterial;
        }
    }
}
