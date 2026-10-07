using UnityEngine;
using UnityEngine.InputSystem;

namespace LocalTanks
{
    public sealed class VisionDebugOverlay : MonoBehaviour
    {
        [SerializeField] private TeamVisionSystem visionSystem;
        private bool visible;

        public bool IsVisible => visible;

        public void Configure(TeamVisionSystem system)
        {
            visionSystem = system;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f3Key.wasPressedThisFrame)
            {
                visible = !visible;
            }

            if (!visible || visionSystem == null)
            {
                return;
            }

            foreach (VisionDiagnostic diagnostic in visionSystem.Diagnostics)
            {
                if (diagnostic.Observer == null || diagnostic.Target == null)
                {
                    continue;
                }

                Color color = diagnostic.Result.Detected
                    ? new Color(0.35f, 0.8f, 0.42f)
                    : diagnostic.Result.Reason == DetectionReason.HardBlocker
                        ? new Color(0.9f, 0.45f, 0.25f)
                        : new Color(0.75f, 0.65f, 0.25f);
                Debug.DrawLine(diagnostic.Observer.transform.position, diagnostic.Target.transform.position, color);
            }
        }

        private void OnGUI()
        {
            if (!visible || visionSystem == null)
            {
                return;
            }

            Rect panel = new Rect(12f, Screen.height - 230f, 440f, 218f);
            GUI.Box(panel, "F3 — диагностика обзора");
            int line = 0;
            foreach (VisionDiagnostic diagnostic in visionSystem.Diagnostics)
            {
                if (line >= 8 || diagnostic.Observer == null || diagnostic.Target == null)
                {
                    break;
                }

                string text = $"{diagnostic.Observer.name} → {diagnostic.Target.name}: " +
                              $"{diagnostic.Result.Reason}, {diagnostic.Distance:0.0}/" +
                              $"{diagnostic.Result.DetectionDistance:0.0}, кусты {diagnostic.BushBonus:0.00}";
                GUI.Label(new Rect(22f, Screen.height - 202f + line * 22f, 420f, 22f), text);
                line++;
            }
        }

        private void OnDrawGizmos()
        {
            if (!visible || visionSystem == null)
            {
                return;
            }

            Gizmos.color = new Color(0.45f, 0.75f, 0.55f, 0.45f);
            foreach (TeamMember member in TeamMember.ActiveMembers)
            {
                if (member != null && member.Definition != null && member.IsAlive)
                {
                    Gizmos.DrawWireSphere(member.transform.position, member.Definition.viewRange);
                }
            }
        }
    }
}
