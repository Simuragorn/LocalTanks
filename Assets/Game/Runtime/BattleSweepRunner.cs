using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LocalTanks
{
    /// <summary>
    /// Runs one battle per parameter value and reports a comparison table.
    /// <para>
    /// Picking a movement constant used to mean changing a number, rebuilding the scene, running one
    /// battle and hoping the difference was real. That attempt failed on the avoidance radius precisely
    /// because two runs of identical code already differ by about ten percent, so a single run per value
    /// measures noise. This runner fixes both halves of that problem: it runs many values in one process,
    /// and it repeats each value over several seeds so the spread is visible next to the effect.
    /// </para>
    /// <para>
    /// It never touches the physics step, only the simulation rate, so every run describes the same game.
    /// Start it with <c>-executeMethod LocalTanks.Editor.LocalTanksSweepRunner.RunFromCommandLine</c> and
    /// name the field, its values and the seeds; see <c>Tools/Invoke-LocalTanksTests.ps1</c> for the
    /// switches this reads.
    /// </para>
    /// </summary>
    public static class LocalTanksSweepRunner
    {
        /// <summary>Command line switch giving the scene to load, as <c>-sweepScene Battle_10_hills</c>.</summary>
        public const string SceneSwitch = "-sweepScene";

        /// <summary>Command line switch giving the field to vary, as <c>-sweepField NavigationAgent.avoidanceRadius</c>.</summary>
        public const string FieldSwitch = "-sweepField";

        /// <summary>Command line switch giving the values to try, comma separated.</summary>
        public const string ValuesSwitch = "-sweepValues";

        /// <summary>Command line switch giving the seeds to repeat each value over.</summary>
        public const string SeedsSwitch = "-sweepSeeds";

        /// <summary>Command line switch giving simulated seconds per run.</summary>
        public const string SecondsSwitch = "-sweepSeconds";

        /// <summary>Command line switch giving the simulation multiplier.</summary>
        public const string SpeedSwitch = "-sweepSpeed";

        /// <summary>Command line switch giving the output directory.</summary>
        public const string OutputSwitch = "-sweepOut";

        /// <summary>Command line switch giving fixed overrides applied to every run.</summary>
        public const string FixedSwitch = "-sweepFixed";

        /// <summary>
        /// Entry point for batch mode. Every result is printed as a table line and the whole table is
        /// written as JSON beside the per-run recordings.
        /// </summary>
        public static void RunFromCommandLine()
        {
            try
            {
                Run(SweepRequest.FromCommandLine());
                // Batch methods do not return exit codes; a failure throws, which Unity reports and which
                // stops the run rather than leaving a half-written table that looks like success.
            }
            catch (Exception error)
            {
                Debug.LogError($"Sweep failed: {error}");
                throw;
            }
        }

        /// <summary>Runs the requested sweep and writes its table.</summary>
        public static void Run(SweepRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            request.Validate();

            string directory = string.IsNullOrWhiteSpace(request.OutputDirectory)
                ? Path.Combine("TestResults", "Sweep")
                : request.OutputDirectory;
            Directory.CreateDirectory(directory);

            List<SweepResult> results = new List<SweepResult>();
            int runIndex = 0;
            int totalRuns = request.Values.Count * request.Seeds.Count;
            Debug.Log($"Sweeping {request.Field} over {string.Join(", ", request.Values.Select(Format))} " +
                      $"across {request.Seeds.Count} seed(s): {totalRuns} run(s), " +
                      $"{Format(request.Seconds)} simulated seconds each at {Format(request.Speed)}x.");

            foreach (float seed in request.Seeds)
            {
                foreach (float value in request.Values)
                {
                    runIndex++;
                    SweepResult result = RunOne(request, directory, seed, value, runIndex, totalRuns);
                    results.Add(result);
                }
            }

            string tablePath = Path.Combine(directory, "sweep.json");
            File.WriteAllText(tablePath, BuildTable(request, results),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Debug.Log($"Sweep wrote {results.Count} run(s) to {tablePath}");
            PrintTable(request, results);
        }

        /// <summary>
        /// Loads the scene for one run and advances it to completion.
        /// <para>
        /// A batch method does not run inside Unity's frame loop, so nothing would tick the battle and the
        /// scene would sit still. The sweep therefore needs both a way to advance frames and a scene to
        /// advance them into. The host component owns an empty persistent scene, and the battle loads
        /// additively into the scene the host lives in, so every run starts from the same clean state and
        /// the previous run's objects are gone.
        /// </para>
        /// </summary>
        private static SweepResult RunOne(
            SweepRequest request,
            string directory,
            float seed,
            float value,
            int runIndex,
            int totalRuns)
        {
            string label = $"{request.FieldName}={Format(value)} seed={Format(seed)}";
            Debug.Log($"[{runIndex}/{totalRuns}] {label}");

            LocalTanksOverrides.SetTransient(request.FieldName, value);
            UnityEngine.Random.InitState((int)seed);

            SweepResult outcome = null;
            GameObject host = new GameObject("SweepHost");
            UnityEngine.Object.DontDestroyOnLoad(host);
            SweepHost driver = host.AddComponent<SweepHost>();
            Scene hostScene = host.scene;

            driver.Begin(request, directory, seed, value, label, result => outcome = result);

            int frameBudget = Mathf.CeilToInt(
                request.Seconds * Mathf.Clamp(request.Speed, 1f, BattleSimulationClock.MaximumMultiplier) * 20f);
            int frames = 0;
            float deadline = Time.realtimeSinceStartup + request.RealtimeBudgetSeconds;
            while (outcome == null && frames < frameBudget && Time.realtimeSinceStartup < deadline)
            {
                driver.Pump();
                frames++;
            }

            if (outcome == null)
            {
                Debug.LogWarning($"Run '{label}' did not finish within {frames} frames " +
                                 $"({request.RealtimeBudgetSeconds:0} real seconds) and was abandoned.");
                outcome = SweepResult.CreateAbandoned(request, seed, value, label);
            }

            driver.UnloadBattleScene();
            SceneManager.SetActiveScene(hostScene);
            UnityEngine.Object.DestroyImmediate(host);
            return outcome;
        }

        /// <summary>Pumps one simulation step per call, which is what keeps a batch sweep moving.</summary>
        private sealed class SweepHost : MonoBehaviour
        {
            private IEnumerator routine;

            public void Begin(
                SweepRequest request,
                string directory,
                float seed,
                float value,
                string label,
                Action<SweepResult> completed)
            {
                routine = RunBattle(request, directory, seed, value, label, completed);
            }

            /// <summary>Advances the routine by one step, which is what makes a frame pass.</summary>
            public void Pump()
            {
                if (routine == null) return;
                if (!routine.MoveNext()) routine = null;
            }

            /// <summary>Removes the battle scene so the next run starts from nothing.</summary>
            public void UnloadBattleScene()
            {
                Scene battle = SceneManager.GetSceneByName(battleSceneName);
                if (battle.IsValid() && battle.isLoaded) SceneManager.UnloadSceneAsync(battle);
            }

            private IEnumerator RunBattle(
                SweepRequest request,
                string directory,
                float seed,
                float value,
                string label,
                Action<SweepResult> completed)
            {
                // The battle loads into this scene rather than replacing it, so the host survives and every
                // run begins from an empty scene. Two frames then let Awake, Start and the first ticks run.
                Scene hostScene = gameObject.scene;
                SceneManager.LoadScene(request.SceneName, LoadSceneMode.Additive);
                battleSceneName = request.SceneName;
                SceneManager.SetActiveScene(SceneManager.GetSceneByName(request.SceneName));
                yield return null;
                yield return null;

                BattleDirector director = FindAnyObjectByType<BattleDirector>();
                if (director == null)
                {
                    Debug.LogError($"Sweep scene '{request.SceneName}' has no BattleDirector.");
                    completed(SweepResult.CreateAbandoned(request, seed, value, label));
                    yield break;
                }

                string recordingPath = Path.Combine(
                    directory, $"run-{Sanitize(request.FieldName)}-{Format(value)}-seed{Format(seed)}.json");
                BattleTelemetry telemetry = BattleTelemetry.Attach(director, recordingPath);
                float multiplier = Mathf.Clamp(request.Speed, 1f, BattleSimulationClock.MaximumMultiplier);
                Time.timeScale = multiplier;
                Time.captureDeltaTime = Mathf.Min(Time.fixedDeltaTime * multiplier, 0.1f);

                float deadline = Time.realtimeSinceStartup + request.RealtimeBudgetSeconds;
                while (telemetry.Outcome().Seconds < request.Seconds &&
                       Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                Time.timeScale = 1f;
                Time.captureDeltaTime = 0f;
                telemetry.Stop();
                BattleOutcome outcome = telemetry.Outcome();
                completed(SweepResult.From(request, seed, value, label, outcome));
                SceneManager.SetActiveScene(hostScene);
            }

            private string battleSceneName;
        }

        public static string BuildTable(SweepRequest request, List<SweepResult> results)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append('{');
            builder.Append($"\"scene\":\"{request.SceneName}\",");
            builder.Append($"\"field\":\"{request.FieldName}\",");
            builder.Append($"\"seconds\":{Format(request.Seconds)},");
            builder.Append($"\"speed\":{Format(request.Speed)},");
            builder.Append("\"runs\":[");
            builder.Append(string.Join(",", results.Select(result => result.ToJson())));
            builder.Append("],\"summary\":[");
            builder.Append(string.Join(",", Summarize(request, results).Select(item => item.ToJson())));
            builder.Append("]}");
            return builder.ToString();
        }

        /// <summary>
        /// Averages each value over its seeds and reports the spread. The spread is the point: an effect
        /// smaller than it cannot be claimed from these runs.
        /// </summary>
        public static List<SweepSummary> Summarize(SweepRequest request, List<SweepResult> results)
        {
            List<SweepSummary> summaries = new List<SweepSummary>();
            foreach (float value in request.Values)
            {
                List<SweepResult> group = results.Where(result => Mathf.Approximately(result.Value, value)).ToList();
                if (group.Count == 0) continue;
                summaries.Add(new SweepSummary
                {
                    Value = value,
                    Runs = group.Count,
                    MeanClosestApproach = group.Average(item => item.ClosestApproach),
                    MinClosestApproach = group.Min(item => item.ClosestApproach),
                    MaxClosestApproach = group.Max(item => item.ClosestApproach),
                    MeanDamage = (float)group.Average(item => item.DamageTaken),
                    MeanSeconds = group.Average(item => item.Seconds)
                });
            }

            return summaries;
        }

        public static void PrintTable(SweepRequest request, List<SweepResult> results)
        {
            Debug.Log($"--- {request.Field} over {request.Seconds:0} s " +
                      $"({request.Seeds.Count} seed(s) per value) ---");
            Debug.Log($"{"value",10} {"closest mean",14} {"closest min",13} {"damage mean",13} {"seconds",9}");
            foreach (SweepSummary item in Summarize(request, results))
            {
                Debug.Log($"{Format(item.Value),10} {item.MeanClosestApproach,14:0.0} " +
                          $"{item.MinClosestApproach,13:0.0} {item.MeanDamage,13:0.0} {item.MeanSeconds,9:0.0}");
            }
        }

        private static string Sanitize(string name) =>
            new string(name.Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());

        private static string Format(float value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>What a sweep was asked to do, parsed from the command line or built directly.</summary>
    public sealed class SweepRequest
    {
        public string SceneName = "Battle_10_hills";
        public string FieldName;
        public List<float> Values = new List<float>();
        public List<float> Seeds = new List<float> { 1911f };
        public float Seconds = 150f;
        public float Speed = 20f;
        public string OutputDirectory;
        public Dictionary<string, float> Fixed = new Dictionary<string, float>();

        /// <summary>How long one run may take in real seconds before it is abandoned.</summary>
        public float RealtimeBudgetSeconds = 120f;

        public string Field => FieldName ?? string.Empty;

        /// <summary>Seeds as a list of the same type the results use.</summary>
        public System.Collections.Generic.List<float> SeedValues => Seeds;

        /// <summary>Reads the sweep switches from the process command line.</summary>
        public static SweepRequest FromCommandLine()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            SweepRequest request = new SweepRequest
            {
                SceneName = Value(arguments, LocalTanksSweepRunner.SceneSwitch) ?? "Battle_10_hills",
                FieldName = Value(arguments, LocalTanksSweepRunner.FieldSwitch),
                OutputDirectory = Value(arguments, LocalTanksSweepRunner.OutputSwitch)
            };

            string values = Value(arguments, LocalTanksSweepRunner.ValuesSwitch);
            if (!string.IsNullOrWhiteSpace(values))
            {
                foreach (string part in values.Split(','))
                {
                    if (float.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                            out float parsed))
                    {
                        request.Values.Add(parsed);
                    }
                }
            }

            string seeds = Value(arguments, LocalTanksSweepRunner.SeedsSwitch);
            if (!string.IsNullOrWhiteSpace(seeds))
            {
                request.Seeds = seeds.Split(',')
                    .Select(part => float.TryParse(part.Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out float parsed) ? parsed : 0f)
                    .Where(seed => seed != 0f)
                    .ToList();
                if (request.Seeds.Count == 0) request.Seeds.Add(1911f);
            }

            request.Seconds = Number(arguments, LocalTanksSweepRunner.SecondsSwitch, request.Seconds);
            request.Speed = Number(arguments, LocalTanksSweepRunner.SpeedSwitch, request.Speed);

            string fixedOverrides = Value(arguments, LocalTanksSweepRunner.FixedSwitch);
            if (!string.IsNullOrWhiteSpace(fixedOverrides))
            {
                foreach (string part in fixedOverrides.Split(';'))
                {
                    int equals = part.IndexOf('=');
                    if (equals <= 0) continue;
                    if (float.TryParse(part.Substring(equals + 1).Trim(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out float parsed))
                    {
                        request.Fixed[part.Substring(0, equals).Trim()] = parsed;
                    }
                }
            }

            return request;
        }

        /// <summary>Fails loudly on a sweep that could not produce a comparison.</summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(FieldName))
            {
                throw new InvalidOperationException(
                    $"A sweep needs a field to vary. Pass {LocalTanksSweepRunner.FieldSwitch} " +
                    "Component.field.");
            }

            if (Values.Count == 0)
            {
                throw new InvalidOperationException(
                    $"A sweep needs at least one value. Pass {LocalTanksSweepRunner.ValuesSwitch} v1,v2.");
            }

            if (Seconds <= 0f)
            {
                throw new InvalidOperationException("A sweep needs a positive number of simulated seconds.");
            }
        }

        private static string Value(string[] arguments, string name)
        {
            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
                {
                    return arguments[index + 1];
                }
            }

            return null;
        }

        private static float Number(string[] arguments, string name, float fallback)
        {
            string raw = Value(arguments, name);
            return raw != null && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture,
                out float parsed)
                ? parsed
                : fallback;
        }
    }

    /// <summary>One run of a sweep: the value tried, the seed, and what happened.</summary>
    public sealed class SweepResult
    {
        public float Value;
        public float Seed;
        public string Label;
        public float Seconds;
        public int TeamAAlive;
        public int TeamBAlive;
        public int TeamAHealth;
        public int TeamBHealth;
        public float ClosestApproach;
        public int DamageTaken;
        public bool Abandoned;

        public static SweepResult From(
            SweepRequest request,
            float seed,
            float value,
            string label,
            BattleOutcome outcome) =>
            new SweepResult
            {
                Value = value,
                Seed = seed,
                Label = label,
                Seconds = outcome.Seconds,
                TeamAAlive = outcome.TeamAAlive,
                TeamBAlive = outcome.TeamBAlive,
                TeamAHealth = outcome.TeamAHealth,
                TeamBHealth = outcome.TeamBHealth,
                ClosestApproach = outcome.ClosestApproach,
                DamageTaken = outcome.DamageTaken,
                Abandoned = false
            };

        public static SweepResult CreateAbandoned(SweepRequest request, float seed, float value, string label) =>
            new SweepResult { Value = value, Seed = seed, Label = label, Abandoned = true };

        public string ToJson() =>
            $"{{\"value\":{Number(Value)},\"seed\":{Number(Seed)},\"abandoned\":{(Abandoned ? "true" : "false")}," +
            $"\"seconds\":{Number(Seconds)},\"closestApproach\":{Number(ClosestApproach)}," +
            $"\"damage\":{DamageTaken},\"teamAAlive\":{TeamAAlive},\"teamBAlive\":{TeamBAlive}," +
            $"\"teamAHealth\":{TeamAHealth},\"teamBHealth\":{TeamBHealth}}}";

        private static string Number(float value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>One value's average across its seeds, with the spread that says whether it means anything.</summary>
    public sealed class SweepSummary
    {
        public float Value;
        public int Runs;
        public float MeanClosestApproach;
        public float MinClosestApproach;
        public float MaxClosestApproach;
        public float MeanDamage;
        public float MeanSeconds;

        /// <summary>Half the spread between the best and worst seed, the noise floor of this sweep.</summary>
        public float Spread => (MaxClosestApproach - MinClosestApproach) * 0.5f;

        public string ToJson() =>
            $"{{\"value\":{Number(Value)},\"runs\":{Runs}," +
            $"\"meanClosestApproach\":{Number(MeanClosestApproach)}," +
            $"\"minClosestApproach\":{Number(MinClosestApproach)}," +
            $"\"maxClosestApproach\":{Number(MaxClosestApproach)}," +
            $"\"spread\":{Number(Spread)},\"meanDamage\":{Number(MeanDamage)}," +
            $"\"meanSeconds\":{Number(MeanSeconds)}}}";

        private static string Number(float value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
