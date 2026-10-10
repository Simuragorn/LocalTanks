using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    public static class NavigationAvoidanceMath
    {
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
    public sealed class NavigationAgent : MonoBehaviour
    {
        [SerializeField] private NavigationMap map;
        [SerializeField] private TankMotor motor;
        [SerializeField] private Vector2[] patrolPoints;
        [SerializeField, Min(0)] private int clearanceCells;
        [SerializeField, Min(0.05f)] private float waypointTolerance = 0.3f;
        [SerializeField, Min(0.1f)] private float avoidanceRadius = 1.2f;
        [SerializeField, Min(0.5f)] private float unstuckDuration = 1.8f;
        [SerializeField, Min(0.01f)] private float minimumProgressDistance = 0.18f;
        [SerializeField, Min(1f)] private float minimumProgressTurn = 8f;
        [SerializeField] private bool loopPatrol = true;

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

        public IReadOnlyList<Vector2> CurrentPath => path;
        public bool HasPath => pathIndex < path.Count;
        public bool HasDestination => hasDestination;
        public bool DestinationReached => destinationReached;
        public bool MovementPaused => movementPaused;
        public Vector2 Destination => destination;
        public bool IsRecoveringFromStall => unstuckTime > 0f;
        public NavigationMap Map => map;
        public int ClearanceCells => clearanceCells;
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
            map = navigationMap;
            motor = tankMotor;
            patrolPoints = waypoints;
            clearanceCells = Mathf.Max(0, clearance);
            activeClearanceCells = clearanceCells;
            loopPatrol = loop;
            destinationReached = false;
            movementPaused = false;
            progressSamplePosition = transform.position;
            progressSampleTime = Time.time;
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
        }

        private void Start()
        {
            if (!hasDestination)
            {
                BeginPatrol();
            }
        }

        private void Update()
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
            while (pathIndex < path.Count && Vector2.Distance(position, path[pathIndex]) <= waypointTolerance)
            {
                pathIndex++;
            }

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

            foreach (KeyValuePair<NavigationAgent, float> entry in clearances)
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
                    progressSampleTime = Time.time;
                }
                return;
            }

            if (Time.time - progressSampleTime < 2f)
            {
                return;
            }

            float progress = Vector2.Distance(progressSamplePosition, position);
            float turnProgress = Mathf.Abs(Mathf.DeltaAngle(progressSampleAngle, transform.eulerAngles.z));
            progressSamplePosition = position;
            progressSampleAngle = transform.eulerAngles.z;
            progressSampleTime = Time.time;
            if (HasPath && progress < minimumProgressDistance && turnProgress < minimumProgressTurn)
            {
                unstuckAttempt++;
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
