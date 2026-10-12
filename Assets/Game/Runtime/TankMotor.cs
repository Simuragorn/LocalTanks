using UnityEngine;

namespace LocalTanks
{
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class TankMotor : MonoBehaviour
    {
        [SerializeField] private TankDefinition config;

        private Rigidbody2D body;
        private float driveInput;
        private float turnInput;
        private float terrainSpeedMultiplier = 1f;
        private float terrainAccelerationMultiplier = 1f;
        private float terrainResistanceMultiplier = 1f;

        public float CurrentSpeed { get; private set; }
        public float CurrentTurnRate { get; private set; }
        public float TerrainSpeedMultiplier => terrainSpeedMultiplier;
        public float TerrainAccelerationMultiplier => terrainAccelerationMultiplier;
        public float TerrainResistanceMultiplier => terrainResistanceMultiplier;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
        }

        public void Configure(TankDefinition newConfig)
        {
            config = newConfig;
        }

        public void SetInput(float drive, float turn)
        {
            driveInput = Mathf.Clamp(drive, -1f, 1f);
            turnInput = Mathf.Clamp(turn, -1f, 1f);
        }

        public void SetTerrainModifiers(
            float speedMultiplier,
            float accelerationMultiplier,
            float resistanceMultiplier = 1f)
        {
            terrainSpeedMultiplier = Mathf.Clamp(speedMultiplier, 0.05f, 2f);
            terrainAccelerationMultiplier = Mathf.Clamp(accelerationMultiplier, 0.05f, 2f);
            terrainResistanceMultiplier = Mathf.Clamp(resistanceMultiplier, 0.1f, 10f);
        }

        public void StopImmediately()
        {
            driveInput = 0f;
            turnInput = 0f;
            CurrentSpeed = 0f;
            CurrentTurnRate = 0f;
            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
            }
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
                config.maxForwardSpeed * terrainSpeedMultiplier,
                config.maxReverseSpeed * terrainSpeedMultiplier);

            CurrentSpeed = TankMotionMath.ApproachSpeed(
                CurrentSpeed,
                targetSpeed,
                config.acceleration * terrainAccelerationMultiplier,
                config.groundResistance * terrainResistanceMultiplier,
                config.braking * terrainResistanceMultiplier,
                deltaTime);

            float steeringInput = TankMotionMath.SteeringInput(turnInput, CurrentSpeed);
            CurrentTurnRate = steeringInput * config.hullTurnSpeed;
            float nextAngle = body.rotation - steeringInput * config.hullTurnSpeed * deltaTime;
            Vector2 nextPosition = body.position + (Vector2)transform.up * (CurrentSpeed * deltaTime);
            body.MoveRotation(nextAngle);
            body.MovePosition(nextPosition);
        }
    }
}
