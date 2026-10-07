using UnityEngine;
using UnityEngine.InputSystem;

namespace LocalTanks
{
    public sealed class DeveloperTimeScaleController : MonoBehaviour
    {
        private const float NormalScale = 1f;

        [SerializeField, Min(1f)] private float acceleratedScale = 3f;

        public bool IsAccelerated { get; private set; }
        public float CurrentScale => Time.timeScale;

        private void OnEnable()
        {
            ApplyScale(NormalScale);
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f5Key.wasPressedThisFrame)
            {
                Toggle();
            }
        }

        private void OnDisable()
        {
            IsAccelerated = false;
            ApplyScale(NormalScale);
        }

        public void Toggle()
        {
            SetAccelerated(!IsAccelerated);
        }

        public void SetAccelerated(bool accelerated)
        {
            IsAccelerated = accelerated;
            ApplyScale(accelerated ? Mathf.Max(NormalScale, acceleratedScale) : NormalScale);
        }

        private void OnGUI()
        {
            Rect button = new Rect(Screen.width - 262f, 84f, 250f, 28f);
            string label = IsAccelerated
                ? "Вернуть скорость ×1 (F5)"
                : "Ускорить время ×3 (F5)";
            if (GUI.Button(button, label))
            {
                Toggle();
            }
        }

        private static void ApplyScale(float scale)
        {
            Time.timeScale = Mathf.Max(0.01f, scale);
        }
    }
}
