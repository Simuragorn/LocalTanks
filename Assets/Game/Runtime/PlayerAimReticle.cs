using UnityEngine;
using UnityEngine.InputSystem;

namespace LocalTanks
{
    [RequireComponent(typeof(Camera))]
    public sealed class PlayerAimReticle : MonoBehaviour
    {
        private const int AimSegmentCount = 40;
        private const int ReloadSegmentCount = 24;
        private static readonly Color ReadyColor = new Color(0.28f, 0.72f, 0.39f, 0.92f);
        private static readonly Color ReloadingColor = new Color(0.78f, 0.28f, 0.25f, 0.92f);
        private static readonly Color CursorColor = new Color(0.86f, 0.89f, 0.82f, 0.9f);
        private static readonly Color ClearLineColor = new Color(0.28f, 0.72f, 0.39f, 0.72f);
        private static readonly Color BlockedLineColor = new Color(0.78f, 0.28f, 0.25f, 0.82f);

        [SerializeField] private Transform controlledTank;
        [SerializeField] private TurretAiming turret;
        [SerializeField] private WeaponController weapon;
        [SerializeField] private GunDispersionController dispersion;

        private Camera worldCamera;

        public Transform ControlledTank => controlledTank;
        public bool IsLineOfFireBlocked { get; private set; }

        public void Configure(Transform tank)
        {
            controlledTank = tank;
            turret = tank != null ? tank.GetComponentInChildren<TurretAiming>() : null;
            weapon = tank != null ? tank.GetComponent<WeaponController>() : null;
            dispersion = tank != null ? tank.GetComponent<GunDispersionController>() : null;
        }

        private void Awake()
        {
            worldCamera = GetComponent<Camera>();
        }

        private void OnEnable()
        {
            if (Application.isPlaying) Cursor.visible = false;
        }

        private void OnDisable()
        {
            Cursor.visible = true;
        }

        private void OnDestroy()
        {
            Cursor.visible = true;
        }

        private void OnGUI()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || controlledTank == null || turret == null || weapon == null || dispersion == null)
            {
                return;
            }

            if (worldCamera == null) worldCamera = GetComponent<Camera>();
            Vector2 mouseScreen = mouse.position.ReadValue();
            Vector2 cursorGui = new Vector2(mouseScreen.x, Screen.height - mouseScreen.y);
            Vector3 cursorWorld3 = worldCamera.ScreenToWorldPoint(new Vector3(mouseScreen.x, mouseScreen.y, -worldCamera.transform.position.z));
            Vector2 cursorWorld = cursorWorld3;
            Vector2 lineOrigin = weapon.Muzzle != null ? weapon.Muzzle.position : turret.transform.position;
            IsLineOfFireBlocked = LineOfFireProbe.IsBlocked(controlledTank, lineOrigin, cursorWorld);
            Vector3 muzzleScreen3 = worldCamera.WorldToScreenPoint(lineOrigin);
            Vector2 muzzleGui = new Vector2(muzzleScreen3.x, Screen.height - muzzleScreen3.y);
            DrawLine(
                muzzleGui,
                cursorGui,
                1.5f,
                IsLineOfFireBlocked ? BlockedLineColor : ClearLineColor);
            DrawCross(cursorGui);

            Vector2 turretPosition = turret.transform.position;
            float aimDistance = Mathf.Max(1f, Vector2.Distance(turretPosition, cursorWorld));
            Vector2 gunPoint = turretPosition + (Vector2)turret.transform.up * aimDistance;
            Vector3 gunScreen3 = worldCamera.WorldToScreenPoint(gunPoint);
            Vector2 gunGui = new Vector2(gunScreen3.x, Screen.height - gunScreen3.y);
            float radius = CalculateRadiusPixels(turretPosition, gunPoint, aimDistance, dispersion.CurrentDispersionDegrees);
            Color stateColor = weapon.IsReady ? ReadyColor : ReloadingColor;
            DrawDottedCircle(gunGui, radius, stateColor);
            DrawReloadSegments(gunGui, radius + 8f, weapon.ReloadProgress, stateColor);
        }

        private float CalculateRadiusPixels(Vector2 origin, Vector2 center, float distance, float degrees)
        {
            float worldRadius = Mathf.Tan(Mathf.Deg2Rad * Mathf.Max(0f, degrees)) * distance;
            Vector2 perpendicular = Vector2.Perpendicular((center - origin).normalized);
            Vector3 centerScreen = worldCamera.WorldToScreenPoint(center);
            Vector3 edgeScreen = worldCamera.WorldToScreenPoint(center + perpendicular * worldRadius);
            return Mathf.Clamp(Vector2.Distance(centerScreen, edgeScreen), 15f, 150f);
        }

        private static void DrawCross(Vector2 center)
        {
            DrawLine(center + Vector2.left * 11f, center + Vector2.left * 3f, 1.5f, CursorColor);
            DrawLine(center + Vector2.right * 3f, center + Vector2.right * 11f, 1.5f, CursorColor);
            DrawLine(center + Vector2.up * 11f, center + Vector2.up * 3f, 1.5f, CursorColor);
            DrawLine(center + Vector2.down * 3f, center + Vector2.down * 11f, 1.5f, CursorColor);
        }

        private static void DrawDottedCircle(Vector2 center, float radius, Color color)
        {
            for (int index = 0; index < AimSegmentCount; index++)
            {
                float angle = index * 360f / AimSegmentCount;
                Vector2 radial = Direction(angle);
                Vector2 tangent = new Vector2(-radial.y, radial.x);
                Vector2 point = center + radial * radius;
                DrawLine(point - tangent * 2.7f, point + tangent * 2.7f, 1.8f, color);
            }
        }

        private static void DrawReloadSegments(Vector2 center, float radius, float progress, Color stateColor)
        {
            int completed = Mathf.RoundToInt(Mathf.Clamp01(progress) * ReloadSegmentCount);
            Color pending = new Color(0.16f, 0.18f, 0.16f, 0.58f);
            for (int index = 0; index < ReloadSegmentCount; index++)
            {
                float angle = index * 360f / ReloadSegmentCount - 90f;
                Vector2 radial = Direction(angle);
                Vector2 point = center + radial * radius;
                Color color = index < completed ? stateColor : pending;
                DrawLine(point - radial * 3.5f, point + radial * 3.5f, 2.4f, color);
            }
        }

        private static Vector2 Direction(float angleDegrees)
        {
            float radians = angleDegrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        private static void DrawLine(Vector2 start, Vector2 end, float thickness, Color color)
        {
            Vector2 delta = end - start;
            float length = delta.magnitude;
            if (length <= 0.001f) return;
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, start);
            GUI.DrawTexture(new Rect(start.x, start.y - thickness * 0.5f, length, thickness), Texture2D.whiteTexture);
            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }
    }
}
