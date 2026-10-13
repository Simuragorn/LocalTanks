using System.Collections;
using System.Collections.Generic;
using LocalTanks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LocalTanks.Tests
{
    /// <summary>
    /// Checks the generated effect library itself, not the code that plays it.
    /// </summary>
    /// <remarks>
    /// A prefab can exist, have a material and still emit nothing: a disabled emission module, a burst
    /// outside the duration, a lifetime shorter than the first frame. None of that is visible in the
    /// profile file or in a code review, and all of it looks exactly like "the effect layer does nothing".
    /// The test therefore instantiates every generated prefab, runs its own simulation and looks at the
    /// particles, which works headless because particle simulation is not rendering.
    /// </remarks>
    public sealed class CombatVfxLibraryPlayModeTests
    {
        /// <summary>Step used to advance a simulation; small enough to catch short flashes.</summary>
        private const float StepSeconds = 0.05f;

        [UnityTest]
        public IEnumerator EveryGeneratedEffect_ExistsAndIsUsable()
        {
            foreach (string effectId in CombatVfxEffects.All())
            {
                GameObject prefab = Load(effectId);
                Assert.That(prefab, Is.Not.Null,
                    $"Effect '{effectId}' has no prefab at Resources/{CombatVfxEffects.ResourceFolder}/{effectId}; " +
                    "run 'Local Tanks/Build Combat Vfx Assets'.");

                ParticleSystem[] systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
                Assert.That(systems.Length, Is.GreaterThan(0), $"Effect '{effectId}' contains no particle system.");

                foreach (ParticleSystem system in systems)
                {
                    ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
                    Assert.That(renderer.sharedMaterial, Is.Not.Null,
                        $"Effect '{effectId}' has a system without a material, so it would draw nothing.");
                    Assert.That(renderer.sharedMaterial.shader, Is.Not.Null,
                        $"Effect '{effectId}' has a material without a shader.");

                    // The rig owns the rate of every persistent effect, so the prefab must be built to run
                    // unattended: if it were stopped, setting a rate on it would never produce a particle.
                    Assert.That(system.main.playOnAwake, Is.EqualTo(CombatVfxEffects.IsLoop(effectId)),
                        $"Effect '{effectId}' has the wrong playOnAwake for a " +
                        $"{(CombatVfxEffects.IsLoop(effectId) ? "persistent" : "one-shot")} effect.");
                }
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator EveryGeneratedEffect_EmitsParticles()
        {
            foreach (string effectId in CombatVfxEffects.All())
            {
                GameObject instance = Object.Instantiate(Load(effectId));
                try
                {
                    ParticleSystem[] systems = instance.GetComponentsInChildren<ParticleSystem>(true);
                    Restart(systems);

                    int peak = 0;
                    for (float elapsed = 0f; elapsed < 1.5f; elapsed += StepSeconds)
                    {
                        Advance(systems, StepSeconds);
                        peak = Mathf.Max(peak, CountParticles(systems));
                    }

                    Assert.That(peak, Is.GreaterThan(0),
                        $"Effect '{effectId}' emitted no particles at all in 1.5 seconds of its own simulation.");
                }
                finally
                {
                    Object.Destroy(instance);
                }

                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator OneShotEffects_StopOnTheirOwn()
        {
            foreach (string effectId in CombatVfxEffects.OneShots)
            {
                GameObject instance = Object.Instantiate(Load(effectId));
                try
                {
                    ParticleSystem[] systems = instance.GetComponentsInChildren<ParticleSystem>(true);
                    Restart(systems);

                    // Advance just past the moment the last particle of this prefab must be gone, rather
                    // than a fixed guess: a burst with a two-second smoke trail and a two-frame flash do
                    // not settle at the same time.
                    float settle = SettleSeconds(systems);
                    for (float elapsed = 0f; elapsed < settle; elapsed += StepSeconds)
                    {
                        Advance(systems, StepSeconds);
                    }

                    Assert.That(CountParticles(systems), Is.EqualTo(0),
                        $"One-shot effect '{effectId}' still has live particles after {settle:0.##} seconds, " +
                        "so a pooled instance would never be quiet.");
                }
                finally
                {
                    Object.Destroy(instance);
                }

                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator TrackMarkSprites_AreOneWorldUnitAcross()
        {
            foreach (string variant in CombatVfxEffects.MarkVariants)
            {
                Sprite sprite = Resources.Load<Sprite>(CombatVfxEffects.MarkSpritePath(variant));

                Assert.That(sprite, Is.Not.Null,
                    $"Print pattern '{variant}' is missing at Resources/{CombatVfxEffects.MarkSpritePath(variant)}; " +
                    "the machines of that band would lay no prints at all.");
                Assert.That(sprite.bounds.size.x, Is.EqualTo(1f).Within(0.01f),
                    "A print pattern must be one world unit across, so the emitter's scale is the print size.");
                Assert.That(sprite.bounds.size.y, Is.EqualTo(1f).Within(0.01f));
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator TrackMarkEmitter_LaysPrintsWithTheGeneratedSprite()
        {
            GameObject owner = new GameObject("TrackMarkLibraryOwner");
            try
            {
                TankTrackMarkEmitter emitter = owner.AddComponent<TankTrackMarkEmitter>();
                emitter.Configure(
                    Resources.Load<Sprite>(CombatVfxEffects.MarkSpritePath(CombatVfxEffects.TrackMarkHeavy)),
                    8);

                emitter.Emit(Vector3.zero, Quaternion.identity, 0.12f, 0.16f, Color.white, 5f);

                Assert.That(emitter.EmittedCount, Is.EqualTo(1));
                Assert.That(emitter.SkippedCount, Is.EqualTo(0),
                    "A print with the generated sprite must not be skipped.");
                Assert.That(emitter.RendererCount, Is.EqualTo(1));
            }
            finally
            {
                Object.Destroy(owner);
            }

            yield return null;
        }

        /// <summary>
        /// The rig must attach every persistent effect of a real machine.
        /// </summary>
        /// <remarks>
        /// This is the check that was missing. The first version looked for one particle system on the
        /// root of each generated prefab, while the generator puts the systems on children, so every loop
        /// effect was silently switched off: the machines raised no dust, showed no damage smoke, never
        /// burned and had no exhaust, and the profile file, the prefabs and the asset checks all looked
        /// correct. A test that counts what the rig actually attached is the only thing that notices.
        /// </remarks>
        [UnityTest]
        public IEnumerator Rig_AttachesEveryGeneratedLoop()
        {
            // The library provider answers nothing in batchmode on purpose, so the test installs a provider
            // that reads the real generated assets without that guard.
            CombatVfxSystem system = CombatVfxSystem.Ensure(new LibraryAssetProvider());
            GameObject tank = new GameObject("VfxRigProbe");
            try
            {
                TankDefinition definition = ScriptableObject.CreateInstance<TankDefinition>();
                definition.id = "probe";
                definition.displayName = "Probe";
                definition.massTonnes = 7f;
                definition.maxHitPoints = 100;
                definition.maxForwardSpeed = 2f;
                definition.maxReverseSpeed = 1f;
                definition.hullTurnSpeed = 40f;

                tank.AddComponent<Rigidbody2D>().gravityScale = 0f;
                PolygonCollider2D collider = tank.AddComponent<PolygonCollider2D>();
                collider.points = new[]
                {
                    new Vector2(-0.25f, -0.5f), new Vector2(0.25f, -0.5f),
                    new Vector2(0.25f, 0.5f), new Vector2(-0.25f, 0.5f)
                };

                TankMotor motor = tank.AddComponent<TankMotor>();
                motor.Configure(definition);
                TankHealth health = tank.AddComponent<TankHealth>();
                health.Configure(definition);
                GameObject muzzle = new GameObject("Muzzle");
                muzzle.transform.SetParent(tank.transform, false);
                WeaponController weapon = tank.AddComponent<WeaponController>();
                weapon.Configure(definition, null, muzzle.transform);

                TankVfxRig rig = tank.AddComponent<TankVfxRig>();
                rig.Configure(definition, motor, health, weapon, trackSeparation: 0.4f);

                yield return null;

                Assert.That(rig.IsBuilt, Is.True);
                // Two dust plumes, two sprays, damage smoke, the flame and its embers, the exhaust and the
                // barrel plume. The count is a minimum, not an exact figure, so a richer prefab still passes.
                Assert.That(rig.AttachedLoopSystems, Is.GreaterThanOrEqualTo(9),
                    "The rig attached " + rig.AttachedLoopSystems + " of the persistent effect systems. When " +
                    "this is zero the machine shows no dust, no damage smoke, no fire and no exhaust while " +
                    "every asset check still passes.");

                Object.Destroy(definition);
            }
            finally
            {
                Object.Destroy(tank);
                Object.Destroy(system.gameObject);
            }

            yield return null;
        }

        /// <summary>Reads the generated library without the batchmode silence of the runtime provider.</summary>
        private sealed class LibraryAssetProvider : ICombatVfxAssetProvider
        {
            public GameObject LoadPrefab(string effectId)
            {
                return Resources.Load<GameObject>($"{CombatVfxEffects.ResourceFolder}/{effectId}");
            }

            public Sprite LoadMarkSprite(string variant)
            {
                return Resources.Load<Sprite>(CombatVfxEffects.MarkSpritePath(variant));
            }
        }

        private static GameObject Load(string effectId)
        {
            // Loaded directly instead of through the runtime provider: the provider deliberately answers
            // nothing in batchmode, and this test exists to inspect what was generated.
            return Resources.Load<GameObject>($"{CombatVfxEffects.ResourceFolder}/{effectId}");
        }

        /// <summary>
        /// Longest life any particle of a prefab can still have. Used as the moment a one-shot is
        /// certainly over instead of a fixed guess for every effect.
        /// </summary>
        private static float SettleSeconds(ParticleSystem[] systems)
        {
            float settle = 0.5f;
            foreach (ParticleSystem system in systems)
            {
                ParticleSystem.MainModule main = system.main;
                settle = Mathf.Max(settle, main.duration + main.startLifetime.constantMax + 0.25f);
            }

            return settle;
        }

        /// <summary>
        /// Puts every system of a prefab back to its first frame.
        /// </summary>
        /// <remarks>
        /// A stopped particle system ignores a plain simulation step: the simulation only advances a
        /// system that was explicitly restarted, so a test that just calls <c>Simulate</c> on a freshly
        /// created prefab measures zero particles and would report every effect as broken.
        /// </remarks>
        private static void Restart(ParticleSystem[] systems)
        {
            foreach (ParticleSystem system in systems)
            {
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                system.Clear(true);
                system.Simulate(0.001f, true, true);
            }
        }

        private static void Advance(ParticleSystem[] systems, float step)
        {
            foreach (ParticleSystem system in systems)
            {
                system.Simulate(step, true, false);
            }
        }

        private static int CountParticles(ParticleSystem[] systems)
        {
            int total = 0;
            foreach (ParticleSystem system in systems)
            {
                total += system.particleCount;
            }

            return total;
        }
    }
}
