using System.Collections.Generic;
using System.Linq;
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
        /// <summary>
        /// Art for one library object, bound when the scene is built. A sprite reference cannot live in
        /// the map asset, because that asset is generated from JSON and the library is local-only, so the
        /// scene carries the mapping and this component resolves it by id at load time.
        /// </summary>
        [System.Serializable]
        public sealed class SpriteBinding
        {
            public string spriteId;
            public Sprite sprite;

            [Tooltip("Physical size of the drawn object in source metres, used to scale it to its footprint.")]
            public Vector2 worldSizeMeters;

            /// <summary>
            /// What the library object is: a building, a rock, or vegetation. Obstacles and plants are
            /// drawn from separate families, so a tree can never stand in for a boulder. That mix-up is
            /// what put burnt-tree art on rocks, where it read as debris that could not be driven through.
            /// </summary>
            public string family;
        }

        [SerializeField] private WoTMapAsset map;
        [SerializeField] private SpriteBinding[] sprites = System.Array.Empty<SpriteBinding>();

        [Tooltip("Set when the scene is inspected and the generated objects must be visible in the editor.")]
        [SerializeField] private bool buildOnAwake = true;

        private Transform obstacleRoot;
        private Transform vegetationRoot;
        private readonly System.Collections.Generic.Dictionary<string, SpriteBinding> spriteLookup =
            new System.Collections.Generic.Dictionary<string, SpriteBinding>();

        public int BuiltObstacles { get; private set; }
        public int BuiltPlants { get; private set; }
        public int SpritesApplied { get; private set; }
        public WoTMapAsset Map => map;

        /// <summary>How much concealment a bush gives. Matches the value the other arenas use.</summary>
        private const float BushConcealmentBonus = 0.18f;

        /// <summary>How much concealment a standing tree gives: it hides a hull outright.</summary>
        private const float TreeConcealmentBonus = 0.45f;

        public void Configure(WoTMapAsset asset)
        {
            map = asset;
        }

        /// <summary>Binds the art the scene builder resolved from the environment library.</summary>
        public void ConfigureSprites(SpriteBinding[] bindings)
        {
            sprites = bindings ?? System.Array.Empty<SpriteBinding>();
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
            spriteLookup.Clear();
            foreach (SpriteBinding binding in sprites)
            {
                if (binding != null && !string.IsNullOrWhiteSpace(binding.spriteId) && binding.sprite != null)
                {
                    spriteLookup[binding.spriteId] = binding;
                }
            }

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

        /// <summary>
        /// Builds the obstacles that read as parts of the battlefield. Only barriers are built: a drawn
        /// object implies a wall, so the map now shows exactly what a tank can and cannot drive through.
        /// Small clutter — single boulders, carts, low walls, roughly a thousand and a half objects on
        /// Mines — is left out entirely rather than drawn as scenery a player would expect to stop them.
        /// Keeping collision, routing and art on one predicate is what makes that promise hold.
        /// </summary>
        private void BuildObstacles()
        {
            WoTMapObstacle[] obstacles = map.obstacles;
            if (obstacles == null) return;

            foreach (WoTMapObstacle obstacle in obstacles)
            {
                if (obstacle == null || obstacle.size.x <= 0f || obstacle.size.y <= 0f) continue;
                if (!map.IsSubstantial(obstacle)) continue;

                GameObject target = new GameObject(obstacle.id);
                target.transform.SetParent(obstacleRoot, false);
                target.transform.localPosition = new Vector3(obstacle.center.x, obstacle.center.y, 0f);
                target.transform.localRotation = Quaternion.Euler(0f, 0f, obstacle.rotationDegrees);

                bool solid = map.IsSolid(obstacle);
                AddShape(target, obstacle, !solid);

                if (solid && !obstacle.blocksProjectiles)
                {
                    target.AddComponent<FirePassThroughObstacle>();
                }

                if (obstacle.blocksVision)
                {
                    target.AddComponent<VisionBlocker>();
                }

                AddSprite(target, obstacle.sourceResource, obstacle.category, obstacle.size,
                    allowVegetation: false);

                BuiltObstacles++;
            }
        }

        /// <summary>
        /// Draws the art chosen for this object, scaled so the sprite occupies the footprint the pipeline
        /// measured. The art hangs on a child transform on purpose: scaling the object itself would scale
        /// its collider too, and the collider is already the measured size. The library draws each object
        /// at its true physical size, so the scale is the ratio of wanted to drawn size rather than a
        /// constant, which lets one sprite serve small and large instances of a family.
        /// </summary>
        /// <param name="allowVegetation">
        /// Whether vegetation art may be used. Plants allow it and obstacles do not, so a boulder can never
        /// be drawn as a burnt tree. Keeping the two families apart is what stopped debris art appearing on
        /// solid rocks, where it promised an obstacle the art did not show.
        /// </param>
        private void AddSprite(
            GameObject target,
            string sourceResource,
            string category,
            Vector2 wantedSize,
            bool allowVegetation)
        {
            List<string> allowed = MapSpriteMatcher.OfFamily(
                spriteLookup.Select(entry => new KeyValuePair<string, string>(entry.Key, entry.Value.family)),
                allowVegetation);

            string spriteId = MapSpriteMatcher.Resolve(sourceResource, category, allowed);
            if (spriteId == null || !spriteLookup.TryGetValue(spriteId, out SpriteBinding binding))
            {
                return;
            }

            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(target.transform, false);
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = binding.sprite;
            // Walls draw under the units that pass them, vegetation draws over them so cover reads as cover.
            renderer.sortingOrder = allowVegetation ? -3 : -4;

            Vector2 drawn = binding.sprite.bounds.size;
            if (drawn.x > 0.0001f && drawn.y > 0.0001f && wantedSize.x > 0.0001f && wantedSize.y > 0.0001f)
            {
                visual.transform.localScale = new Vector3(wantedSize.x / drawn.x, wantedSize.y / drawn.y, 1f);
            }

            SpritesApplied++;
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
                bool isTree = plant.kind == MapCoverKind.Tree;
                GameObject target = new GameObject(isTree ? "Tree" : "Bush");
                target.transform.SetParent(vegetationRoot, false);
                target.transform.localPosition = new Vector3(plant.center.x, plant.center.y, 0f);

                // Vegetation is cover, never a wall. Its collider is a trigger, exactly as the River
                // Crossing arena builds its concealment, so a tank drives through and a shell flies
                // through. A standing tree used to carry a solid collider because it can be knocked down,
                // which made forests stop both movement and shooting - the one thing cover must not do.
                CircleCollider2D circle = target.AddComponent<CircleCollider2D>();
                circle.radius = Mathf.Max(0.2f, plant.radius);
                circle.isTrigger = true;

                // A tree hides a tank outright; a bush only shades it. The zone also fades the sprite as
                // the player drives close, so cover cannot hide the player's own machine or the ground.
                target.AddComponent<ConcealmentZone>().Configure(isTree ? TreeConcealmentBonus : BushConcealmentBonus);

                AddSprite(target, plant.sourceResource, isTree ? "tree" : "bush",
                    new Vector2(plant.radius * 2f, plant.radius * 2f), allowVegetation: true);
                BuiltPlants++;
            }
        }
    }
}
