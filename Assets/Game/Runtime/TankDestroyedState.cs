using UnityEngine;

namespace LocalTanks
{
    [RequireComponent(typeof(TankHealth))]
    public sealed class TankDestroyedState : MonoBehaviour
    {
        /// <summary>
        /// Damping applied to a wreck's body. A live tank is driven by
        /// <see cref="TankMotor"/> through MovePosition, so external pushes are
        /// overwritten every physics step and it feels immovable. A wreck loses the
        /// motor, so without extra damping its body would slide on effectively
        /// frictionless colliders as if its mass had vanished. Jammed tracks biting
        /// into the ground are the physical reason a wreck resists pushing harder
        /// than a tank that is still on its tracks.
        /// </summary>
        private const float WreckLinearDamping = 6f;
        private const float WreckAngularDamping = 14f;

        [SerializeField] private TankHealth health;
        [SerializeField] private TankMotor motor;
        [SerializeField] private TurretAiming turret;
        [SerializeField] private WeaponController weapon;
        [SerializeField] private PlayerTankInput playerInput;
        [SerializeField] private Rigidbody2D body;

        public static float WreckLinearDampingValue => WreckLinearDamping;
        public static float WreckAngularDampingValue => WreckAngularDamping;

        private void Awake()
        {
            if (health == null)
            {
                health = GetComponent<TankHealth>();
            }

            if (body == null)
            {
                body = GetComponent<Rigidbody2D>();
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

            ApplyWreckResistance();

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

        private void ApplyWreckResistance()
        {
            if (body == null)
            {
                body = GetComponent<Rigidbody2D>();
            }

            if (body == null)
            {
                return;
            }

            body.linearDamping = WreckLinearDamping;
            body.angularDamping = WreckAngularDamping;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
    }
}
