using UnityEngine;

namespace LocalTanks
{
    [RequireComponent(typeof(TankMotor))]
    public sealed class TerrainMotorModifier : MonoBehaviour
    {
        [SerializeField] private NavigationMap map;
        [SerializeField] private TankMotor motor;

        public TerrainKind CurrentTerrain { get; private set; }

        public void Configure(NavigationMap navigationMap, TankMotor tankMotor)
        {
            map = navigationMap;
            motor = tankMotor;
            ApplyCurrentCell();
        }

        private void Awake()
        {
            if (motor == null)
            {
                motor = GetComponent<TankMotor>();
            }
        }

        private void FixedUpdate()
        {
            ApplyCurrentCell();
        }

        private void ApplyCurrentCell()
        {
            if (map == null || motor == null)
            {
                return;
            }

            Vector2Int cell = map.WorldToCell(transform.position);
            CurrentTerrain = map.GetTerrain(cell);
            motor.SetTerrainModifiers(
                map.GetSpeedMultiplier(cell),
                map.GetAccelerationMultiplier(cell));
        }
    }
}
