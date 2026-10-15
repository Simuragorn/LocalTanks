using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LocalTanks
{
    public static class NavigationAvoidanceMath
    {
        public static float EffectiveWaypointTolerance(
            float authoredTolerance,
            float largeCellSafetyTolerance,
            float navigationCellSize)
        {
            float scaleAwareFloor = navigationCellSize > 0f
                ? Mathf.Min(largeCellSafetyTolerance, navigationCellSize * 0.3f)
                : largeCellSafetyTolerance;
            return Mathf.Max(0.05f, authoredTolerance, scaleAwareFloor);
        }

        public static float DriveFactor(float clearance, float avoidanceRadius)
        {
            float radius = Mathf.Max(0.1f, avoidanceRadius);
            float normalized = Mathf.InverseLerp(0.08f, radius, Mathf.Max(0f, clearance));
            return Mathf.Lerp(0.12f, 1f, normalized);
        }

        public static float TurnStrength(float clearance, float avoidanceRadius)
        {
            float radius = Mathf.Max(0.1f, avoidanceRadius);
            float normalized = Mathf.InverseLerp(0.08f, radius, Mathf.Max(0f, clearance));
            return Mathf.Lerp(0.9f, 0.25f, normalized);
        }
    }

    [RequireComponent(typeof(TankMotor), typeof(Rigidbody2D))]
    [DefaultExecutionOrder(0)]
    public sealed class NavigationAgent : MonoBehaviour
    {
        [SerializeField] private NavigationMap map;
        [SerializeField] private TankMotor motor;
        [SerializeField] private Vector2[] patrolPoints;
        [SerializeField, Min(0)] private int clearanceCells;
        [SerializeField, Min(0.05f)] private float waypointTolerance = 0.3f;

        /// <summary>
        /// Largest scale-derived waypoint safety tolerance the agent may use, in world units.
        /// <para>
        /// Three tenths of a unit is smaller than a hull and far smaller than a navigation cell, which is
        /// four units across on the converted arena. A tank travelling at 2.59 units per second covers
        /// several metres before it can come round, so it overshoots a point that close and its turning
        /// circle is wider than the distance left; it then circles the point it has already passed. On the
        /// converted Mines bots sat at path index one for a whole battle with a correct four-point path for
        /// exactly this reason.
        /// </para>
        /// </summary>
        [Tooltip("Upper limit for the scale-derived waypoint tolerance. The actual floor is 30% of the " +
                 "navigation cell, so compact maps keep their authored precision.")]
        [SerializeField, Min(0.05f)] private float largeCellSafetyTolerance = 1.2f;
        /// <summary>
        /// How far a neighbouring tank is noticed, in world units.
        /// <para>
        /// This value is measured rather than chosen. Local avoidance multiplies drive by
        /// <see cref="NavigationAvoidanceMath.DriveFactor"/>, which falls to 0.12 when the clearance to the
        /// nearest neighbour is small, and the clearance is compared against this radius. A small radius
        /// therefore throttles a tank hard rather than gently, which is the opposite of the intuition that a
        /// smaller radius means less interference.
        /// </para>
        /// <para>
        /// A sweep of 0.6, 1.2, 1.8 and 2.4 over five seeds each, on the converted Mines over 150 seconds,
        /// found the closest approach falling from 46 units to 23 and average damage rising from 11 to 235
        /// as the radius grew, while the spread between seeds narrowed from 26 units to 4.8. The wider
        /// radius is also better behaved, not merely luckier: at 2.4 every run came within 27 units, where
        /// at 1.2 four of five stayed beyond 56. The same sweep on River Crossing produced no new failures.
        /// </para>
        /// </summary>
        [Tooltip("How far a neighbouring tank is noticed. Smaller is not gentler: the drive penalty " +
                 "scales against this radius, so a small value throttles tanks on contact.")]
        [SerializeField, Min(0.1f)] private float avoidanceRadius = 2.4f;
        [SerializeField, Min(0.5f)] private float unstuckDuration = 1.8f;
        [SerializeField, Min(0.01f)] private float minimumProgressDistance = 0.18f;
        [SerializeField, Min(1f)] private float minimumProgressTurn = 8f;
        [SerializeField] private bool loopPatrol = true;

        [Tooltip("How far a tank may drift from its current waypoint before it rejoins the path at a nearer " +
                 "point. A hull at speed covers several metres before it can turn, so a tight value makes " +
                 "the tank circle a waypoint it has already passed.")]
        [SerializeField, Min(0.5f)] private float pathRecoveryDrift = 3f;

        private readonly List<Vector2> path = new List<Vector2>();
        private int pathIndex;
        private int patrolIndex;
        private int plannedMapVersion = -1;
        private Vector2 destination;
        private bool hasDestination;
        private Vector2 progressSamplePosition;
        private float progressSampleTime;
        private float progressSampleAngle;
        private float unstuckTime;
        private float unstuckTurn;
        private float unstuckDrive;
        private int unstuckAttempt;
        private bool replanAfterUnstuck;
        private float committedTurn;
        private bool movementPaused;
        private bool destinationReached;
        private bool hasFacingDirection;
        private Vector2 facingDirection;
        private int activeClearanceCells;
        private bool movingInReverse;
        private BattleDirector battleDirector;

        public IReadOnlyList<Vector2> CurrentPath => path;
        public bool HasPath => pathIndex < path.Count;
        public bool HasDestination => hasDestination;
        public bool DestinationReached => destinationReached;
        public bool MovementPaused => movementPaused;
        public Vector2 Destination => destination;

        /// <summary>
        /// The path the agent is walking, in world units. Exposed for diagnostics: a path that leads away
        /// from its own destination, or that has collapsed to one point, is a different fault from a tank
        /// that has a correct path and does not move along it.
        /// </summary>
        public Vector2[] PathPoints => path.ToArray();

        /// <summary>How far along the current path the agent has walked.</summary>
        public int PathIndex => pathIndex;

        /// <summary>How many times the tank rejoined its path after drifting past a waypoint.</summary>
        public int PathRecoveryCount { get; private set; }
        public bool IsRecoveringFromStall => unstuckTime > 0f;

        /// <summary>
        /// How many times the tank has entered stall recovery. A high count on a dense arena means the
        /// tank spends the battle backing out of geometry rather than advancing, which is a different
        /// fault from being slow.
        /// </summary>
        public int StallRecoveryCount { get; private set; }
        public NavigationMap Map => map;
        public int ClearanceCells => clearanceCells;
        private float Now => battleDirector != null ? (float)battleDirector.SimulationTime : Time.time;

        /// <summary>
        /// How close a tank must come to a waypoint before it counts as reached. Route consumers need
        /// this to decide the same thing the agent does; a tighter demand than the agent's own leaves a
        /// tank circling a point it can never satisfy.
        /// </summary>
        public float WaypointTolerance =>
            NavigationAvoidanceMath.EffectiveWaypointTolerance(
                waypointTolerance,
                largeCellSafetyTolerance,
                map != null ? map.CellSize : 0f);
        public bool HasFacingDirection => hasFacingDirection;
        public Vector2 FacingDirection => facingDirection;
        public bool MovingInReverse => movingInReverse;

        public void Configure(
            NavigationMap navigationMap,
            TankMotor tankMotor,
            Vector2[] waypoints,
            int clearance = 0,
            bool loop = true)
        {
            battleDirector ??= GetComponentInParent<BattleDirector>();
            map = navigationMap;
            motor = tankMotor;
            patrolPoints = waypoints;
            clearanceCells = Mathf.Max(0, clearance);
            activeClearanceCells = clearanceCells;
            loopPatrol = loop;
            destinationReached = false;
            movementPaused = false;
            progressSamplePosition = transform.position;
            progressSampleTime = Now;
            progressSampleAngle = transform.eulerAngles.z;
            unstuckAttempt = 0;
            unstuckTime = 0f;
            replanAfterUnstuck = false;
            hasFacingDirection = false;
            movingInReverse = false;
            BeginPatrol();
        }

        public bool SetDestination(Vector2 worldDestination)
        {
            return SetDestination(worldDestination, clearanceCells);
        }

        public bool SetDestination(Vector2 worldDestination, int minimumClearanceCells)
        {
            return SetDestination(worldDestination, minimumClearanceCells, false);
        }

        public bool SetDestination(Vector2 worldDestination, int minimumClearanceCells, bool reverse)
        {
            destination = worldDestination;
            hasDestination = true;
            destinationReached = false;
            hasFacingDirection = false;
            movingInReverse = reverse;
            activeClearanceCells = Mathf.Max(clearanceCells, minimumClearanceCells);
            return RebuildPath();
        }

        public void SetFacingDirection(Vector2 worldDirection)
        {
            if (worldDirection.sqrMagnitude <= 0.0001f)
            {
                hasFacingDirection = false;
                return;
            }

            facingDirection = worldDirection.normalized;
            hasFacingDirection = true;
        }

        public void ClearFacingDirection()
        {
            hasFacingDirection = false;
        }

        /// <summary>
        /// Leaves reverse approach mode. A reverse-capable task switches it on through
        /// <see cref="SetDestination(Vector2, int, bool)"/>, and nothing switched it off when that
        /// task ended: the next task only set a new destination for a forward approach, or paused
        /// movement entirely, so the flag outlived the maneuver and the tank kept reversing while
        /// fighting or holding cover.
        /// </summary>
        public void ClearReverseMode()
        {
            movingInReverse = false;
        }

        public void SetPaused(bool paused)
        {
            movementPaused = paused;
            if (paused && motor != null && !hasFacingDirection)
            {
                motor.SetInput(0f, 0f);
            }
        }

        public void ClearDestination()
        {
            path.Clear();
            pathIndex = 0;
            hasDestination = false;
            destinationReached = false;
            movingInReverse = false;
            motor?.SetInput(0f, 0f);
        }

        private void Awake()
        {
            if (motor == null)
            {
                motor = GetComponent<TankMotor>();
            }

            // A tank that appears after the battle started, or in a scene with no director, still honours
            // command line overrides. Without this a swept value would apply to some machines and not
            // others, which is worse than not sweeping at all.
            LocalTanksOverrides.ApplyToComponent(this);
        }

        private void Start()
        {
            if (!hasDestination)
            {
                BeginPatrol();
            }
        }

        private void FixedUpdate()
        {
            if (map == null || motor == null)
            {
                return;
            }

            if (movementPaused)
            {
                if (hasFacingDirection)
                {
                    float facingAngle = Vector2.SignedAngle(transform.up, facingDirection);
                    float facingTurn = Mathf.Abs(facingAngle) <= 2f
                        ? 0f
                        : TankMotionMath.PathFollowingInput(facingAngle).y;
                    motor.SetInput(0f, facingTurn);
                }
                else
                {
                    motor.SetInput(0f, 0f);
                }
                return;
            }

            if (hasDestination && map.Version != plannedMapVersion)
            {
                RebuildPath();
            }

            if (!HasPath)
            {
                motor.SetInput(0f, 0f);
                if (hasDestination)
                {
                    destinationReached = true;
                }

                AdvancePatrol();
                return;
            }

            Vector2 position = transform.position;
            while (pathIndex < path.Count && Vector2.Distance(position, path[pathIndex]) <= WaypointTolerance)
            {
                pathIndex++;
            }

            // A tank that overshoots a waypoint cannot simply turn back to it: at speed a hull needs many
            // metres to come round, so it circles the point it missed while the path stands still. This
            // rejoins the path at a nearer forward point instead.

            if (!HasPath)
            {
                motor.SetInput(0f, 0f);
                destinationReached = true;
                AdvancePatrol();
                return;
            }

            Vector2 desiredDirection = (path[pathIndex] - position).normalized;
            Vector2 movementForward = movingInReverse ? -(Vector2)transform.up : (Vector2)transform.up;
            float angle = Vector2.SignedAngle(movementForward, desiredDirection);
            Vector2 pathInput = TankMotionMath.PathFollowingInput(angle);
            float drive = movingInReverse ? -pathInput.x : pathInput.x;
            float requestedTurn = movingInReverse ? -pathInput.y : pathInput.y;
            float turn = ResolveCommittedTurn(angle, requestedTurn);

            ApplyLocalAvoidance(ref drive, ref turn, movementForward, movingInReverse);
            ApplyUnstuck(ref drive, ref turn, position, movingInReverse);
            motor.SetInput(drive, turn);
        }

        /// <summary>
        /// Rejoins the path near the tank when it has drifted far from its current waypoint instead of
        /// trying to turn back to it. The replacement point must be nearer the destination than the tank
        /// is, so recovery cannot walk the tank backwards along its own route.
        /// <para>
        /// Currently not called. It was tried on the converted arena to stop bots circling a waypoint they
        /// had overshot, and it did not change their progress; on River Crossing the same call broke the
        /// behaviour audit, which compares manoeuvres frame by frame. Kept because the reasoning is sound
        /// and the next attempt should test it deliberately rather than rediscover it, but any caller must
        /// re-run the audit before committing.
        /// </para>
        /// </summary>
        private void RecoverPathIfDrifted(Vector2 position)
        {
            if (!HasPath || pathIndex >= path.Count) return;
            if (Vector2.Distance(position, path[pathIndex]) <= pathRecoveryDrift) return;

            float toDestination = Vector2.Distance(position, destination);
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int index = pathIndex; index < path.Count; index++)
            {
                if (Vector2.Distance(path[index], destination) >= toDestination) continue;
                float distance = Vector2.Distance(position, path[index]);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = index;
                }
            }

            if (best >= 0 && bestDistance < Vector2.Distance(position, path[pathIndex]))
            {
                PathRecoveryCount++;
                pathIndex = best;
            }
        }

        private float ResolveCommittedTurn(float angle, float requestedTurn)
        {
            float absoluteAngle = Mathf.Abs(angle);
            if (absoluteAngle >= 120f)
            {
                if (Mathf.Abs(committedTurn) < 0.5f)
                {
                    committedTurn = Mathf.Abs(requestedTurn) > 0.01f
                        ? Mathf.Sign(requestedTurn)
                        : (StableDecisionSeed() & 1) == 0 ? 1f : -1f;
                }

                return committedTurn;
            }

            if (absoluteAngle <= 60f)
            {
                committedTurn = 0f;
            }

            return requestedTurn;
        }

        private void BeginPatrol()
        {
            if (patrolPoints == null || patrolPoints.Length == 0 || map == null)
            {
                return;
            }

            patrolIndex = Mathf.Clamp(patrolIndex, 0, patrolPoints.Length - 1);
            SetDestination(patrolPoints[patrolIndex]);
        }

        private void AdvancePatrol()
        {
            if (patrolPoints == null || patrolPoints.Length == 0)
            {
                hasDestination = false;
                return;
            }

            if (!loopPatrol && patrolIndex >= patrolPoints.Length - 1)
            {
                hasDestination = false;
                return;
            }

            patrolIndex = (patrolIndex + 1) % patrolPoints.Length;
            SetDestination(patrolPoints[patrolIndex]);
        }

        private bool RebuildPath()
        {
            path.Clear();
            pathIndex = 0;
            destinationReached = false;
            if (map == null)
            {
                return false;
            }

            path.AddRange(map.FindWorldPath(transform.position, destination, activeClearanceCells));
            plannedMapVersion = map.Version;
            if (path.Count > 1)
            {
                pathIndex = 1;
            }

            return HasPath;
        }

        private void ApplyLocalAvoidance(
            ref float drive,
            ref float turn,
            Vector2 movementForward,
            bool reverse)
        {
            Collider2D[] nearby = Physics2D.OverlapCircleAll(transform.position, avoidanceRadius);
            Dictionary<NavigationAgent, float> clearances = new Dictionary<NavigationAgent, float>();
            foreach (Collider2D collider in nearby)
            {
                NavigationAgent other = collider.GetComponentInParent<NavigationAgent>();
                if (other == null || other == this)
                {
                    continue;
                }

                float clearance = Vector2.Distance(transform.position, collider.ClosestPoint(transform.position));
                if (!clearances.TryGetValue(other, out float previous) || clearance < previous)
                    clearances[other] = clearance;
            }

            foreach (KeyValuePair<NavigationAgent, float> entry in clearances
                         .OrderBy(item => item.Key != null ? item.Key.gameObject.name : string.Empty,
                             System.StringComparer.Ordinal))
            {
                NavigationAgent other = entry.Key;
                Vector2 offset = (Vector2)other.transform.position - (Vector2)transform.position;
                float distance = offset.magnitude;
                if (distance <= 0.01f || Vector2.Dot(movementForward, offset.normalized) < 0.15f)
                {
                    continue;
                }

                float clearance = entry.Value;
                drive *= NavigationAvoidanceMath.DriveFactor(clearance, avoidanceRadius);
                float side = Vector3.Cross(movementForward, offset.normalized).z;
                float avoidanceTurn = NavigationAvoidanceMath.TurnStrength(clearance, avoidanceRadius);
                float steeringSign = reverse ? -1f : 1f;
                turn += (side >= 0f ? avoidanceTurn : -avoidanceTurn) * steeringSign;
                turn = Mathf.Clamp(turn, -1f, 1f);
            }
        }

        private void ApplyUnstuck(ref float drive, ref float turn, Vector2 position, bool reverse)
        {
            if (unstuckTime > 0f)
            {
                unstuckTime -= Time.deltaTime;
                drive = unstuckDrive;
                turn = unstuckTurn;
                if (unstuckTime <= 0f && replanAfterUnstuck)
                {
                    replanAfterUnstuck = false;
                    if (movingInReverse && unstuckAttempt >= 2)
                    {
                        // A reverse retreat is tactically preferable, but repeatedly wedging the
                        // hull against a cover corner is worse than briefly exposing the side.
                        // Keep the same destination and finish the route forwards.
                        movingInReverse = false;
                    }
                    RebuildPath();
                    progressSamplePosition = position;
                    progressSampleAngle = transform.eulerAngles.z;
                    progressSampleTime = Now;
                }
                return;
            }

            if (Now - progressSampleTime < 2f)
            {
                return;
            }

            float progress = Vector2.Distance(progressSamplePosition, position);
            float turnProgress = Mathf.Abs(Mathf.DeltaAngle(progressSampleAngle, transform.eulerAngles.z));
            progressSamplePosition = position;
            progressSampleAngle = transform.eulerAngles.z;
            progressSampleTime = Now;
            if (HasPath && progress < minimumProgressDistance && turnProgress < minimumProgressTurn)
            {
                unstuckAttempt++;
                StallRecoveryCount++;
                int directionSeed = StableDecisionSeed() + unstuckAttempt;
                unstuckTurn = (directionSeed & 1) == 0 ? 0.9f : -0.9f;
                float routeDrive = reverse ? -1f : 1f;
                unstuckDrive = (unstuckAttempt & 1) == 1 ? -routeDrive : routeDrive;
                unstuckTime = Mathf.Min(3.2f,
                    Mathf.Max(2.4f, unstuckDuration + (unstuckAttempt - 1) * 0.25f));
                replanAfterUnstuck = true;
            }
            else if (progress >= minimumProgressDistance * 2f)
            {
                unstuckAttempt = 0;
            }
        }

        private int StableDecisionSeed()
        {
            BotIdentityComponent identity = GetComponent<BotIdentityComponent>();
            string entityId = identity?.Identity?.entityId;
            return BotPersonalityGenerator.StableSeed(
                string.IsNullOrWhiteSpace(entityId) ? gameObject.name : entityId);
        }
    }
}
