using System;
using UnityEngine;

namespace LocalTanks
{
    public sealed class WeaponController : MonoBehaviour
    {
        [SerializeField] private TankDefinition config;
        [SerializeField] private Projectile2D projectilePrefab;
        [SerializeField] private Transform muzzle;
        [SerializeField, Min(0f)] private float reloadOverrideSeconds;

        private readonly ReloadTimer reloadTimer = new ReloadTimer();

        public event Action<WeaponController> Fired;

        public bool IsReady => reloadTimer.IsReady;
        public float EffectiveReloadSeconds => reloadOverrideSeconds > 0f
            ? reloadOverrideSeconds
            : config != null && config.weapon != null
                ? config.weapon.reloadSeconds
                : 0f;

        public void Configure(
            TankDefinition newConfig,
            Projectile2D newProjectilePrefab,
            Transform newMuzzle)
        {
            config = newConfig;
            projectilePrefab = newProjectilePrefab;
            muzzle = newMuzzle;
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

            Projectile2D projectile = Instantiate(projectilePrefab, muzzle.position, muzzle.rotation);
            projectile.Initialize(
                muzzle.up,
                shell,
                transform.root);
            reloadTimer.Start(EffectiveReloadSeconds);
            Fired?.Invoke(this);
            return true;
        }
    }
}
