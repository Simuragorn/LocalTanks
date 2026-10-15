using UnityEngine;

namespace LocalTanks
{
    [RequireComponent(typeof(TankMotor))]
    public sealed class GunDispersionController : MonoBehaviour
    {
        public const float ShotBloomHoldSeconds = 0.16f;

        [SerializeField] private TankDefinition config;
        [SerializeField] private TankMotor motor;
        [SerializeField] private TurretAiming turret;

        private float currentDispersionDegrees;
        private float previousHullAngle;
        private float previousTurretAngle;
        private int shotSequence;
        private bool anglesInitialized;
        private float shotBloomHoldRemaining;

        public float CurrentDispersionDegrees => currentDispersionDegrees;
        public float CurrentAimDistanceUnits => turret != null && turret.HasTarget
            ? Vector2.Distance(turret.transform.position, turret.TargetPosition)
            : 0f;
        public float CurrentEffectiveDispersionDegrees => EffectiveDispersionDegreesAtDistance(CurrentAimDistanceUnits);
        public float NormalizedDispersion => config == null || ShotMaximum <= config.minimumDispersionDegrees
            ? 0f
            : Mathf.InverseLerp(config.minimumDispersionDegrees, ShotMaximum, currentDispersionDegrees);

        private float ShotMaximum => config != null
            ? GunDispersionMath.MaximumAfterShot(config.maximumDispersionDegrees, config.shotDispersionDegrees)
            : 0f;

        public void Configure(TankDefinition newConfig, TankMotor newMotor, TurretAiming newTurret)
        {
            config = newConfig;
            motor = newMotor;
            turret = newTurret;
            ResetState();
        }

        private void Awake()
        {
            if (motor == null) motor = GetComponent<TankMotor>();
            if (turret == null) turret = GetComponentInChildren<TurretAiming>();
            ResetState();
        }

        private void OnEnable()
        {
            ResetState();
        }

        private void Update()
        {
            if (config == null || motor == null || Time.deltaTime <= 0f)
            {
                return;
            }

            float hullAngle = transform.eulerAngles.z;
            float turretAngle = turret != null ? turret.transform.localEulerAngles.z : 0f;
            if (!anglesInitialized)
            {
                previousHullAngle = hullAngle;
                previousTurretAngle = turretAngle;
                anglesInitialized = true;
            }

            float hullDegreesPerSecond = Mathf.Max(
                Mathf.Abs(motor.CurrentTurnRate),
                Mathf.Abs(Mathf.DeltaAngle(previousHullAngle, hullAngle)) / Time.deltaTime);
            float turretRelativeDegreesPerSecond = Mathf.Abs(Mathf.DeltaAngle(previousTurretAngle, turretAngle)) / Time.deltaTime;
            previousHullAngle = hullAngle;
            previousTurretAngle = turretAngle;

            float speedLimit = motor.CurrentSpeed < 0f ? config.maxReverseSpeed : config.maxForwardSpeed;
            float movement01 = Mathf.Abs(motor.CurrentSpeed) / Mathf.Max(0.01f, speedLimit);
            float hullTraverse01 = hullDegreesPerSecond / Mathf.Max(0.01f, config.hullTurnSpeed);
            float turretTraverse01 = turretRelativeDegreesPerSecond / Mathf.Max(0.01f, config.turretTurnSpeed);
            float target = GunDispersionMath.CalculateTarget(
                config.minimumDispersionDegrees,
                config.maximumDispersionDegrees,
                movement01,
                hullTraverse01,
                turretTraverse01,
                config.movementDispersionDegrees,
                config.hullTraverseDispersionDegrees,
                config.turretTraverseDispersionDegrees);
            if (shotBloomHoldRemaining > 0f)
            {
                shotBloomHoldRemaining = Mathf.Max(0f, shotBloomHoldRemaining - Time.deltaTime);
                target = Mathf.Max(target, currentDispersionDegrees);
            }
            currentDispersionDegrees = GunDispersionMath.Step(
                currentDispersionDegrees,
                target,
                config.minimumDispersionDegrees,
                ShotMaximum,
                config.aimingTimeSeconds,
                Time.deltaTime);
        }

        public Vector2 ApplyToDirection(Vector2 direction)
        {
            return ApplyToDirection(direction, CurrentAimDistanceUnits);
        }

        public Vector2 ApplyToDirection(Vector2 direction, float aimDistanceUnits)
        {
            Vector2 normalized = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.up;
            float effectiveDispersion = EffectiveDispersionDegreesAtDistance(aimDistanceUnits);
            float offset = GunDispersionMath.SampleOffsetDegrees(effectiveDispersion, shotSequence++);
            currentDispersionDegrees = GunDispersionMath.AddShotPenalty(
                currentDispersionDegrees,
                config != null ? config.shotDispersionDegrees : 0f,
                config != null ? config.minimumDispersionDegrees : 0f,
                config != null ? ShotMaximum : currentDispersionDegrees);
            shotBloomHoldRemaining = ShotBloomHoldSeconds;
            return Quaternion.Euler(0f, 0f, offset) * normalized;
        }

        public float EffectiveDispersionDegreesAtDistance(float distanceUnits)
        {
            return config == null
                ? Mathf.Max(0f, currentDispersionDegrees)
                : DistanceDispersionTable.EffectiveDegrees(
                    config.vehicleClass,
                    distanceUnits,
                    currentDispersionDegrees);
        }

        private void ResetState()
        {
            currentDispersionDegrees = config != null ? config.maximumDispersionDegrees : 0f;
            previousHullAngle = transform.eulerAngles.z;
            previousTurretAngle = turret != null ? turret.transform.localEulerAngles.z : 0f;
            anglesInitialized = true;
            shotSequence = 0;
            shotBloomHoldRemaining = 0f;
        }
    }
}
