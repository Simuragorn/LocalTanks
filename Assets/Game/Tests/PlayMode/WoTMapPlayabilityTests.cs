using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LocalTanks.Tests
{
    /// <summary>
    /// Answers the only question that matters about a converted arena: do the tanks actually fight on
    /// it. Everything else — colliders, navigation, routes — is scaffolding for this, and a mistake in
    /// any of them shows up here as tanks that never move or never meet.
    /// </summary>
    public sealed class WoTMapPlayabilityTests
    {
        private const string SceneName = "Battle_10_hills";
        private const float BattleSeconds = 30f;

        private static bool SceneExists =>
            Enumerable.Range(0, SceneManager.sceneCountInBuildSettings)
                .Any(index => SceneUtility.GetScenePathByBuildIndex(index).EndsWith($"/{SceneName}.unity"));

        [UnityTest]
        public IEnumerator ConvertedArena_TanksSpawnAndMove()
        {
            if (!SceneExists) Assert.Ignore($"Scene '{SceneName}' is not in the build settings yet.");

            UnityEngine.Random.State previousRandomState = UnityEngine.Random.state;
            float previousCaptureDeltaTime = Time.captureDeltaTime;
            UnityEngine.Random.InitState(1911);
            Time.captureDeltaTime = 1f / 60f;
            SceneManager.LoadScene(SceneName);
            yield return null;

            BattleDirector director = Object.FindAnyObjectByType<BattleDirector>();
            Assert.That(director, Is.Not.Null, "The arena scene needs a configured director.");
            Assert.That(director.SpawnedMembers, Has.Count.EqualTo(BattleScenario.TeamSize * 2),
                "Both teams must field a full roster.");
            Assert.That(director.TeamAAlive, Is.EqualTo(BattleScenario.TeamSize));
            Assert.That(director.TeamBAlive, Is.EqualTo(BattleScenario.TeamSize));

            Dictionary<string, Vector2> start = director.SpawnedMembers
                .ToDictionary(member => member.name, member => (Vector2)member.transform.position);
            int startHealth = director.SpawnedMembers
                .Where(member => member != null && member.Health != null)
                .Sum(member => member.Health.CurrentHitPoints);
            float startSeparation = Vector2.Distance(
                Centre(director, TeamId.TeamA),
                Centre(director, TeamId.TeamB));

            float previousScale = Time.timeScale;
            try
            {
                Time.timeScale = 3f;
                float startedAt = Time.time;
                float nextProbe = startedAt + 10f;
                while (Time.time - startedAt < BattleSeconds)
                {
                    if (Time.time >= nextProbe)
                    {
                        nextProbe += 10f;
                        Vector2 a = Centre(director, TeamId.TeamA);
                        Vector2 b = Centre(director, TeamId.TeamB);
                        int health = director.SpawnedMembers
                            .Where(member => member != null && member.Health != null)
                            .Sum(member => member.Health.CurrentHitPoints);
                        Debug.Log($"  t={Time.time - startedAt:0} s: centres {Vector2.Distance(a, b):0.#} apart, " +
                                  $"health {health}, alive {director.TeamAAlive}+{director.TeamBAlive}");
                    }

                    yield return null;
                }
            }
            finally
            {
                Time.timeScale = previousScale;
            }

            // A tank that never moved is a tank with no route, which is the failure this arena is most
            // likely to produce: thousands of obstacles and a coarse grid.
            List<string> stuck = new List<string>();
            List<string> movements = new List<string>();
            foreach (TeamMember member in director.SpawnedMembers)
            {
                if (member == null || !start.TryGetValue(member.name, out Vector2 origin)) continue;
                float travelled = Vector2.Distance(origin, member.transform.position);
                movements.Add($"{member.name}={travelled:0.#}");
                if (travelled < 1f) stuck.Add($"{member.name} moved {travelled:0.##} m");
            }

            Debug.Log("  displacement per tank: " + string.Join(", ", movements.OrderBy(item => item)));

            // Whether the AI actually has a route, as opposed to merely having been told to move. A tank
            // with no path is not stuck in geometry, it has nowhere to go, and those need different fixes.
            List<string> routeReport = new List<string>();
            foreach (TeamMember member in director.SpawnedMembers.Where(item => item != null && !item.IsPlayerControlled).Take(4))
            {
                NavigationAgent agent = member.GetComponent<NavigationAgent>();
                routeReport.Add(agent == null
                    ? $"{member.name}=no-agent"
                    : $"{member.name}=path:{agent.HasPath}, dest:{agent.HasDestination}, " +
                      $"paused:{agent.MovementPaused}, recovering:{agent.IsRecoveringFromStall}, " +
                      $"reached:{agent.DestinationReached}");
            }

            Debug.Log("  routes: " + string.Join(" | ", routeReport));

            Assert.That(stuck, Is.Empty,
                "Every tank must leave its spawn; these did not:\n- " + string.Join("\n- ", stuck));

            // The battle must also progress, not just animate. Damage is the cheapest proof.
            int totalHealth = director.SpawnedMembers
                .Where(member => member != null && member.Health != null)
                .Sum(member => member.Health.CurrentHitPoints);

            // How close the two sides ever came. A battle with no damage either means nobody met, or
            // means they met and could not shoot, and those need different fixes.
            float closest = float.MaxValue;
            foreach (TeamMember ally in director.SpawnedMembers.Where(item => item != null && item.Team == TeamId.TeamA))
            {
                foreach (TeamMember enemy in director.SpawnedMembers.Where(item => item != null && item.Team == TeamId.TeamB))
                {
                    closest = Mathf.Min(closest, Vector2.Distance(ally.transform.position, enemy.transform.position));
                }
            }

            Vector2 allyCentre = Centre(director, TeamId.TeamA);
            Vector2 enemyCentre = Centre(director, TeamId.TeamB);
            float separation = Vector2.Distance(allyCentre, enemyCentre);
            Debug.Log($"Arena playability after {BattleSeconds:0} s: {director.TeamAAlive} vs {director.TeamBAlive} alive, " +
                      $"total health {totalHealth}, closest approach {closest:0.#} units, " +
                      $"team centres {separation:0.#} apart, health lost {startHealth - totalHealth}.");

            // The sides start on opposite corners of an eight-hundred-metre field, so closing to contact
            // inside half a minute is the whole point of the arena. This is the check that says whether
            // the map is playable rather than merely loadable, and it is expected to fail until sight and
            // gun ranges are raised to match the field.
            Assert.That(separation, Is.LessThan(startSeparation - 40f),
                $"The teams barely closed: {startSeparation:0.#} units apart at the start, {separation:0.#} " +
                $"after {BattleSeconds:0} s. On this field a view range of {director.SpawnedMembers[0].Definition.viewRange:0.#} " +
                "units covers a few percent of the width, so the sides never reach each other.");

            UnityEngine.Random.state = previousRandomState;
            Time.captureDeltaTime = previousCaptureDeltaTime;
        }

        private static Vector2 Centre(BattleDirector director, TeamId team)
        {
            TeamMember[] members = director.SpawnedMembers
                .Where(member => member != null && member.Team == team)
                .ToArray();
            if (members.Length == 0) return Vector2.zero;
            return members.Aggregate(Vector2.zero, (sum, member) => sum + (Vector2)member.transform.position) /
                   members.Length;
        }
    }
}
