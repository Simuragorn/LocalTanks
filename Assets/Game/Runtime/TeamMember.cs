using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LocalTanks
{
    public enum TeamId
    {
        Neutral,
        TeamA,
        TeamB
    }

    [RequireComponent(typeof(TankHealth))]
    public sealed class TeamMember : MonoBehaviour
    {
        private static readonly HashSet<TeamMember> Members = new HashSet<TeamMember>();
        private static TeamMember[] orderedMembers = Array.Empty<TeamMember>();
        private static bool memberOrderDirty = true;

        [SerializeField] private TeamId team = TeamId.Neutral;
        [SerializeField] private bool playerControlled;
        [SerializeField] private TankHealth health;
        [SerializeField] private TankMotor motor;
        [SerializeField] private WeaponController weapon;
        [SerializeField] private TankVisibilityPresenter visibilityPresenter;
        [SerializeField] private Transform visionDirection;

        public static event Action<TeamMember, bool> MembershipChanged;
        /// <summary>
        /// Active machines in a stable gameplay order. A HashSet is still used for membership, but its
        /// iteration order depends on transient Unity instance ids and changed between identical scene
        /// reloads. Target selection and vision must never inherit that process-specific order.
        /// </summary>
        public static IReadOnlyList<TeamMember> ActiveMembers
        {
            get
            {
                if (!memberOrderDirty) return orderedMembers;
                orderedMembers = Members
                    .Where(item => item != null)
                    .OrderBy(item => item.Team)
                    .ThenBy(StableOrderKey, StringComparer.Ordinal)
                    .ToArray();
                memberOrderDirty = false;
                return orderedMembers;
            }
        }

        public TeamId Team => team;
        public bool IsPlayerControlled => playerControlled;
        public TankHealth Health => health;
        public TankDefinition Definition => health != null ? health.Definition : null;
        public TankVisibilityPresenter VisibilityPresenter => visibilityPresenter;
        public bool IsAlive => health != null && !health.IsDestroyed;
        public bool IsMoving => motor != null && Mathf.Abs(motor.CurrentSpeed) > 0.05f;
        public float LastFiredTime { get; private set; } = float.NegativeInfinity;
        public Transform VisionDirection => visionDirection != null ? visionDirection : transform;
        public Vector2 VisionForward
        {
            get
            {
                Vector2 forward = VisionDirection.up;
                return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector2.up;
            }
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (weapon != null)
            {
                weapon.Fired += OnFired;
            }

            if (Members.Add(this))
            {
                memberOrderDirty = true;
                MembershipChanged?.Invoke(this, true);
            }
        }

        private void OnDisable()
        {
            if (weapon != null)
            {
                weapon.Fired -= OnFired;
            }

            if (Members.Remove(this))
            {
                memberOrderDirty = true;
                MembershipChanged?.Invoke(this, false);
            }
        }

        public void Configure(TeamId newTeam, bool isPlayerControlled)
        {
            team = newTeam;
            playerControlled = isPlayerControlled;
            ResolveReferences();
            Members.Add(this);
            memberOrderDirty = true;
            MembershipChanged?.Invoke(this, true);
        }

        internal static string StableOrderKey(TeamMember item)
        {
            if (item == null) return string.Empty;
            BotIdentity identity = item.GetComponent<BotIdentityComponent>()?.Identity;
            return identity != null && !string.IsNullOrWhiteSpace(identity.entityId)
                ? identity.entityId
                : item.gameObject.name;
        }

        public void RecordFiring(float time)
        {
            LastFiredTime = time;
        }

        private void ResolveReferences()
        {
            if (health == null) health = GetComponent<TankHealth>();
            if (motor == null) motor = GetComponent<TankMotor>();
            if (weapon == null) weapon = GetComponent<WeaponController>();
            if (visibilityPresenter == null) visibilityPresenter = GetComponent<TankVisibilityPresenter>();
            if (visionDirection == null)
            {
                TurretAiming turret = GetComponentInChildren<TurretAiming>(true);
                visionDirection = turret != null ? turret.transform : transform;
            }
        }

        private void OnFired(WeaponController source)
        {
            LastFiredTime = Time.time;
        }
    }
}
