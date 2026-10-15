using System.Collections;
using System.Collections.Generic;
using LocalTanks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LocalTanks.Tests
{
    /// <summary>
    /// The two promises the visual layer makes to the rest of the game: temporary effects come from a
    /// bounded pool instead of being created per shot, and track prints come from a fixed ring buffer
    /// instead of growing with the distance a machine drives.
    /// </summary>
    public sealed class CombatVfxPoolPlayModeTests
    {
        private const string EffectId = CombatVfxEffects.MuzzleFlash;

        private readonly List<Object> spawned = new List<Object>();
        private GameObject template;
        private CombatVfxPool pool;
        private FakeAssetProvider provider;

        [SetUp]
        public void SetUp()
        {
            template = CreateTemplate();
            provider = new FakeAssetProvider(template);
            pool = new CombatVfxPool();
            pool.Configure(provider, null, 8, 1f);
        }

        [TearDown]
        public void TearDown()
        {
            pool?.Destroy();
            foreach (Object item in spawned)
            {
                if (item != null)
                {
                    Object.Destroy(item);
                }
            }

            spawned.Clear();
            if (template != null)
            {
                Object.Destroy(template);
            }
        }

        [UnityTest]
        public IEnumerator Pool_ReusesAnInstanceInsteadOfCreatingAnother()
        {
            GameObject first = pool.Play(VfxEmission.At(EffectId, Vector3.zero, Quaternion.identity).WithLifetime(0.5f));
            Assert.That(first, Is.Not.Null);

            pool.Tick(1f);
            GameObject second = pool.Play(VfxEmission.At(EffectId, Vector3.one, Quaternion.identity).WithLifetime(0.5f));

            Assert.That(second, Is.SameAs(first), "A finished effect belongs back in the pool, not in the bin.");
            Assert.That(pool.CreatedCount, Is.EqualTo(1));
            Assert.That(pool.ReusedCount, Is.EqualTo(1));

            yield return null;
        }

        [UnityTest]
        public IEnumerator Pool_PrewarmMovesBurstInstantiationBeforeTheFirstImpact()
        {
            pool.Prewarm(EffectId, 4);
            Assert.That(pool.TotalInstances, Is.EqualTo(4));
            Assert.That(pool.ActiveCount, Is.Zero);

            pool.ResetStatistics();
            for (int index = 0; index < 4; index++)
            {
                pool.Play(VfxEmission.At(EffectId, Vector3.one * index, Quaternion.identity).WithLifetime(1f));
            }

            Assert.That(pool.CreatedCount, Is.Zero,
                "The first automatic-fire burst must only rent already prepared particle systems.");
            Assert.That(pool.ReusedCount, Is.EqualTo(4));
            Assert.That(pool.TotalInstances, Is.EqualTo(4));

            yield return null;
        }

        [UnityTest]
        public IEnumerator Pool_StaysWithinItsBudget()
        {
            pool.Configure(provider, null, 2, 1f);
            for (int index = 0; index < 6; index++)
            {
                pool.Play(VfxEmission.At(EffectId, Vector3.one * index, Quaternion.identity).WithLifetime(5f));
            }

            Assert.That(pool.TotalInstances, Is.LessThanOrEqualTo(2),
                "A burst of fire must recycle its own oldest effect instead of growing the object count.");
            Assert.That(pool.ActiveCount, Is.LessThanOrEqualTo(2));

            yield return null;
        }

        [UnityTest]
        public IEnumerator Pool_IgnoresAnEffectThatHasNoPrefab()
        {
            CombatVfxPool empty = new CombatVfxPool();
            empty.Configure(new FakeAssetProvider(null), null, 4, 1f);

            GameObject instance = empty.Play(VfxEmission.At(EffectId, Vector3.zero, Quaternion.identity));

            Assert.That(instance, Is.Null);
            Assert.That(empty.MissingAssetCount, Is.EqualTo(1));
            Assert.That(empty.TotalInstances, Is.EqualTo(0));

            yield return null;
        }

        [UnityTest]
        public IEnumerator Pool_AppliesScaleTintAndBurstCount()
        {
            GameObject instance = pool.Play(
                VfxEmission.At(EffectId, new Vector3(3f, 4f, 0f), Quaternion.identity, 2f)
                    .WithTint(Color.red)
                    .WithCount(7)
                    .WithLifetime(1f));

            Assert.That(instance.transform.position, Is.EqualTo(new Vector3(3f, 4f, 0f)));
            Assert.That(instance.transform.localScale.x, Is.EqualTo(2f).Within(0.001f));

            ParticleSystem system = instance.GetComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            Assert.That(main.startColor.color.r, Is.EqualTo(1f).Within(0.01f));
            Assert.That(main.startColor.color.g, Is.LessThan(0.01f));

            ParticleSystem.Burst[] bursts = new ParticleSystem.Burst[system.emission.burstCount];
            system.emission.GetBursts(bursts);
            Assert.That(bursts[0].count.constant, Is.EqualTo(7f).Within(0.01f),
                "The number of sparks belongs to the impact band in the profile file, not to the prefab.");

            yield return null;
        }

        [UnityTest]
        public IEnumerator Pool_ScalesParticleCountsByTheQualitySetting()
        {
            pool.Configure(provider, null, 4, 0.5f);
            GameObject instance = pool.Play(VfxEmission.At(EffectId, Vector3.zero, Quaternion.identity, 2f).WithLifetime(1f));

            Assert.That(instance.transform.localScale.x, Is.EqualTo(1f).Within(0.001f),
                "The quality scale is the single place the whole effect layer is dialled down.");

            yield return null;
        }

        [UnityTest]
        public IEnumerator System_ParentsEffectsToAnIdentityRoot()
        {
            CombatVfxSystem system = CombatVfxSystem.Ensure(provider);
            spawned.Add(system.gameObject);
            system.transform.localScale = new Vector3(3f, 3f, 1f);
            system.transform.position = new Vector3(17f, -4f, 0f);

            GameObject instance = system.Play(
                VfxEmission.At(EffectId, new Vector3(2f, 0f, 0f), Quaternion.identity, 2f).WithLifetime(1f));

            Assert.That(instance, Is.Not.Null);
            Assert.That(instance.transform.lossyScale.x, Is.EqualTo(2f).Within(0.001f),
                "An effect must not inherit the scale of the object that coordinates the battle: the pool " +
                "instance already carries the world size the effect needs.");
            Assert.That(instance.transform.position.x, Is.EqualTo(2f).Within(0.001f));

            yield return null;
        }

        [UnityTest]
        public IEnumerator ResourcesProvider_DrawsNothingInBatchmode()
        {
            if (!Application.isBatchMode)
            {
                Assert.Ignore("This guarantee only applies to a batchmode run.");
            }

            ResourcesCombatVfxAssetProvider resources = new ResourcesCombatVfxAssetProvider();

            Assert.That(resources.LoadPrefab(EffectId), Is.Null,
                "A headless run has no display, so it must not create particle systems for nobody.");
            Assert.That(resources.LoadMarkSprite(CombatVfxEffects.TrackMarkMedium), Is.Null);

            yield return null;
        }

        [UnityTest]
        public IEnumerator TrackMarkEmitter_ReusesAFixedSetOfRenderers()
        {
            GameObject owner = new GameObject("MarkEmitterOwner");
            spawned.Add(owner);
            TankTrackMarkEmitter emitter = owner.AddComponent<TankTrackMarkEmitter>();
            emitter.Configure(CreateMarkSprite(), 4);

            for (int index = 0; index < 12; index++)
            {
                emitter.Emit(
                    new Vector3(index * 0.1f, 0f, 0f),
                    Quaternion.identity,
                    0.1f,
                    0.15f,
                    new Color(1f, 1f, 1f, 0.5f),
                    4f);
            }

            Assert.That(emitter.EmittedCount, Is.EqualTo(12));
            Assert.That(emitter.RendererCount, Is.EqualTo(4),
                "A machine keeps a fixed number of prints for the whole battle; driving further must not " +
                "create more objects.");
            Assert.That(emitter.ReusedCount, Is.EqualTo(8));
            Assert.That(emitter.LiveCount, Is.LessThanOrEqualTo(4));

            yield return null;
        }

        [UnityTest]
        public IEnumerator TrackMarkEmitter_FadesAndReleasesItsPrints()
        {
            GameObject owner = new GameObject("FadingMarkEmitter");
            spawned.Add(owner);
            TankTrackMarkEmitter emitter = owner.AddComponent<TankTrackMarkEmitter>();
            emitter.Configure(CreateMarkSprite(), 4);
            emitter.Emit(Vector3.zero, Quaternion.identity, 0.1f, 0.15f, Color.white, 1f);

            Assert.That(emitter.LiveCount, Is.EqualTo(1));
            SpriteRenderer print = GameObject.Find("TrackMarks_FadingMarkEmitter")
                .GetComponentInChildren<SpriteRenderer>();
            emitter.Tick(0.5f);
            Assert.That(print.color.a, Is.EqualTo(1f).Within(0.01f),
                "A long-lived print should remain readable before its final fade begins.");
            emitter.Tick(0.3f);
            Assert.That(print.color.a, Is.InRange(0.05f, 0.95f),
                "A print should disappear gradually near the end of its lifetime.");
            emitter.Tick(0.7f);

            Assert.That(emitter.LiveCount, Is.EqualTo(0), "A print that outlived its fade must stop drawing.");

            yield return null;
        }

        private static GameObject CreateTemplate()
        {
            GameObject root = new GameObject("VfxTemplate");
            ParticleSystem system = root.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.2f;
            main.startLifetime = 0.2f;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0.02f, (short)1) });
            return root;
        }

        private static Sprite CreateMarkSprite()
        {
            Texture2D texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[16];
            for (int index = 0; index < pixels.Length; index++)
            {
                pixels[index] = Color.white;
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 4f);
        }

        /// <summary>Hands out one template instead of reading Resources, so a test owns its own assets.</summary>
        private sealed class FakeAssetProvider : ICombatVfxAssetProvider
        {
            private readonly GameObject prefab;
            private readonly Sprite mark;

            public FakeAssetProvider(GameObject prefab)
            {
                this.prefab = prefab;
                mark = prefab != null ? CreateMarkSprite() : null;
            }

            public GameObject LoadPrefab(string effectId)
            {
                return prefab;
            }

            public Sprite LoadMarkSprite(string variant)
            {
                return mark;
            }
        }
    }
}
