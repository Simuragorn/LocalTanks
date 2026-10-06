using UnityEngine;

namespace LocalTanks
{
    public sealed class WeaponController : MonoBehaviour
    {
        [SerializeField] private TankDefinition config;
        [SerializeField] private Projectile2D projectilePrefab;
        [SerializeField] private Transform muzzle;

        private readonly ReloadTimer reloadTimer = new ReloadTimer();

        public bool IsReady => reloadTimer.IsReady;

        public void Configure(
            TankDefinition newConfig,
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
            reloadTimer.Start(weapon.reloadSeconds);
            return true;
        }
    }
}
