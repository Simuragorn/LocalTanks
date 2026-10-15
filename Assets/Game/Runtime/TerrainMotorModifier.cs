using UnityEngine;

namespace LocalTanks
{
    [RequireComponent(typeof(TankMotor), typeof(Rigidbody2D))]
    [DefaultExecutionOrder(-10)]
    public sealed class TerrainMotorModifier : MonoBehaviour
    {
        [SerializeField] private NavigationMap map;
        [SerializeField] private TankMotor motor;
        private Rigidbody2D body;

        public TerrainKind CurrentTerrain { get; private set; }

        public void Configure(NavigationMap navigationMap, TankMotor tankMotor)
        {
            map = navigationMap;
            motor = tankMotor;
            ApplyCurrentCell();
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
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

            Vector2Int cell = map.WorldToCell(body != null ? body.position : (Vector2)transform.position);
            CurrentTerrain = map.GetTerrain(cell);
            motor.SetTerrainModifiers(
                map.GetSpeedMultiplier(cell),
                map.GetAccelerationMultiplier(cell),
                CurrentTerrain == TerrainKind.ShallowWater ? 8f : 1f);
        }
    }
}
