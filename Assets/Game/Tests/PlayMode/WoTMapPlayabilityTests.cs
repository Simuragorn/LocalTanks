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
    /// it. Everything else Ã¢â‚¬â€ colliders, navigation, routes Ã¢â‚¬â€ is scaffolding for this, and a mistake in
    /// any of them shows up here as tanks that never move or never meet.
    /// </summary>
    public sealed class WoTMapPlayabilityTests
    {
        private const string SceneName = "Battle_10_hills";

        /// <summary>
        /// Simulated battle length. The sides start on opposite corners of an eight-hundred-metre field,
        /// so at roughly two units per second they need about ninety seconds of travel before they can
        /// even see each other. A shorter run measures the approach rather than the battle, which is how
        /// a "no damage" result was misread as a broken AI twice.
        /// </summary>
        private const float BattleSeconds = 150f;

        /// <summary>Time compression, kept modest so physics and AI decisions stay faithful.</summary>
        private const float TimeCompression = 5f;

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
            float closestEver = float.MaxValue;
            // Path length actually walked, accumulated per frame. Net displacement cannot separate a tank
            // that crawls from one that drives out and returns, and that ambiguity has already cost two
            // wrong diagnoses.
            Dictionary<string, float> walked = director.SpawnedMembers
                .Where(member => member != null)
                .ToDictionary(member => member.name, _ => 0f);
            Dictionary<string, Vector2> lastSeen = director.SpawnedMembers
                .Where(member => member != null)
                .ToDictionary(member => member.name, member => (Vector2)member.transform.position);
            try
            {
                Time.timeScale = TimeCompression;
                float startedAt = Time.time;
                float nextProbe = startedAt + 50f;
                while (Time.time - startedAt < BattleSeconds)
                {
                    foreach (TeamMember member in director.SpawnedMembers)
                    {
                        if (member == null || !lastSeen.ContainsKey(member.name)) continue;
                        Vector2 now = member.transform.position;
                        walked[member.name] += Vector2.Distance(lastSeen[member.name], now);
                        lastSeen[member.name] = now;
                    }
                    // Track the closest the two sides ever come. A battle that never starts and a battle
                    // that starts and then stalls look identical in an end-of-run snapshot, and this is
                    // what separates them.
                    foreach (TeamMember ally in director.SpawnedMembers.Where(item => item != null && item.Team == TeamId.TeamA))
                    {
                        foreach (TeamMember enemy in director.SpawnedMembers.Where(item => item != null && item.Team == TeamId.TeamB))
                        {
                            closestEver = Mathf.Min(closestEver,
                                Vector2.Distance(ally.transform.position, enemy.transform.position));
                        }
                    }

                    if (Time.time >= nextProbe)
                    {
                        nextProbe += 50f;
                        Vector2 a = Centre(director, TeamId.TeamA);
                        Vector2 b = Centre(director, TeamId.TeamB);
                        int health = director.SpawnedMembers
                            .Where(member => member != null && member.Health != null)
                            .Sum(member => member.Health.CurrentHitPoints);
                        Debug.Log($"  t={Time.time - startedAt:0} s: centres {Vector2.Distance(a, b):0.#} apart, " +
                                  $"closest yet {closestEver:0.#}, health {health}, " +
                                  $"alive {director.TeamAAlive}+{director.TeamBAlive}, " +
                                  $"stalled {StalledCount(director)}/{director.SpawnedMembers.Count}");
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

            // Whether the route even starts where the tank stands. A route whose first point is already
            // reached makes the AI declare the march complete before it moves, which looks like an idle
            // bot rather than a routing fault.
            CaptureBase baseA = Object.FindObjectsByType<CaptureBase>(FindObjectsSortMode.None)
                .Single(item => item.BaseId == "A");
            CaptureBase baseB = Object.FindObjectsByType<CaptureBase>(FindObjectsSortMode.None)
                .Single(item => item.BaseId == "B");
            Debug.Log($"  bases: A {baseA.transform.position}, B {baseB.transform.position}, " +
                      $"apart {Vector2.Distance(baseA.transform.position, baseB.transform.position):0.#}");

            List<string> routeStarts = new List<string>();
            foreach (TeamMember member in director.SpawnedMembers.Where(item => item != null && !item.IsPlayerControlled).Take(3))
            {
                CombatTankAI ai = member.GetComponent<CombatTankAI>();
                if (ai == null) continue;
                Vector2[] points = ai.RoutePoints;
                Vector2 first = points.FirstOrDefault();
                Vector2 last = points.LastOrDefault();
                routeStarts.Add($"{member.name} ({member.Team}): {points.Length} points, " +
                                $"first {first} (toA {Vector2.Distance(first, baseA.transform.position):0.#}, " +
                                $"toB {Vector2.Distance(first, baseB.transform.position):0.#}), " +
                                $"last {last} (toA {Vector2.Distance(last, baseA.transform.position):0.#}, " +
                                $"toB {Vector2.Distance(last, baseB.transform.position):0.#})");
            }

            Debug.Log("  route geometry: " + string.Join(" | ", routeStarts));

            // Actual motion, not just intent: a tank with a path that crawls is a different fault from one
            // with no path, and the two need different fixes.
            List<string> motion = new List<string>();
            foreach (TeamMember member in director.SpawnedMembers.Where(item => item != null && !item.IsPlayerControlled).Take(4))
            {
                TankMotor motor = member.GetComponent<TankMotor>();
                NavigationAgent agent = member.GetComponent<NavigationAgent>();
                motion.Add($"{member.name}: speed {motor?.CurrentSpeed ?? 0f:0.00}, " +
                           $"limit {member.Definition.maxForwardSpeed:0.00}, " +
                           $"pathPoints {(agent != null && agent.HasPath ? "yes" : "no")}");
            }

            Debug.Log("  motion: " + string.Join(" | ", motion));

            // The number that decides the pace question: distance actually travelled divided by the time
            // and the tank's own limit. A value near one means the tank uses its speed; a small value
            // means something throttles it every frame.
            List<string> pace = new List<string>();
            foreach (TeamMember member in director.SpawnedMembers.Where(item => item != null && !item.IsPlayerControlled).Take(4))
            {
                float metres = walked[member.name];
                float limit = member.Definition.maxForwardSpeed;
                pace.Add($"{member.name}: walked {metres:0.#} units, " +
                         $"{(limit > 0f ? metres / BattleSeconds / limit : 0f):P0} of its speed");
            }

            Debug.Log("  pace: " + string.Join(" | ", pace));

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

        /// <summary>
        /// How many tanks are in the stall-recovery manoeuvre right now. Recovery overrides the steering
        /// and drives the tank backwards for a couple of seconds, so a high count means the tanks are
        /// spending the battle escaping rather than advancing.
        /// </summary>
        private static int StalledCount(BattleDirector director)
        {
            return director.SpawnedMembers
                .Where(member => member != null)
                .Select(member => member.GetComponent<NavigationAgent>())
                .Count(agent => agent != null && agent.IsRecoveringFromStall);
        }

        private static Vector2 Centre(BattleDirector director, TeamId team)        {
            TeamMember[] members = director.SpawnedMembers
                .Where(member => member != null && member.Team == team)
                .ToArray();
            if (members.Length == 0) return Vector2.zero;
            return members.Aggregate(Vector2.zero, (sum, member) => sum + (Vector2)member.transform.position) /
                   members.Length;
        }
    }
}
