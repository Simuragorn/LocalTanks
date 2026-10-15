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
        private bool[] active;
        private int cursor;
        private int liveCount;
        private Transform container;
        private Sprite sprite;
        private TeamId ownerTeam = TeamId.Neutral;
        private TeamVisionSystem visionSystem;
        private float visibilityRefreshSecondsLeft;
        private static int nextVisibilityPhase;

        private const float VisibilityRefreshSeconds = 0.2f;
        private const float FadeTailFraction = 0.35f;

        /// <summary>Draw order below the hull (5) and below every other effect of the machine.</summary>
        public const int SortingOrder = 4;

        public int Capacity { get; private set; }
        public int LiveCount => liveCount;
        public int VisibleCount
        {
            get
            {
                if (marks == null || active == null)
                {
                    return 0;
                }

                int count = 0;
                for (int slot = 0; slot < marks.Length; slot++)
                {
                    if (active[slot] && marks[slot] != null && marks[slot].enabled)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

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
            active = new bool[Capacity];
            cursor = 0;
            liveCount = 0;
        }

        /// <summary>
        /// Enemy prints are world evidence, not children of the enemy's silhouette. They remain active in
        /// the ring buffer but render only where the local team currently observes the ground.
        /// </summary>
        public void ConfigureVisibility(TeamId team, TeamVisionSystem vision)
        {
            ownerTeam = team;
            visionSystem = vision;
            // Tanks are constructed together. Giving every emitter the same refresh phase caused all
            // enemy trails to perform their visibility work on one frame every 0.2 seconds. A stable
            // per-instance phase spreads that cosmetic work over the interval.
            int phase = nextVisibilityPhase++ % 8;
            visibilityRefreshSecondsLeft = VisibilityRefreshSeconds * (phase + 1) / 8f;
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
            bool replacesLivePrint = active[slot];
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
            renderer.enabled = ShouldRender(position);

            ages[slot] = 0f;
            lifetimes[slot] = Mathf.Max(0.05f, fadeSeconds);
            colors[slot] = color;
            active[slot] = true;
            if (!replacesLivePrint)
            {
                liveCount = Mathf.Min(Capacity, liveCount + 1);
            }
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
            visibilityRefreshSecondsLeft -= step;
            bool refreshVisibility = visibilityRefreshSecondsLeft <= 0f;
            if (refreshVisibility)
            {
                visibilityRefreshSecondsLeft = VisibilityRefreshSeconds;
            }

            int remaining = 0;
            for (int slot = 0; slot < Capacity; slot++)
            {
                SpriteRenderer renderer = marks[slot];
                if (renderer == null || !active[slot])
                {
                    continue;
                }

                ages[slot] += step;
                float life = lifetimes[slot];
                if (ages[slot] >= life)
                {
                    active[slot] = false;
                    renderer.enabled = false;
                    continue;
                }

                float fadeStart = life * (1f - FadeTailFraction);
                if (ages[slot] >= fadeStart)
                {
                    Color color = colors[slot];
                    float fadeProgress = Mathf.InverseLerp(fadeStart, life, ages[slot]);
                    color.a = colors[slot].a * (1f - fadeProgress);
                    renderer.color = color;
                }
                if (refreshVisibility)
                {
                    renderer.enabled = ShouldRender(renderer.transform.position);
                }

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

            for (int slot = 0; slot < marks.Length; slot++)
            {
                SpriteRenderer renderer = marks[slot];
                if (renderer != null)
                {
                    renderer.enabled = false;
                }

                active[slot] = false;
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
            active = null;
            liveCount = 0;
        }

        private bool ShouldRender(Vector3 position)
        {
            if (visionSystem == null || ownerTeam == TeamId.Neutral ||
                ownerTeam == visionSystem.LocalPlayerTeam)
            {
                return true;
            }

            return visionSystem.IsWorldPositionVisibleToCached(
                visionSystem.LocalPlayerTeam,
                position);
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
