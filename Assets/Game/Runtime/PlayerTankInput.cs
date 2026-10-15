using UnityEngine;
using UnityEngine.InputSystem;

namespace LocalTanks
{
    public sealed class PlayerTankInput : MonoBehaviour
    {
        public const float AimDeadZoneMetres = 2f;

        [SerializeField] private TankMotor motor;
        [SerializeField] private TurretAiming turret;
        [SerializeField] private WeaponController weapon;
        [SerializeField] private Camera worldCamera;
        private Vector2 lastStableAimDirection = Vector2.up;
        private float aimDeadZoneUnits;

        private void Awake()
        {
            RefreshAimDeadZone();
        }

        public void Configure(TankMotor newMotor, TurretAiming newTurret, WeaponController newWeapon)
        {
            motor = newMotor;
            turret = newTurret;
            weapon = newWeapon;
            lastStableAimDirection = turret != null ? (Vector2)turret.transform.up : Vector2.up;
            RefreshAimDeadZone();
        }

        private void Update()
        {
            // An overlay owns the pointer and the keyboard while it is open: dragging a settings slider
            // must not fire the gun, and the turret must not follow the cursor across the panel. The
            // motor is explicitly zeroed, because keeping the last input would drive the machine on.
            if (ModalUiState.AnyOpen)
            {
                motor?.SetInput(0f, 0f);
                return;
            }

            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            if (keyboard != null && motor != null)
            {
                float drive = ReadAxis(keyboard.sKey.isPressed, keyboard.wKey.isPressed);
                float turn = ReadAxis(keyboard.aKey.isPressed, keyboard.dKey.isPressed);
                motor.SetInput(drive, turn);
            }

            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            {
                weapon?.TryStartReload();
            }

            if (mouse == null)
            {
                return;
            }

            // Non-modal laboratory and diagnostic tools should not stop WASD, but the mouse belongs to
            // the control under it. Without this gate a click on an IMGUI button also fires and dragging a
            // slider turns the turret across the battlefield behind the window.
            if (ModalUiState.BlocksPointer(mouse.position.ReadValue()))
            {
                return;
            }

            Camera cameraToUse = worldCamera != null ? worldCamera : Camera.main;
            if (cameraToUse != null && turret != null)
            {
                Vector3 screen = mouse.position.ReadValue();
                Vector3 world = cameraToUse.ScreenToWorldPoint(screen);
                Vector2 origin = turret.transform.position;
                Vector2 requested = world;
                float deadZoneUnits = aimDeadZoneUnits > 0f
                    ? aimDeadZoneUnits
                    : GameScale.MetresToUnits(AimDeadZoneMetres);
                Vector2 delta = requested - origin;
                if (delta.sqrMagnitude >= deadZoneUnits * deadZoneUnits)
                {
                    lastStableAimDirection = delta.normalized;
                }

                turret.SetTarget(StabilizeAimTarget(
                    origin,
                    requested,
                    lastStableAimDirection,
                    deadZoneUnits));
            }

            if (mouse.leftButton.isPressed)
            {
                weapon?.TryFire();
            }
        }

        private static float ReadAxis(bool negative, bool positive)
        {
            return (positive ? 1f : 0f) - (negative ? 1f : 0f);
        }

        /// <summary>
        /// Keeps the turret direction stable while the pointer crosses the vehicle itself. Without this
        /// dead zone, moving the cursor by one pixel across the turret pivot changes the desired angle by
        /// almost 180 degrees and makes the sight spin uncontrollably.
        /// </summary>
        public static Vector2 StabilizeAimTarget(
            Vector2 origin,
            Vector2 requested,
            Vector2 fallbackDirection,
            float minimumDistanceUnits)
        {
            float safeDistance = Mathf.Max(0f, minimumDistanceUnits);
            Vector2 delta = requested - origin;
            if (safeDistance <= 0f || delta.sqrMagnitude >= safeDistance * safeDistance)
            {
                return requested;
            }

            Vector2 direction = fallbackDirection.sqrMagnitude > 0.0001f
                ? fallbackDirection.normalized
                : Vector2.up;
            return origin + direction * safeDistance;
        }

        /// <summary>
        /// Covers the whole machine plus a small clearance around it. A fixed two-metre circle was smaller
        /// than a tank hull, so the shifted sniper camera could feed its own movement back into turret aim
        /// while the cursor was still visibly over the player's machine.
        /// </summary>
        public static float AimDeadZoneForBounds(
            Vector2 turretOrigin,
            Bounds vehicleBounds,
            float clearanceUnits)
        {
            Vector2 centerOffset = (Vector2)vehicleBounds.center - turretOrigin;
            Vector2 farthestOffset = new Vector2(
                Mathf.Abs(centerOffset.x) + vehicleBounds.extents.x,
                Mathf.Abs(centerOffset.y) + vehicleBounds.extents.y);
            return Mathf.Max(0f, clearanceUnits) + farthestOffset.magnitude;
        }

        private void RefreshAimDeadZone()
        {
            float clearance = GameScale.MetresToUnits(AimDeadZoneMetres);
            Vector2 origin = turret != null ? turret.transform.position : transform.position;
            Collider2D[] colliders = GetComponentsInChildren<Collider2D>(true);
            bool found = false;
            Bounds combined = default;
            foreach (Collider2D collider in colliders)
            {
                if (collider == null || collider.isTrigger)
                {
                    continue;
                }

                if (!found)
                {
                    combined = collider.bounds;
                    found = true;
                }
                else
                {
                    combined.Encapsulate(collider.bounds);
                }
            }

            aimDeadZoneUnits = found
                ? AimDeadZoneForBounds(origin, combined, clearance)
                : clearance;
        }
    }
}
