using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LocalTanks.Tests
{
    public sealed class TankRammingPlayModeTests
    {
        [UnityTest]
        public IEnumerator PhysicalRammingDamagesEnemiesAndAlliesEvenWithShellFriendlyFireDisabled()
        {
            RamPair allies = CreatePair("Allies", 0f, TeamId.TeamA, TeamId.TeamA);
            RamPair enemies = CreatePair("Enemies", 10f, TeamId.TeamA, TeamId.TeamB);

            for (int step = 0; step < 30; step++)
            {
                yield return new WaitForFixedUpdate();
            }

            try
            {
                AssertDamaged(allies, "Two allied vehicles must damage one another when they ram.");
                AssertDamaged(enemies, "Vehicles on opposing teams must damage one another when they ram.");
            }
            finally
            {
                allies.Destroy();
                enemies.Destroy();
            }

            yield return null;
        }

        private static RamPair CreatePair(string name, float y, TeamId firstTeam, TeamId secondTeam)
        {
            CombatRules combat = new CombatRules
            {
                ramming = new RammingRules
                {
                    enabled = true,
                    minimumImpactSpeedMps = 1f,
                    hitPointsPerKilojoule = 1f,
                    armorProtectionReferenceMm = 50f,
                    repeatContactCooldownSeconds = 0.35f
                }
            };
            // The projectile switch is intentionally false. Ramming reads CombatRules directly and must
            // remain bilateral physical damage for allies, per the game design requirement.
            MatchRules match = new MatchRules { friendlyFire = false, combat = combat };
            Assert.That(match.AllowsDamageTo(firstTeam, secondTeam),
                Is.EqualTo(firstTeam != secondTeam), "The control proves shell friendly fire is disabled.");

            TankRig first = CreateTank(name + "_First", new Vector2(-1.1f, y), firstTeam, combat);
            TankRig second = CreateTank(name + "_Second", new Vector2(1.1f, y), secondTeam, combat);
            first.Body.linearVelocity = Vector2.right * 2f;
            second.Body.linearVelocity = Vector2.left * 2f;
            return new RamPair(first, second);
        }

        private static TankRig CreateTank(string name, Vector2 position, TeamId team, CombatRules rules)
        {
            TankDefinition definition = ScriptableObject.CreateInstance<TankDefinition>();
            definition.id = name;
            definition.displayName = name;
            definition.maxHitPoints = 500;
            definition.massTonnes = 10f;
            definition.armor = new ArmorProfile(10f, 10f, 10f, 10f);

            GameObject root = new GameObject(name);
            root.transform.position = position;
            Rigidbody2D body = root.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.mass = definition.massTonnes;
            body.freezeRotation = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            BoxCollider2D collider = root.AddComponent<BoxCollider2D>();
            collider.size = Vector2.one;
            TankHealth health = root.AddComponent<TankHealth>();
            health.Configure(definition);
            TeamMember member = root.AddComponent<TeamMember>();
            member.Configure(team, false);
            TankRammingDamage ramming = root.AddComponent<TankRammingDamage>();
            ramming.Configure(rules);
            return new TankRig(root, definition, body, health, ramming);
        }

        private static void AssertDamaged(RamPair pair, string message)
        {
            Assert.That(pair.First.Health.CurrentHitPoints, Is.LessThan(pair.First.Health.MaximumHitPoints), message);
            Assert.That(pair.Second.Health.CurrentHitPoints, Is.LessThan(pair.Second.Health.MaximumHitPoints), message);
            Assert.That(pair.ImpactCount, Is.EqualTo(1),
                "Unity reports the collision to both bodies, but the pair must resolve exactly once.");

            int firstLoss = pair.First.Health.MaximumHitPoints - pair.First.Health.CurrentHitPoints;
            int secondLoss = pair.Second.Health.MaximumHitPoints - pair.Second.Health.CurrentHitPoints;
            if (pair.Resolver == pair.First.Ramming)
            {
                Assert.That(firstLoss, Is.EqualTo(pair.Result.DamageToFirst));
                Assert.That(secondLoss, Is.EqualTo(pair.Result.DamageToSecond));
            }
            else
            {
                Assert.That(firstLoss, Is.EqualTo(pair.Result.DamageToSecond));
                Assert.That(secondLoss, Is.EqualTo(pair.Result.DamageToFirst));
            }
        }

        private sealed class TankRig
        {
            public TankRig(
                GameObject root,
                TankDefinition definition,
                Rigidbody2D body,
                TankHealth health,
                TankRammingDamage ramming)
            {
                Root = root;
                Definition = definition;
                Body = body;
                Health = health;
                Ramming = ramming;
            }

            public GameObject Root { get; }
            public TankDefinition Definition { get; }
            public Rigidbody2D Body { get; }
            public TankHealth Health { get; }
            public TankRammingDamage Ramming { get; }

            public void Destroy()
            {
                Object.Destroy(Root);
                Object.Destroy(Definition);
            }
        }

        private sealed class RamPair
        {
            public RamPair(TankRig first, TankRig second)
            {
                First = first;
                Second = second;
                First.Ramming.ImpactResolved += OnImpactResolved;
                Second.Ramming.ImpactResolved += OnImpactResolved;
            }

            public TankRig First { get; }
            public TankRig Second { get; }
            public int ImpactCount { get; private set; }
            public TankRammingDamage Resolver { get; private set; }
            public RammingDamageResult Result { get; private set; }

            private void OnImpactResolved(TankRammingDamage resolver, RammingImpactObservation observation)
            {
                ImpactCount++;
                Resolver = resolver;
                Result = observation.Result;
            }

            public void Destroy()
            {
                First.Ramming.ImpactResolved -= OnImpactResolved;
                Second.Ramming.ImpactResolved -= OnImpactResolved;
                First.Destroy();
                Second.Destroy();
            }
        }
    }
}
