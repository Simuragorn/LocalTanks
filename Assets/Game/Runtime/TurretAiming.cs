using UnityEngine;

namespace LocalTanks
{
    public sealed class TurretAiming : MonoBehaviour
    {
        [SerializeField] private TankPrototypeConfig config;
        private Vector2 targetPosition;
        private bool hasTarget;

        public void Configure(TankPrototypeConfig newConfig)
        {
            config = newConfig;
        }

        public void SetTarget(Vector2 worldPosition)
        {
            targetPosition = worldPosition;
            hasTarget = true;
        }

        private void Update()
        {
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
            float nextAngle = TankMotionMath.StepAngle(
                transform.eulerAngles.z,
                targetAngle,
                config.turretTurnSpeed,
                Time.deltaTime);
            transform.rotation = Quaternion.Euler(0f, 0f, nextAngle);
        }
    }
}
