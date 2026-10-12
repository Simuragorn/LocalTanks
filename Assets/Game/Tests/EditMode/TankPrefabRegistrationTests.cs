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

        [Test]
        public void TankPrefabLookup_RejectsAnUnknownId()
        {
            Assert.That(PrototypeSetup.FindTankPrefabPath("no_such_tank"), Is.Null);
            Assert.That(PrototypeSetup.FindTankPrefabPath(null), Is.Null);
        }
    }
}
