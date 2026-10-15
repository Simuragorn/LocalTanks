using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LocalTanks.Editor
{
    /// <summary>
    /// Starts a parameter sweep from batch mode by entering play mode, which the runtime sweep needs.
    /// <para>
    /// The sweep loads scenes and keeps objects alive across them, and Unity allows neither outside play
    /// mode: <c>SceneManager.CreateScene</c> and <c>DontDestroyOnLoad</c> both refuse to run in an editor
    /// script. Rather than reimplement the sweep for the editor, this class does the one thing an editor
    /// can do that a player cannot - start play mode - and then hands over to the same runtime runner a
    /// player would use. The result is that a batch sweep and an in-editor sweep execute identical code.
    /// </para>
    /// <para>
    /// The whole run is driven from <see cref="EditorApplication.update"/>, so no frames depend on a
    /// display, and the process leaves play mode and quits when the table is written.
    /// </para>
    /// </summary>
    public static class LocalTanksSweepEditorRunner
    {
        private static bool running;

        /// <summary>Batch entry point: <c>-executeMethod LocalTanks.Editor.LocalTanksSweepEditorRunner.RunFromCommandLine</c>.</summary>
        public static void RunFromCommandLine()
        {
            SweepRequest request = SweepRequest.FromCommandLine();
            request.Validate();

            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += Pump;
            running = true;
            Debug.Log("Entering play mode to run the sweep.");
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            // Reached only if something stopped the run early; without it a failed sweep would leave the
            // editor in play mode and batch mode would never exit.
            if (state == PlayModeStateChange.ExitingPlayMode && running)
            {
                Debug.LogWarning("A sweep left play mode before finishing.");
                Finish(0);
            }
        }

        private static void Pump()
        {
            if (!running) return;
            if (!EditorApplication.isPlaying)
            {
                return;
            }

            if (sweepRoutine == null)
            {
                sweepRoutine = RunSweep();
            }

            if (!sweepRoutine.MoveNext())
            {
                sweepRoutine = null;
                Finish(0);
            }
        }

        private static IEnumerator sweepRoutine;

        private static IEnumerator RunSweep()
        {
            // One frame so play mode is fully entered and the initial scene is awake.
            yield return null;

            SweepRequest request = SweepRequest.FromCommandLine();
            Debug.Log($"Sweep: {request.Field} over {string.Join(", ", request.Values)} across " +
                      $"{request.Seeds.Count} seed(s), {request.Seconds:0} s each at {request.Speed:0}x.");

            string directory = string.IsNullOrWhiteSpace(request.OutputDirectory)
                ? Path.Combine("TestResults", "Sweep")
                : request.OutputDirectory;
            Directory.CreateDirectory(directory);

            var results = new System.Collections.Generic.List<SweepResult>();
            int index = 0;
            int total = request.Values.Count * request.Seeds.Count;
            foreach (float seed in request.SeedValues)
            {
                foreach (float value in request.Values)
                {
                    index++;
                    LocalTanksOverrides.SetTransient(request.FieldName, value);
                    UnityEngine.Random.InitState((int)seed);
                    Debug.Log($"[{index}/{total}] {request.Field}={value} seed={seed}");

                    string sceneName = request.SceneName;
                    Scene previous = SceneManager.GetActiveScene();
                    if (previous.name != sceneName)
                    {
                        AsyncOperation load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
                        while (load != null && !load.isDone) yield return null;
                    }

                    yield return null;
                    yield return null;

                    BattleDirector director = UnityEngine.Object.FindAnyObjectByType<BattleDirector>();
                    if (director == null)
                    {
                        Debug.LogError($"Sweep scene '{sceneName}' has no BattleDirector.");
                        results.Add(SweepResult.CreateAbandoned(request, seed, value, "no director"));
                        continue;
                    }

                    string recording = Path.Combine(directory,
                        $"run-{Sanitize(request.Field)}-{value:0.###}-seed{seed:0}.json");
                    BattleTelemetry telemetry = BattleTelemetry.Attach(director, recording);
                    float multiplier = Mathf.Clamp(request.Speed, 1f, BattleSimulationClock.MaximumMultiplier);
                    Time.timeScale = multiplier;
                    Time.captureDeltaTime = Mathf.Min(Time.fixedDeltaTime * multiplier, 0.1f);
                    Time.fixedDeltaTime = 0.02f;

                    // Frame budget rather than wall clock: in batch mode frames are free, and a wall-clock
                    // limit would abandon good runs on a slow machine and waste fast ones.
                    int frameBudget = Mathf.CeilToInt(request.Seconds * multiplier * 20f);
                    int frames = 0;
                    while (telemetry.Outcome().Seconds < request.Seconds && frames < frameBudget)
                    {
                        frames++;
                        yield return null;
                    }

                    Time.timeScale = 1f;
                    Time.captureDeltaTime = 0f;
                    telemetry.Stop();
                    results.Add(SweepResult.From(request, seed, value, $"{request.Field}={value}", telemetry.Outcome()));
                }
            }

            string tablePath = Path.Combine(directory, "sweep.json");
            File.WriteAllText(tablePath, LocalTanksSweepRunner.BuildTable(request, results),
                new System.Text.UTF8Encoding(false));
            Debug.Log($"Sweep wrote {results.Count} run(s) to {tablePath}");
            LocalTanksSweepRunner.PrintTable(request, results);
        }

        private static string Sanitize(string name) =>
            new string(name.Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());

        private static void Finish(int exitCode)
        {
            running = false;
            EditorApplication.update -= Pump;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }
    }

    /// <summary>Enters play mode; the command-line runtime bootstrap owns the timed run and process exit.</summary>
    public static class LocalTanksPerformanceEditorRunner
    {
        public static void RunFromCommandLine()
        {
            Debug.Log("Entering play mode for the River Crossing frame performance run.");
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            BattlePerformanceCommandLineRunner.Create();
        }
    }
}
