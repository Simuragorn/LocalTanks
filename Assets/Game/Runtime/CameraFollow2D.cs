using UnityEngine;
using UnityEngine.InputSystem;

namespace LocalTanks
{
    [RequireComponent(typeof(Camera))]
    public sealed class CameraFollow2D : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField, Min(0f)] private float smoothTime = 0.15f;
        [SerializeField, Min(0.1f)] private float minimumZoom = 4f;
        [SerializeField, Min(0.1f)] private float maximumZoom = 12f;
        [SerializeField, Min(0.1f)] private float zoomStep = 1f;
        [SerializeField, Min(0.1f)] private float zoomSpeed = 10f;

        private Camera cameraComponent;
        private Vector3 velocity;
        private float targetZoom;

        public Transform Target => target;
        public float CurrentZoom => cameraComponent != null ? cameraComponent.orthographicSize : 0f;
        public float TargetZoom => targetZoom;
        public float MinimumZoom => minimumZoom;
        public float MaximumZoom => maximumZoom;

        private void Awake()
        {
            InitializeCamera();
            EnsureAimReticle().Configure(target);
        }

        public void Configure(Transform newTarget)
        {
            target = newTarget;
            InitializeCamera();
            EnsureAimReticle().Configure(newTarget);
        }

        public void ConfigureZoomRange(float minimum, float maximum, float step = 1f)
        {
            minimumZoom = Mathf.Max(0.1f, minimum);
            maximumZoom = Mathf.Max(minimumZoom, maximum);
            zoomStep = Mathf.Max(0.1f, step);
            InitializeCamera();
            SetZoom(targetZoom > 0f ? targetZoom : minimumZoom);
        }

        public void SetZoom(float size, bool immediate = false)
        {
            InitializeCamera();
            targetZoom = Mathf.Clamp(size, minimumZoom, maximumZoom);
            if (immediate && cameraComponent != null)
            {
                cameraComponent.orthographicSize = targetZoom;
            }
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > Mathf.Epsilon)
            {
                SetZoom(targetZoom - Mathf.Sign(scroll) * zoomStep);
            }
        }

        private void LateUpdate()
        {
            InitializeCamera();
            if (cameraComponent != null)
            {
                cameraComponent.orthographicSize = Mathf.MoveTowards(
                    cameraComponent.orthographicSize,
                    targetZoom,
                    zoomSpeed * Time.deltaTime);
            }

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

        private void InitializeCamera()
        {
            if (cameraComponent == null)
            {
                cameraComponent = GetComponent<Camera>();
            }

            if (cameraComponent != null && targetZoom <= 0f)
            {
                maximumZoom = Mathf.Max(minimumZoom, maximumZoom);
                targetZoom = Mathf.Clamp(cameraComponent.orthographicSize, minimumZoom, maximumZoom);
            }
        }

        private PlayerAimReticle EnsureAimReticle()
        {
            PlayerAimReticle reticle = GetComponent<PlayerAimReticle>();
            if (reticle == null) reticle = gameObject.AddComponent<PlayerAimReticle>();
            return reticle;
        }
    }
}
