using UnityEngine;

namespace LocalTanks
{
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class TankMotor : MonoBehaviour
    {
        [SerializeField] private TankPrototypeConfig config;

        private Rigidbody2D body;
        private float driveInput;
        private float turnInput;

        public float CurrentSpeed { get; private set; }

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
        }

        public void Configure(TankPrototypeConfig newConfig)
        {
            config = newConfig;
        }

        public void SetInput(float drive, float turn)
        {
            driveInput = Mathf.Clamp(drive, -1f, 1f);
            turnInput = Mathf.Clamp(turn, -1f, 1f);
        }

        private void FixedUpdate()
        {
            if (config == null)
            {
                return;
            }

            float deltaTime = Time.fixedDeltaTime;
            float targetSpeed = TankMotionMath.TargetSpeed(
                driveInput,
                config.maxForwardSpeed,
                config.maxReverseSpeed);

            CurrentSpeed = TankMotionMath.ApproachSpeed(
                CurrentSpeed,
                targetSpeed,
                config.acceleration,
                config.braking,
                deltaTime);

            float steeringInput = TankMotionMath.SteeringInput(turnInput, CurrentSpeed);
            float nextAngle = body.rotation - steeringInput * config.hullTurnSpeed * deltaTime;
            Vector2 nextPosition = body.position + (Vector2)transform.up * (CurrentSpeed * deltaTime);
            body.MoveRotation(nextAngle);
            body.MovePosition(nextPosition);
        }
    }
}
