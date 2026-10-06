using UnityEngine;

namespace LocalTanks
{
    [RequireComponent(typeof(Rigidbody2D), typeof(TankHealth))]
    public sealed class RotatingTankDisplay : MonoBehaviour
    {
        [SerializeField] private float degreesPerSecond = 9f;
        [SerializeField] private bool isPaused;

        private Rigidbody2D body;
        private TankHealth health;

        public float DegreesPerSecond => degreesPerSecond;
        public bool IsPaused => isPaused;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            health = GetComponent<TankHealth>();
        }

        public void Configure(float rotationSpeed)
        {
            degreesPerSecond = rotationSpeed;
        }

        public void SetPaused(bool paused)
        {
            isPaused = paused;
            if (paused && body != null)
            {
                body.angularVelocity = 0f;
            }
        }

        private void FixedUpdate()
        {
            if (isPaused || health != null && health.IsDestroyed)
            {
                return;
            }

            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.MoveRotation(body.rotation + degreesPerSecond * Time.fixedDeltaTime);
        }
    }
}
