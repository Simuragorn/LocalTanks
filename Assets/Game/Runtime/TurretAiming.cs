using UnityEngine;

namespace LocalTanks
{
    public sealed class TurretAiming : MonoBehaviour
    {
        [SerializeField] private TankDefinition config;
        private Vector2 targetPosition;
        private bool hasTarget;

        public bool HasTarget => hasTarget;
        public Vector2 TargetPosition => targetPosition;
        public float AngularVelocityDegreesPerSecond { get; private set; }
        public float AimErrorDegrees
        {
            get
            {
                Vector2 delta = targetPosition - (Vector2)transform.position;
                return !hasTarget || delta.sqrMagnitude < 0.0001f
                    ? 180f
                    : Mathf.Abs(Vector2.SignedAngle(transform.up, delta.normalized));
            }
        }

        public void Configure(TankDefinition newConfig)
        {
            config = newConfig;
        }

        public void SetTarget(Vector2 worldPosition)
        {
            targetPosition = worldPosition;
            hasTarget = true;
        }

        public void ClearTarget()
        {
            hasTarget = false;
        }

        private void Update()
        {
            AngularVelocityDegreesPerSecond = 0f;
            if (!hasTarget || config == null)
            {
                return;
            }

            Vector2 delta = targetPosition - (Vector2)transform.position;
            if (delta.sqrMagnitude < 0.0001f)
            {
                return;
            }

            float targetAngle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - 90f;
            float currentAngle = transform.eulerAngles.z;
            float nextAngle = TankMotionMath.StepAngle(
                currentAngle,
                targetAngle,
                config.turretTurnSpeed,
                Time.deltaTime);
            if (Time.deltaTime > 0f)
            {
                AngularVelocityDegreesPerSecond = Mathf.DeltaAngle(currentAngle, nextAngle) / Time.deltaTime;
            }
            transform.rotation = Quaternion.Euler(0f, 0f, nextAngle);
        }
    }
}
