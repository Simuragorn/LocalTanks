using System;
using System.IO;
using System.Linq;
using LocalTanks.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LocalTanks.Tests
{
    /// <summary>
    /// The combat effect table is data, and it is the only thing that decides how a 45 mm shot differs
    /// from an 88 mm one. A gap in it is invisible in code review and shows up only as a machine that
    /// suddenly has no dust, so these tests read the real committed file and the real definitions.
    /// </summary>
    public sealed class CombatVfxProfileTests
    {
        [Test]
        public void CommittedProfiles_AreValid()
        {
            string[] errors = CombatVfxProfileValidator.CollectErrors();

            Assert.That(errors, Is.Empty,
                "The committed combat VFX profiles must cover every surface, outcome and the whole fleet:\n- " +
                string.Join("\n- ", errors));
        }

        [Test]
        public void SourceFile_IsWhereTheRuntimeLooks()
        {
            Assert.That(File.Exists(Path.GetFullPath(CombatVfxCatalog.SourcePath)), Is.True);
        }

        [Test]
        public void EveryTerrainKind_HasItsOwnSurfaceProfile()
        {
            CombatVfxCatalog catalog = CombatVfxProfileValidator.LoadSource();
            TerrainKind[] terrains = (TerrainKind[])Enum.GetValues(typeof(TerrainKind));

            foreach (TerrainKind terrain in terrains)
            {
                Assert.That(catalog.ResolveSurface(terrain), Is.Not.Null,
                    $"TerrainKind.{terrain} must say how it answers a track, a shell and a splash.");
            }

            Assert.That(
                terrains.Select(catalog.ResolveSurface).Distinct().Count(),
                Is.EqualTo(terrains.Length),
                "Two surfaces sharing one profile would make mud look like a road.");
        }

        [Test]
        public void EveryImpactOutcome_HasItsOwnBand()
        {
            CombatVfxCatalog catalog = CombatVfxProfileValidator.LoadSource();

            ImpactVfxProfile penetrated = catalog.ResolveImpact(ImpactOutcome.Penetrated);
            ImpactVfxProfile blocked = catalog.ResolveImpact(ImpactOutcome.Blocked);
            ImpactVfxProfile ricochet = catalog.ResolveImpact(ImpactOutcome.Ricocheted);
            ImpactVfxProfile ground = catalog.GroundImpact;

            Assert.That(new[] { penetrated, blocked, ricochet, ground }, Has.None.Null);
            Assert.That(new[] { penetrated, blocked, ricochet, ground }.Distinct().Count(), Is.EqualTo(4),
                "A hit on the map must not reuse an armour result, and a ricochet must not look like a blocked hit.");
            Assert.That(ricochet.sparkCount, Is.GreaterThan(penetrated.sparkCount),
                "A ricochet throws more sparks than a penetration, which is the readable difference between them.");
        }

        [Test]
        public void BiggerGuns_FlashAndSmokeMore()
        {
            CombatVfxCatalog catalog = CombatVfxProfileValidator.LoadSource();

            WeaponVfxProfile light = catalog.ResolveWeapon(20f);
            WeaponVfxProfile medium = catalog.ResolveWeapon(88f);
            WeaponVfxProfile siege = catalog.ResolveWeapon(152f);

            Assert.That(light.id, Is.EqualTo("autocannon"));
            Assert.That(medium.id, Is.EqualTo("medium_gun"));
            Assert.That(siege.id, Is.EqualTo("siege_gun"),
                "A caliber above the whole table must still resolve instead of returning nothing.");
            Assert.That(medium.flashScale, Is.GreaterThan(light.flashScale));
            Assert.That(siege.smokeBurst, Is.GreaterThan(medium.smokeBurst));
        }

        [Test]
        public void HeavierMachines_RaiseMoreDustAndPressDeeperPrints()
        {
            CombatVfxCatalog catalog = CombatVfxProfileValidator.LoadSource();

            MovementVfxProfile scout = catalog.ResolveMovement(5f);
            MovementVfxProfile heavy = catalog.ResolveMovement(70f);

            Assert.That(scout.id, Is.EqualTo("light"));
            Assert.That(heavy.id, Is.EqualTo("heavy"));
            Assert.That(heavy.dustRate, Is.GreaterThan(scout.dustRate));
            Assert.That(heavy.markOpacity, Is.GreaterThan(scout.markOpacity));
            Assert.That(heavy.markLimit, Is.GreaterThanOrEqualTo(scout.markLimit));
            Assert.That(catalog.movementProfiles.All(profile => profile.exhaustIdleRate >= 3f), Is.True,
                "Even idle exhaust must remain faintly legible at the inspection zoom.");
            Assert.That(catalog.movementProfiles.All(profile => profile.exhaustLoadedRate >= 13f), Is.True,
                "Loaded exhaust must form a readable trail instead of isolated transparent particles.");
        }

        [Test]
        public void TrackMarks_KeepAUsefulTrailBeforeTheyFade()
        {
            CombatVfxCatalog catalog = CombatVfxProfileValidator.LoadSource();

            Assert.That(catalog.movementProfiles.All(profile => profile.markFadeSeconds >= 24f), Is.True,
                "A track print must remain long enough to form a trail behind the machine.");
            Assert.That(catalog.movementProfiles.All(profile => profile.markLimit >= 160), Is.True,
                "The ring buffer must not recycle the beginning of a trail almost immediately.");
        }

        [Test]
        public void DamageBands_FollowTheRemainingHealth()
        {
            CombatVfxCatalog catalog = CombatVfxProfileValidator.LoadSource();

            Assert.That(catalog.ResolveDamage(1f, false).id, Is.EqualTo("damaged"));
            Assert.That(catalog.ResolveDamage(0.5f, false).id, Is.EqualTo("heavily_damaged"));
            Assert.That(catalog.ResolveDamage(0.1f, false).id, Is.EqualTo("burning"));
            Assert.That(catalog.ResolveDamage(0f, false).id, Is.EqualTo("burning"),
                "A machine on the very edge of destruction still belongs to a live band.");
            Assert.That(catalog.ResolveDamage(0.1f, true).id, Is.EqualTo("wreck"),
                "A destroyed machine smokes as a wreck regardless of the health it had left.");
            Assert.That(catalog.ResolveDamage(0.1f, false).fire, Is.False,
                "A machine on its last legs smokes; fire marks a machine as finished, so only a wreck burns.");
            Assert.That(catalog.ResolveDamage(0f, true).fire, Is.True);
        }

        [Test]
        public void Catalog_RejectsAnIdUsedByTwoTables()
        {
            CombatVfxCatalog catalog = CombatVfxProfileValidator.LoadSource();
            catalog.surfaceProfiles[0].id = catalog.impactProfiles[0].id;

            Assert.That(catalog.Validate().Any(error => error.Contains("twice")), Is.True,
                "One id must name exactly one band, or a lookup by id would depend on which table was searched.");
        }

        [Test]
        public void Catalog_RejectsAnUnknownTerrainAndAnUnknownOutcome()
        {
            CombatVfxCatalog terrain = CombatVfxProfileValidator.LoadSource();
            terrain.surfaceProfiles[0].terrain = "Swamp";
            Assert.That(terrain.Validate().Any(error => error.Contains("TerrainKind")), Is.True);

            CombatVfxCatalog outcome = CombatVfxProfileValidator.LoadSource();
            outcome.impactProfiles[0].outcome = "bounced";
            Assert.That(outcome.Validate().Any(error => error.Contains("outcome")), Is.True);
        }

        [Test]
        public void Catalog_RejectsABrokenProgression()
        {
            CombatVfxCatalog flash = CombatVfxProfileValidator.LoadSource();
            flash.weaponProfiles[0].flashScale = 9f;
            Assert.That(flash.Validate().Any(error => error.Contains("flashScale")), Is.True,
                "A small gun cannot flash larger than a heavy one in a table ordered by caliber.");

            CombatVfxCatalog smoke = CombatVfxProfileValidator.LoadSource();
            smoke.damageProfiles[0].smokeRate = 100f;
            Assert.That(smoke.Validate().Any(error => error.Contains("smokeRate")), Is.True,
                "A healthier machine cannot smoke more than a machine closer to destruction.");
        }

        [Test]
        public void Catalog_RejectsAGapInTheDamageTable()
        {
            CombatVfxCatalog catalog = CombatVfxProfileValidator.LoadSource();
            catalog.damageProfiles = catalog.damageProfiles.Where(profile => !profile.wreck).ToArray();
            DamageVfxProfile lowest = catalog.damageProfiles[catalog.damageProfiles.Length - 1];
            lowest.minHealthFraction = 0.2f;

            string[] errors = catalog.Validate();

            Assert.That(errors.Any(error => error.Contains("wreck")), Is.True,
                "A battle without a wreck band would leave every destroyed machine silent.");
            Assert.That(errors.Any(error => error.Contains("lowest damage band")), Is.True,
                "Without a band starting at zero the loudest state would be the only silent one.");
        }

        [Test]
        public void EveryGeneratedEffect_HasABuilderEntryAndARuntimePath()
        {
            Assert.That(CombatVfxEffects.All().Distinct().Count(), Is.EqualTo(CombatVfxEffects.All().Length));
            foreach (string oneShot in CombatVfxEffects.OneShots)
            {
                Assert.That(CombatVfxEffects.IsLoop(oneShot), Is.False);
            }

            foreach (string loop in CombatVfxEffects.Loops)
            {
                Assert.That(CombatVfxEffects.IsLoop(loop), Is.True);
            }

            foreach (string effectId in CombatVfxEffects.All())
            {
                string path = CombatVfxAssetBuilder.PrefabPath(effectId);

                // The builder writes where Resources.Load reads, or the effects exist but never appear.
                Assert.That(path, Is.EqualTo(
                    $"Assets/Game/Resources/{CombatVfxEffects.ResourceFolder}/{effectId}.prefab"));
            }

            // Each print pattern is loaded by name at run time, so every generated file must sit in the same
            // Resources folder as the prefabs. A sprite in the art folder would look correct in the project
            // window and never be found by the game.
            foreach (string variant in CombatVfxEffects.MarkVariants)
            {
                Assert.That(CombatVfxAssetBuilder.MarkSpritePath(variant), Is.EqualTo(
                    $"Assets/Game/Resources/{CombatVfxEffects.MarkSpritePath(variant)}.png"));
            }

            // Every movement band must resolve to a pattern that exists, or a machine silently lays no
            // prints at all.
            foreach (MovementVfxProfile movement in CombatVfxProfileValidator.LoadSource().movementProfiles)
            {
                Assert.That(CombatVfxEffects.MarkVariants, Does.Contain(CombatVfxEffects.MarkVariantFor(movement.id)),
                    $"Movement band '{movement.id}' resolves to no print pattern.");
            }
        }

        [Test]
        public void GeneratedLibrary_IsEitherAbsentOrComplete()
        {
            string folder = Path.GetFullPath(CombatVfxAssetBuilder.PrefabFolder);
            bool started = Directory.Exists(folder) && Directory.GetFiles(folder, "*.prefab").Length > 0;
            if (!started)
            {
                Assert.Pass("The effect library has not been generated yet; " +
                            "'Local Tanks/Build Combat Vfx Assets' produces it.");
            }

            string[] errors = CombatVfxAssetBuilder.CollectAssetErrors();
            Assert.That(errors, Is.Empty,
                "A generated effect library must be complete; half of it is worse than none:\n- " +
                string.Join("\n- ", errors));
        }

        [Test]
        public void GeneratedFireMuzzleAndExhaust_AreLayeredInsteadOfSingleSprites()
        {
            GameObject muzzle = AssetDatabase.LoadAssetAtPath<GameObject>(
                CombatVfxAssetBuilder.PrefabPath(CombatVfxEffects.MuzzleFlash));
            GameObject muzzleSmoke = AssetDatabase.LoadAssetAtPath<GameObject>(
                CombatVfxAssetBuilder.PrefabPath(CombatVfxEffects.MuzzleSmoke));
            GameObject barrelSmoke = AssetDatabase.LoadAssetAtPath<GameObject>(
                CombatVfxAssetBuilder.PrefabPath(CombatVfxEffects.BarrelSmoke));
            GameObject exhaust = AssetDatabase.LoadAssetAtPath<GameObject>(
                CombatVfxAssetBuilder.PrefabPath(CombatVfxEffects.ExhaustSmoke));
            GameObject fire = AssetDatabase.LoadAssetAtPath<GameObject>(
                CombatVfxAssetBuilder.PrefabPath(CombatVfxEffects.DamageFire));

            Assert.That(muzzle, Is.Not.Null);
            Assert.That(muzzleSmoke, Is.Not.Null);
            Assert.That(barrelSmoke, Is.Not.Null);
            Assert.That(exhaust, Is.Not.Null);
            Assert.That(fire, Is.Not.Null);
            Assert.That(muzzle.GetComponentsInChildren<ParticleSystem>(true).Select(item => item.name),
                Is.EquivalentTo(new[] { "Core", "Plume", "SideFlames", "Sparks" }));
            Assert.That(muzzleSmoke.GetComponentsInChildren<ParticleSystem>(true).Select(item => item.name),
                Is.EquivalentTo(new[] { "PropellantCloud" }));
            Assert.That(barrelSmoke.GetComponentsInChildren<ParticleSystem>(true).Select(item => item.name),
                Is.EquivalentTo(new[] { "Jet", "Residual" }));
            Assert.That(exhaust.GetComponentsInChildren<ParticleSystem>(true).Select(item => item.name),
                Is.EquivalentTo(new[] { "DensePuffs", "Drift" }));
            ParticleSystem denseExhaust = exhaust.GetComponentsInChildren<ParticleSystem>(true)
                .Single(item => item.name == "DensePuffs");
            ParticleSystem driftExhaust = exhaust.GetComponentsInChildren<ParticleSystem>(true)
                .Single(item => item.name == "Drift");
            Assert.That(denseExhaust.main.startSize.constantMin, Is.GreaterThanOrEqualTo(0.2f),
                "The dense exhaust pulse must remain legible at the inspection zoom.");
            Assert.That(denseExhaust.main.startColor.color.a, Is.GreaterThanOrEqualTo(0.8f));
            Assert.That(driftExhaust.main.startLifetime.constantMax, Is.GreaterThanOrEqualTo(2f),
                "The pale exhaust layer must persist long enough to form a trail instead of isolated dots.");
            Assert.That(driftExhaust.main.startColor.color.a, Is.GreaterThanOrEqualTo(0.45f));
            Assert.That(denseExhaust.velocityOverLifetime.y.constantMax, Is.LessThan(0f));
            Assert.That(driftExhaust.velocityOverLifetime.y.constantMax, Is.LessThan(0f),
                "Both exhaust layers must leave towards the rear instead of fading over the engine deck.");
            Assert.That(fire.GetComponentsInChildren<ParticleSystem>(true).Select(item => item.name),
                Is.EquivalentTo(new[] { "Soot", "Glow", "Tongues", "TallTongues", "Core", "Ember" }));
        }

        [Test]
        public void GeneratedArmorSparks_AreStretchedIntoAnOutwardFan()
        {
            GameObject impact = AssetDatabase.LoadAssetAtPath<GameObject>(
                CombatVfxAssetBuilder.PrefabPath(CombatVfxEffects.ImpactSpark));

            Assert.That(impact, Is.Not.Null);
            ParticleSystem sparks = impact.GetComponentsInChildren<ParticleSystem>(true)
                .Single(item => item.name == "Sparks");
            ParticleSystemRenderer renderer = sparks.GetComponent<ParticleSystemRenderer>();
            ParticleSystem.VelocityOverLifetimeModule velocity = sparks.velocityOverLifetime;

            Assert.That(renderer.renderMode, Is.EqualTo(ParticleSystemRenderMode.Stretch));
            Assert.That(renderer.lengthScale, Is.GreaterThan(2f));
            Assert.That(velocity.space, Is.EqualTo(ParticleSystemSimulationSpace.Local));
            Assert.That(velocity.x.constantMin, Is.LessThan(0f));
            Assert.That(velocity.x.constantMax, Is.GreaterThan(0f));
            Assert.That(velocity.y.constantMin, Is.GreaterThan(0f),
                "Every armour spark must leave the plate along its outward normal.");
        }
    }
}
