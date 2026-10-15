using UnityEngine;
using UnityEngine.InputSystem;

namespace LocalTanks
{
    [RequireComponent(typeof(Camera))]
    public sealed class CameraFollow2D : MonoBehaviour
    {
        public const float CombatMinimumZoom = 2.5f;
        public const float CloseInspectionZoom = 0.75f;
        public const float AimOffsetViewportShare = 0.82f;

        [SerializeField] private Transform target;
        [SerializeField, Min(0f)] private float smoothTime = 0.15f;
        [SerializeField, Min(0.1f)] private float minimumZoom = CombatMinimumZoom;
        [SerializeField, Min(0.1f)] private float maximumZoom = 12f;
        [SerializeField, Min(0.1f)] private float zoomStep = 1f;
        [SerializeField, Min(0.1f)] private float zoomSpeed = 10f;
        [SerializeField, Tooltip("Developer-only: allow the point-blank inspection zoom used to check tank art and effects.")]
        private bool allowCloseInspectionZoom;

        private Camera cameraComponent;
        private TurretAiming targetTurret;
        private Vector3 velocity;
        private float targetZoom;
        private bool aimMode;

        public Transform Target => target;
        public float CurrentZoom => cameraComponent != null ? cameraComponent.orthographicSize : 0f;
        public float TargetZoom => targetZoom;
        // Older generated scenes serialize larger minima. The runtime cap keeps one consistent combat
        // close-up on every map without editing generated Unity scenes by hand.
        public float MinimumZoom => Mathf.Min(
            minimumZoom,
            allowCloseInspectionZoom ? CloseInspectionZoom : CombatMinimumZoom);
        public float MaximumZoom => maximumZoom;
        public bool IsAimMode => aimMode;
        public bool CloseInspectionZoomEnabled => allowCloseInspectionZoom;

        private void Awake()
        {
            targetTurret = target != null ? target.GetComponentInChildren<TurretAiming>() : null;
            if (target != null)
            {
                maximumZoom = MaximumZoomFor(target);
            }
            InitializeCamera();
            EnsureAimReticle().Configure(target);
        }

        public void Configure(Transform newTarget)
        {
            target = newTarget;
            targetTurret = target != null ? target.GetComponentInChildren<TurretAiming>() : null;
            maximumZoom = MaximumZoomFor(target);
            InitializeCamera();
            SetZoom(targetZoom > 0f ? targetZoom : MinimumZoom);
            EnsureAimReticle().Configure(newTarget);
        }

        public void ConfigureZoomRange(float minimum, float maximum, float step = 1f)
        {
            minimumZoom = Mathf.Max(0.1f, minimum);
            maximumZoom = Mathf.Max(minimumZoom, maximum);
            zoomStep = Mathf.Max(0.1f, step);
            InitializeCamera();
            SetZoom(targetZoom > 0f ? targetZoom : minimumZoom);
        }

        public void SetZoom(float size, bool immediate = false)
        {
            InitializeCamera();
            targetZoom = Mathf.Clamp(size, MinimumZoom, maximumZoom);
            if (immediate && cameraComponent != null)
            {
                cameraComponent.orthographicSize = targetZoom;
            }
        }

        public void SetCloseInspectionZoomEnabled(bool enabled, bool immediate = false)
        {
            allowCloseInspectionZoom = enabled;
            SetZoom(targetZoom > 0f ? targetZoom : MinimumZoom, immediate);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            bool shiftPressed = keyboard != null &&
                                (keyboard.leftShiftKey.wasPressedThisFrame || keyboard.rightShiftKey.wasPressedThisFrame);
            bool diagnosticTab = keyboard != null && keyboard.tabKey.wasPressedThisFrame;
            if (!ModalUiState.AnyOpen && shiftPressed && !diagnosticTab)
            {
                SetAimMode(!aimMode);
            }

            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            if (ModalUiState.BlocksPointer(mouse.position.ReadValue()))
            {
                return;
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > Mathf.Epsilon)
            {
                SetZoom(targetZoom - Mathf.Sign(scroll) * zoomStep);
            }
        }

        private void LateUpdate()
        {
            InitializeCamera();
            if (cameraComponent != null)
            {
                cameraComponent.orthographicSize = Mathf.MoveTowards(
                    cameraComponent.orthographicSize,
                    targetZoom,
                    zoomSpeed * Time.deltaTime);
            }

            if (target == null)
            {
                return;
            }

            Vector2 gunDirection = targetTurret != null ? targetTurret.transform.up : target.up;
            float aspect = cameraComponent != null ? cameraComponent.aspect : 1f;
            Vector2 centre = CalculateViewCenter(target.position, gunDirection, CurrentZoom, aspect, aimMode);
            Vector3 desired = new Vector3(centre.x, centre.y, transform.position.z);
            transform.position = Vector3.SmoothDamp(
                transform.position,
                desired,
                ref velocity,
                smoothTime);
        }

        public void SetAimMode(bool enabled)
        {
            aimMode = enabled;
        }

        public static Vector2 CalculateViewCenter(
            Vector2 tankPosition,
            Vector2 gunDirection,
            float orthographicSize,
            float cameraAspect,
            bool aiming)
        {
            if (!aiming || gunDirection.sqrMagnitude <= 0.0001f)
            {
                return tankPosition;
            }

            Vector2 direction = gunDirection.normalized;
            float halfHeight = Mathf.Max(0f, orthographicSize);
            if (halfHeight <= 0.0001f)
            {
                return tankPosition;
            }

            float halfWidth = halfHeight * Mathf.Max(0.01f, cameraAspect);
            float edgeScale = Mathf.Sqrt(
                direction.x * direction.x / Mathf.Max(0.0001f, halfWidth * halfWidth) +
                direction.y * direction.y / Mathf.Max(0.0001f, halfHeight * halfHeight));
            float distanceToEdge = edgeScale > 0.0001f ? 1f / edgeScale : 0f;
            return tankPosition + direction * distanceToEdge * AimOffsetViewportShare;
        }

        public static float MaximumZoomFor(Transform tank)
        {
            TankHealth health = tank != null ? tank.GetComponent<TankHealth>() : null;
            TankDefinition definition = health != null ? health.Definition : null;
            if (definition == null)
            {
                return 12f;
            }

            return definition.viewRange > 0f
                ? definition.viewRange
                : MaximumZoomFor(definition.vehicleClass, definition.tier);
        }

        public static float MaximumZoomFor(VehicleClass vehicleClass, int tier)
        {
            return VisionRangeTable.UnitsFor(vehicleClass, tier);
        }

        private void InitializeCamera()
        {
            if (cameraComponent == null)
            {
                cameraComponent = GetComponent<Camera>();
            }

            if (cameraComponent != null && targetZoom <= 0f)
            {
                maximumZoom = Mathf.Max(MinimumZoom, maximumZoom);
                targetZoom = Mathf.Clamp(cameraComponent.orthographicSize, MinimumZoom, maximumZoom);
            }
        }

        private PlayerAimReticle EnsureAimReticle()
        {
            PlayerAimReticle reticle = GetComponent<PlayerAimReticle>();
            if (reticle == null) reticle = gameObject.AddComponent<PlayerAimReticle>();
            return reticle;
        }
    }
}
