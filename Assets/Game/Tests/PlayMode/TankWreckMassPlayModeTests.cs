using System.Collections;
using LocalTanks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LocalTanks.Tests
{
    /// <summary>
    /// A live tank is moved by <see cref="TankMotor"/> through MovePosition, so an
    /// external push is overwritten every physics step and the tank behaves as if it
    /// could not be moved at all. A wreck loses that motor, so its body must keep its
    /// real mass and gain the ground resistance of jammed tracks; otherwise it slides
    /// as if it had no mass.
    /// </summary>
    public sealed class TankWreckMassPlayModeTests
    {
        private const string Ms1PrefabPath = "Assets/Game/Prefabs/Tanks/MS1_Player.prefab";

        private const float PushImpulse = 0.5f;
        private const int MeasuredSteps = 50;

        [UnityTest]
        public IEnumerator Wreck_KeepsMassAndGainsGroundResistance()
        {
            GameObject tank = Object.Instantiate(LoadPrefab(Ms1PrefabPath));
            tank.transform.position = new Vector3(500f, 500f, 0f);

            Rigidbody2D body = tank.GetComponent<Rigidbody2D>();
            TankHealth health = tank.GetComponent<TankHealth>();
            Assert.That(body, Is.Not.Null, "The tank prefab must carry a Rigidbody2D.");
            Assert.That(health, Is.Not.Null, "The tank prefab must carry TankHealth.");

            float authoredMass = body.mass;
            RigidbodyType2D authoredBodyType = body.bodyType;
            float authoredLinearDamping = body.linearDamping;
            float authoredAngularDamping = body.angularDamping;

            Assert.That(authoredMass, Is.EqualTo(health.Definition.massTonnes).Within(0.0001f),
                "The prefab must take its mass from the tank definition.");
            Assert.That(authoredMass, Is.GreaterThan(1f),
                "A tank must not keep the default Rigidbody2D mass.");

            health.ApplyDamage(health.Definition.maxHitPoints);
            Assert.That(health.IsDestroyed, Is.True, "Full damage must destroy the tank.");

            try
            {
                Assert.That(body.mass, Is.EqualTo(authoredMass).Within(0.0001f),
                    "Destroying a tank must not change its mass.");
                Assert.That(body.bodyType, Is.EqualTo(authoredBodyType),
                    "A wreck must remain a physical obstacle that can be pushed.");
                Assert.That(body.linearDamping, Is.EqualTo(TankDestroyedState.WreckLinearDampingValue).Within(0.0001f),
                    "A wreck must receive the wreck ground resistance.");
                Assert.That(body.angularDamping, Is.EqualTo(TankDestroyedState.WreckAngularDampingValue).Within(0.0001f));
                Assert.That(body.linearDamping, Is.GreaterThan(authoredLinearDamping),
                    "Jammed tracks must resist sliding more than a tank that is still on its tracks.");
                Assert.That(body.angularDamping, Is.GreaterThan(authoredAngularDamping));
            }
            finally
            {
                Object.Destroy(tank);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Wreck_ResistsTheSamePushMoreThanAFrictionlessBody()
        {
            GameObject tank = Object.Instantiate(LoadPrefab(Ms1PrefabPath));
            tank.transform.position = new Vector3(500f, 500f, 0f);

            Rigidbody2D body = tank.GetComponent<Rigidbody2D>();
            TankHealth health = tank.GetComponent<TankHealth>();

            // Disable active behaviour so the wreck is a pure physics body, as after death.
            foreach (MonoBehaviour behaviour in tank.GetComponents<MonoBehaviour>())
            {
                behaviour.enabled = false;
            }

            health.enabled = true;
            health.ApplyDamage(health.Definition.maxHitPoints);
            Assert.That(health.IsDestroyed, Is.True, "Full damage must destroy the tank.");
            Assert.That(body.bodyType, Is.EqualTo(RigidbodyType2D.Dynamic),
                "The measurement requires a dynamic wreck body.");

            float frictionlessTravel = 0f;
            float wreckTravel = 0f;
            yield return MeasurePushTravel(body, 0f, value => frictionlessTravel = value);
            yield return MeasurePushTravel(body, TankDestroyedState.WreckLinearDampingValue, value => wreckTravel = value);

            try
            {
                Assert.That(frictionlessTravel, Is.GreaterThan(0.05f),
                    "The control push must move the body, otherwise the comparison proves nothing.");
                Assert.That(wreckTravel, Is.LessThan(frictionlessTravel),
                    $"The same push moved a frictionless wreck {frictionlessTravel:0.###} m but the wreck with " +
                    $"ground resistance only {wreckTravel:0.###} m; the resistance must reduce sliding.");
                Assert.That(wreckTravel, Is.LessThan(frictionlessTravel * 0.5f),
                    "Wreck ground resistance must be strong enough to be noticeable.");
                Assert.That(wreckTravel, Is.GreaterThan(0f),
                    "A wreck must still be pushable, only harder to push.");
            }
            finally
            {
                Object.Destroy(tank);
            }

            yield return null;
        }

        private static IEnumerator MeasurePushTravel(Rigidbody2D body, float linearDamping, System.Action<float> report)
        {
            body.linearDamping = linearDamping;
            body.angularDamping = linearDamping * 2f;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.position = new Vector2(500f, 500f);
            body.Sleep();
            yield return new WaitForFixedUpdate();

            Vector2 start = body.position;
            body.AddForce(new Vector2(PushImpulse / Time.fixedDeltaTime, 0f), ForceMode2D.Force);
            yield return new WaitForFixedUpdate();

            for (int step = 1; step < MeasuredSteps; step++)
            {
                yield return new WaitForFixedUpdate();
            }

            report((body.position - start).magnitude);
            body.linearDamping = 0f;
            body.angularDamping = 0f;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }

        private static GameObject LoadPrefab(string path)
        {
            GameObject prefab = Resources.Load<GameObject>(path);
#if UNITY_EDITOR
            if (prefab == null)
            {
                prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
#endif
            Assert.That(prefab, Is.Not.Null, $"Prefab was not found at '{path}'.");
            return prefab;
        }
    }
}
