using UnityEngine;

namespace LocalTanks
{
    [RequireComponent(typeof(Collider2D))]
    public sealed class DestructibleObstacle : MonoBehaviour
    {
        [SerializeField, Min(1)] private int maximumHitPoints = 500;
        [SerializeField] private NavigationMap map;
        [SerializeField] private Vector2Int[] occupiedCells;
        [SerializeField] private TerrainKind destroyedTerrain = TerrainKind.Grass;
        [SerializeField, Min(0.01f)] private float destroyedNavigationCost = 1.25f;
        [SerializeField, Min(0.05f)] private float destroyedSpeedMultiplier = 0.85f;
        [SerializeField, Min(0.05f)] private float destroyedAccelerationMultiplier = 0.85f;

        private Collider2D obstacleCollider;

        public int CurrentHitPoints { get; private set; }
        public bool IsDestroyed => CurrentHitPoints <= 0;

        private void Awake()
        {
            obstacleCollider = GetComponent<Collider2D>();
            CurrentHitPoints = maximumHitPoints;
        }

        public void Configure(
            NavigationMap navigationMap,
            Vector2Int[] cells,
            int hitPoints,
            TerrainKind replacementTerrain = TerrainKind.Grass,
            float replacementNavigationCost = 1.25f,
            float replacementSpeedMultiplier = 0.85f,
            float replacementAccelerationMultiplier = 0.85f)
        {
            map = navigationMap;
            occupiedCells = cells;
            maximumHitPoints = Mathf.Max(1, hitPoints);
            destroyedTerrain = replacementTerrain;
            destroyedNavigationCost = Mathf.Max(0.01f, replacementNavigationCost);
            destroyedSpeedMultiplier = Mathf.Max(0.05f, replacementSpeedMultiplier);
            destroyedAccelerationMultiplier = Mathf.Max(0.05f, replacementAccelerationMultiplier);
            CurrentHitPoints = maximumHitPoints;
            if (obstacleCollider == null)
            {
                obstacleCollider = GetComponent<Collider2D>();
            }
        }

        public void ApplyDamage(int amount)
        {
            if (IsDestroyed || amount <= 0)
            {
                return;
            }

            CurrentHitPoints = Mathf.Max(0, CurrentHitPoints - amount);
            if (!IsDestroyed)
            {
                return;
            }

            if (obstacleCollider != null)
            {
                obstacleCollider.enabled = false;
            }

            foreach (SpriteRenderer renderer in GetComponentsInChildren<SpriteRenderer>())
            {
                renderer.color = new Color(0.22f, 0.18f, 0.12f, 0.35f);
            }

            if (map != null && occupiedCells != null)
            {
                foreach (Vector2Int cell in occupiedCells)
                {
                    map.SetTerrain(
                        cell,
                        destroyedTerrain,
                        destroyedNavigationCost,
                        destroyedSpeedMultiplier,
                        destroyedAccelerationMultiplier,
                        false);
                }
            }
        }
    }
}
