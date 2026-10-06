using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LocalTanks.Tests
{
    public sealed class PrototypePlayModeTests
    {
        [UnityTest]
        public IEnumerator BattleTestRange_LoadsPlayerAndCamera()
        {
            SceneManager.LoadScene("Battle_TestRange");
            yield return null;

            Assert.That(GameObject.Find("TigerII_Player"), Is.Not.Null);
            Assert.That(Camera.main, Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator Projectile_IsDestroyedWhenLifetimeExpires()
        {
            GameObject projectileObject = new GameObject("LifetimeTestProjectile");
            Projectile2D projectile = projectileObject.AddComponent<Projectile2D>();
            projectile.Initialize(Vector2.up, 0f, 0.05f, 0.01f, 10f, null);

            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.That(projectile == null, Is.True);
        }
    }
}
