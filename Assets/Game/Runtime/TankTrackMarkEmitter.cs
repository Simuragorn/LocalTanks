using UnityEngine;

namespace LocalTanks
{
    /// <summary>
    /// The prints two tracks leave on the ground.
    /// </summary>
    /// <remarks>
    /// Not a <c>TrailRenderer</c>: a trail has one direction for the whole machine, so on a pivot in place
    /// it twists around itself and folds over the hull. A print is placed at a fixed spacing along the
    /// path of one track, in the direction that track actually travelled, which is why a turn in place
    /// leaves two concentric arcs instead of a knot.
    /// <para>
    /// Slots are a ring buffer: the machine owns a fixed number of renderers for the whole battle, the
    /// oldest print is reused first, and nothing is created or destroyed while it drives. The sprite
    /// arrives from the generated effect library, so an unbuilt library simply leaves no prints.
    /// </para>
    /// </remarks>
    public sealed class TankTrackMarkEmitter : MonoBehaviour
    {
        private SpriteRenderer[] marks;
        private float[] ages;
        private float[] lifetimes;
        private Color[] colors;
        private int cursor;
        private int liveCount;
        private Transform container;
        private Sprite sprite;

        /// <summary>Draw order below the hull (5) and below every other effect of the machine.</summary>
        public const int SortingOrder = 4;

        public int Capacity { get; private set; }
        public int LiveCount => liveCount;

        /// <summary>Renderers this emitter has ever created. It can never exceed <see cref="Capacity"/>.</summary>
        public int RendererCount
        {
            get
            {
                if (marks == null)
                {
                    return 0;
                }

                int count = 0;
                foreach (SpriteRenderer renderer in marks)
                {
                    if (renderer != null)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int EmittedCount { get; private set; }
        public int ReusedCount { get; private set; }
        public int SkippedCount { get; private set; }

        /// <summary>Prepared by the rig once the machine knows its own size and the effect library exists.</summary>
        public void Configure(Sprite markSprite, int capacity)
        {
            sprite = markSprite;
            Capacity = Mathf.Clamp(capacity, 1, 512);
            marks = new SpriteRenderer[Capacity];
            ages = new float[Capacity];
            lifetimes = new float[Capacity];
            colors = new Color[Capacity];
            cursor = 0;
            liveCount = 0;
        }

        /// <summary>Places one print. Silently does nothing when the mark sprite was never generated.</summary>
        public void Emit(Vector3 position, Quaternion rotation, float width, float length, Color color, float fadeSeconds)
        {
            if (sprite == null || marks == null || Capacity == 0 || color.a <= 0.002f)
            {
                SkippedCount++;
                return;
            }

            int slot = cursor;
            cursor = (cursor + 1) % Capacity;
            SpriteRenderer renderer = marks[slot];
            if (renderer == null)
            {
                GameObject instance = new GameObject($"TrackMark_{slot:00}");
                instance.transform.SetParent(EnsureContainer(), false);
                renderer = instance.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingOrder = SortingOrder;
                marks[slot] = renderer;
            }
            else
            {
                ReusedCount++;
            }

            renderer.transform.SetPositionAndRotation(position, rotation);
            // The sprite is authored as a unit square with the print running along +Y, so the two scales
            // are the width and the length of this particular machine's track.
            renderer.transform.localScale = new Vector3(width, length, 1f);
            renderer.color = color;
            renderer.enabled = true;

            ages[slot] = 0f;
            lifetimes[slot] = Mathf.Max(0.05f, fadeSeconds);
            colors[slot] = color;
            liveCount = Mathf.Min(Capacity, liveCount + 1);
            EmittedCount++;
        }

        /// <summary>Fades every print and disables the ones that expired.</summary>
        public void Tick(float deltaTime)
        {
            if (marks == null || liveCount == 0)
            {
                return;
            }

            float step = Mathf.Max(0f, deltaTime);
            int remaining = 0;
            for (int slot = 0; slot < Capacity; slot++)
            {
                SpriteRenderer renderer = marks[slot];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                ages[slot] += step;
                float life = lifetimes[slot];
                if (ages[slot] >= life)
                {
                    renderer.enabled = false;
                    continue;
                }

                Color color = colors[slot];
                color.a = colors[slot].a * (1f - ages[slot] / life);
                renderer.color = color;
                remaining++;
            }

            liveCount = remaining;
        }

        /// <summary>Hides every print at once, used when a machine is torn down or respawned.</summary>
        public void Clear()
        {
            if (marks == null)
            {
                return;
            }

            foreach (SpriteRenderer renderer in marks)
            {
                if (renderer != null)
                {
                    renderer.enabled = false;
                }
            }

            liveCount = 0;
        }

        /// <summary>
        /// Takes the prints with the machine that laid them. Nobody would fade them afterwards, so they
        /// would stay on the ground at full strength forever — a respawned test-range target would leave a
        /// permanent scar behind.
        /// </summary>
        private void OnDestroy()
        {
            if (container != null)
            {
                Destroy(container.gameObject);
            }

            container = null;
            marks = null;
            liveCount = 0;
        }

        private Transform EnsureContainer()
        {
            if (container != null)
            {
                return container;
            }

            // Prints belong to the ground, not to the machine: parenting them to the tank would rotate
            // and drag them along with it. They go into the shared world-space container of the effect
            // system instead, which also keeps them out of the visibility presenter's sprite cache.
            GameObject parent = new GameObject($"TrackMarks_{name}");
            CombatVfxSystem system = CombatVfxSystem.Current;
            parent.transform.SetParent(system != null ? system.MarkContainer() : null, false);
            container = parent.transform;
            return container;
        }
    }
}
