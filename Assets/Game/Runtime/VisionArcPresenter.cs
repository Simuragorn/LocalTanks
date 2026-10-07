using System.Linq;
using UnityEngine;

namespace LocalTanks
{
    public sealed class VisionArcPresenter : MonoBehaviour
    {
        private const int OverlaySortingOrder = 10;
        private const int BoundarySortingOrder = 11;
        private const float DegreesPerSegment = 4f;

        [SerializeField] private TeamVisionSystem visionSystem;
        [SerializeField] private Material lineMaterial;

        private Transform visualRoot;
        private MeshFilter overlayFilter;
        private MeshRenderer overlayRenderer;
        private LineRenderer leftBoundary;
        private LineRenderer rightBoundary;
        private Mesh overlayMesh;
        private Material runtimeMaterial;
        private bool ownsMaterial;
        private TeamMember trackedMember;
        private float currentViewAngle = -1f;
        private float currentRadius = -1f;

        public TeamMember TrackedMember => trackedMember;
        public float CurrentViewAngle => currentViewAngle;
        public LineRenderer LeftBoundary => leftBoundary;
        public LineRenderer RightBoundary => rightBoundary;
        public MeshRenderer OutsideOverlayRenderer => overlayRenderer;

        private void Awake()
        {
            EnsureVisuals();
        }

        private void OnEnable()
        {
            EnsureVisuals();
        }

        private void LateUpdate()
        {
            RefreshNow();
        }

        private void OnDestroy()
        {
            if (overlayMesh != null)
            {
                Destroy(overlayMesh);
            }

            if (ownsMaterial && runtimeMaterial != null)
            {
                Destroy(runtimeMaterial);
            }
        }

        public void Configure(TeamVisionSystem system, Material sharedLineMaterial)
        {
            visionSystem = system;
            lineMaterial = sharedLineMaterial;
            if (Application.isPlaying)
            {
                RecreateMaterial();
                EnsureVisuals();
                RefreshNow();
            }
        }

        public void RefreshNow()
        {
            EnsureVisuals();
            VisionRules rules = visionSystem != null ? visionSystem.Rules : null;
            trackedMember = ResolveLocalPlayer();
            bool active = rules != null && trackedMember != null && trackedMember.Definition != null && trackedMember.IsAlive;
            if (visualRoot != null)
            {
                visualRoot.gameObject.SetActive(active);
            }

            if (!active)
            {
                return;
            }

            float angle = rules.GetViewAngle(trackedMember.Definition.vehicleClass);
            float radius = ResolveVisualRadius(rules);
            if (!Mathf.Approximately(angle, currentViewAngle) || !Mathf.Approximately(radius, currentRadius))
            {
                currentViewAngle = angle;
                currentRadius = radius;
                RebuildGeometry(angle, radius);
            }

            visualRoot.position = trackedMember.transform.position;
            Vector2 forward = trackedMember.VisionForward;
            float rotation = Mathf.Atan2(forward.y, forward.x) * Mathf.Rad2Deg - 90f;
            visualRoot.rotation = Quaternion.Euler(0f, 0f, rotation);
            ApplyAppearance(rules);
        }

        private static float ResolveVisualRadius(VisionRules rules)
        {
            float radius = Mathf.Max(10f, rules.viewArcVisualRadius);
            Camera camera = Camera.main;
            if (camera == null || !camera.orthographic)
            {
                return radius;
            }

            float halfHeight = camera.orthographicSize;
            float halfWidth = halfHeight * camera.aspect;
            float viewportDiagonal = Mathf.Sqrt(halfWidth * halfWidth + halfHeight * halfHeight);
            return Mathf.Max(radius, viewportDiagonal + 2f);
        }

        private TeamMember ResolveLocalPlayer()
        {
            if (trackedMember != null && trackedMember.isActiveAndEnabled && trackedMember.IsPlayerControlled)
            {
                return trackedMember;
            }

            TeamId team = visionSystem != null ? visionSystem.LocalPlayerTeam : TeamId.TeamA;
            return TeamMember.ActiveMembers.FirstOrDefault(member =>
                member != null && member.isActiveAndEnabled && member.IsAlive &&
                member.IsPlayerControlled && member.Team == team);
        }

