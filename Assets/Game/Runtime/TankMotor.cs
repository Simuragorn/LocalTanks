using UnityEngine;

namespace LocalTanks
{
    [RequireComponent(typeof(Rigidbody2D))]
    [DefaultExecutionOrder(100)]
    public sealed class TankMotor : MonoBehaviour
    {
        private static double lastCoordinatedFixedTime = double.NegativeInfinity;

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
            using var performanceScope = BattlePerformanceProbe.Measure(BattlePerformanceSubsystem.Motor);
            TeamMember member = GetComponent<TeamMember>();
            if (member == null)
            {
                TickMotor(null);
                return;
            }

            double fixedTime = Time.fixedTimeAsDouble;
            if (fixedTime.Equals(lastCoordinatedFixedTime)) return;
            lastCoordinatedFixedTime = fixedTime;

            // Unity orders component classes, but not multiple instances of the same class. The order in
            // which six kinematic bodies submit MovePosition/MoveRotation affects Box2D contact solving,
            // so identical seeded squad battles could diverge after the first vehicle contact. Submit all
            // tank motion in the canonical TeamMember order and let the remaining callbacks be no-ops.
            foreach (TeamMember activeMember in TeamMember.ActiveMembers)
            {
                TankMotor activeMotor = activeMember != null ? activeMember.GetComponent<TankMotor>() : null;
                if (activeMotor != null && activeMotor.isActiveAndEnabled)
                    activeMotor.TickMotor(activeMember);
            }
        }

        private void TickMotor(TeamMember member)
        {
            if (config == null)
            {
                return;
            }

            BattleTelemetry.Active?.RecordMotorStep(
                member,
                driveInput,
                turnInput,
                terrainSpeedMultiplier,
                terrainAccelerationMultiplier,
                terrainResistanceMultiplier,
                body,
                CurrentSpeed,
                CurrentTurnRate);

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
            float radians = body.rotation * Mathf.Deg2Rad;
            Vector2 bodyForward = new Vector2(-Mathf.Sin(radians), Mathf.Cos(radians));
            Vector2 nextPosition = body.position + bodyForward * (CurrentSpeed * deltaTime);
            body.MoveRotation(nextAngle);
            body.MovePosition(nextPosition);
        }
    }
}
