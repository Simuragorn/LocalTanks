using UnityEngine;

namespace LocalTanks
{
    public sealed class TestRangeTargetRespawner : MonoBehaviour
    {
        [SerializeField] private GameObject[] tankPrefabs;
        [SerializeField] private string[] targetNames;
        [SerializeField] private GameObject[] currentTargets;
        [SerializeField] private Vector3[] spawnPositions;
        [SerializeField] private float[] spawnRotations;
        [SerializeField] private float[] rotationSpeeds;

        public GameObject[] CurrentTargets => currentTargets;

        public void Configure(
            GameObject[] prefabs,
            string[] names,
            GameObject[] targets,
            Vector3[] positions,
            float[] rotations,
            float[] speeds)
        {
            tankPrefabs = prefabs;
            targetNames = names;
            currentTargets = targets;
            spawnPositions = positions;
            spawnRotations = rotations;
            rotationSpeeds = speeds;
        }

        public void RespawnAllTargets()
        {
            if (tankPrefabs == null)
            {
                return;
            }

            if (currentTargets == null || currentTargets.Length != tankPrefabs.Length)
            {
                currentTargets = new GameObject[tankPrefabs.Length];
            }

            for (int index = 0; index < tankPrefabs.Length; index++)
            {
                if (tankPrefabs[index] == null ||
                    spawnPositions == null || index >= spawnPositions.Length)
                {
                    continue;
                }

                GameObject previousTarget = currentTargets[index];
                if (previousTarget != null)
                {
                    previousTarget.SetActive(false);
                    Destroy(previousTarget);
                }

                float angle = spawnRotations != null && index < spawnRotations.Length
                    ? spawnRotations[index]
                    : 0f;
                GameObject target = Instantiate(
                    tankPrefabs[index],
                    spawnPositions[index],
                    Quaternion.Euler(0f, 0f, angle));
                target.name = GetTargetName(index);
                DisablePlayerControls(target);

                RotatingTankDisplay display = target.GetComponent<RotatingTankDisplay>();
                if (display == null)
                {
                    display = target.AddComponent<RotatingTankDisplay>();
                }

                float speed = rotationSpeeds != null && index < rotationSpeeds.Length
                    ? rotationSpeeds[index]
                    : 9f;
                display.Configure(speed);
                currentTargets[index] = target;
            }
        }

        private string GetTargetName(int index)
        {
            if (targetNames != null && index < targetNames.Length && !string.IsNullOrWhiteSpace(targetNames[index]))
            {
                return targetNames[index];
            }

            return $"Tank_{index + 1}_Target";
        }

        private static void DisablePlayerControls(GameObject target)
        {
            PlayerTankInput input = target.GetComponent<PlayerTankInput>();
            TankMotor motor = target.GetComponent<TankMotor>();
            TurretAiming turret = target.GetComponentInChildren<TurretAiming>();
            WeaponController weapon = target.GetComponent<WeaponController>();
            if (input != null) input.enabled = false;
            if (motor != null) motor.enabled = false;
            if (turret != null) turret.enabled = false;
            if (weapon != null) weapon.enabled = false;
        }
    }
}