        private void EnsureVisuals()
        {
            if (visualRoot != null)
            {
                return;
            }

            GameObject root = new GameObject("PlayerVisionArc");
            root.transform.SetParent(transform, false);
            visualRoot = root.transform;

            GameObject overlay = new GameObject("OutsideViewTint");
            overlay.transform.SetParent(visualRoot, false);
            overlayFilter = overlay.AddComponent<MeshFilter>();
            overlayRenderer = overlay.AddComponent<MeshRenderer>();
            overlayRenderer.sortingOrder = OverlaySortingOrder;

            leftBoundary = CreateBoundary("LeftBoundary");
            rightBoundary = CreateBoundary("RightBoundary");
            RecreateMaterial();
        }

        private LineRenderer CreateBoundary(string objectName)
        {
            GameObject boundary = new GameObject(objectName);
            boundary.transform.SetParent(visualRoot, false);
            LineRenderer line = boundary.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.numCapVertices = 2;
            line.sortingOrder = BoundarySortingOrder;
            return line;
        }

        private void RecreateMaterial()
        {
            if (ownsMaterial && runtimeMaterial != null)
            {
                Destroy(runtimeMaterial);
            }

            Material source = lineMaterial;
            if (source == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    runtimeMaterial = new Material(shader);
                    ownsMaterial = true;
                }
            }
            else
            {
                runtimeMaterial = source;
                ownsMaterial = false;
            }

            if (overlayRenderer != null)
            {
                overlayRenderer.sharedMaterial = runtimeMaterial;
            }

            if (leftBoundary != null)
            {
                leftBoundary.sharedMaterial = runtimeMaterial;
            }

            if (rightBoundary != null)
            {
                rightBoundary.sharedMaterial = runtimeMaterial;
            }
        }

        private void RebuildGeometry(float viewAngle, float radius)
        {
            if (overlayMesh == null)
            {
                overlayMesh = new Mesh { name = "Outside View Arc Tint" };
                overlayMesh.MarkDynamic();
                overlayFilter.sharedMesh = overlayMesh;
            }

            float clampedAngle = Mathf.Clamp(viewAngle, 1f, 359.9f);
            float outsideAngle = 360f - clampedAngle;
            int segments = Mathf.Max(2, Mathf.CeilToInt(outsideAngle / DegreesPerSegment));
            Vector3[] vertices = new Vector3[segments + 2];
            int[] triangles = new int[segments * 3];
            vertices[0] = Vector3.zero;
            float start = clampedAngle * 0.5f;
            for (int index = 0; index <= segments; index++)
            {
                float angle = start + outsideAngle * index / segments;
                vertices[index + 1] = Direction(angle) * radius;
                if (index >= segments)
                {
                    continue;
                }

                int triangle = index * 3;
                triangles[triangle] = 0;
                triangles[triangle + 1] = index + 2;
                triangles[triangle + 2] = index + 1;
            }

            overlayMesh.Clear();
            overlayMesh.vertices = vertices;
            overlayMesh.triangles = triangles;
            overlayMesh.RecalculateBounds();

            float halfAngle = clampedAngle * 0.5f;
            leftBoundary.SetPosition(0, Vector3.zero);
            leftBoundary.SetPosition(1, Direction(-halfAngle) * radius);
            rightBoundary.SetPosition(0, Vector3.zero);
            rightBoundary.SetPosition(1, Direction(halfAngle) * radius);
        }

        private void ApplyAppearance(VisionRules rules)
        {
            if (overlayRenderer != null)
            {
                MaterialPropertyBlock properties = new MaterialPropertyBlock();
                overlayRenderer.GetPropertyBlock(properties);
                properties.SetColor("_Color", new Color(0.46f, 0.49f, 0.47f, rules.outsideArcOverlayAlpha));
                overlayRenderer.SetPropertyBlock(properties);
            }

            Color boundaryColor = new Color(0.76f, 0.82f, 0.76f, rules.viewBoundaryAlpha);
            ConfigureBoundary(leftBoundary, rules.viewBoundaryWidth, boundaryColor);
            ConfigureBoundary(rightBoundary, rules.viewBoundaryWidth, boundaryColor);
        }

        private static void ConfigureBoundary(LineRenderer line, float width, Color color)
        {
            if (line == null)
            {
                return;
            }

            line.widthMultiplier = Mathf.Max(0.01f, width);
            line.startColor = color;
            line.endColor = new Color(color.r, color.g, color.b, color.a * 0.35f);
        }

        private static Vector3 Direction(float angleDegrees)
        {
            float radians = angleDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), Mathf.Cos(radians), 0f);
        }
    }
}
