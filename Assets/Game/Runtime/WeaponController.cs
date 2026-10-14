using System;
using UnityEngine;

namespace LocalTanks
{
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
            StateChanged?.Invoke(this);
        }

        private void Awake()
        {
            if (dispersion == null) dispersion = GetComponent<GunDispersionController>();
            if (roundsRemaining <= 0) roundsRemaining = MagazineSize;
            ResolveProjectileOwner();
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

        private void Update()
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
            Projectile2D projectile = Instantiate(projectilePrefab, muzzle.position, muzzle.rotation);
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
    }
}
