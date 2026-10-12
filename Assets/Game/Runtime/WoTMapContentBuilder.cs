using UnityEngine;

namespace LocalTanks
{
    /// <summary>
    /// Fills a battle scene with the obstacles and vegetation of an imported arena at load time.
    /// <para>
    /// The content is built here rather than saved into the scene because the converted Mines carries
    /// 5413 obstacles and 3058 plants: writing them as scene objects made one scene file twenty
    /// megabytes, which is too much to version, review or open comfortably. The scene keeps the map
    /// reference and this component rebuilds the same colliders deterministically, so the arena stays a
    /// data-driven artifact and the scene stays small.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WoTMapContentBuilder : MonoBehaviour
    {
        [SerializeField] private WoTMapAsset map;

        [Tooltip("Set when the scene is inspected and the generated objects must be visible in the editor.")]
        [SerializeField] private bool buildOnAwake = true;

        private Transform obstacleRoot;
        private Transform vegetationRoot;

        public int BuiltObstacles { get; private set; }
        public int BuiltPlants { get; private set; }
        public WoTMapAsset Map => map;

        public void Configure(WoTMapAsset asset)
        {
            map = asset;
        }

        private void Awake()
        {
            if (!buildOnAwake || map == null)
            {
                return;
            }

            Build();
        }

        /// <summary>
        /// Creates every collider the arena needs. Safe to call once; calling it again rebuilds from
        /// scratch instead of stacking duplicates.
        /// </summary>
        public void Build()
        {
            if (map == null)
            {
                Debug.LogError("WoT map content builder has no map asset; nothing to build.", this);
                return;
            }

            Clear();
            obstacleRoot = CreateRoot("Obstacles");
            vegetationRoot = CreateRoot("Vegetation");
            BuildObstacles();
            BuildVegetation();
        }

        private void Clear()
        {
            for (int index = transform.childCount - 1; index >= 0; index--)
            {
                Transform child = transform.GetChild(index);
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
        }

        private Transform CreateRoot(string name)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(transform, false);
            return root.transform;
        }

        private void BuildObstacles()
        {
            WoTMapObstacle[] obstacles = map.obstacles;
            if (obstacles == null) return;

            foreach (WoTMapObstacle obstacle in obstacles)
            {
                if (obstacle == null || obstacle.size.x <= 0f || obstacle.size.y <= 0f) continue;

                GameObject target = new GameObject(obstacle.id);
                target.transform.SetParent(obstacleRoot, false);
                target.transform.localPosition = new Vector3(obstacle.center.x, obstacle.center.y, 0f);
                target.transform.localRotation = Quaternion.Euler(0f, 0f, obstacle.rotationDegrees);

                // One collider per obstacle. A projectile that passes through still has to stop a tank,
                // so the shape is a solid collider and the pass-through is expressed by the marker
                // component the gunnery code already understands, not by dropping the collider.
                bool solid = IsSolidFor(obstacle);
                AddShape(target, obstacle, !solid);

                if (solid && !obstacle.blocksProjectiles)
                {
                    target.AddComponent<FirePassThroughObstacle>();
                }

                if (obstacle.blocksVision)
                {
                    target.AddComponent<VisionBlocker>();
                }

                BuiltObstacles++;
            }
        }

        /// <summary>
        /// Whether an obstacle is a physical wall rather than something a tank pushes through. This
        /// mirrors the routing rule so that what stops a tank and what steers a route agree: scatter,
        /// fences, vines, logs and carts are removable clutter, while buildings and cliffs are solid.
        /// </summary>
        private bool IsSolidFor(WoTMapObstacle obstacle)
        {
            if (!obstacle.blocksMovement) return false;
            if (map.navigationPolicy == MapNavigationPolicy.Never) return false;
            if (map.navigationPolicy == MapNavigationPolicy.AllMovementBlockers) return true;

            // Substantial ground: anything the routing rule refuses to cross is also solid to drive into.
            return obstacle.blocksNavigation;
        }

        /// <summary>
        /// Adds the collider matching the measured footprint. A rectangle becomes a box, a rounded rock
        /// becomes a circle, and an object that neither stops a tank nor blocks sight is a trigger so it
        /// only carries its own facts.
        /// </summary>
        private static void AddShape(GameObject target, WoTMapObstacle obstacle, bool triggerOnly)
        {
            if (obstacle.shape == MapObstacleShape.Ellipse || obstacle.shape == MapObstacleShape.Circle)
            {
                CircleCollider2D circle = target.AddComponent<CircleCollider2D>();
                // The pipeline emits an ellipse; the smaller radius keeps the collider inside the drawn
                // footprint instead of letting it stick out along the long axis.
                circle.radius = Mathf.Min(obstacle.size.x, obstacle.size.y) * 0.5f;
                circle.isTrigger = triggerOnly;
            }
            else
            {
                BoxCollider2D box = target.AddComponent<BoxCollider2D>();
                box.size = obstacle.size;
                box.isTrigger = triggerOnly;
            }
        }

        /// <summary>
        /// Plants become concealment volumes. A standing tree also blocks movement, so it gets a solid
        /// collider; a bush only hides, so its volume is a trigger.
        /// </summary>
        private void BuildVegetation()
        {
            WoTMapVegetation[] plants = map.vegetation;
            if (plants == null) return;

            foreach (WoTMapVegetation plant in plants)
            {
                if (plant == null) continue;
                GameObject target = new GameObject(plant.kind == MapCoverKind.Tree ? "Tree" : "Bush");
                target.transform.SetParent(vegetationRoot, false);
                target.transform.localPosition = new Vector3(plant.center.x, plant.center.y, 0f);

                CircleCollider2D circle = target.AddComponent<CircleCollider2D>();
                circle.radius = Mathf.Max(0.2f, plant.radius);
                circle.isTrigger = !plant.destructible;
                BuiltPlants++;
            }
        }
    }
}
