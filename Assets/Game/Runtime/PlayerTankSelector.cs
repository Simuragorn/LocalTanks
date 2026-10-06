using UnityEngine;
using UnityEngine.InputSystem;

namespace LocalTanks
{
    public sealed class PlayerTankSelector : MonoBehaviour
    {
        [SerializeField] private GameObject[] tankPrefabs;
        [SerializeField] private string[] tankNames;
        [SerializeField] private GameObject currentTank;
        [SerializeField] private int currentTankIndex;
        [SerializeField] private CameraFollow2D cameraFollow;

        public GameObject CurrentTank => currentTank;
        public int CurrentTankIndex => currentTankIndex;

        public void Configure(
            GameObject[] prefabs,
            string[] displayNames,
            GameObject initialTank,
            int initialIndex,
            CameraFollow2D follow)
        {
            tankPrefabs = prefabs;
            tankNames = displayNames;
            currentTank = initialTank;
            currentTankIndex = initialIndex;
            cameraFollow = follow;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame)
            {
                SelectTank(0);
            }
            else if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame)
            {
                SelectTank(1);
            }
            else if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame)
            {
                SelectTank(2);
            }
            else if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame)
            {
                SelectTank(3);
            }
        }

        public bool SelectTank(int index)
        {
            if (tankPrefabs == null ||
                index < 0 ||
                index >= tankPrefabs.Length ||
                tankPrefabs[index] == null ||
                index == currentTankIndex)
            {
                return false;
            }

            Vector3 position = currentTank != null ? currentTank.transform.position : Vector3.zero;
            Quaternion rotation = currentTank != null ? currentTank.transform.rotation : Quaternion.identity;
            GameObject previousTank = currentTank;
            if (previousTank != null)
            {
                previousTank.SetActive(false);
            }

            currentTank = Instantiate(tankPrefabs[index], position, rotation);
            currentTank.name = $"Player_{GetTankName(index).Replace(' ', '_').Replace('/', '_')}";
            currentTankIndex = index;
            cameraFollow?.Configure(currentTank.transform);

            if (previousTank != null)
            {
                Destroy(previousTank);
            }

            return true;
        }

        private void OnGUI()
        {
            if (tankPrefabs == null || tankPrefabs.Length == 0)
            {
                return;
            }

            const float width = 235f;
            float height = 36f + tankPrefabs.Length * 29f;
            GUI.Box(new Rect(12f, 12f, width, height), "Выбор танка (1–4)");

            for (int index = 0; index < tankPrefabs.Length; index++)
            {
                string marker = index == currentTankIndex ? "▶ " : string.Empty;
                if (GUI.Button(
                    new Rect(22f, 41f + index * 29f, width - 20f, 24f),
                    $"{marker}{index + 1}. {GetTankName(index)}"))
                {
                    SelectTank(index);
                }
            }
        }

        private string GetTankName(int index)
        {
            if (tankNames != null && index >= 0 && index < tankNames.Length && !string.IsNullOrWhiteSpace(tankNames[index]))
            {
                return tankNames[index];
            }

            return tankPrefabs[index] != null ? tankPrefabs[index].name : $"Tank {index + 1}";
        }
    }
}
