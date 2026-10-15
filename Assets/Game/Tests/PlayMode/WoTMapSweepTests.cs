using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using NUnit.Framework.Internal;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LocalTanks.Tests
{
    /// <summary>
    /// Measures one battle per parameter value, over several seeds, and reports the spread beside the mean.
    /// <para>
    /// Picking a movement constant by changing it, rebuilding and running once does not work: two runs of
    /// identical code on this arena already differ by about ten percent, so a single run per value measures
    /// noise. The sweep exists to make that spread visible, and it runs inside a PlayMode test because that
    /// is the only place frames flow - a batch <c>executeMethod</c> cannot create a scene or keep an object
    /// across scenes, and entering play mode from one receives no editor update callbacks.
    /// </para>
    /// <para>
    /// Each case writes its own result file under <c>TestResults/Sweep</c>, named after the case, so a run
    /// interrupted halfway still yields the cases that finished and a later case cannot overwrite an
    /// earlier one.
    /// </para>
    /// </summary>
    public sealed class WoTMapSweepTests
    {
        private const string SceneName = "Battle_Mines";

        /// <summary>
        /// Field under test and the values to try. Deliberately a small grid: this is a diagnostic tool,
        /// and a sweep long enough to be inconvenient will not be run.
        /// </summary>
        private const string Field = "NavigationAgent.avoidanceRadius";

        private static readonly float[] Values = { 0.6f, 1.2f, 1.8f, 2.4f };

        /// <summary>
        /// Five seeds per value. Three were not enough: the spread within a value matched the difference
        /// between values, so the comparison could not separate an effect from run-to-run noise. Seeds are
        /// fixed rather than random so that adding a seed extends the previous result instead of replacing it.
        /// </summary>
        private static readonly int[] Seeds = { 1911, 2024, 31337, 7, 4242 };

        /// <summary>Simulated seconds per battle. Long enough for the sides to close, short enough to repeat.</summary>
        private const float BattleSeconds = 150f;

        /// <summary>
        /// How much simulation time one frame may carry. The engine clamps the frame delta to
        /// <c>Time.maximumDeltaTime</c>, so this is a request rather than a guarantee, and the loop also
        /// stops on a frame budget so a wrong estimate cannot hang the run.
        /// </summary>
        private const float FrameDelta = 1f / 20f;

        private static bool SceneExists =>
            Enumerable.Range(0, SceneManager.sceneCountInBuildSettings)
                .Any(index => SceneUtility.GetScenePathByBuildIndex(index).EndsWith($"/{SceneName}.unity"));

        /// <summary>One run per value per seed. The case name carries both, so the report is self-describing.</summary>
        private static IEnumerable<(float Value, int Seed)> Cases()
        {
            foreach (float value in Values)
            {
                foreach (int seed in Seeds)
                {
                    yield return (value, seed);
                }
            }
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator MeasuresEachValueOverSeveralSeeds()
        {
            if (!SceneExists) Assert.Ignore($"Scene '{SceneName}' is not in the build settings yet.");

            UnityEngine.Random.State previousRandomState = UnityEngine.Random.state;
            float previousCaptureDeltaTime = Time.captureDeltaTime;
            float previousScale = Time.timeScale;

            foreach ((float value, int seed) in Cases())
            {
                UnityEngine.Random.InitState(seed);
                SceneManager.LoadScene(SceneName);
                yield return null;

                BattleDirector director = Object.FindAnyObjectByType<BattleDirector>();
                Assert.That(director, Is.Not.Null, "The arena scene needs a configured director.");

                // The override is set before the battle starts and applied to everything already alive, so
                // the value is in force from the first frame rather than from the first late spawn.
                LocalTanksOverrides.SetTransient(Field, value);
                foreach (MonoBehaviour behaviour in Object.FindObjectsByType<MonoBehaviour>(
                             FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    LocalTanksOverrides.ApplyToComponent(behaviour);
                }

                BattleTelemetry telemetry = BattleTelemetry.Attach(director);
                Time.captureDeltaTime = FrameDelta;

                // A frame budget as well as a simulated-time stop: the engine may clamp the frame delta, so
                // a loop trusting the clock alone could run far longer than intended.
                int frameBudget = Mathf.CeilToInt(BattleSeconds / FrameDelta * 4f);
                int frames = 0;
                while (telemetry.Outcome().Seconds < BattleSeconds && frames < frameBudget)
                {
                    frames++;
                    yield return null;
                }

                Time.captureDeltaTime = previousCaptureDeltaTime;
                telemetry.Stop();

                SweepCaseResult result = SweepCaseResult.From(Field, value, seed, telemetry.Outcome());
                SweepReport.Write(result);
                Debug.Log($"sweep {result.ToLine()}");
            }

            Time.timeScale = previousScale;
            UnityEngine.Random.state = previousRandomState;

            // The table is printed here as well as written, so an interrupted sweep still shows what it has.
            Debug.Log("sweep table\n" + SweepReport.BuildTable(SweepReport.ReadSummaries()));
        }
    }

    /// <summary>One measured battle: the value tried, the seed, and what came out of it.</summary>
    public sealed class SweepCaseResult
    {
        public string Field;
        public float Value;
        public int Seed;
        public float Seconds;
        public float ClosestApproach;
        public int DamageTaken;
        public int TeamAAlive;
        public int TeamBAlive;

        public static SweepCaseResult From(string field, float value, int seed, BattleOutcome outcome) =>
            new SweepCaseResult
            {
                Field = field,
                Value = value,
                Seed = seed,
                Seconds = outcome.Seconds,
                ClosestApproach = outcome.ClosestApproach,
                DamageTaken = outcome.DamageTaken,
                TeamAAlive = outcome.TeamAAlive,
                TeamBAlive = outcome.TeamBAlive
            };

        public string ToLine() =>
            $"{Field}={Number(Value)} seed={Seed} closest={Number(ClosestApproach)} " +
            $"damage={DamageTaken} seconds={Number(Seconds)}";

        public string ToJson() =>
            $"{{\"field\":\"{Field}\",\"value\":{Number(Value)},\"seed\":{Seed}," +
            $"\"seconds\":{Number(Seconds)},\"closestApproach\":{Number(ClosestApproach)}," +
            $"\"damage\":{DamageTaken},\"teamAAlive\":{TeamAAlive},\"teamBAlive\":{TeamBAlive}}}";

        public static string Number(float value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Writes sweep case files and reads them back as a comparison table. The table is built from the files
    /// rather than held in memory, so a sweep can be run in pieces and still compared as a whole.
    /// </summary>
    public static class SweepReport
    {
        /// <summary>Where case files live, relative to the project root.</summary>
        public const string Directory = "TestResults/Sweep";

        public static string DirectoryPath =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", Directory);

        /// <summary>
        /// Writes one case. The file name carries the case identity so cases cannot overwrite each other,
        /// which matters because a long sweep is routinely stopped partway.
        /// </summary>
        public static void Write(SweepCaseResult result)
        {
            string directory = DirectoryPath;
            System.IO.Directory.CreateDirectory(directory);
            string name = $"{result.Field.Replace('.', '_')}-{SweepCaseResult.Number(result.Value)}" +
                          $"-seed{result.Seed}.json";
            File.WriteAllText(Path.Combine(directory, name), result.ToJson(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        /// <summary>
        /// Case files grouped by value, each with its mean and spread. The spread is the point of the whole
        /// exercise: an effect smaller than it cannot be claimed from these runs.
        /// </summary>
        public static List<SweepValueSummary> ReadSummaries()
        {
            string directory = DirectoryPath;
            if (!System.IO.Directory.Exists(directory)) return new List<SweepValueSummary>();

            List<SweepCaseResult> cases = new List<SweepCaseResult>();
            foreach (string path in System.IO.Directory.GetFiles(directory, "*.json"))
            {
                SweepCaseResult parsed = Parse(File.ReadAllText(path));
                if (parsed != null) cases.Add(parsed);
            }

            return cases
                .GroupBy(item => (item.Field, item.Value))
                .Select(group => SweepValueSummary.From(group.Key.Field, group.Key.Value, group.ToList()))
                .OrderBy(summary => summary.Value)
                .ToList();
        }

        /// <summary>The comparison table as text, ready to print or to attach to a report.</summary>
        public static string BuildTable(IEnumerable<SweepValueSummary> summaries)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("value   runs  closest mean  spread  damage mean");
            foreach (SweepValueSummary summary in summaries)
            {
                builder.AppendLine(
                    $"{SweepCaseResult.Number(summary.Value),5}  {summary.Runs,4}  " +
                    $"{SweepCaseResult.Number(summary.MeanClosestApproach),12}  " +
                    $"{SweepCaseResult.Number(summary.Spread),6}  " +
                    $"{SweepCaseResult.Number(summary.MeanDamage),11}");
            }

            return builder.ToString();
        }

        private static SweepCaseResult Parse(string json)
        {
            // Written and read by the same code, so a small hand parse avoids depending on a serializer in
            // the test assembly for five numbers.
            try
            {
                return new SweepCaseResult
                {
                    Field = Text(json, "field"),
                    Value = Number(json, "value"),
                    Seed = (int)Number(json, "seed"),
                    Seconds = Number(json, "seconds"),
                    ClosestApproach = Number(json, "closestApproach"),
                    DamageTaken = (int)Number(json, "damage"),
                    TeamAAlive = (int)Number(json, "teamAAlive"),
                    TeamBAlive = (int)Number(json, "teamBAlive")
                };
            }
            catch (System.Exception error)
            {
                Debug.LogWarning($"A sweep case file could not be read: {error.Message}");
                return null;
            }
        }

        private static string Text(string json, string key)
        {
            int start = json.IndexOf($"\"{key}\":\"", System.StringComparison.Ordinal);
            if (start < 0) return string.Empty;
            start += key.Length + 4;
            int end = json.IndexOf('"', start);
            return end < 0 ? string.Empty : json.Substring(start, end - start);
        }

        private static float Number(string json, string key)
        {
            int start = json.IndexOf($"\"{key}\":", System.StringComparison.Ordinal);
            if (start < 0) return 0f;
            start += key.Length + 3;
            int end = start;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-' || json[end] == '.')) end++;
            return float.Parse(json.Substring(start, end - start), CultureInfo.InvariantCulture);
        }
    }

    /// <summary>One value's runs, averaged, with the spread that decides whether the value matters.</summary>
    public sealed class SweepValueSummary
    {
        public string Field;
        public float Value;
        public int Runs;
        public float MeanClosestApproach;
        public float MinClosestApproach;
        public float MaxClosestApproach;
        public float MeanDamage;

        /// <summary>Half the gap between the best and worst seed: the noise floor of these runs.</summary>
        public float Spread => (MaxClosestApproach - MinClosestApproach) * 0.5f;

        public static SweepValueSummary From(string field, float value, List<SweepCaseResult> cases) =>
            new SweepValueSummary
            {
                Field = field,
                Value = value,
                Runs = cases.Count,
                MeanClosestApproach = cases.Average(item => item.ClosestApproach),
                MinClosestApproach = cases.Min(item => item.ClosestApproach),
                MaxClosestApproach = cases.Max(item => item.ClosestApproach),
                MeanDamage = (float)cases.Average(item => item.DamageTaken)
            };
    }
}
