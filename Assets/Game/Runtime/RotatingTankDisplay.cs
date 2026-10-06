using UnityEngine;

namespace LocalTanks
{
    [RequireComponent(typeof(Rigidbody2D), typeof(TankHealth))]
    public sealed class RotatingTankDisplay : MonoBehaviour
    {
        [SerializeField] private float degreesPerSecond = 9f;

        private Rigidbody2D body;
        private TankHealth health;

        public float DegreesPerSecond => degreesPerSecond;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            health = GetComponent<TankHealth>();
        }

        public void Configure(float rotationSpeed)
        {
            degreesPerSecond = rotationSpeed;
        }

        private void FixedUpdate()
        {
            if (health != null && health.IsDestroyed)
            {
                return;
            }

            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.MoveRotation(body.rotation + degreesPerSecond * Time.fixedDeltaTime);
        }
    }
}
