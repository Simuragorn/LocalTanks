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
        [SerializeField] private TestRangeTargetRespawner targetRespawner;
        [SerializeField] private bool developerFastReload;

        public GameObject CurrentTank => currentTank;
        public int CurrentTankIndex => currentTankIndex;
        public bool DeveloperFastReloadEnabled => developerFastReload;

        public void Configure(
            GameObject[] prefabs,
            string[] displayNames,
            GameObject initialTank,
            int initialIndex,
            CameraFollow2D follow,
            TestRangeTargetRespawner respawner)
        {
            tankPrefabs = prefabs;
            tankNames = displayNames;
            currentTank = initialTank;
            currentTankIndex = initialIndex;
            cameraFollow = follow;
            targetRespawner = respawner;
            ApplyDeveloperReload();
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

            if (keyboard.pKey.wasPressedThisFrame)
            {
                targetRespawner?.ToggleTargetRotation();
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
            ApplyDeveloperReload();

            if (previousTank != null)
            {
                Destroy(previousTank);
            }

            return true;
        }

        public void SetDeveloperFastReload(bool enabled)
        {
            developerFastReload = enabled;
            ApplyDeveloperReload();
        }

        private void OnGUI()
        {
            if (tankPrefabs == null || tankPrefabs.Length == 0)
            {
                return;
            }

            const float width = 310f;
            float controlsTop = 41f + tankPrefabs.Length * 29f;
            float height = controlsTop + 94f;
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

            bool fastReload = GUI.Toggle(
                new Rect(22f, controlsTop, width - 20f, 24f),
                developerFastReload,
                "Режим разработчика: перезарядка 1 с");
            if (fastReload != developerFastReload)
            {
                SetDeveloperFastReload(fastReload);
            }

            if (GUI.Button(new Rect(22f, controlsTop + 29f, width - 20f, 26f), "Респавн ИИ-танков"))
            {
                targetRespawner?.RespawnAllTargets();
            }

            string rotationLabel = targetRespawner != null && targetRespawner.RotationPaused
                ? "Возобновить вращение (P)"
                : "Остановить вращение (P)";
            if (GUI.Button(new Rect(22f, controlsTop + 58f, width - 20f, 26f), rotationLabel))
            {
                targetRespawner?.ToggleTargetRotation();
            }
        }

        private void ApplyDeveloperReload()
        {
            WeaponController weapon = currentTank != null ? currentTank.GetComponent<WeaponController>() : null;
            weapon?.SetReloadOverride(developerFastReload ? 1f : 0f);
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
