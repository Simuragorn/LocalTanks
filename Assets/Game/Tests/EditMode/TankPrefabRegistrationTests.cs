using System.Linq;
using LocalTanks.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LocalTanks.Tests
{
    /// <summary>
    /// Scene builders derive their prefab bindings from the combat database, so the only place a
    /// tank still has to be registered by hand is the build spec list. These checks fail loudly when
    /// a playable tank has no spec, or a spec produces no prefab, instead of letting a scene bind an
    /// incomplete set of tanks.
    /// </summary>
    public sealed class TankPrefabRegistrationTests
    {
        [Test]
        public void EveryPlayableTank_HasAPrefabPathFromABuildSpec()
        {
            CombatDatabase database = AssetDatabase.LoadAssetAtPath<CombatDatabase>(
                CombatDefinitionImporter.DatabasePath);
            Assert.That(database, Is.Not.Null, "Combat database was not generated.");

            string[] missing = database.tanks
                .Where(tank => tank != null && tank.availableInGame)
                .Where(tank => string.IsNullOrWhiteSpace(PrototypeSetup.FindTankPrefabPath(tank.id)))
                .Select(tank => tank.id)
                .ToArray();

            Assert.That(missing, Is.Empty,
                "These playable tanks have no build spec in PrototypeSetup.TankSpecs: " +
                string.Join(", ", missing));
        }

        [Test]
        public void EveryBuildSpec_ProducesACommittedPrefab()
        {
            CombatDatabase database = AssetDatabase.LoadAssetAtPath<CombatDatabase>(
                CombatDefinitionImporter.DatabasePath);
            Assert.That(database, Is.Not.Null);

            foreach (TankDefinition tank in database.tanks.Where(item => item != null))
            {
                string prefabPath = PrototypeSetup.FindTankPrefabPath(tank.id);
                if (string.IsNullOrWhiteSpace(prefabPath))
                {
                    // A hidden tank may legitimately have no build spec; a playable one is covered by
                    // the other test. Only a declared path has to resolve.
                    continue;
                }

                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), Is.Not.Null,
                    $"Build spec for tank '{tank.id}' points at a prefab that was not generated: {prefabPath}");
            }
        }

        /// <summary>
        /// Machines must be correct relative to each other, which no per-tank check can see: a sprite
        /// rendered at the wrong scale still matches its own collider and still looks right alone. The
        /// T1 Cunningham once came out 77% longer than a Tiger II this way. This compares every built
        /// hull against the reference machine at the shared game scale.
        /// </summary>
        [Test]
        public void EveryTank_IsBuiltAtTheSharedFleetScale()
        {
            CombatDatabase database = AssetDatabase.LoadAssetAtPath<CombatDatabase>(
                CombatDefinitionImporter.DatabasePath);
            Assert.That(database, Is.Not.Null);

            // The reference machine defines the scale. Its collider sits slightly inside its sprite so
            // the silhouette covers it, so compare against the drawn hull rather than the contour.
            Assert.That(PrototypeSetup.TryGetExpectedHullSize("tiger_ii", out Vector2 referenceSprite), Is.True);
            float tigerLength = HullLengthInUnits("tiger_ii");
            Assert.That(tigerLength, Is.EqualTo(referenceSprite.y).Within(referenceSprite.y * 0.05f),
                "The reference machine must measure its documented length in units.");

            // A Tiger II hull is 7.38 m and an MS-1 hull is 4.38 m, so the light tank must come out
            // visibly shorter. Any pair would do; this one guards the ordering the player sees.
            float ms1Length = HullLengthInUnits("ms_1");
            Assert.That(ms1Length, Is.LessThan(tigerLength * 0.7f),
                "A 4.38 m hull must be clearly shorter than a 7.38 m hull.");
            Assert.That(ms1Length, Is.GreaterThan(tigerLength * 0.5f),
                "A 4.38 m hull must stay close to its share of the 7.38 m hull.");
        }

        [Test]
        public void ScaleForHullLength_MatchesTheReferenceMachine()
        {
            // Feeding the reference values back in must reproduce the reference scale exactly, which is
            // what makes the formula trustworthy for a tank nobody has built yet.
            float scale = PrototypeSetup.ScaleForHullLength(
                PrototypeSetup.ReferenceHullLengthMetres,
                PrototypeSetup.ReferenceHullLengthPixels);

            Assert.That(scale, Is.EqualTo(PrototypeSetup.ReferenceVisualScale).Within(0.0001f));
        }

        private static float HullLengthInUnits(string tankId)
        {
            string prefabPath = PrototypeSetup.FindTankPrefabPath(tankId);
            Assert.That(prefabPath, Is.Not.Null, $"No build spec for '{tankId}'.");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null);

            GameObject instance = Object.Instantiate(prefab);
            try
            {
                PolygonCollider2D collider = instance.GetComponent<PolygonCollider2D>();
                Assert.That(collider, Is.Not.Null);
                return collider.bounds.size.y;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void TankPrefabLookup_RejectsAnUnknownId()
        {
            Assert.That(PrototypeSetup.FindTankPrefabPath("no_such_tank"), Is.Null);
            Assert.That(PrototypeSetup.FindTankPrefabPath(null), Is.Null);
        }

        /// <summary>
        /// A hull contour is written in the same units as the sprite rectangle, so the generated
        /// collider must span exactly that rectangle times the visual scale. A contour entered in
        /// the wrong units still compiles and still hits things, and only this check notices that
        /// the collider no longer matches the visible hull.
        /// </summary>
        [Test]
        public void EveryTankCollider_MatchesItsSpriteRectangle()
        {
            CombatDatabase database = AssetDatabase.LoadAssetAtPath<CombatDatabase>(
                CombatDefinitionImporter.DatabasePath);
            Assert.That(database, Is.Not.Null);

            foreach (TankDefinition tank in database.tanks.Where(item => item != null))
            {
                string prefabPath = PrototypeSetup.FindTankPrefabPath(tank.id);
                if (string.IsNullOrWhiteSpace(prefabPath)) continue;
                if (!PrototypeSetup.TryGetExpectedHullSize(tank.id, out Vector2 expected))
                {
                    Assert.Fail($"Tank '{tank.id}' has a prefab but no usable build spec.");
                }

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.That(prefab, Is.Not.Null);
                PolygonCollider2D prefabCollider = prefab.GetComponent<PolygonCollider2D>();
                Assert.That(prefabCollider, Is.Not.Null, $"Tank '{tank.id}' has no hull collider.");
                Assert.That(prefabCollider.points.Length, Is.GreaterThanOrEqualTo(10),
                    $"Tank '{tank.id}' hull collider must follow the visible outline, not a box.");

                // A prefab asset does not build its physics shape, so the bounds are read from a live
                // instance instead of the serialized asset.
                GameObject instance = Object.Instantiate(prefab);
                try
                {
                    PolygonCollider2D collider = instance.GetComponent<PolygonCollider2D>();
                    Bounds bounds = collider.bounds;
                    Assert.That(bounds.size.x, Is.EqualTo(expected.x).Within(expected.x * 0.06f),
                        $"Tank '{tank.id}' collider width {bounds.size.x:0.###} does not match its sprite width " +
                        $"{expected.x:0.###}; the outline is probably written in the wrong units.");
                    Assert.That(bounds.size.y, Is.EqualTo(expected.y).Within(expected.y * 0.06f),
                        $"Tank '{tank.id}' collider length {bounds.size.y:0.###} does not match its sprite length " +
                        $"{expected.y:0.###}.");
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }
    }
}
