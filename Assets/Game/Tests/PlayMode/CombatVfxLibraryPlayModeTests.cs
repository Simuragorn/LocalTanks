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
        public IEnumerator TrackMarkSprite_IsOneWorldUnitAcross()
        {
            Sprite sprite = Resources.Load<Sprite>(CombatVfxEffects.MarkSpritePath);

            Assert.That(sprite, Is.Not.Null,
                $"The track print sprite is missing at Resources/{CombatVfxEffects.MarkSpritePath}; the emitter " +
                "would lay no prints at all.");
            Assert.That(sprite.bounds.size.x, Is.EqualTo(1f).Within(0.01f),
                "The print sprite must be one world unit wide, so the emitter's scale is the print size.");
            Assert.That(sprite.bounds.size.y, Is.EqualTo(1f).Within(0.01f));

            yield return null;
        }

        [UnityTest]
        public IEnumerator TrackMarkEmitter_LaysPrintsWithTheGeneratedSprite()
        {
            GameObject owner = new GameObject("TrackMarkLibraryOwner");
            try
            {
                TankTrackMarkEmitter emitter = owner.AddComponent<TankTrackMarkEmitter>();
                emitter.Configure(Resources.Load<Sprite>(CombatVfxEffects.MarkSpritePath), 8);

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
