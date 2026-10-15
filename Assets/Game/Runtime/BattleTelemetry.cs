using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LocalTanks
{
    internal enum BattlePerformanceSubsystem
    {
        CombatAi,
        Navigation,
        Motor,
        Vision,
        TankVfx,
        TrackAnimation,
        Audio,
        Hud,
        CombatVfx,
        Capture,
        Count
    }

    /// <summary>
    /// Allocation-free diagnostic scopes used only while battle telemetry is recording. They attribute
    /// long frames and managed allocations to broad gameplay systems without enabling Unity Deep Profile.
    /// </summary>
    internal static class BattlePerformanceProbe
    {
        private static readonly long[] ElapsedTicks = new long[(int)BattlePerformanceSubsystem.Count];
        private static readonly long[] AllocatedBytes = new long[(int)BattlePerformanceSubsystem.Count];
        private static bool enabled;

        internal static Scope Measure(BattlePerformanceSubsystem subsystem) => new Scope(enabled, subsystem);

        internal static void Start()
        {
            Array.Clear(ElapsedTicks, 0, ElapsedTicks.Length);
            Array.Clear(AllocatedBytes, 0, AllocatedBytes.Length);
            enabled = true;
        }

        internal static void Stop()
        {
            enabled = false;
            Array.Clear(ElapsedTicks, 0, ElapsedTicks.Length);
            Array.Clear(AllocatedBytes, 0, AllocatedBytes.Length);
        }

        internal static void Consume(
            BattlePerformanceSubsystem subsystem,
            out float elapsedMs,
            out long allocatedBytes)
        {
            int index = (int)subsystem;
            elapsedMs = (float)(ElapsedTicks[index] * 1000d / System.Diagnostics.Stopwatch.Frequency);
            allocatedBytes = AllocatedBytes[index];
            ElapsedTicks[index] = 0L;
            AllocatedBytes[index] = 0L;
        }

        internal readonly struct Scope : IDisposable
        {
            private readonly bool active;
            private readonly int subsystem;
            private readonly long startedAt;
            private readonly long allocatedAtStart;

            internal Scope(bool active, BattlePerformanceSubsystem subsystem)
            {
                this.active = active;
                this.subsystem = (int)subsystem;
                startedAt = active ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
                allocatedAtStart = active ? GC.GetAllocatedBytesForCurrentThread() : 0L;
            }

            public void Dispose()
            {
                if (!active) return;
                ElapsedTicks[subsystem] += System.Diagnostics.Stopwatch.GetTimestamp() - startedAt;
                AllocatedBytes[subsystem] += Math.Max(
                    0L,
                    GC.GetAllocatedBytesForCurrentThread() - allocatedAtStart);
            }
        }
    }

    /// <summary>
    /// End state of a recorded battle, for callers that compare runs rather than read a file.
    /// </summary>
    public struct BattleOutcome
    {
        public float Seconds;
        public int TeamAAlive;
        public int TeamBAlive;
        public int TeamAHealth;
        public int TeamBHealth;
        public float ClosestApproach;
        public int Samples;
        public int Events;

        /// <summary>Health both sides started with, so damage can be told from an untouched battle.</summary>
        public int StartingHealth;

        /// <summary>Health lost across both sides. Zero means nobody was hit at all.</summary>
        public int DamageTaken => Mathf.Max(0, StartingHealth - (TeamAHealth + TeamBHealth));
    }

    /// <summary>Wall-clock frame performance reduced from the per-frame telemetry samples.</summary>
    public struct BattlePerformanceSummary
    {
        public int Frames;
        public float AverageFps;
        public float AverageFrameMs;
        public float MedianFrameMs;
        public float P95FrameMs;
        public float P99FrameMs;
        public float MaximumFrameMs;
        public float P95CpuFrameMs;
        public float P95GpuFrameMs;
        public int FramesOver33Ms;
        public int FramesOver50Ms;
        public int FramesOver100Ms;
        public long TotalGcBytes;
        public long PeakGcBytes;
        public int GcAllocatingFrames;
        public long PeakDrawCalls;
        public long PeakSetPassCalls;
        public long PeakTriangles;
        public long PeakVertices;
        public int PeakFixedStepsPerFrame;
        public int PeakVisionQueriesPerFrame;
        public int PeakVisionEvaluationsPerFrame;
        public int TotalVisionEvaluations;
        public int TrackMarkEmitters;
        public int TrackMarkCapacity;
        public int TrackMarkRenderers;
        public int LiveTrackMarks;
        public int EmittedTrackMarks;
        public int SkippedTrackMarks;
    }

    /// <summary>
    /// Records a battle as a time series so its behaviour can be queried instead of watched.
    /// <para>
    /// Investigating why bots stalled took many full battles, each one read by eye out of a log. With a
    /// recording, questions like "when and why did this bot stop" are answered by a query over the data,
    /// and a parameter sweep can compare runs without anyone watching them.
    /// </para>
    /// <para>
    /// Recording is off unless the process was started with <c>-localtanksTelemetry</c>, or a caller
    /// enables it explicitly. That is deliberate: a normal battle must pay nothing for a facility only
    /// diagnosis uses. While disabled every update returns on its first comparison, and nothing is
    /// allocated.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleTelemetry : MonoBehaviour
    {
        public const int SchemaVersion = 4;

        /// <summary>Command line switch that starts recording. Consumed by the batch runners.</summary>
        public const string SwitchName = "-localtanksTelemetry";

        /// <summary>Command line switch giving the output file, as <c>-localtanksTelemetryOut &lt;path&gt;</c>.</summary>
        public const string OutputSwitchName = "-localtanksTelemetryOut";

        /// <summary>
        /// How often per-tank state is written, in simulation seconds. One second is fine enough to see a
        /// stall begin and coarse enough that a long battle stays a small file. Events are never sampled:
        /// every fire, hit, penetration, kill and decision change is written as it happens.
        /// </summary>
        private const float SampleIntervalSeconds = 1f;

        private static BattleTelemetry instance;

        private readonly List<TankSample> samples = new List<TankSample>();
        private readonly List<FramePerformanceSample> frameSamples = new List<FramePerformanceSample>(4096);
        private readonly List<BattleEvent> events = new List<BattleEvent>();
        private readonly Dictionary<UnityEngine.EntityId, TankTelemetryTrack> tracks = new Dictionary<UnityEngine.EntityId, TankTelemetryTrack>();
        private readonly AiDecisionFingerprint decisionFingerprint = new AiDecisionFingerprint();
        private readonly AiDecisionFingerprint motorControlFingerprint = new AiDecisionFingerprint();
        private readonly AiDecisionFingerprint motorPhysicsFingerprint = new AiDecisionFingerprint();
        private readonly List<Collider2D> concealmentHits = new List<Collider2D>(32);
        private ContactFilter2D concealmentFilter = new ContactFilter2D().NoFilter();
        private BattleDirector director;
        private float startedAt;
        private float nextSampleAt;

        /// <summary>
        /// Simulated time reached by the last recording pass. The duration is taken from here rather than
        /// from the clock at the moment of stopping, because stopping happens while the scene is being torn
        /// down and the clock has already restarted, which produced a negative battle length.
        /// </summary>
        private float elapsedAtLastSample;
        private int startingHealth;
        private bool recording;
        private bool writeOnStop = true;
        private string outputPath;
        private ProfilerRecorder gcAllocatedRecorder;
        private ProfilerRecorder drawCallsRecorder;
        private ProfilerRecorder setPassCallsRecorder;
        private ProfilerRecorder trianglesRecorder;
        private ProfilerRecorder verticesRecorder;
        private readonly FrameTiming[] frameTimings = new FrameTiming[1];
        private int fixedStepsSinceLastFrame;
        private int previousVisionQueries;
        private int previousVisionEvaluations;

        /// <summary>The active recorder, or null when this battle is not being recorded.</summary>
        public static BattleTelemetry Active => instance;

        /// <summary>Whether the process was started with the telemetry switch.</summary>
        public static bool RequestedFromCommandLine
        {
            get
            {
                string[] arguments = Environment.GetCommandLineArgs();
                return arguments.Any(argument =>
                    string.Equals(argument, SwitchName, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>Output path given on the command line, or null.</summary>
        public static string CommandLineOutputPath
        {
            get
            {
                string[] arguments = Environment.GetCommandLineArgs();
                for (int index = 0; index < arguments.Length - 1; index++)
                {
                    if (string.Equals(arguments[index], OutputSwitchName, StringComparison.OrdinalIgnoreCase))
                    {
                        return arguments[index + 1];
                    }
                }

                return null;
            }
        }

        /// <summary>Whether state is being recorded right now.</summary>
        public bool IsRecording => recording;

        /// <summary>
        /// Where the next recording is written. A sweep sets this before each run so one process can
        /// produce one file per parameter value instead of overwriting a single result.
        /// </summary>
        public void SetOutputPath(string path)
        {
            outputPath = path;
        }

        /// <summary>
        /// Simulated seconds recorded so far, and the end state of the battle. A sweep reads these to
        /// compare runs without parsing the JSON it just wrote.
        /// </summary>
        public BattleOutcome Outcome()
        {
            BattleDirector battle = director;
            int healthA = 0;
            int healthB = 0;
            int aliveA = 0;
            int aliveB = 0;
            float closest = float.MaxValue;
            if (battle != null)
            {
                foreach (TeamMember member in battle.SpawnedMembers)
                {
                    if (member == null) continue;
                    int hp = member.Health != null ? member.Health.CurrentHitPoints : 0;
                    if (member.Team == TeamId.TeamA) { healthA += hp; if (member.IsAlive) aliveA++; }
                    else { healthB += hp; if (member.IsAlive) aliveB++; }
                    foreach (TeamMember other in battle.SpawnedMembers)
                    {
                        if (other == null || other.Team == member.Team) continue;
                        closest = Mathf.Min(closest,
                            Vector2.Distance(member.transform.position, other.transform.position));
                    }
                }
            }

            return new BattleOutcome
            {
                Seconds = elapsedAtLastSample,
                TeamAAlive = aliveA,
                TeamBAlive = aliveB,
                TeamAHealth = healthA,
                TeamBHealth = healthB,
                ClosestApproach = closest == float.MaxValue ? 0f : closest,
                Samples = samples.Count,
                Events = events.Count,
                StartingHealth = startingHealth
            };
        }

        public int SampleCount => samples.Count;
        public int EventCount => events.Count;
        public int FrameSampleCount => frameSamples.Count;
        public string DecisionFingerprint => decisionFingerprint.Value;
        public int FingerprintedDecisionCount => decisionFingerprint.Count;
        public string MotorControlFingerprint => motorControlFingerprint.Value;
        public string MotorPhysicsFingerprint => motorPhysicsFingerprint.Value;
        public int FingerprintedMotorStepCount => motorControlFingerprint.Count;
        public string DecisionTrace => string.Join("\n", events
            .Where(item => ContributesToDecisionFingerprint(item.kind))
            .Select(item => $"{item.kind}|{item.name}|{item.detail}"));

        /// <summary>
        /// Reduces wall-clock frame samples into stable percentiles. It is intentionally independent of
        /// simulation time: a 100 ms hitch remains a 100 ms hitch when a diagnostic run accelerates time.
        /// </summary>
        public BattlePerformanceSummary Performance()
        {
            if (frameSamples.Count == 0) return default;

            float[] frameTimes = frameSamples.Select(sample => sample.frameMs).OrderBy(value => value).ToArray();
            float[] cpuTimes = frameSamples.Where(sample => sample.cpuFrameMs > 0f)
                .Select(sample => sample.cpuFrameMs).OrderBy(value => value).ToArray();
            float[] gpuTimes = frameSamples.Where(sample => sample.gpuFrameMs > 0f)
                .Select(sample => sample.gpuFrameMs).OrderBy(value => value).ToArray();
            double totalFrameMs = frameTimes.Sum(value => (double)value);
            TankTrackMarkEmitter[] markEmitters = FindObjectsByType<TankTrackMarkEmitter>(
                FindObjectsInactive.Include);
            return new BattlePerformanceSummary
            {
                Frames = frameSamples.Count,
                AverageFrameMs = Round((float)(totalFrameMs / frameTimes.Length)),
                AverageFps = totalFrameMs > 0d
                    ? Round((float)(frameTimes.Length * 1000d / totalFrameMs))
                    : 0f,
                MedianFrameMs = Round(Percentile(frameTimes, 0.50f)),
                P95FrameMs = Round(Percentile(frameTimes, 0.95f)),
                P99FrameMs = Round(Percentile(frameTimes, 0.99f)),
                MaximumFrameMs = Round(frameTimes[frameTimes.Length - 1]),
                P95CpuFrameMs = Round(Percentile(cpuTimes, 0.95f)),
                P95GpuFrameMs = Round(Percentile(gpuTimes, 0.95f)),
                FramesOver33Ms = frameSamples.Count(sample => sample.frameMs > 33.34f),
                FramesOver50Ms = frameSamples.Count(sample => sample.frameMs > 50f),
                FramesOver100Ms = frameSamples.Count(sample => sample.frameMs > 100f),
                TotalGcBytes = frameSamples.Sum(sample => sample.gcBytes),
                PeakGcBytes = frameSamples.Max(sample => sample.gcBytes),
                GcAllocatingFrames = frameSamples.Count(sample => sample.gcBytes > 0L),
                PeakDrawCalls = frameSamples.Max(sample => sample.drawCalls),
                PeakSetPassCalls = frameSamples.Max(sample => sample.setPassCalls),
                PeakTriangles = frameSamples.Max(sample => sample.triangles),
                PeakVertices = frameSamples.Max(sample => sample.vertices),
                PeakFixedStepsPerFrame = frameSamples.Max(sample => sample.fixedSteps),
                PeakVisionQueriesPerFrame = frameSamples.Max(sample => sample.visionQueries),
                PeakVisionEvaluationsPerFrame = frameSamples.Max(sample => sample.visionEvaluations),
                TotalVisionEvaluations = frameSamples.Sum(sample => sample.visionEvaluations),
                TrackMarkEmitters = markEmitters.Length,
                TrackMarkCapacity = markEmitters.Sum(emitter => emitter.Capacity),
                TrackMarkRenderers = markEmitters.Sum(emitter => emitter.RendererCount),
                LiveTrackMarks = markEmitters.Sum(emitter => emitter.LiveCount),
                EmittedTrackMarks = markEmitters.Sum(emitter => emitter.EmittedCount),
                SkippedTrackMarks = markEmitters.Sum(emitter => emitter.SkippedCount)
            };
        }

        /// <summary>
        /// Attaches a recorder to a director when the command line asked for one. Returns null otherwise,
        /// so the caller can simply ignore the result.
        /// </summary>
        public static BattleTelemetry AttachWhenRequested(BattleDirector battleDirector)
        {
            if (!RequestedFromCommandLine || battleDirector == null) return null;
            return Attach(battleDirector, CommandLineOutputPath);
        }

        /// <summary>Attaches a recorder to a director and begins recording immediately.</summary>
        public static BattleTelemetry Attach(
            BattleDirector battleDirector,
            string path = null,
            bool writeOutput = true)
        {
            if (battleDirector == null) return null;
            BattleTelemetry telemetry = battleDirector.gameObject.GetComponent<BattleTelemetry>();
            if (telemetry == null)
            {
                telemetry = battleDirector.gameObject.AddComponent<BattleTelemetry>();
            }

            telemetry.director = battleDirector;
            telemetry.outputPath = path;
            telemetry.writeOnStop = writeOutput;
            telemetry.Begin();
            return telemetry;
        }

        /// <summary>Starts recording. A second call while recording only changes nothing.</summary>
        public void Begin()
        {
            if (recording) return;
            if (director == null) director = GetComponent<BattleDirector>();
            instance = this;
            samples.Clear();
            frameSamples.Clear();
            events.Clear();
            tracks.Clear();
            decisionFingerprint.Reset();
            motorControlFingerprint.Reset();
            motorPhysicsFingerprint.Reset();
            startedAt = Time.time;
            nextSampleAt = 0f;
            fixedStepsSinceLastFrame = 0;
            previousVisionQueries = director != null && director.VisionSystem != null
                ? director.VisionSystem.WorldVisibilityQueryCount
                : 0;
            previousVisionEvaluations = director != null && director.VisionSystem != null
                ? director.VisionSystem.WorldVisibilityEvaluationCount
                : 0;
            StartPerformanceRecorders();
            BattlePerformanceProbe.Start();
            recording = true;
            CollectTracks();
            startingHealth = 0;
            foreach (TeamMember member in director.SpawnedMembers)
            {
                if (member != null && member.Health != null) startingHealth += member.Health.MaximumHitPoints;
            }
        }

        /// <summary>
        /// Starts a fresh measurement window without recreating the profiler recorders. Recorder startup
        /// is expensive in the Editor and would otherwise appear as a multi-second gameplay hitch in the
        /// first sample of a performance run.
        /// </summary>
        public void ResetMeasurementWindow()
        {
            if (!recording || director == null) return;

            samples.Clear();
            frameSamples.Clear();
            events.Clear();
            tracks.Clear();
            decisionFingerprint.Reset();
            motorControlFingerprint.Reset();
            motorPhysicsFingerprint.Reset();
            CollectTracks();
            startingHealth = director.SpawnedMembers
                .Where(member => member != null && member.Health != null)
                .Sum(member => member.Health.CurrentHitPoints);
            startedAt = Time.time;
            elapsedAtLastSample = 0f;
            nextSampleAt = SampleIntervalSeconds;
            fixedStepsSinceLastFrame = 0;
            previousVisionQueries = director.VisionSystem != null
                ? director.VisionSystem.WorldVisibilityQueryCount
                : 0;
            previousVisionEvaluations = director.VisionSystem != null
                ? director.VisionSystem.WorldVisibilityEvaluationCount
                : 0;
            BattlePerformanceProbe.Start();
            SampleTanks(0f);
        }

        /// <summary>Stops recording and returns the recording as JSON text.</summary>
        public string Stop()
        {
            recording = false;
            BattlePerformanceProbe.Stop();
            if (instance == this) instance = null;
            string json = ToJson();
            DisposePerformanceRecorders();
            if (writeOnStop) Write(json);
            return json;
        }

        private void Awake()
        {
            if (director == null) director = GetComponent<BattleDirector>();
        }

        private void LateUpdate()
        {
            // The whole cost of a battle without telemetry is this one comparison.
            if (!recording || director == null) return;

            float elapsed = Time.time - startedAt;
            elapsedAtLastSample = elapsed;
            RecordFramePerformance(elapsed);
            if (elapsed >= nextSampleAt)
            {
                nextSampleAt = elapsed + SampleIntervalSeconds;
                SampleTanks(elapsed);
            }
        }

        private void FixedUpdate()
        {
            if (recording) fixedStepsSinceLastFrame++;
        }

        private void StartPerformanceRecorders()
        {
            DisposePerformanceRecorders();
            gcAllocatedRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC Allocated In Frame",
                1);
            drawCallsRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Render,
                "Draw Calls Count",
                1);
            setPassCallsRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Render,
                "SetPass Calls Count",
                1);
            trianglesRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Render,
                "Triangles Count",
                1);
            verticesRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Render,
                "Vertices Count",
                1);
            FrameTimingManager.CaptureFrameTimings();
        }

        private void RecordFramePerformance(float elapsed)
        {
            float frameMs = Mathf.Max(0f, Time.unscaledDeltaTime * 1000f);
            if (frameMs <= 0f) return;

            FrameTimingManager.CaptureFrameTimings();
            uint timingCount = FrameTimingManager.GetLatestTimings(1, frameTimings);
            float cpuFrameMs = timingCount > 0 ? (float)frameTimings[0].cpuFrameTime : 0f;
            float gpuFrameMs = timingCount > 0 ? (float)frameTimings[0].gpuFrameTime : 0f;
            TeamVisionSystem vision = director != null ? director.VisionSystem : null;
            int currentVisionQueries = vision != null ? vision.WorldVisibilityQueryCount : previousVisionQueries;
            int currentVisionEvaluations = vision != null
                ? vision.WorldVisibilityEvaluationCount
                : previousVisionEvaluations;
            BattlePerformanceProbe.Consume(BattlePerformanceSubsystem.CombatAi, out float aiMs, out long aiGcBytes);
            BattlePerformanceProbe.Consume(BattlePerformanceSubsystem.Navigation, out float navigationMs, out long navigationGcBytes);
            BattlePerformanceProbe.Consume(BattlePerformanceSubsystem.Motor, out float motorMs, out long motorGcBytes);
            BattlePerformanceProbe.Consume(BattlePerformanceSubsystem.Vision, out float visionMs, out long visionGcBytes);
            BattlePerformanceProbe.Consume(BattlePerformanceSubsystem.TankVfx, out float tankVfxMs, out long tankVfxGcBytes);
            BattlePerformanceProbe.Consume(BattlePerformanceSubsystem.TrackAnimation, out float tracksMs, out long tracksGcBytes);
            BattlePerformanceProbe.Consume(BattlePerformanceSubsystem.Audio, out float audioMs, out long audioGcBytes);
            BattlePerformanceProbe.Consume(BattlePerformanceSubsystem.Hud, out float hudMs, out long hudGcBytes);
            BattlePerformanceProbe.Consume(BattlePerformanceSubsystem.CombatVfx, out float combatVfxMs, out long combatVfxGcBytes);
            BattlePerformanceProbe.Consume(BattlePerformanceSubsystem.Capture, out float captureMs, out long captureGcBytes);
            frameSamples.Add(new FramePerformanceSample
            {
                t = Round(elapsed),
                frameMs = Round(frameMs),
                cpuFrameMs = Round(cpuFrameMs),
                gpuFrameMs = Round(gpuFrameMs),
                gcBytes = RecorderValue(gcAllocatedRecorder),
                drawCalls = RecorderValue(drawCallsRecorder),
                setPassCalls = RecorderValue(setPassCallsRecorder),
                triangles = RecorderValue(trianglesRecorder),
                vertices = RecorderValue(verticesRecorder),
                fixedSteps = fixedStepsSinceLastFrame,
                visionQueries = Mathf.Max(0, currentVisionQueries - previousVisionQueries),
                visionEvaluations = Mathf.Max(0, currentVisionEvaluations - previousVisionEvaluations),
                aiMs = Round(aiMs), aiGcBytes = aiGcBytes,
                navigationMs = Round(navigationMs), navigationGcBytes = navigationGcBytes,
                motorMs = Round(motorMs), motorGcBytes = motorGcBytes,
                visionMs = Round(visionMs), visionGcBytes = visionGcBytes,
                tankVfxMs = Round(tankVfxMs), tankVfxGcBytes = tankVfxGcBytes,
                tracksMs = Round(tracksMs), tracksGcBytes = tracksGcBytes,
                audioMs = Round(audioMs), audioGcBytes = audioGcBytes,
                hudMs = Round(hudMs), hudGcBytes = hudGcBytes,
                combatVfxMs = Round(combatVfxMs), combatVfxGcBytes = combatVfxGcBytes,
                captureMs = Round(captureMs), captureGcBytes = captureGcBytes
            });
            fixedStepsSinceLastFrame = 0;
            previousVisionQueries = currentVisionQueries;
            previousVisionEvaluations = currentVisionEvaluations;
        }

        private void DisposePerformanceRecorders()
        {
            Dispose(ref gcAllocatedRecorder);
            Dispose(ref drawCallsRecorder);
            Dispose(ref setPassCallsRecorder);
            Dispose(ref trianglesRecorder);
            Dispose(ref verticesRecorder);
        }

        private static long RecorderValue(ProfilerRecorder recorder)
        {
            return recorder.Valid ? Math.Max(0L, recorder.LastValue) : 0L;
        }

        private static void Dispose(ref ProfilerRecorder recorder)
        {
            if (recorder.Valid) recorder.Dispose();
            recorder = default;
        }

        private static float Percentile(float[] sortedValues, float percentile)
        {
            if (sortedValues == null || sortedValues.Length == 0) return 0f;
            int index = Mathf.Clamp(
                Mathf.CeilToInt(sortedValues.Length * Mathf.Clamp01(percentile)) - 1,
                0,
                sortedValues.Length - 1);
            return sortedValues[index];
        }

        private void OnDestroy()
        {
            if (recording) Stop();
            else DisposePerformanceRecorders();
        }

        /// <summary>Registers every spawned member so events can be attributed by index, not by name.</summary>
        private void CollectTracks()
        {
            foreach (TeamMember member in director.SpawnedMembers)
            {
                if (member == null) continue;
                Register(member);
            }
        }

        private TankTelemetryTrack Register(TeamMember member)
        {
            // Keyed by entity id rather than by name: names are not unique once clones appear, and an
            // event must be attributable even on the frame a member is destroyed.
            UnityEngine.EntityId key = member.GetEntityId();
            if (tracks.TryGetValue(key, out TankTelemetryTrack existing)) return existing;

            TankTelemetryTrack track = new TankTelemetryTrack
            {
                id = tracks.Count,
                name = member.name,
                stableOrderKey = TeamMember.StableOrderKey(member),
                team = member.Team.ToString(),
                definition = member.Definition != null ? member.Definition.id : string.Empty,
                isPlayer = member.IsPlayerControlled,
                previousHealth = member.Health != null ? member.Health.CurrentHitPoints : 0,
                previousState = string.Empty,
                previousReason = string.Empty
            };
            tracks[key] = track;
            return track;
        }

        private void SampleTanks(float elapsed)
        {
            foreach (TeamMember member in director.SpawnedMembers)
            {
                if (member == null) continue;
                TankTelemetryTrack track = Register(member);
                CombatTankAI ai = member.GetComponent<CombatTankAI>();
                NavigationAgent agent = member.GetComponent<NavigationAgent>();
                TankMotor motor = member.GetComponent<TankMotor>();
                TankHealth health = member.Health;
                Vector2 position = member.transform.position;

                samples.Add(new TankSample
                {
                    t = Round(elapsed),
                    id = track.id,
                    name = track.name,
                    x = Round(position.x),
                    y = Round(position.y),
                    speed = motor != null ? Round(motor.CurrentSpeed) : 0f,
                    maxSpeed = member.Definition != null ? Round(member.Definition.maxForwardSpeed) : 0f,
                    terrain = motor != null ? Round(motor.TerrainSpeedMultiplier) : 0f,
                    hp = health != null ? health.CurrentHitPoints : 0,
                    maxHp = health != null ? health.MaximumHitPoints : 0,
                    alive = member.IsAlive,
                    state = ai != null ? ai.State.ToString() : string.Empty,
                    role = ai != null ? ai.Role.ToString() : string.Empty,
                    lane = ai != null ? ai.Lane.ToString() : string.Empty,
                    target = ai != null && ai.CurrentTarget != null ? ai.CurrentTarget.name : string.Empty,
                    targetVisible = ai != null && ai.CurrentTarget != null && ai.CurrentTarget.IsAlive,
                    fireBlock = ai != null ? ai.CurrentFireBlockReason.ToString() : string.Empty,
                    routeIndex = ai != null ? ai.RouteIndex : 0,
                    routeLength = ai != null ? ai.RoutePoints.Length : 0,
                    hasPath = agent != null && agent.HasPath,
                    pathIndex = agent != null ? agent.PathIndex : 0,
                    pathLength = agent != null && agent.PathPoints != null ? agent.PathPoints.Length : 0,
                    destReached = agent != null && agent.DestinationReached,
                    paused = agent != null && agent.MovementPaused,
                    stalled = agent != null && agent.IsRecoveringFromStall,
                    stalls = agent != null ? agent.StallRecoveryCount : 0,
                    routeContinuations = ai != null ? ai.RouteContinuationCount : 0,
                    strategicOverrides = ai != null ? ai.StrategicOverrideCount : 0,
                    tacticalPurpose = ai != null ? ai.TacticalMovementPurpose.ToString() : string.Empty,
                    tacticalReason = ai != null ? ai.TacticalDecisionReason : string.Empty,
                    positionResult = ai != null ? ai.LastTacticalPositionResult : string.Empty,
                    strategicIntent = ai != null && ai.StrategicDecision != null
                        ? ai.StrategicDecision.intent.ToString()
                        : string.Empty,
                    threats = ai != null ? ai.ActiveThreatCount : 0,
                    crossfire = ai != null && ai.IsUnderCrossfire,
                    concealment = ConcealmentAt(position)
                });

                RecordChanges(track, ai, health, elapsed, position);
            }
        }

        /// <summary>
        /// Writes the transitions, which are what actually explain a battle: a state change, a decision
        /// reason change, damage taken or a kill. Sampling would miss most of them.
        /// </summary>
        private void RecordChanges(
            TankTelemetryTrack track,
            CombatTankAI ai,
            TankHealth health,
            float elapsed,
            Vector2 position)
        {
            int currentHealth = health != null ? health.CurrentHitPoints : 0;
            if (currentHealth != track.previousHealth)
            {
                int delta = currentHealth - track.previousHealth;
                AddEvent(elapsed, delta < 0 ? "damage" : "heal", track.id, track.name, delta.ToString(CultureInfo.InvariantCulture));
                track.previousHealth = currentHealth;
                if (currentHealth <= 0)
                {
                    AddEvent(elapsed, "kill", track.id, track.name, $"at {Round(position.x)},{Round(position.y)}");
                }
            }

            if (ai == null) return;

            string state = ai.State.ToString();
            if (!string.Equals(state, track.previousState, StringComparison.Ordinal))
            {
                AddEvent(elapsed, "state", track.id, track.name, state);
                track.previousState = state;
            }

            string reason = ai.TacticalDecisionReason ?? string.Empty;
            if (!string.Equals(reason, track.previousReason, StringComparison.Ordinal))
            {
                AddEvent(elapsed, "decision", track.id, track.name, reason);
                track.previousReason = reason;
            }

            string targetName = ai.CurrentTarget != null ? ai.CurrentTarget.name : string.Empty;
            if (!string.Equals(targetName, track.previousTarget, StringComparison.Ordinal))
            {
                AddEvent(elapsed, targetName.Length > 0 ? "target_acquired" : "target_lost",
                    track.id, track.name, targetName);
                track.previousTarget = targetName;
            }
        }

        /// <summary>Records a shot. Called by the weapon path so no sampling can miss a shot.</summary>
        public void RecordFire(TeamMember shooter, string shellId, ShotRandomization randomization)
        {
            if (!recording || shooter == null) return;
            TankTelemetryTrack track = Register(shooter);
            string detail = $"shell={shellId} sequence={randomization.Sequence} " +
                            $"penetrationMultiplier={Number(randomization.PenetrationMultiplier)} " +
                            $"damageMultiplier={Number(randomization.DamageMultiplier)}";
            AddEvent(Time.time - startedAt, "fire", track.id, track.name, detail);
        }

        /// <summary>Records the impact without rolling again; both multipliers came from the original fire.</summary>
        public void RecordShot(
            TeamMember shooter,
            TeamMember victim,
            string outcome,
            int damage,
            string shellId,
            ShotRandomization randomization = default,
            float penetrationChance = 0f)
        {
            if (!recording || shooter == null) return;
            TankTelemetryTrack track = Register(shooter);
            string detail = $"{outcome} dmg={damage} shell={shellId} " +
                            $"sequence={randomization.Sequence} " +
                            $"penetrationMultiplier={Number(randomization.PenetrationMultiplier)} " +
                            $"damageMultiplier={Number(randomization.DamageMultiplier)} " +
                            $"penetrationChance={Number(penetrationChance)} " +
                            $"at {Round(victim != null ? victim.transform.position.x : 0f)}," +
                            $"{Round(victim != null ? victim.transform.position.y : 0f)}";
            AddEvent(Time.time - startedAt, "shot", track.id, track.name, detail);
        }

        /// <summary>
        /// Separates the command stream from the Box2D state it acts on. If two seeded runs disagree,
        /// these hashes show whether navigation produced a different input first or physics had already
        /// diverged while receiving the same ordered commands.
        /// </summary>
        public void RecordMotorStep(
            TeamMember member,
            float drive,
            float turn,
            float speedTerrain,
            float accelerationTerrain,
            float resistanceTerrain,
            Rigidbody2D body,
            float currentSpeed,
            float currentTurnRate)
        {
            if (!recording || member == null || body == null) return;
            TankTelemetryTrack track = Register(member);
            track.motorSteps++;
            string actor = track.stableOrderKey;
            motorControlFingerprint.AppendMotorControl(
                actor,
                track.motorSteps,
                drive,
                turn,
                speedTerrain,
                accelerationTerrain,
                resistanceTerrain);
            motorPhysicsFingerprint.AppendMotorPhysics(
                actor,
                track.motorSteps,
                body.position.x,
                body.position.y,
                body.rotation,
                body.linearVelocity.x,
                body.linearVelocity.y,
                body.angularVelocity,
                currentSpeed,
                currentTurnRate);
        }

        private void AddEvent(float elapsed, string kind, int id, string name, string detail)
        {
            events.Add(new BattleEvent
            {
                t = Round(elapsed),
                kind = kind,
                id = id,
                name = name ?? string.Empty,
                detail = detail ?? string.Empty
            });
            if (ContributesToDecisionFingerprint(kind))
            {
                decisionFingerprint.Append(kind, name, detail);
            }
        }

        private static bool ContributesToDecisionFingerprint(string kind)
        {
            return kind == "state" || kind == "decision" || kind == "target_acquired" ||
                   kind == "target_lost" || kind == "fire" || kind == "shot";
        }

        /// <summary>
        /// Concealment bonus the tank is standing in, as recorded cover. A non-allocating local physics
        /// query avoids walking all three thousand plants on Mines for every tank and every sample.
        /// </summary>
        private string ConcealmentAt(Vector2 position)
        {
            concealmentHits.Clear();
            Physics2D.OverlapCircle(position, 6f, concealmentFilter, concealmentHits);
            float largestBonus = 0f;
            foreach (Collider2D collider in concealmentHits)
            {
                ConcealmentZone zone = collider != null
                    ? collider.GetComponentInParent<ConcealmentZone>()
                    : null;
                if (zone != null)
                {
                    largestBonus = Mathf.Max(largestBonus, zone.ConcealmentBonus);
                }
            }

            return largestBonus > 0f
                ? largestBonus.ToString("0.00", CultureInfo.InvariantCulture)
                : "0";
        }

        private static float Round(float value) => Mathf.Round(value * 100f) / 100f;

        /// <summary>The recording plus a summary, as JSON.</summary>
        public string ToJson()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("{\"schemaVersion\":").Append(SchemaVersion).Append(',');
            builder.Append("\"summary\":").Append(SummaryJson()).Append(',');
            builder.Append("\"performance\":").Append(PerformanceJson()).Append(',');
            builder.Append("\"tanks\":").Append(TracksJson()).Append(',');
            builder.Append("\"samples\":").Append(SamplesJson()).Append(',');
            builder.Append("\"frames\":").Append(FrameSamplesJson()).Append(',');
            builder.Append("\"events\":").Append(EventsJson()).Append('}');
            return builder.ToString();
        }

        private string PerformanceJson()
        {
            BattlePerformanceSummary value = Performance();
            return new StringBuilder()
                .Append('{')
                .Append($"\"frames\":{value.Frames},")
                .Append($"\"averageFps\":{Number(value.AverageFps)},")
                .Append($"\"averageFrameMs\":{Number(value.AverageFrameMs)},")
                .Append($"\"medianFrameMs\":{Number(value.MedianFrameMs)},")
                .Append($"\"p95FrameMs\":{Number(value.P95FrameMs)},")
                .Append($"\"p99FrameMs\":{Number(value.P99FrameMs)},")
                .Append($"\"maximumFrameMs\":{Number(value.MaximumFrameMs)},")
                .Append($"\"p95CpuFrameMs\":{Number(value.P95CpuFrameMs)},")
                .Append($"\"p95GpuFrameMs\":{Number(value.P95GpuFrameMs)},")
                .Append($"\"framesOver33Ms\":{value.FramesOver33Ms},")
                .Append($"\"framesOver50Ms\":{value.FramesOver50Ms},")
                .Append($"\"framesOver100Ms\":{value.FramesOver100Ms},")
                .Append($"\"totalGcBytes\":{value.TotalGcBytes},")
                .Append($"\"peakGcBytes\":{value.PeakGcBytes},")
                .Append($"\"gcAllocatingFrames\":{value.GcAllocatingFrames},")
                .Append($"\"peakDrawCalls\":{value.PeakDrawCalls},")
                .Append($"\"peakSetPassCalls\":{value.PeakSetPassCalls},")
                .Append($"\"peakTriangles\":{value.PeakTriangles},")
                .Append($"\"peakVertices\":{value.PeakVertices},")
                .Append($"\"peakFixedStepsPerFrame\":{value.PeakFixedStepsPerFrame},")
                .Append($"\"peakVisionQueriesPerFrame\":{value.PeakVisionQueriesPerFrame},")
                .Append($"\"peakVisionEvaluationsPerFrame\":{value.PeakVisionEvaluationsPerFrame},")
                .Append($"\"totalVisionEvaluations\":{value.TotalVisionEvaluations},")
                .Append($"\"trackMarkEmitters\":{value.TrackMarkEmitters},")
                .Append($"\"trackMarkCapacity\":{value.TrackMarkCapacity},")
                .Append($"\"trackMarkRenderers\":{value.TrackMarkRenderers},")
                .Append($"\"liveTrackMarks\":{value.LiveTrackMarks},")
                .Append($"\"emittedTrackMarks\":{value.EmittedTrackMarks},")
                .Append($"\"skippedTrackMarks\":{value.SkippedTrackMarks}")
                .Append('}')
                .ToString();
        }

        private string SummaryJson()
        {
            BattleDirector battle = director;
            float elapsed = elapsedAtLastSample;
            int healthA = 0;
            int healthB = 0;
            int aliveA = 0;
            int aliveB = 0;
            float closest = float.MaxValue;
            foreach (TeamMember member in battle != null ? battle.SpawnedMembers : new List<TeamMember>())
            {
                if (member == null) continue;
                int hp = member.Health != null ? member.Health.CurrentHitPoints : 0;
                if (member.Team == TeamId.TeamA) { healthA += hp; if (member.IsAlive) aliveA++; }
                else { healthB += hp; if (member.IsAlive) aliveB++; }
                foreach (TeamMember other in battle.SpawnedMembers)
                {
                    if (other == null || other.Team == member.Team) continue;
                    closest = Mathf.Min(closest,
                        Vector2.Distance(member.transform.position, other.transform.position));
                }
            }

            return new StringBuilder()
                .Append('{')
                .Append($"\"scene\":\"{Escape(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)}\",")
                .Append($"\"randomSeed\":{(battle != null ? battle.RandomSeed : 0)},")
                .Append($"\"seconds\":{Round(elapsed).ToString(CultureInfo.InvariantCulture)},")
                .Append($"\"samples\":{samples.Count},")
                .Append($"\"events\":{events.Count},")
                .Append($"\"decisionFingerprint\":\"{decisionFingerprint.Value}\",")
                .Append($"\"fingerprintedDecisions\":{decisionFingerprint.Count},")
                .Append($"\"motorControlFingerprint\":\"{motorControlFingerprint.Value}\",")
                .Append($"\"motorPhysicsFingerprint\":\"{motorPhysicsFingerprint.Value}\",")
                .Append($"\"fingerprintedMotorSteps\":{motorControlFingerprint.Count},")
                .Append($"\"teamAAlive\":{aliveA},\"teamBAlive\":{aliveB},")
                .Append($"\"teamAHealth\":{healthA},\"teamBHealth\":{healthB},")
                .Append($"\"closestApproach\":{(closest == float.MaxValue ? 0f : Round(closest)).ToString(CultureInfo.InvariantCulture)}")
                .Append('}')
                .ToString();
        }

        private string TracksJson()
        {
            return "[" + string.Join(",", tracks.Values.Select(track =>
                $"{{\"id\":{track.id},\"name\":\"{Escape(track.name)}\",\"team\":\"{track.team}\"," +
                $"\"definition\":\"{Escape(track.definition)}\",\"isPlayer\":{(track.isPlayer ? "true" : "false")}}}")) + "]";
        }

        private string SamplesJson()
        {
            return "[" + string.Join(",", samples.Select(sample =>
                $"{{\"t\":{Number(sample.t)},\"id\":{sample.id}," +
                $"\"x\":{Number(sample.x)},\"y\":{Number(sample.y)}," +
                $"\"speed\":{Number(sample.speed)},\"maxSpeed\":{Number(sample.maxSpeed)}," +
                $"\"terrain\":{Number(sample.terrain)}," +
                $"\"hp\":{sample.hp},\"maxHp\":{sample.maxHp},\"alive\":{(sample.alive ? "true" : "false")}," +
                $"\"state\":\"{Escape(sample.state)}\",\"role\":\"{Escape(sample.role)}\",\"lane\":\"{Escape(sample.lane)}\"," +
                $"\"target\":\"{Escape(sample.target)}\",\"targetVisible\":{(sample.targetVisible ? "true" : "false")}," +
                $"\"fireBlock\":\"{Escape(sample.fireBlock)}\"," +
                $"\"routeIndex\":{sample.routeIndex},\"routeLength\":{sample.routeLength}," +
                $"\"hasPath\":{(sample.hasPath ? "true" : "false")},\"pathIndex\":{sample.pathIndex},\"pathLength\":{sample.pathLength}," +
                $"\"destReached\":{(sample.destReached ? "true" : "false")},\"paused\":{(sample.paused ? "true" : "false")}," +
                $"\"stalled\":{(sample.stalled ? "true" : "false")},\"stalls\":{sample.stalls}," +
                $"\"routeContinuations\":{sample.routeContinuations},\"strategicOverrides\":{sample.strategicOverrides}," +
                $"\"tacticalPurpose\":\"{Escape(sample.tacticalPurpose)}\"," +
                $"\"tacticalReason\":\"{Escape(sample.tacticalReason)}\"," +
                $"\"positionResult\":\"{Escape(sample.positionResult)}\"," +
                $"\"strategicIntent\":\"{Escape(sample.strategicIntent)}\"," +
                $"\"threats\":{sample.threats},\"crossfire\":{(sample.crossfire ? "true" : "false")}," +
                $"\"concealment\":{sample.concealment}}}")) + "]";
        }

        private string FrameSamplesJson()
        {
            return "[" + string.Join(",", frameSamples.Select(sample =>
                $"{{\"t\":{Number(sample.t)},\"frameMs\":{Number(sample.frameMs)}," +
                $"\"fps\":{Number(sample.frameMs > 0f ? 1000f / sample.frameMs : 0f)}," +
                $"\"cpuFrameMs\":{Number(sample.cpuFrameMs)},\"gpuFrameMs\":{Number(sample.gpuFrameMs)}," +
                $"\"gcBytes\":{sample.gcBytes},\"drawCalls\":{sample.drawCalls}," +
                $"\"setPassCalls\":{sample.setPassCalls},\"triangles\":{sample.triangles}," +
                $"\"vertices\":{sample.vertices},\"fixedSteps\":{sample.fixedSteps}," +
                $"\"visionQueries\":{sample.visionQueries}," +
                $"\"visionEvaluations\":{sample.visionEvaluations}," +
                $"\"aiMs\":{Number(sample.aiMs)},\"aiGcBytes\":{sample.aiGcBytes}," +
                $"\"navigationMs\":{Number(sample.navigationMs)},\"navigationGcBytes\":{sample.navigationGcBytes}," +
                $"\"motorMs\":{Number(sample.motorMs)},\"motorGcBytes\":{sample.motorGcBytes}," +
                $"\"visionMs\":{Number(sample.visionMs)},\"visionGcBytes\":{sample.visionGcBytes}," +
                $"\"tankVfxMs\":{Number(sample.tankVfxMs)},\"tankVfxGcBytes\":{sample.tankVfxGcBytes}," +
                $"\"tracksMs\":{Number(sample.tracksMs)},\"tracksGcBytes\":{sample.tracksGcBytes}," +
                $"\"audioMs\":{Number(sample.audioMs)},\"audioGcBytes\":{sample.audioGcBytes}," +
                $"\"hudMs\":{Number(sample.hudMs)},\"hudGcBytes\":{sample.hudGcBytes}," +
                $"\"combatVfxMs\":{Number(sample.combatVfxMs)},\"combatVfxGcBytes\":{sample.combatVfxGcBytes}," +
                $"\"captureMs\":{Number(sample.captureMs)},\"captureGcBytes\":{sample.captureGcBytes}}}")) + "]";
        }

        private string EventsJson()
        {
            return "[" + string.Join(",", events.Select(item =>
                $"{{\"t\":{Number(item.t)},\"kind\":\"{Escape(item.kind)}\",\"id\":{item.id}," +
                $"\"name\":\"{Escape(item.name)}\",\"detail\":\"{Escape(item.detail)}\"}}")) + "]";
        }

        private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
        }

        /// <summary>Writes the recording to the configured path, if there is one.</summary>
        private void Write(string json)
        {
            string path = outputPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                path = Path.Combine("TestResults", $"telemetry-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            }

            try
            {
                string directory = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                // Written without a byte order mark: a BOM makes the file invalid for jq and for strict
                // JSON readers, which is exactly the tooling a telemetry file is meant to feed.
                File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                Debug.Log($"Battle telemetry written to {path} " +
                          $"({samples.Count} samples, {events.Count} events).");
            }
            catch (Exception error)
            {
                Debug.LogWarning($"Battle telemetry could not be written to {path}: {error.Message}");
            }
        }

        private sealed class TankTelemetryTrack
        {
            public int id;
            public string name;
            public string stableOrderKey;
            public string team;
            public string definition;
            public bool isPlayer;
            public int previousHealth;
            public string previousState;
            public string previousReason;
            public string previousTarget = string.Empty;
            public int motorSteps;
        }

        private struct TankSample
        {
            public float t;
            public int id;
            public string name;
            public float x;
            public float y;
            public float speed;
            public float maxSpeed;
            public float terrain;
            public int hp;
            public int maxHp;
            public bool alive;
            public string state;
            public string role;
            public string lane;
            public string target;
            public bool targetVisible;
            public string fireBlock;
            public int routeIndex;
            public int routeLength;
            public bool hasPath;
            public int pathIndex;
            public int pathLength;
            public bool destReached;
            public bool paused;
            public bool stalled;
            public int stalls;
            public int routeContinuations;
            public int strategicOverrides;
            public string tacticalPurpose;
            public string tacticalReason;
            public string positionResult;
            public string strategicIntent;
            public int threats;
            public bool crossfire;
            public string concealment;
        }

        private struct BattleEvent
        {
            public float t;
            public string kind;
            public int id;
            public string name;
            public string detail;
        }

        private struct FramePerformanceSample
        {
            public float t;
            public float frameMs;
            public float cpuFrameMs;
            public float gpuFrameMs;
            public long gcBytes;
            public long drawCalls;
            public long setPassCalls;
            public long triangles;
            public long vertices;
            public int fixedSteps;
            public int visionQueries;
            public int visionEvaluations;
            public float aiMs;
            public long aiGcBytes;
            public float navigationMs;
            public long navigationGcBytes;
            public float motorMs;
            public long motorGcBytes;
            public float visionMs;
            public long visionGcBytes;
            public float tankVfxMs;
            public long tankVfxGcBytes;
            public float tracksMs;
            public long tracksGcBytes;
            public float audioMs;
            public long audioGcBytes;
            public float hudMs;
            public long hudGcBytes;
            public float combatVfxMs;
            public long combatVfxGcBytes;
            public float captureMs;
            public long captureGcBytes;
        }
    }

    /// <summary>
    /// Runs the graphical performance gate in the normal player loop. It exists only when the
    /// dedicated command-line switch is present, keeping Unity Test Framework allocations out of the trace.
    /// </summary>
    public sealed class BattlePerformanceCommandLineRunner : MonoBehaviour
    {
        private const string SwitchName = "-localtanksPerformance";
        private const float WarmupSeconds = 2f;
        private const float ProfilerWarmupSeconds = 1f;
        private const float MeasurementSeconds = 30f;
        private BattleTelemetry telemetry;
        private BattleDirector director;
        private int previousTargetFrameRate;
        private int previousVSyncCount;
        private float previousTimeScale;
        private int phase;
        private int lastSpawnCount = -1;
        private long phaseEndsAt;
        private Camera battleCamera;
        private RenderTexture renderTarget;

        public static bool IsRequested => Environment.GetCommandLineArgs().Contains(SwitchName);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (!IsRequested) return;
            Create();
        }

        public static void Create()
        {
            if (FindAnyObjectByType<BattlePerformanceCommandLineRunner>() != null) return;
            GameObject host = new GameObject("BattlePerformanceCommandLineRunner");
            DontDestroyOnLoad(host);
            host.AddComponent<BattlePerformanceCommandLineRunner>();
            Debug.Log("River Crossing performance runner created.");
        }

        private void Start()
        {
            Debug.Log("River Crossing performance runner started.");
            previousTargetFrameRate = Application.targetFrameRate;
            previousVSyncCount = QualitySettings.vSyncCount;
            previousTimeScale = Time.timeScale;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Time.timeScale = 1f;

            // Always reload the arena. An ExecuteMethod starts from whichever scene happened to be open
            // in the Editor; reusing an already-running battle would measure stale wrecks and registries.
            SceneManager.LoadScene("Battle_RiverCrossing", LoadSceneMode.Single);
            phase = 0;
            phaseEndsAt = Deadline(30f);
        }

        private void Update()
        {
            RenderBattleFrame();
            if (phase == 0)
            {
                director = FindAnyObjectByType<BattleDirector>();
                int spawnCount = director != null ? director.SpawnedMembers.Count : -1;
                if (spawnCount != lastSpawnCount)
                {
                    lastSpawnCount = spawnCount;
                    Debug.Log($"River Crossing performance runner sees {spawnCount} spawned member(s).");
                }
                if (director != null && spawnCount == BattleScenario.TeamSize * 2)
                {
                    Debug.Log($"River Crossing performance scene ready: {SceneManager.GetActiveScene().name}.");
                    PrepareRenderer();
                    phase = 1;
                    phaseEndsAt = Deadline(WarmupSeconds);
                }
                else if (Expired())
                {
                    Debug.LogError("River Crossing did not spawn a complete 7x7 battle within 30 seconds.");
                    Exit(2);
                }
                return;
            }

            if (phase == 1)
            {
                if (!Expired()) return;
                string outputPath = Path.GetFullPath(Path.Combine(
                    Application.dataPath,
                    "..",
                    "TestResults",
                    "RiverCrossingPerformance.json"));
                telemetry = BattleTelemetry.Attach(director, outputPath);
                phase = 2;
                phaseEndsAt = Deadline(ProfilerWarmupSeconds);
                return;
            }

            if (phase == 2)
            {
                if (!Expired()) return;
                telemetry.ResetMeasurementWindow();
                phase = 3;
                phaseEndsAt = Deadline(MeasurementSeconds);
                return;
            }

            if (phase != 3 || !Expired()) return;
            telemetry.Stop();
            BattlePerformanceSummary performance = telemetry.Performance();
            Debug.Log(
                $"RIVER_CROSSING_PERFORMANCE frames={performance.Frames} " +
                $"fps={performance.AverageFps:0.0} p95={performance.P95FrameMs:0.00}ms " +
                $"p99={performance.P99FrameMs:0.00}ms max={performance.MaximumFrameMs:0.00}ms " +
                $"cpu95={performance.P95CpuFrameMs:0.00}ms gpu95={performance.P95GpuFrameMs:0.00}ms " +
                $"slow50={performance.FramesOver50Ms} slow100={performance.FramesOver100Ms} " +
                $"gc={performance.TotalGcBytes} peakDraws={performance.PeakDrawCalls} " +
                $"marks={performance.LiveTrackMarks}/{performance.TrackMarkRenderers}/" +
                $"{performance.TrackMarkCapacity} emitted={performance.EmittedTrackMarks} " +
                $"skipped={performance.SkippedTrackMarks} tanks={director.SpawnedMembers.Count}");

            bool passed = performance.Frames > 300 &&
                          performance.P95FrameMs <= 33.34f &&
                          performance.FramesOver100Ms <= 2;
            phase = 4;
            Exit(passed ? 0 : 1);
        }

        private void PrepareRenderer()
        {
            battleCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            if (battleCamera == null)
            {
                Debug.LogWarning("River Crossing performance runner found no camera to render.");
                return;
            }

            renderTarget = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32)
            {
                name = "RiverCrossingPerformanceTarget"
            };
            renderTarget.Create();
            battleCamera.targetTexture = renderTarget;
        }

        private void RenderBattleFrame()
        {
            if (battleCamera == null || renderTarget == null || phase == 0) return;
            battleCamera.Render();
        }

        private static long Deadline(float seconds) => System.Diagnostics.Stopwatch.GetTimestamp() +
                                                       (long)(seconds * System.Diagnostics.Stopwatch.Frequency);

        private bool Expired() => System.Diagnostics.Stopwatch.GetTimestamp() >= phaseEndsAt;

        private void OnDestroy()
        {
            if (telemetry != null && telemetry.IsRecording) telemetry.Stop();
            ReleaseRenderer();
            RestoreTiming();
        }

        private void Exit(int exitCode)
        {
            ReleaseRenderer();
            RestoreTiming();
            Application.Quit(exitCode);
            Type editorApplication = Type.GetType("UnityEditor.EditorApplication,UnityEditor");
            editorApplication?.GetMethod("Exit", new[] { typeof(int) })?.Invoke(null, new object[] { exitCode });
        }

        private void RestoreTiming()
        {
            Application.targetFrameRate = previousTargetFrameRate;
            QualitySettings.vSyncCount = previousVSyncCount;
            Time.timeScale = previousTimeScale;
        }

        private void ReleaseRenderer()
        {
            if (battleCamera != null && battleCamera.targetTexture == renderTarget)
            {
                battleCamera.targetTexture = null;
            }
            if (renderTarget != null)
            {
                renderTarget.Release();
                Destroy(renderTarget);
            }
            renderTarget = null;
            battleCamera = null;
        }
    }
}
