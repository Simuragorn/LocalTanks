using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    /// <summary>
    /// Stable names of the generated effect prefabs. They are asset names, not data: the runtime, the
    /// Editor generator and the tests all read this one list, so a new effect cannot appear in the
    /// builder without appearing here.
    /// </summary>
    public static class CombatVfxEffects
    {
        public const string ResourceFolder = "CombatVfx";

        public const string MuzzleFlash = "muzzle_flash";
        public const string MuzzleSmoke = "muzzle_smoke";
        public const string ImpactSpark = "impact_spark";
        public const string ImpactDebris = "impact_debris";
        public const string ImpactDust = "impact_dust";
        public const string DestructionBurst = "destruction_burst";
        public const string TrackDust = "track_dust";
        public const string WaterSpray = "water_spray";
        public const string DamageSmoke = "damage_smoke";
        public const string DamageFire = "damage_fire";
        public const string ExhaustSmoke = "exhaust_smoke";
        public const string BarrelSmoke = "barrel_smoke";

        /// <summary>
        /// Print patterns. Several exist so that a fleet does not share one stamped tread, and each is
        /// reused by every machine of the band it belongs to rather than drawn per tank.
        /// </summary>
        public const string TrackMarkLight = "track_mark_light";
        public const string TrackMarkMedium = "track_mark_medium";
        public const string TrackMarkHeavy = "track_mark_heavy";

        /// <summary>Effects that are played once and returned to the pool.</summary>
        public static readonly string[] OneShots =
        {
            MuzzleFlash, MuzzleSmoke, ImpactSpark, ImpactDebris, ImpactDust, DestructionBurst
        };

        /// <summary>Effects that live on a machine and are driven by its state.</summary>
        public static readonly string[] Loops =
        {
            TrackDust, WaterSpray, DamageSmoke, DamageFire, ExhaustSmoke, BarrelSmoke
        };

        /// <summary>Every print pattern, in a stable order.</summary>
        public static readonly string[] MarkVariants =
        {
            TrackMarkLight, TrackMarkMedium, TrackMarkHeavy
        };

        /// <summary>Every effect the generator must produce, in a stable order.</summary>
        public static string[] All()
        {
            string[] all = new string[OneShots.Length + Loops.Length];
            Array.Copy(OneShots, all, OneShots.Length);
            Array.Copy(Loops, 0, all, OneShots.Length, Loops.Length);
            return all;
        }

        public static bool IsLoop(string effectId)
        {
            return Array.IndexOf(Loops, effectId) >= 0;
        }

        /// <summary>
        /// Print pattern of a movement band. The band is the honest key: it already says how heavy the
        /// machine is, and the pattern only has to differ between a light, a medium and a heavy track.
        /// </summary>
        public static string MarkVariantFor(string movementProfileId)
        {
            switch (movementProfileId)
            {
                case "light": return TrackMarkLight;
                case "heavy": return TrackMarkHeavy;
                default: return TrackMarkMedium;
            }
        }

        /// <summary>Resource path of one print pattern, which is what the runtime provider loads.</summary>
        public static string MarkSpritePath(string variant)
        {
            return $"{ResourceFolder}/{(string.IsNullOrWhiteSpace(variant) ? TrackMarkMedium : variant)}";
        }
    }

    /// <summary>
    /// Where effect prefabs come from. Replaceable so a test can supply its own template, exactly like
    /// the audio clip provider: the pool never reaches into <c>Resources</c> behind a test's back.
    /// </summary>
    public interface ICombatVfxAssetProvider
    {
        GameObject LoadPrefab(string effectId);
        Sprite LoadMarkSprite(string variant);
    }

    /// <summary>
    /// The runtime provider. Batchmode runs the automated suites on a build machine: there is no display,
    /// and creating particle systems whose frames nobody will ever see only makes the suites slower and
    /// noisier. Sound is silenced for the same reason, and the simulation is unaffected either way.
    /// </summary>
    public sealed class ResourcesCombatVfxAssetProvider : ICombatVfxAssetProvider
    {
        public GameObject LoadPrefab(string effectId)
        {
            if (Application.isBatchMode || string.IsNullOrWhiteSpace(effectId))
            {
                return null;
            }

            return Resources.Load<GameObject>($"{CombatVfxEffects.ResourceFolder}/{effectId}");
        }

        public Sprite LoadMarkSprite(string variant)
        {
            return Application.isBatchMode ? null : Resources.Load<Sprite>(CombatVfxEffects.MarkSpritePath(variant));
        }
    }

    /// <summary>One request to play an effect: where, how large, in which colour and how many particles.</summary>
    public readonly struct VfxEmission
    {
        public VfxEmission(
            string effectId,
            Vector3 position,
            Quaternion rotation,
            float scale,
            Color tint,
            bool applyTint,
            int count,
            float lifetimeSeconds)
        {
            EffectId = effectId;
            Position = position;
            Rotation = rotation;
            Scale = scale;
            Tint = tint;
            ApplyTint = applyTint;
            Count = count;
            Lifetime = lifetimeSeconds;
        }

        public string EffectId { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public float Scale { get; }
        public Color Tint { get; }
        public bool ApplyTint { get; }

        /// <summary>Particles in the first burst, or zero to keep the count the prefab was built with.</summary>
        public int Count { get; }

        public float Lifetime { get; }

        /// <summary>An effect that keeps the colour its prefab was built with.</summary>
        public static VfxEmission At(string effectId, Vector3 position, Quaternion rotation, float scale = 1f)
        {
            return new VfxEmission(effectId, position, rotation, scale, Color.white, false, 0, 0f);
        }

        public VfxEmission WithTint(Color tint)
        {
            return new VfxEmission(EffectId, Position, Rotation, Scale, tint, true, Count, Lifetime);
        }

        public VfxEmission WithScale(float scale)
        {
            return new VfxEmission(EffectId, Position, Rotation, scale, Tint, ApplyTint, Count, Lifetime);
        }

        public VfxEmission WithCount(int count)
        {
            return new VfxEmission(EffectId, Position, Rotation, Scale, Tint, ApplyTint, Mathf.Max(0, count), Lifetime);
        }

        public VfxEmission WithLifetime(float seconds)
        {
            return new VfxEmission(EffectId, Position, Rotation, Scale, Tint, ApplyTint, Count, seconds);
        }
    }

    /// <summary>
    /// A bounded pile of reusable effect instances.
    /// </summary>
    /// <remarks>
    /// Instantiate and destroy per shot is what makes a battle hitch: a 7×7 fight fires several shells a
    /// second and every one of them would create and later destroy a hierarchy of particle systems. Every
    /// instance here is created once, parked when it finishes and handed out again. The pile has a hard
    /// ceiling, and when it is reached the oldest live effect is recycled rather than a new object being
    /// created, so the object count cannot grow with the rate of fire.
    /// </remarks>
    public sealed class CombatVfxPool
    {
        private readonly Dictionary<string, Stack<GameObject>> idle = new Dictionary<string, Stack<GameObject>>(StringComparer.Ordinal);
        private readonly List<ActiveEffect> active = new List<ActiveEffect>();

        private ICombatVfxAssetProvider provider;
        private Transform container;
        private int capacity = 192;
        private float qualityScale = 1f;
        private int totalInstances;

        public int ActiveCount => active.Count;
        public int TotalInstances => totalInstances;
        public int CreatedCount { get; private set; }
        public int ReusedCount { get; private set; }
        public int DroppedCount { get; private set; }
        public int MissingAssetCount { get; private set; }
        public int Capacity => capacity;

        public void Configure(ICombatVfxAssetProvider assetProvider, Transform parent, int maxInstances, float quality)
        {
            provider = assetProvider;
            container = parent;
            capacity = Mathf.Clamp(maxInstances, 1, 4096);
            qualityScale = Mathf.Clamp(quality, 0.05f, 2f);
        }

        /// <summary>
        /// Creates a small idle reserve before the battle starts. This moves the cost of particle-system
        /// instantiation out of the first automatic-fire exchange without allowing the pool to exceed its
        /// normal hard ceiling.
        /// </summary>
        public void Prewarm(string effectId, int desiredInstances)
        {
            if (string.IsNullOrWhiteSpace(effectId) || provider == null || desiredInstances <= 0)
            {
                return;
            }

            int existing = 0;
            if (idle.TryGetValue(effectId, out Stack<GameObject> parked))
            {
                existing += parked.Count;
            }
            for (int index = 0; index < active.Count; index++)
            {
                if (active[index].EffectId == effectId && active[index].Instance != null)
                {
                    existing++;
                }
            }

            int requested = Mathf.Max(0, desiredInstances - existing);
            for (int index = 0; index < requested && totalInstances < capacity; index++)
            {
                GameObject instance = Create(effectId);
                if (instance == null)
                {
                    MissingAssetCount++;
                    break;
                }

                Park(new ActiveEffect(instance, effectId, 0f));
            }
        }

        /// <summary>
        /// Plays one effect and returns the instance it used, or null when the effect has no prefab yet
        /// or the pool refused the request. A missing prefab is a normal state: the battle runs before
        /// the assets are generated and after a quality setting removed them, and neither may break it.
        /// </summary>
        public GameObject Play(VfxEmission emission)
        {
            if (string.IsNullOrWhiteSpace(emission.EffectId) || provider == null)
            {
                return null;
            }

            GameObject instance = TakeFromIdle(emission.EffectId);
            if (instance == null)
            {
                if (totalInstances >= capacity)
                {
                    RecycleOldest();
                    instance = TakeFromIdle(emission.EffectId);
                    if (instance == null)
                    {
                        DroppedCount++;
                        return null;
                    }
                }
                else
                {
                    instance = Create(emission.EffectId);
                    if (instance == null)
                    {
                        MissingAssetCount++;
                        return null;
                    }
                }
            }

            Apply(instance, emission);
            active.Add(new ActiveEffect(instance, emission.EffectId, EstimateLifetime(instance, emission.Lifetime)));
            return instance;
        }

        /// <summary>Returns finished effects to the pile. Called once per frame by the system that owns it.</summary>
        public void Tick(float deltaTime)
        {
            if (active.Count == 0)
            {
                return;
            }

            float step = Mathf.Max(0f, deltaTime);
            for (int index = active.Count - 1; index >= 0; index--)
            {
                ActiveEffect effect = active[index];
                effect.RemainingSeconds -= step;
                active[index] = effect;
                if (effect.RemainingSeconds <= 0f)
                {
                    active.RemoveAt(index);
                    Park(effect);
                }
            }
        }

        /// <summary>Stops everything and returns every instance to the pile.</summary>
        public void Clear()
        {
            for (int index = 0; index < active.Count; index++)
            {
                Park(active[index]);
            }

            active.Clear();
        }

        /// <summary>Destroys every instance; used when the battle that owned the pool is gone.</summary>
        public void Destroy()
        {
            Clear();
            foreach (Stack<GameObject> stack in idle.Values)
            {
                while (stack.Count > 0)
                {
                    GameObject instance = stack.Pop();
                    if (instance != null)
                    {
                        UnityEngine.Object.Destroy(instance);
                    }
                }
            }

            idle.Clear();
            totalInstances = 0;
        }

        public void ResetStatistics()
        {
            CreatedCount = 0;
            ReusedCount = 0;
            DroppedCount = 0;
            MissingAssetCount = 0;
        }

        private GameObject Create(string effectId)
        {
            GameObject prefab = provider.LoadPrefab(effectId);
            if (prefab == null)
            {
                return null;
            }

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = $"CombatVfx_{effectId}";
            if (container != null)
            {
                instance.transform.SetParent(container, false);
            }

            totalInstances++;
            CreatedCount++;
            return instance;
        }

        private GameObject TakeFromIdle(string effectId)
        {
            if (!idle.TryGetValue(effectId, out Stack<GameObject> stack))
            {
                return null;
            }

            while (stack.Count > 0)
            {
                GameObject instance = stack.Pop();
                if (instance == null)
                {
                    totalInstances--;
                    continue;
                }

                instance.SetActive(true);
                ReusedCount++;
                return instance;
            }

            return null;
        }

        private void Park(ActiveEffect effect)
        {
            if (effect.Instance == null)
            {
                totalInstances--;
                return;
            }

            Stop(effect.Instance);
            effect.Instance.SetActive(false);
            if (!idle.TryGetValue(effect.EffectId, out Stack<GameObject> stack))
            {
                stack = new Stack<GameObject>();
                idle[effect.EffectId] = stack;
            }

            stack.Push(effect.Instance);
        }

        /// <summary>
        /// Frees the effect that has been alive longest. This is the ceiling doing its job: a burst of
        /// fire reuses its own oldest effect instead of growing the object count without limit.
        /// </summary>
        private void RecycleOldest()
        {
            if (active.Count == 0)
            {
                return;
            }

            ActiveEffect oldest = active[0];
            active.RemoveAt(0);
            Park(oldest);
        }

        private static void Stop(GameObject instance)
        {
            foreach (ParticleSystem system in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void Apply(GameObject instance, VfxEmission emission)
        {
            instance.transform.SetPositionAndRotation(emission.Position, emission.Rotation);
            instance.transform.localScale = Vector3.one * (Mathf.Max(0.01f, emission.Scale) * qualityScale);
            foreach (ParticleSystem system in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                system.Clear(true);
                if (emission.ApplyTint)
                {
                    ParticleSystem.MainModule main = system.main;
                    main.startColor = emission.Tint;
                }

                if (emission.Count > 0)
                {
                    ApplyBurstCount(system, emission.Count);
                }

                system.Play(true);
            }
        }

        /// <summary>
        /// Sets the size of the first burst. The count of sparks and debris is an art parameter of the
        /// impact band, so it is applied to the prefab instead of being baked into eleven prefabs that
        /// would then have to agree with the profile file.
        /// </summary>
        private static void ApplyBurstCount(ParticleSystem system, int count)
        {
            ParticleSystem.EmissionModule emission = system.emission;
            int burstCount = emission.burstCount;
            if (burstCount <= 0)
            {
                return;
            }

            ParticleSystem.Burst[] bursts = new ParticleSystem.Burst[burstCount];
            emission.GetBursts(bursts);
            bursts[0].count = count;
            emission.SetBursts(bursts);
        }

        /// <summary>
        /// How long an instance stays out of the pile. A prefab knows its own length, and a caller may
        /// override it when the effect is stretched by a parameter.
        /// </summary>
        private static float EstimateLifetime(GameObject instance, float requested)
        {
            if (requested > 0f)
            {
                return requested;
            }

            float longest = 0f;
            foreach (ParticleSystem system in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = system.main;
                longest = Mathf.Max(longest, main.duration + main.startLifetime.constantMax);
            }

            return longest > 0f ? longest : 1.5f;
        }

        private struct ActiveEffect
        {
            public ActiveEffect(GameObject instance, string effectId, float remainingSeconds)
            {
                Instance = instance;
                EffectId = effectId;
                RemainingSeconds = remainingSeconds;
            }

            public GameObject Instance;
            public string EffectId;
            public float RemainingSeconds;
        }
    }
}
