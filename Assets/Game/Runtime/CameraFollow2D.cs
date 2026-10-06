using UnityEngine;

namespace LocalTanks
{
    public sealed class CameraFollow2D : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField, Min(0f)] private float smoothTime = 0.15f;
        private Vector3 velocity;

        public Transform Target => target;

        public void Configure(Transform newTarget)
        {
            target = newTarget;
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            Vector3 desired = new Vector3(target.position.x, target.position.y, transform.position.z);
            transform.position = Vector3.SmoothDamp(
                transform.position,
                desired,
                ref velocity,
                smoothTime);
        }
    }
}
