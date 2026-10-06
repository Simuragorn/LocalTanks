using UnityEngine;

namespace LocalTanks
{
    public sealed class WeaponController : MonoBehaviour
    {
        [SerializeField] private TankPrototypeConfig config;
        [SerializeField] private Projectile2D projectilePrefab;
        [SerializeField] private Transform muzzle;

        private readonly ReloadTimer reloadTimer = new ReloadTimer();

        public bool IsReady => reloadTimer.IsReady;

        public void Configure(
            TankPrototypeConfig newConfig,
            Projectile2D newProjectilePrefab,
            Transform newMuzzle)
        {
            config = newConfig;
            projectilePrefab = newProjectilePrefab;
            muzzle = newMuzzle;
        }

        private void Update()
        {
            reloadTimer.Tick(Time.deltaTime);
        }

        public bool TryFire()
        {
            if (!reloadTimer.IsReady || config == null || projectilePrefab == null || muzzle == null)
            {
                return false;
            }

            Projectile2D projectile = Instantiate(projectilePrefab, muzzle.position, muzzle.rotation);
            projectile.Initialize(
                muzzle.up,
                config.projectileSpeed,
                config.projectileRadius,
                config.projectileLifetime,
                config.projectileRange,
                transform.root);
            reloadTimer.Start(config.reloadSeconds);
            return true;
        }
    }
}
