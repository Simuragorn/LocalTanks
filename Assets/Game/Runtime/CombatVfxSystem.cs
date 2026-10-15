using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LocalTanks
{
    /// <summary>
    /// The single owner of the combat visual effect library: it holds the committed profile catalog, the
    /// bounded pool every temporary effect comes from and the reactions to events no single machine owns.
    /// </summary>
    /// <remarks>
    /// It is attached by <see cref="BattleDirector"/> the same way the audio and diagnostic layers are, so
    /// no scene holds a reference to it and no prefab has to name an effect. Per-machine effects (tracks,
    /// dust, damage smoke) live in <see cref="TankVfxRig"/> and ask this system for the catalog and the
    /// pool, which keeps the resolution of "what a 45 mm gun looks like" in exactly one place.
    /// <para>
    /// Everything here is optional by construction. A missing profile file, a missing prefab and a
    /// batchmode run all leave the battle identical and simply draw nothing; the alternative — throwing
    /// or creating broken references — would make the visual layer able to break the simulation.
    /// </para>
    /// </remarks>
    public sealed class CombatVfxSystem : MonoBehaviour
    {
        private const string PoolContainerName = "CombatVfxPool";
        private const string MarkContainerName = "CombatVfxMarks";
        // Seven automatic cannons can overlap four smoke lifetimes each before the first effect returns.
        // Preparing that real 7x7 burst during loading is cheaper than growing the pool during contact.
        private const int SalvoPrewarmCount = 32;
        private const int DestructionPrewarmCount = 8;

        private readonly List<Action> unsubscribers = new List<Action>();
        private readonly CombatVfxPool pool = new CombatVfxPool();

        private BattleDirector director;
        private CombatVfxCatalog catalog;
        private ICombatVfxAssetProvider assetProvider;
        private NavigationMap navigationMap;
        private GameObject poolContainer;
        private GameObject markContainer;
        private string status = "визуальные эффекты не подключены";

        /// <summary>The system a battle is using. Null before one exists, which is a valid state.</summary>
        public static CombatVfxSystem Current { get; private set; }

        public CombatVfxCatalog Catalog => catalog;
        public CombatVfxPool Pool => pool;
        public ICombatVfxAssetProvider AssetProvider => assetProvider;
        public TeamVisionSystem VisionSystem => director != null ? director.VisionSystem : null;
        public string Status => status;
        public bool HasCatalog => catalog != null;
        public int ArmorImpactEffects { get; private set; }
        public int SurfaceImpactEffects { get; private set; }

        /// <summary>
        /// Attaches the visual layer to a battle. Called from <see cref="BattleDirector.Awake"/> so the
        /// catalog is ready before the first machine is spawned, and reuses a system that already exists
        /// in the scene instead of creating a second pool.
        /// </summary>
        public static CombatVfxSystem Attach(BattleDirector battleDirector, ICombatVfxAssetProvider provider = null)
        {
            if (battleDirector == null)
            {
                return null;
            }

            CombatVfxSystem system = Current;
            if (system == null)
            {
                system = battleDirector.gameObject.GetComponent<CombatVfxSystem>();
                if (system == null)
                {
                    system = battleDirector.gameObject.AddComponent<CombatVfxSystem>();
                }
            }

            system.Configure(battleDirector, provider);
            return system;
        }

        /// <summary>
        /// Returns the system in use, creating a standalone one when a scene has no battle director. The
        /// test range has no battle to attach to, and it is exactly the place where one machine and its
        /// effects are examined, so a rig may bring the layer up by itself.
        /// </summary>
        public static CombatVfxSystem Ensure(ICombatVfxAssetProvider provider = null)
        {
            if (Current != null)
            {
                if (provider != null)
                {
                    Current.Configure(Current.director, provider);
                }

                return Current;
            }

            GameObject host = new GameObject("CombatVfx");
            CombatVfxSystem system = host.AddComponent<CombatVfxSystem>();
            system.Configure(null, provider);
            return system;
        }

        public void Configure(BattleDirector battleDirector, ICombatVfxAssetProvider provider = null)
        {
            Unsubscribe();
            director = battleDirector;
            navigationMap = battleDirector != null ? battleDirector.NavigationMap : navigationMap;
            assetProvider = provider ?? assetProvider ?? new ResourcesCombatVfxAssetProvider();
            catalog ??= CombatVfxCatalog.Load();
            pool.Configure(assetProvider, EnsurePoolContainer(), catalog.limits?.maxPooledInstances ?? 192,
                catalog.limits?.qualityScale ?? 1f);
            if (battleDirector != null)
            {
                // Automatic cannon bursts can need several copies before the first one has faded. Build
                // that reserve while the battle is loading instead of on the frames that receive damage.
                pool.Prewarm(CombatVfxEffects.ImpactSpark, SalvoPrewarmCount);
                pool.Prewarm(CombatVfxEffects.ImpactDust, SalvoPrewarmCount);
                pool.Prewarm(CombatVfxEffects.ImpactDebris, SalvoPrewarmCount);
                // Spotting often makes several bots fire within the same few physics ticks. The muzzle
                // flash and smoke used to instantiate their particle hierarchies on those first shots,
                // which looked like a detection hitch even while the FPS counter stayed near 60.
                pool.Prewarm(CombatVfxEffects.MuzzleFlash, SalvoPrewarmCount);
                pool.Prewarm(CombatVfxEffects.MuzzleSmoke, SalvoPrewarmCount);
                pool.Prewarm(CombatVfxEffects.DestructionBurst, DestructionPrewarmCount);
            }
            Subscribe();

            int errors = catalog.Validate().Length;
            status = errors == 0
                ? $"визуальные эффекты готовы: {catalog.weaponProfiles.Length} калибров, " +
                  $"{catalog.movementProfiles.Length} масс, {catalog.surfaceProfiles.Length} покрытий"
                : $"профиль эффектов содержит {errors} ошибок";
        }

        /// <summary>Replaces the effect library, which tests use to install their own assets.</summary>
        public void SetCatalog(CombatVfxCatalog replacement)
        {
            catalog = replacement ?? new CombatVfxCatalog();
            pool.Configure(assetProvider, EnsurePoolContainer(), catalog.limits?.maxPooledInstances ?? 192,
                catalog.limits?.qualityScale ?? 1f);
        }

        public void ResetStatistics()
        {
            ArmorImpactEffects = 0;
            SurfaceImpactEffects = 0;
            pool.ResetStatistics();
        }

        /// <summary>Plays a pooled one-shot effect. Does nothing when the effect has no generated prefab.</summary>
        public GameObject Play(VfxEmission emission)
        {
            return pool.Play(emission);
        }

        /// <summary>Surface the given world point belongs to, from the battle's navigation map.</summary>
        public TerrainKind TerrainAt(Vector3 position)
        {
            if (navigationMap == null)
            {
                return TerrainKind.Grass;
            }

            return navigationMap.GetTerrain(navigationMap.WorldToCell(position));
        }

        public SurfaceVfxProfile SurfaceAt(Vector3 position)
        {
            return catalog?.ResolveSurface(TerrainAt(position));
        }

        /// <summary>Container for track prints, shared by every machine. Never a child of a tank.</summary>
        public Transform MarkContainer()
        {
            if (markContainer == null)
            {
                markContainer = CreateRootContainer(MarkContainerName);
            }

            return markContainer.transform;
        }

        /// <summary>
        /// Impact on armour. The result decides the look; the point and the normal come from the armour
        /// that was hit, because the shared observation struct deliberately carries no coordinates.
        /// </summary>
        public void PlayArmorImpact(TankArmor armor, ArmorImpactObservation observation)
        {
            if (armor == null || catalog == null)
            {
                return;
            }

            ImpactVfxProfile profile = catalog.ResolveImpact(observation.Outcome);
            if (profile == null)
            {
                return;
            }

            SurfaceVfxProfile surface = SurfaceAt(armor.LastImpactPoint);
            Color dustTint = surface != null ? surface.dustTint.ToColor() : Color.white;
            Quaternion rotation = PlaneRotation(armor.LastImpactNormal);
            Vector3 point = armor.LastImpactPoint;

            if (profile.sparkCount > 0)
            {
                Play(VfxEmission.At(CombatVfxEffects.ImpactSpark, point, rotation, profile.flashScale)
                    .WithCount(profile.sparkCount));
            }

            if (profile.dustScale > 0f)
            {
                Play(VfxEmission.At(CombatVfxEffects.ImpactDust, point, rotation, profile.dustScale)
                    .WithTint(dustTint));
            }

            if (profile.debrisCount > 0)
            {
                Play(VfxEmission.At(CombatVfxEffects.ImpactDebris, point, rotation, profile.flashScale)
                    .WithCount(profile.debrisCount)
                    .WithTint(dustTint));
            }

            ArmorImpactEffects++;
        }

        /// <summary>Impact on the map: ground, an obstacle or water. Called by <see cref="Projectile2D"/>.</summary>
        public void PlaySurfaceImpact(Vector3 point, Vector2 normal, TerrainKind terrain, float sizeScale = 1f)
        {
            ImpactVfxProfile profile = catalog?.GroundImpact;
            if (profile == null)
            {
                return;
            }

            SurfaceVfxProfile surface = catalog.ResolveSurface(terrain);
            Color tint = surface != null ? surface.dustTint.ToColor() : Color.white;
            float surfaceDust = surface != null ? Mathf.Max(0.05f, surface.dustRateMultiplier) : 1f;
            float size = Mathf.Max(0.05f, sizeScale);
            Quaternion rotation = PlaneRotation(normal);

            if (profile.dustScale > 0f)
            {
                Play(VfxEmission.At(CombatVfxEffects.ImpactDust, point, rotation, profile.dustScale * surfaceDust * size)
                    .WithTint(tint));
            }

            if (profile.debrisCount > 0)
            {
                Play(VfxEmission.At(CombatVfxEffects.ImpactDebris, point, rotation, profile.flashScale * size)
                    .WithCount(profile.debrisCount)
                    .WithTint(tint));
            }

            SurfaceImpactEffects++;
        }

        /// <summary>Rotation that points the local +Y axis of an effect along a world direction.</summary>
        public static Quaternion PlaneRotation(Vector2 direction)
        {
            if (direction.sqrMagnitude <= 0.000001f)
            {
                return Quaternion.identity;
            }

            float degrees = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            return Quaternion.Euler(0f, 0f, degrees - 90f);
        }

        private void Awake()
        {
            Current = this;
        }

        private void OnDestroy()
        {
            Unsubscribe();
            pool.Destroy();
            if (Current == this)
            {
                Current = null;
            }

            DestroyContainer(ref poolContainer);
            DestroyContainer(ref markContainer);
        }

        private void Update()
        {
            using var performanceScope = BattlePerformanceProbe.Measure(BattlePerformanceSubsystem.CombatVfx);
            pool.Tick(Time.deltaTime);
        }

        private void Subscribe()
        {
            if (director == null)
            {
                return;
            }

            director.TankSpawned += OnTankSpawned;
            foreach (TeamMember member in director.SpawnedMembers)
            {
                HookMember(member);
            }
        }

        private void Unsubscribe()
        {
            if (director != null)
            {
                director.TankSpawned -= OnTankSpawned;
            }

            foreach (Action unsubscribe in unsubscribers)
            {
                unsubscribe();
            }

            unsubscribers.Clear();
        }

        private void OnTankSpawned(TeamMember member)
        {
            HookMember(member);
        }

        /// <summary>
        /// Watches the impacts of one machine. Only events no machine owns on its own are hooked here;
        /// the muzzle flash, the tracks and the damage smoke belong to the machine's own rig.
        /// </summary>
        private void HookMember(TeamMember member)
        {
            if (member == null)
            {
                return;
            }

            TankArmor armor = member.GetComponentInChildren<TankArmor>();
            if (armor == null)
            {
                return;
            }

            Action<TankArmor, ArmorImpactObservation> impact = OnArmorImpact;
            armor.ImpactResolved += impact;
            unsubscribers.Add(() => armor.ImpactResolved -= impact);
        }

        private void OnArmorImpact(TankArmor armor, ArmorImpactObservation observation)
        {
            PlayArmorImpact(armor, observation);
        }

        private Transform EnsurePoolContainer()
        {
            if (poolContainer == null)
            {
                poolContainer = CreateRootContainer(PoolContainerName);
            }

            return poolContainer.transform;
        }

        /// <summary>
        /// Creates an identity root object in this scene. The pool must not live under the battle
        /// coordinator: an inherited position or scale would move and stretch every effect, and the rig
        /// transforms already carry the world placement of each machine.
        /// </summary>
        private GameObject CreateRootContainer(string containerName)
        {
            GameObject container = new GameObject(containerName);
            // Thousands of pooled particles and tread prints make the Editor redraw a huge Hierarchy on
            // every change. Hiding only their generated root keeps the battle view identical while
            // removing that Editor-only UI cost; diagnostics still find inactive children by type.
            container.hideFlags = HideFlags.HideInHierarchy;
            container.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            container.transform.localScale = Vector3.one;
            if (gameObject.scene.IsValid())
            {
                SceneManager.MoveGameObjectToScene(container, gameObject.scene);
            }

            return container;
        }

        private static void DestroyContainer(ref GameObject container)
        {
            if (container != null)
            {
                Destroy(container);
                container = null;
            }
        }
    }

    /// <summary>
    /// Entry point for effects raised where no machine exists: a shell that hit the map. The projectile
    /// must not know about the effect layer, exactly as it does not know about the audio layer, so this
    /// thin façade forwards to whatever system a battle installed and stays silent when there is none.
    /// </summary>
    public static class CombatVfxEmitter
    {
        /// <summary>
        /// A shell hit the map. The surface is looked up from the battle's navigation map, so the
        /// projectile does not need to know which cell it landed in.
        /// </summary>
        public static void ShellImpact(Vector3 point, Vector2 normal, float shellRadius)
        {
            CombatVfxSystem system = CombatVfxSystem.Current;
            if (system == null)
            {
                return;
            }

            system.PlaySurfaceImpact(
                point,
                normal,
                system.TerrainAt(point),
                VfxProfileMath.ImpactScaleForRadius(shellRadius));
        }

        public static void SurfaceImpact(Vector3 point, Vector2 normal, TerrainKind terrain, float sizeScale = 1f)
        {
            CombatVfxSystem.Current?.PlaySurfaceImpact(point, normal, terrain, sizeScale);
        }
    }
}
