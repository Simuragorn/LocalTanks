using UnityEngine;
using UnityEngine.InputSystem;

namespace LocalTanks
{
    public sealed class PlayerTankInput : MonoBehaviour
    {
        [SerializeField] private TankMotor motor;
        [SerializeField] private TurretAiming turret;
        [SerializeField] private WeaponController weapon;
        [SerializeField] private Camera worldCamera;

        public void Configure(TankMotor newMotor, TurretAiming newTurret, WeaponController newWeapon)
        {
            motor = newMotor;
            turret = newTurret;
            weapon = newWeapon;
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
                turret.SetTarget(world);
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
    }
}
