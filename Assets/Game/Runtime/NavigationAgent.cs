using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    [RequireComponent(typeof(TankMotor), typeof(Rigidbody2D))]
    public sealed class NavigationAgent : MonoBehaviour
    {
        [SerializeField] private NavigationMap map;
        [SerializeField] private TankMotor motor;
        [SerializeField] private Vector2[] patrolPoints;
        [SerializeField, Min(0)] private int clearanceCells;
        [SerializeField, Min(0.05f)] private float waypointTolerance = 0.3f;
        [SerializeField, Min(0.1f)] private float avoidanceRadius = 1.2f;
        [SerializeField] private bool loopPatrol = true;

        private readonly List<Vector2> path = new List<Vector2>();
        private int pathIndex;
        private int patrolIndex;
        private int plannedMapVersion = -1;
        private Vector2 destination;
        private bool hasDestination;
        private Vector2 progressSamplePosition;
        private float progressSampleTime;
        private float unstuckTime;

        public IReadOnlyList<Vector2> CurrentPath => path;
        public bool HasPath => pathIndex < path.Count;
        public Vector2 Destination => destination;

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
            loopPatrol = loop;
            progressSamplePosition = transform.position;
            progressSampleTime = Time.time;
            BeginPatrol();
        }

        public bool SetDestination(Vector2 worldDestination)
        {
            destination = worldDestination;
            hasDestination = true;
            return RebuildPath();
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

            if (hasDestination && map.Version != plannedMapVersion)
            {
                RebuildPath();
            }

            if (!HasPath)
            {
                motor.SetInput(0f, 0f);
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
                AdvancePatrol();
                return;
            }

            Vector2 desiredDirection = (path[pathIndex] - position).normalized;
            float angle = Vector2.SignedAngle(transform.up, desiredDirection);
            float turn = -Mathf.Clamp(angle / 35f, -1f, 1f);
            float drive = Mathf.Lerp(1f, 0.25f, Mathf.Clamp01(Mathf.Abs(angle) / 90f));

            ApplyLocalAvoidance(ref drive, ref turn);
            ApplyUnstuck(ref drive, ref turn, position);
            motor.SetInput(drive, turn);
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
            if (map == null)
            {
                return false;
            }

            path.AddRange(map.FindWorldPath(transform.position, destination, clearanceCells));
            plannedMapVersion = map.Version;
            if (path.Count > 1)
            {
                pathIndex = 1;
            }

            return HasPath;
        }

        private void ApplyLocalAvoidance(ref float drive, ref float turn)
        {
            Collider2D[] nearby = Physics2D.OverlapCircleAll(transform.position, avoidanceRadius);
            foreach (Collider2D collider in nearby)
            {
                NavigationAgent other = collider.GetComponentInParent<NavigationAgent>();
                if (other == null || other == this)
                {
                    continue;
                }

                Vector2 offset = (Vector2)other.transform.position - (Vector2)transform.position;
                float distance = offset.magnitude;
                if (distance <= 0.01f || Vector2.Dot(transform.up, offset.normalized) < 0.15f)
                {
                    continue;
                }

                drive *= Mathf.Clamp01((distance - 0.35f) / Mathf.Max(0.1f, avoidanceRadius - 0.35f));
                float side = Vector3.Cross(transform.up, offset.normalized).z;
                turn += side >= 0f ? 0.65f : -0.65f;
                turn = Mathf.Clamp(turn, -1f, 1f);
            }
        }

        private void ApplyUnstuck(ref float drive, ref float turn, Vector2 position)
        {
            if (unstuckTime > 0f)
            {
                unstuckTime -= Time.deltaTime;
                drive = -0.65f;
                turn = 0.8f;
                return;
            }

            if (Time.time - progressSampleTime < 2f)
            {
                return;
            }

            float progress = Vector2.Distance(progressSamplePosition, position);
            progressSamplePosition = position;
            progressSampleTime = Time.time;
            if (HasPath && progress < 0.08f)
            {
                unstuckTime = 0.8f;
                RebuildPath();
            }
        }
    }
}
