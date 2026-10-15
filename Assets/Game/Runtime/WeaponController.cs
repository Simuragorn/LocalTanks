using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    [DefaultExecutionOrder(-250)]
    public sealed class WeaponController : MonoBehaviour
    {
        [SerializeField] private TankDefinition config;
        [SerializeField] private Projectile2D projectilePrefab;
        [SerializeField] private Transform muzzle;
        [SerializeField] private GunDispersionController dispersion;
        [SerializeField, Min(0f)] private float reloadOverrideSeconds;

        private readonly ReloadTimer reloadTimer = new ReloadTimer();
        private readonly ReloadTimer shotIntervalTimer = new ReloadTimer();
        private Transform projectileOwnerRoot;
        private Transform projectilePoolRoot;
        private readonly Queue<Projectile2D> idleProjectiles = new Queue<Projectile2D>();
        private readonly List<Projectile2D> allProjectiles = new List<Projectile2D>();
        private int roundsRemaining;
        private ShotRandomStream shotRandomStream;

        /// <summary>
        /// Match rules that decide whether a shell may damage a machine of its own team. Set by
        /// <see cref="BattleDirector"/> when the machine is spawned, so the rule travels from the scenario
        /// through the weapon into the shell instead of being looked up from a global inside the projectile.
        /// A weapon outside a battle keeps the default, which allows friendly fire.
        /// </summary>
        public MatchRules MatchRules { get; set; } = new MatchRules();
        public CombatRules CombatRules => MatchRules?.combat ?? fallbackCombatRules;
        public ShellDefinition CurrentShell => config != null && config.weapon != null ? config.weapon.shell : null;

        private readonly CombatRules fallbackCombatRules = new CombatRules();

        public event Action<WeaponController> Fired;
        public event Action<WeaponController> ReloadStarted;
        public event Action<WeaponController> StateChanged;

        public bool IsReady => reloadTimer.IsReady && shotIntervalTimer.IsReady && roundsRemaining > 0;
        public bool IsReloading => !reloadTimer.IsReady;
        public Transform Muzzle => muzzle;
        public float RemainingReloadSeconds => reloadTimer.IsReady ? shotIntervalTimer.Remaining : reloadTimer.Remaining;
        public int RoundsRemaining => roundsRemaining;
        public int MagazineSize => config != null && config.weapon != null ? Mathf.Max(1, config.weapon.magazineSize) : 1;
        /// <summary>A multi-round magazine is fired as one tactical burst rather than as separate cannon shots.</summary>
        public bool IsBurstWeapon => MagazineSize > 1;
        public bool IsWaitingForNextBurstRound =>
            IsBurstWeapon && !IsReloading && roundsRemaining > 0 && !shotIntervalTimer.IsReady;
        public float ReloadProgress
        {
            get
            {
                if (IsReady) return 1f;
                if (!reloadTimer.IsReady && EffectiveReloadSeconds > 0f)
                    return 1f - Mathf.Clamp01(reloadTimer.Remaining / EffectiveReloadSeconds);
                float interval = EffectiveShotIntervalSeconds;
                return interval <= 0f ? 1f : 1f - Mathf.Clamp01(shotIntervalTimer.Remaining / interval);
            }
        }
        public float EffectiveReloadSeconds => reloadOverrideSeconds > 0f
            ? reloadOverrideSeconds
            : config != null && config.weapon != null
                ? config.weapon.reloadSeconds
                : 0f;
        public float EffectiveShotIntervalSeconds => config != null && config.weapon != null
            ? config.weapon.shotIntervalSeconds
            : 0f;
        public int CreatedProjectileCount => allProjectiles.Count;
        public int IdleProjectileCount => idleProjectiles.Count;

        public void Configure(
            TankDefinition newConfig,
            Projectile2D newProjectilePrefab,
            Transform newMuzzle,
            GunDispersionController newDispersion = null)
        {
            config = newConfig;
            projectilePrefab = newProjectilePrefab;
            muzzle = newMuzzle;
            dispersion = newDispersion != null ? newDispersion : GetComponent<GunDispersionController>();
            roundsRemaining = MagazineSize;
            reloadTimer.Reset();
            shotIntervalTimer.Reset();
            ResolveProjectileOwner();
            EnsureProjectileReserve();
            StateChanged?.Invoke(this);
        }

        private void Awake()
        {
            if (dispersion == null) dispersion = GetComponent<GunDispersionController>();
            if (roundsRemaining <= 0) roundsRemaining = MagazineSize;
            ResolveProjectileOwner();
            EnsureProjectileReserve();
        }

        public void SetReloadOverride(float seconds)
        {
            reloadOverrideSeconds = Mathf.Max(0f, seconds);
            if (reloadOverrideSeconds > 0f && reloadTimer.Remaining > reloadOverrideSeconds)
            {
                reloadTimer.Start(reloadOverrideSeconds);
            }
        }

        /// <summary>
        /// Gives every weapon in a battle the same one-way stream. The director creates it once; a weapon
        /// outside a battle lazily creates a private fallback so laboratory prefabs remain self-contained.
        /// </summary>
        public void ConfigureMatchRules(MatchRules rules, ShotRandomStream randomStream)
        {
            MatchRules = rules ?? new MatchRules();
            shotRandomStream = randomStream ?? throw new ArgumentNullException(nameof(randomStream));
        }

        private void FixedUpdate()
        {
            reloadTimer.Tick(Time.deltaTime);
            shotIntervalTimer.Tick(Time.deltaTime);
            if (reloadTimer.IsReady && roundsRemaining <= 0)
            {
                roundsRemaining = MagazineSize;
                StateChanged?.Invoke(this);
            }
        }

        public bool TryStartReload()
        {
            if (!reloadTimer.IsReady || roundsRemaining >= MagazineSize || EffectiveReloadSeconds <= 0f)
            {
                return false;
            }

            roundsRemaining = 0;
            reloadTimer.Start(EffectiveReloadSeconds);
            ReloadStarted?.Invoke(this);
            StateChanged?.Invoke(this);
            return true;
        }

        public bool TryFire()
        {
            WeaponDefinition weapon = config != null ? config.weapon : null;
            ShellDefinition shell = weapon != null ? weapon.shell : null;
            if (!IsReady || shell == null || projectilePrefab == null || muzzle == null)
            {
                return false;
            }

            Vector2 shotDirection = dispersion != null
                ? dispersion.ApplyToDirection(muzzle.up)
                : (Vector2)muzzle.up;
            ShotRandomStream randomStream = shotRandomStream ??=
                new ShotRandomStream(LocalTanksOverrides.RequestedSeed(MatchRules?.randomSeed ?? 1701));
            ShotRandomization randomization = randomStream.Next(CombatRules);
            Projectile2D projectile = RentProjectile();
            if (projectile == null)
            {
                return false;
            }
            projectile.transform.SetPositionAndRotation(muzzle.position, muzzle.rotation);
            projectile.transform.up = shotDirection;
            projectile.Initialize(
                shotDirection,
                shell,
                projectileOwnerRoot != null ? projectileOwnerRoot : transform,
                MatchRules,
                randomization);
            TeamMember shooter = projectileOwnerRoot != null
                ? projectileOwnerRoot.GetComponent<TeamMember>()
                : GetComponentInParent<TeamMember>();
            BattleTelemetry.Active?.RecordFire(shooter, shell.id, randomization);
            roundsRemaining--;
            if (roundsRemaining <= 0)
            {
                reloadTimer.Start(EffectiveReloadSeconds);
                ReloadStarted?.Invoke(this);
            }
            else
            {
                shotIntervalTimer.Start(EffectiveShotIntervalSeconds);
            }
            Fired?.Invoke(this);
            StateChanged?.Invoke(this);
            return true;
        }

        private void ResolveProjectileOwner()
        {
            TankHealth ownerHealth = GetComponentInParent<TankHealth>();
            projectileOwnerRoot = ownerHealth != null ? ownerHealth.transform : transform;
        }

        private void EnsureProjectileReserve()
        {
            if (projectilePrefab == null) return;
            int reserve = Mathf.Clamp(MagazineSize, 1, 16);
            while (allProjectiles.Count < reserve)
            {
                CreatePooledProjectile();
            }
        }

        private Projectile2D RentProjectile()
        {
            EnsureProjectileReserve();
            if (idleProjectiles.Count == 0)
            {
                CreatePooledProjectile();
            }
            Projectile2D projectile = idleProjectiles.Count > 0 ? idleProjectiles.Dequeue() : null;
            if (projectile == null) return null;
            projectile.gameObject.SetActive(true);
            return projectile;
        }

        private Projectile2D CreatePooledProjectile()
        {
            if (projectilePrefab == null) return null;
            EnsureProjectilePoolRoot();
            Projectile2D projectile = Instantiate(projectilePrefab, projectilePoolRoot);
            projectile.name = $"Projectile_{allProjectiles.Count:00}";
            projectile.SetPoolReturn(ReturnProjectile);
            projectile.gameObject.SetActive(false);
            allProjectiles.Add(projectile);
            idleProjectiles.Enqueue(projectile);
            return projectile;
        }

        private void ReturnProjectile(Projectile2D projectile)
        {
            if (projectile == null || idleProjectiles.Contains(projectile)) return;
            projectile.gameObject.SetActive(false);
            projectile.transform.SetParent(projectilePoolRoot, false);
            idleProjectiles.Enqueue(projectile);
        }

        private void EnsureProjectilePoolRoot()
        {
            if (projectilePoolRoot != null) return;
            GameObject root = new GameObject($"{name}_ProjectilePool");
            BattleDirector director = GetComponentInParent<BattleDirector>();
            if (director != null)
            {
                root.transform.SetParent(director.transform, false);
            }
            projectilePoolRoot = root.transform;
        }

        private void OnDestroy()
        {
            if (projectilePoolRoot != null)
            {
                Destroy(projectilePoolRoot.gameObject);
            }
        }
    }
}
