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
        private Transform projectileOwnerRoot;

        public event Action<WeaponController> Fired;

        public bool IsReady => reloadTimer.IsReady;
        public Transform Muzzle => muzzle;
        public float RemainingReloadSeconds => reloadTimer.Remaining;
        public float ReloadProgress => IsReady || EffectiveReloadSeconds <= 0f
            ? 1f
            : 1f - Mathf.Clamp01(reloadTimer.Remaining / EffectiveReloadSeconds);
        public float EffectiveReloadSeconds => reloadOverrideSeconds > 0f
            ? reloadOverrideSeconds
            : config != null && config.weapon != null
                ? config.weapon.reloadSeconds
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
            ResolveProjectileOwner();
        }

        private void Awake()
        {
            if (dispersion == null) dispersion = GetComponent<GunDispersionController>();
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

        private void Update()
        {
            reloadTimer.Tick(Time.deltaTime);
        }

        public bool TryFire()
        {
            WeaponDefinition weapon = config != null ? config.weapon : null;
            ShellDefinition shell = weapon != null ? weapon.shell : null;
            if (!reloadTimer.IsReady || shell == null || projectilePrefab == null || muzzle == null)
            {
                return false;
            }

            Vector2 shotDirection = dispersion != null
                ? dispersion.ApplyToDirection(muzzle.up)
                : (Vector2)muzzle.up;
            Projectile2D projectile = Instantiate(projectilePrefab, muzzle.position, muzzle.rotation);
            projectile.transform.up = shotDirection;
            projectile.Initialize(
                shotDirection,
                shell,
                projectileOwnerRoot != null ? projectileOwnerRoot : transform);
            reloadTimer.Start(EffectiveReloadSeconds);
            Fired?.Invoke(this);
            return true;
        }

        private void ResolveProjectileOwner()
        {
            TankHealth ownerHealth = GetComponentInParent<TankHealth>();
            projectileOwnerRoot = ownerHealth != null ? ownerHealth.transform : transform;
        }
    }
}
