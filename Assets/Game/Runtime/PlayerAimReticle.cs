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
        private bool uiInteractionActive;
        private GUIStyle distanceStyle;
        private TankDebugSelectionOutline hoverOutline;

        public Transform ControlledTank => controlledTank;
        public Transform HoveredTank { get; private set; }
        public bool IsLineOfFireBlocked { get; private set; }
        public bool UiInteractionActive => uiInteractionActive;
        public bool HasPenetrationSolution { get; private set; }
        public float CurrentPenetrationChance { get; private set; }

        public void Configure(Transform tank)
        {
            ClearHoverOutline();
            controlledTank = tank;
            turret = tank != null ? tank.GetComponentInChildren<TurretAiming>() : null;
            weapon = tank != null ? tank.GetComponent<WeaponController>() : null;
            dispersion = tank != null ? tank.GetComponent<GunDispersionController>() : null;
            RefreshCursorVisibility();
        }

        public void SetUiInteractionActive(bool value)
        {
            uiInteractionActive = value;
            RefreshCursorVisibility();
        }

        private void Awake()
        {
            worldCamera = GetComponent<Camera>();
        }

        private void OnEnable()
        {
            RefreshCursorVisibility();
        }

        private void OnDisable()
        {
            ClearHoverOutline();
            Cursor.visible = true;
        }

        private void OnDestroy()
        {
            ClearHoverOutline();
            Cursor.visible = true;
        }

        private void OnGUI()
        {
            HasPenetrationSolution = false;
            CurrentPenetrationChance = 0f;
            Mouse mouse = Mouse.current;
            bool pointerBlocked = mouse != null && ModalUiState.BlocksPointer(mouse.position.ReadValue());
            Cursor.visible = uiInteractionActive || pointerBlocked || controlledTank == null;
            if (uiInteractionActive || pointerBlocked)
            {
                ClearHoverOutline();
                return;
            }

            if (mouse == null || controlledTank == null || turret == null || weapon == null || dispersion == null)
            {
                ClearHoverOutline();
                return;
            }

            if (worldCamera == null) worldCamera = GetComponent<Camera>();
            Vector2 mouseScreen = mouse.position.ReadValue();
            Vector2 cursorGui = new Vector2(mouseScreen.x, Screen.height - mouseScreen.y);
            Vector3 cursorWorld3 = worldCamera.ScreenToWorldPoint(new Vector3(mouseScreen.x, mouseScreen.y, -worldCamera.transform.position.z));
            Vector2 cursorWorld = cursorWorld3;
            RefreshHoverTargetAt(cursorWorld);
            Vector2 lineOrigin = weapon.Muzzle != null ? weapon.Muzzle.position : turret.transform.position;
            LineOfFireResult lineResult = LineOfFireProbe.Evaluate(controlledTank, lineOrigin, cursorWorld);
            IsLineOfFireBlocked = lineResult.IsBlocked;
            Vector3 muzzleScreen3 = worldCamera.WorldToScreenPoint(lineOrigin);
            Vector2 muzzleGui = new Vector2(muzzleScreen3.x, Screen.height - muzzleScreen3.y);
            if (lineResult.IsBlocked)
            {
                Vector3 blockScreen3 = worldCamera.WorldToScreenPoint(lineResult.BlockPoint);
                Vector2 blockGui = new Vector2(blockScreen3.x, Screen.height - blockScreen3.y);
                DrawLine(muzzleGui, blockGui, 1.5f, ClearLineColor);
                DrawLine(blockGui, cursorGui, 1.5f, BlockedLineColor);
            }
            else
            {
                DrawLine(muzzleGui, cursorGui, 1.5f, ClearLineColor);
            }
            DrawCross(cursorGui);

            Vector2 turretPosition = turret.transform.position;
            float actualDistance = Vector2.Distance(turretPosition, cursorWorld);
            float aimDistance = Mathf.Max(0.05f, actualDistance);
            Vector2 gunPoint = turretPosition + (Vector2)turret.transform.up * aimDistance;
            Vector3 gunScreen3 = worldCamera.WorldToScreenPoint(gunPoint);
            Vector2 gunGui = new Vector2(gunScreen3.x, Screen.height - gunScreen3.y);
            float radius = CalculateRadiusPixels(turretPosition, gunPoint, aimDistance, dispersion.CurrentDispersionDegrees);
            Color reloadColor = Color.Lerp(ReloadingColor, ReadyColor, weapon.ReloadProgress);
            ShellDefinition shell = weapon.CurrentShell;
            ArmorAimSolution aimSolution = default;
            float probeDistance = shell != null && shell.maximumRange > 0f
                ? shell.maximumRange
                : actualDistance + GameScale.MetresToUnits(2f);
            HasPenetrationSolution = shell != null && HoveredTank != null && ArmorAimProbe.TryEvaluateAlongDirection(
                controlledTank,
                HoveredTank,
                lineOrigin,
                weapon.Muzzle != null ? weapon.Muzzle.up : turret.transform.up,
                probeDistance,
                shell.penetration,
                weapon.CombatRules,
                out aimSolution);
            CurrentPenetrationChance = HasPenetrationSolution ? aimSolution.PenetrationChance : 0f;
            Color aimColor = HasPenetrationSolution
                ? PenetrationChanceColor(CurrentPenetrationChance)
                : reloadColor;
            DrawDottedCircle(gunGui, radius, aimColor);
            DrawReloadSegments(gunGui, radius + 8f, weapon.ReloadProgress, reloadColor);
            DrawDistanceLabel(gunGui, radius, actualDistance);
        }

        /// <summary>
        /// Updates the hull outline from a point query, without scanning the battle roster. The same
        /// method is public so Play Mode can verify visibility and team-colour rules without faking an
        /// operating-system cursor.
        /// </summary>
        public bool RefreshHoverTargetAt(Vector2 worldPoint)
        {
            Transform target = LineOfFireProbe.FindLivingTankAt(worldPoint);
            if (!IsEligibleHoverTarget(controlledTank, target))
            {
                ClearHoverOutline();
                return false;
            }

            TankDebugSelectionOutline next = target.GetComponent<TankDebugSelectionOutline>();
            if (next == null) next = target.gameObject.AddComponent<TankDebugSelectionOutline>();
            if (hoverOutline != null && hoverOutline != next)
            {
                hoverOutline.SetHoverVisible(false, Color.clear);
            }

            HoveredTank = target;
            hoverOutline = next;
            TeamMember ownerMember = controlledTank.GetComponent<TeamMember>();
            TeamMember targetMember = target.GetComponent<TeamMember>();
            hoverOutline.SetHoverVisible(
                true,
                RelationOutlineColor(
                    ownerMember != null ? ownerMember.Team : TeamId.Neutral,
                    targetMember != null ? targetMember.Team : TeamId.Neutral));
            return true;
        }

        public void ClearHoverOutline()
        {
            if (hoverOutline != null)
            {
                hoverOutline.SetHoverVisible(false, Color.clear);
            }

            hoverOutline = null;
            HoveredTank = null;
            HasPenetrationSolution = false;
            CurrentPenetrationChance = 0f;
        }

        public static bool IsEligibleHoverTarget(Transform owner, Transform target)
        {
            if (owner == null || target == null || target == owner || target.IsChildOf(owner)) return false;
            TankHealth health = target.GetComponent<TankHealth>();
            if (health == null || health.IsDestroyed) return false;
            TankVisibilityPresenter visibility = target.GetComponent<TankVisibilityPresenter>();
            return visibility == null || visibility.IsVisible;
        }

        public static Color RelationOutlineColor(TeamId owner, TeamId target)
        {
            if (owner == TeamId.Neutral || target == TeamId.Neutral) return TeamPalette.Neutral;
            return TeamPalette.ForRelation(owner == target);
        }

        private void RefreshCursorVisibility()
        {
            if (!Application.isPlaying) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = uiInteractionActive || controlledTank == null;
        }

        private float CalculateRadiusPixels(Vector2 origin, Vector2 center, float distance, float degrees)
        {
            float worldRadius = GunDispersionMath.RadiusAtDistance(distance, degrees);
            Vector2 perpendicular = Vector2.Perpendicular((center - origin).normalized);
            Vector3 centerScreen = worldCamera.WorldToScreenPoint(center);
            Vector3 edgeScreen = worldCamera.WorldToScreenPoint(center + perpendicular * worldRadius);
            return Vector2.Distance(centerScreen, edgeScreen);
        }

        public static Color PenetrationChanceColor(float chance)
        {
            return Color.Lerp(BlockedLineColor, ReadyColor, Mathf.Clamp01(chance));
        }

        public static string FormatDistance(float gameUnits)
        {
            return $"{Mathf.RoundToInt(GameScale.UnitsToMetres(Mathf.Max(0f, gameUnits)))} м";
        }

        private void DrawDistanceLabel(Vector2 center, float circleRadius, float gameUnits)
        {
            const float width = 74f;
            const float height = 20f;
            Rect rect = new Rect(
                center.x - width * 0.5f,
                center.y + Mathf.Max(10f, circleRadius) + 14f,
                width,
                height);
            Color previous = GUI.color;
            GUI.color = new Color(0.035f, 0.045f, 0.035f, 0.78f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
            GUI.Label(rect, FormatDistance(gameUnits), DistanceStyle());
        }

        private GUIStyle DistanceStyle()
        {
            if (distanceStyle == null)
            {
                distanceStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    clipping = TextClipping.Clip
                };
                distanceStyle.normal.textColor = new Color(0.94f, 0.94f, 0.88f, 1f);
            }

            return distanceStyle;
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
