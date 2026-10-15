using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace LocalTanks
{
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
        public const int SchemaVersion = 3;

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
        public string DecisionFingerprint => decisionFingerprint.Value;
        public int FingerprintedDecisionCount => decisionFingerprint.Count;
        public string MotorControlFingerprint => motorControlFingerprint.Value;
        public string MotorPhysicsFingerprint => motorPhysicsFingerprint.Value;
        public int FingerprintedMotorStepCount => motorControlFingerprint.Count;
        public string DecisionTrace => string.Join("\n", events
            .Where(item => ContributesToDecisionFingerprint(item.kind))
            .Select(item => $"{item.kind}|{item.name}|{item.detail}"));

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
            events.Clear();
            tracks.Clear();
            decisionFingerprint.Reset();
            motorControlFingerprint.Reset();
            motorPhysicsFingerprint.Reset();
            startedAt = Time.time;
            nextSampleAt = 0f;
            recording = true;
            CollectTracks();
            startingHealth = 0;
            foreach (TeamMember member in director.SpawnedMembers)
            {
                if (member != null && member.Health != null) startingHealth += member.Health.MaximumHitPoints;
            }
        }

        /// <summary>Stops recording and returns the recording as JSON text.</summary>
        public string Stop()
        {
            recording = false;
            if (instance == this) instance = null;
            string json = ToJson();
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
            if (elapsed >= nextSampleAt)
            {
                nextSampleAt = elapsed + SampleIntervalSeconds;
                SampleTanks(elapsed);
            }
        }

        private void OnDestroy()
        {
            if (recording) Stop();
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
            builder.Append("\"tanks\":").Append(TracksJson()).Append(',');
            builder.Append("\"samples\":").Append(SamplesJson()).Append(',');
            builder.Append("\"events\":").Append(EventsJson()).Append('}');
            return builder.ToString();
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
    }
}
