using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LocalTanks.Tests
{
    /// <summary>
    /// The battle ends through the director and for a stated reason. Each check drives one of the three endings
    /// on the real scene, because the interesting part is the wiring: a rule that is correct but never
    /// evaluated looks exactly like a battle that never ends. The friendly fire switch is checked on a shell
    /// itself, since the rule decides which machine a shell may damage, not how a hit is resolved.
    /// </summary>
    public sealed class MatchOutcomePlayModeTests
    {
        private const string SceneName = "Battle_RiverCrossing";

        [UnityTest]
        public IEnumerator WipingATeamEndsTheBattleAndStopsItsMachines()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;

            BattleDirector director = Object.FindAnyObjectByType<BattleDirector>();
            Assert.That(director, Is.Not.Null);

            foreach (TankHealth health in Object.FindObjectsByType<TankHealth>())
            {
                TeamMember member = health.GetComponent<TeamMember>();
                if (member != null && member.Team == TeamId.TeamB && !health.IsDestroyed)
                {
                    health.ApplyDamage(health.MaximumHitPoints);
                }
            }

            yield return null;

            Assert.That(director.HasEnded, Is.True, "A wiped-out team must end the battle.");
            Assert.That(director.Result.Outcome, Is.EqualTo(MatchOutcome.TeamWipedOut));
            Assert.That(director.Result.Winner, Is.EqualTo(TeamId.TeamA));
            Assert.That(director.Result.ElapsedSeconds, Is.GreaterThanOrEqualTo(0f));

            TankMotor motor = Object.FindObjectsByType<TankMotor>().First(item => item != null);
            Assert.That(motor.enabled, Is.False, "A finished battle must not keep driving its machines.");

            // A result is announced once; a second evaluation must not raise the event again.
            int endings = 0;
            director.MatchEnded += _ => endings++;
            yield return null;
            Assert.That(endings, Is.Zero);
        }

        [UnityTest]
        public IEnumerator TheTimeLimitEndsTheBattleInADraw()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;

            BattleDirector director = Object.FindAnyObjectByType<BattleDirector>();
            Assert.That(director, Is.Not.Null);

            // The rules live in the generated scenario asset, which every test in this session shares, so the
            // original limit is put back even when the test fails: leaving a short limit behind would end the
            // battles of every later test.
            float originalLimit = director.Rules.timeLimitSeconds;
            director.Rules.timeLimitSeconds = 1.5f;
            try
            {
                float deadline = Time.time + 15f;
                while (!director.HasEnded && Time.time < deadline)
                {
                    yield return null;
                }

                Assert.That(director.HasEnded, Is.True, "The clock must end the battle.");
                Assert.That(director.Result.Outcome, Is.EqualTo(MatchOutcome.TimeLimit));
                Assert.That(director.Result.IsDraw, Is.True);
                Assert.That(director.RemainingSeconds, Is.Zero);
            }
            finally
            {
                director.Rules.timeLimitSeconds = originalLimit;
            }
        }

        [UnityTest]
        public IEnumerator ACapturedBaseEndsTheBattleForItsCaptor()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;

            BattleDirector director = Object.FindAnyObjectByType<BattleDirector>();
            CaptureBase baseA = director.Bases.First(item => item.BaseId == "A");
            director.NotifyBaseCaptured(baseA, TeamId.TeamA, TeamId.TeamB);

            yield return null;

            Assert.That(director.HasEnded, Is.True, "A captured base must end the battle.");
            Assert.That(director.Result.Outcome, Is.EqualTo(MatchOutcome.BaseCaptured));
            Assert.That(director.Result.Winner, Is.EqualTo(TeamId.TeamB));
        }

        [UnityTest]
        public IEnumerator FriendlyFireSwitch_DecidesWhichMachinesAShellMayDamage()
        {
            SceneManager.LoadScene("Battle_TestRange");
            yield return null;

            ShellDefinition shell = CreateShell();
            GameObject shooterRoot = new GameObject("FriendlyFireShooter");
            TeamMember shooter = shooterRoot.AddComponent<TeamMember>();
            shooter.Configure(TeamId.TeamA, false);

            GameObject ally = CreateMachine("FriendlyFireAlly", TeamId.TeamA);
            GameObject enemy = CreateMachine("FriendlyFireEnemy", TeamId.TeamB);

            Projectile2D permissive = CreateProjectile(shell, shooterRoot.transform, new MatchRules { friendlyFire = true });
            Assert.That(permissive.CanDamageMachine(ally.GetComponent<TankArmor>()), Is.True);
            Assert.That(permissive.CanDamageMachine(enemy.GetComponent<TankArmor>()), Is.True);

            Projectile2D strict = CreateProjectile(shell, shooterRoot.transform, new MatchRules { friendlyFire = false });
            Assert.That(strict.CanDamageMachine(ally.GetComponent<TankArmor>()), Is.False,
                "With friendly fire off a shell must pass through a living ally.");
            Assert.That(strict.CanDamageMachine(enemy.GetComponent<TankArmor>()), Is.True);

            // A wreck is cover, not a teammate: it keeps stopping shells either way.
            TankHealth allyHealth = ally.GetComponent<TankHealth>();
            allyHealth.ApplyDamage(allyHealth.MaximumHitPoints);
            yield return null;
            Assert.That(strict.CanDamageMachine(ally.GetComponent<TankArmor>()), Is.True,
                "A wreck stays a physical obstacle even when friendly fire is off.");

            Object.Destroy(shooterRoot);
            Object.Destroy(ally);
            Object.Destroy(enemy);
            Object.Destroy(permissive.gameObject);
            Object.Destroy(strict.gameObject);
            Object.Destroy(shell);
        }

        private static ShellDefinition CreateShell()
        {
            ShellDefinition definition = ScriptableObject.CreateInstance<ShellDefinition>();
            definition.damage = 50;
            definition.penetration = 100f;
            definition.speed = 10f;
            definition.radius = 0.02f;
            definition.lifetimeSeconds = 2f;
            definition.maximumRange = 10f;
            definition.ricochetAngle = 70f;
            definition.ricochetSpeedMultiplier = 0.7f;
            definition.ricochetPenetrationMultiplier = 0.65f;
            definition.maximumRicochets = 2;
            return definition;
        }

        private static GameObject CreateMachine(string objectName, TeamId team)
        {
            TankDefinition definition = ScriptableObject.CreateInstance<TankDefinition>();
            definition.maxHitPoints = 100;
            definition.armor = new ArmorProfile(80f, 80f, 80f, 80f);

            GameObject machine = new GameObject(objectName);
            TankHealth health = machine.AddComponent<TankHealth>();
            health.Configure(definition);
            TankArmor armor = machine.AddComponent<TankArmor>();
            armor.Configure(definition, health);
            TeamMember member = machine.AddComponent<TeamMember>();
            member.Configure(team, false);
            return machine;
        }

        private static Projectile2D CreateProjectile(ShellDefinition shell, Transform ownerRoot, MatchRules rules)
        {
            GameObject projectileObject = new GameObject("MatchRulesTestProjectile");
            Projectile2D projectile = projectileObject.AddComponent<Projectile2D>();
            projectile.Initialize(Vector2.up, shell, ownerRoot, rules);
            return projectile;
        }
    }
}
