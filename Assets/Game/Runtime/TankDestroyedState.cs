using UnityEngine;

namespace LocalTanks
{
    [RequireComponent(typeof(TankHealth))]
    public sealed class TankDestroyedState : MonoBehaviour
    {
        [SerializeField] private TankHealth health;
        [SerializeField] private TankMotor motor;
        [SerializeField] private TurretAiming turret;
        [SerializeField] private WeaponController weapon;
        [SerializeField] private PlayerTankInput playerInput;

        private void Awake()
        {
            if (health == null)
            {
                health = GetComponent<TankHealth>();
            }

            health.Destroyed += OnDestroyed;
        }

        private void OnDestroy()
        {
            if (health != null)
            {
                health.Destroyed -= OnDestroyed;
            }
        }

        public void Configure(
            TankHealth newHealth,
            TankMotor newMotor,
            TurretAiming newTurret,
            WeaponController newWeapon,
            PlayerTankInput newPlayerInput)
        {
            health = newHealth;
            motor = newMotor;
            turret = newTurret;
            weapon = newWeapon;
            playerInput = newPlayerInput;
        }

        private void OnDestroyed(TankHealth destroyedHealth)
        {
            motor?.StopImmediately();
            if (motor != null)
            {
                motor.enabled = false;
            }

            if (turret != null)
            {
                turret.enabled = false;
            }

            if (weapon != null)
            {
                weapon.enabled = false;
            }

            if (playerInput != null)
            {
                playerInput.enabled = false;
            }

            NavigationAgent navigation = GetComponent<NavigationAgent>();
            if (navigation != null)
            {
                navigation.ClearDestination();
                navigation.enabled = false;
            }

            CombatTankAI combatAi = GetComponent<CombatTankAI>();
            if (combatAi != null)
            {
                combatAi.HandleDestroyed();
                combatAi.enabled = false;
            }

            foreach (SpriteRenderer renderer in GetComponentsInChildren<SpriteRenderer>())
            {
                renderer.color = new Color(0.25f, 0.25f, 0.25f, renderer.color.a);
            }
        }
    }
}
