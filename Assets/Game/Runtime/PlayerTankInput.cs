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
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            if (keyboard != null && motor != null)
            {
                float drive = ReadAxis(keyboard.sKey.isPressed, keyboard.wKey.isPressed);
                float turn = ReadAxis(keyboard.aKey.isPressed, keyboard.dKey.isPressed);
                motor.SetInput(drive, turn);
            }

            if (mouse == null)
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
