using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LocalTanks
{
    public enum BaseCaptureState
    {
        Owned,
        Capturing,
        Contested,
        Neutral
    }

    [RequireComponent(typeof(CircleCollider2D))]
    public sealed class CaptureBase : MonoBehaviour
    {
        [SerializeField] private string baseId;
        [SerializeField] private string displayName;
        [SerializeField] private TeamId owner = TeamId.Neutral;
        [SerializeField] private CaptureRules rules;
        [SerializeField] private LineRenderer ring;
        [SerializeField] private SpriteRenderer[] spawnMarkers;

        private readonly Dictionary<TeamMember, int> occupants = new Dictionary<TeamMember, int>();

        public event Action<CaptureBase> Changed;
        public event Action<CaptureBase, TeamId, TeamId> Captured;

        public string BaseId => baseId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? baseId : displayName;
        public TeamId Owner => owner;
        public TeamId CapturingTeam { get; private set; } = TeamId.Neutral;
        public BaseCaptureState State { get; private set; } = BaseCaptureState.Neutral;
        public float Progress { get; private set; }
        public int CapturingTankCount { get; private set; }
        public float RemainingSeconds { get; private set; }
        public int SpawnPointCount => spawnMarkers != null ? spawnMarkers.Length : 0;

        public Vector2 GetSpawnPosition(int slotIndex)
        {
            if (spawnMarkers == null || spawnMarkers.Length == 0)
            {
                return transform.position;
            }

            int normalizedIndex = ((slotIndex % spawnMarkers.Length) + spawnMarkers.Length) % spawnMarkers.Length;
            SpriteRenderer marker = spawnMarkers[normalizedIndex];
            return marker != null ? marker.transform.position : transform.position;
        }

        private void Awake()
        {
            if (spawnMarkers == null || spawnMarkers.Length == 0)
            {
                spawnMarkers = GetComponentsInChildren<SpriteRenderer>(true);
            }

            State = owner == TeamId.Neutral ? BaseCaptureState.Neutral : BaseCaptureState.Owned;
            UpdateVisuals();
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        public void Configure(
            string id,
            string title,
            TeamId initialOwner,
            CaptureRules captureRules,
            LineRenderer baseRing)
        {
            baseId = id;
            displayName = title;
            owner = initialOwner;
            rules = captureRules;
            ring = baseRing;
            spawnMarkers = GetComponentsInChildren<SpriteRenderer>(true);
            State = owner == TeamId.Neutral ? BaseCaptureState.Neutral : BaseCaptureState.Owned;
            UpdateVisuals();
        }

        public void SetPresence(TeamMember member, bool present)
        {
            if (member == null)
            {
                return;
            }

            if (present)
            {
                occupants[member] = Math.Max(1, occupants.TryGetValue(member, out int count) ? count : 1);
            }
            else
            {
                occupants.Remove(member);
            }
        }

        public void Tick(float deltaTime)
        {
            if (rules == null || deltaTime <= 0f)
            {
                return;
            }

            foreach (TeamMember missing in occupants.Keys.Where(member => member == null).ToArray())
            {
                occupants.Remove(missing);
            }

            int teamA = CountAlive(TeamId.TeamA);
            int teamB = CountAlive(TeamId.TeamB);
            BaseCaptureState previousState = State;
            TeamId previousCapturingTeam = CapturingTeam;
            float previousProgress = Progress;
            int previousCount = CapturingTankCount;

            if (teamA > 0 && teamB > 0)
            {
                State = BaseCaptureState.Contested;
                CapturingTankCount = 0;
                RemainingSeconds = 0f;
            }
            else
            {
                TeamId presentTeam = teamA > 0 ? TeamId.TeamA : teamB > 0 ? TeamId.TeamB : TeamId.Neutral;
                int presentCount = presentTeam == TeamId.TeamA ? teamA : teamB;
                bool canCapture = presentTeam != TeamId.Neutral && presentTeam != owner;

                if (canCapture && (CapturingTeam == TeamId.Neutral || CapturingTeam == presentTeam || Progress <= 0f))
                {
                    CapturingTeam = presentTeam;
                    CapturingTankCount = Mathf.Min(presentCount, rules.maximumContributingTanks);
                    State = BaseCaptureState.Capturing;
                    Progress = CaptureMath.Advance(
                        Progress,
                        deltaTime,
                        rules.baseCaptureSeconds,
                        CapturingTankCount,
                        rules.maximumContributingTanks);
                    RemainingSeconds = CaptureMath.RemainingSeconds(
                        Progress,
                        rules.baseCaptureSeconds,
                        CapturingTankCount,
                        rules.maximumContributingTanks);

                    if (Progress >= 1f)
                    {
                        TeamId previousOwner = owner;
                        owner = CapturingTeam;
                        Progress = 0f;
                        RemainingSeconds = 0f;
                        CapturingTankCount = 0;
                        CapturingTeam = TeamId.Neutral;
                        State = BaseCaptureState.Owned;
                        Captured?.Invoke(this, previousOwner, owner);
                    }
                }
                else
                {
                    Progress = CaptureMath.Recover(Progress, deltaTime, rules.fullRecoverySeconds);
                    CapturingTankCount = 0;
                    RemainingSeconds = 0f;
                    State = owner == TeamId.Neutral ? BaseCaptureState.Neutral : BaseCaptureState.Owned;
                    if (Progress <= 0f)
                    {
                        CapturingTeam = TeamId.Neutral;
                    }
                }
            }

            if (previousState != State || previousCapturingTeam != CapturingTeam ||
                previousCount != CapturingTankCount || !Mathf.Approximately(previousProgress, Progress))
            {
                UpdateVisuals();
                Changed?.Invoke(this);
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TeamMember member = other.GetComponentInParent<TeamMember>();
            if (member == null)
            {
                return;
            }

            occupants[member] = occupants.TryGetValue(member, out int count) ? count + 1 : 1;
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            TeamMember member = other.GetComponentInParent<TeamMember>();
            if (member == null || !occupants.TryGetValue(member, out int count))
            {
                return;
            }

            if (count <= 1)
            {
                occupants.Remove(member);
            }
            else
            {
                occupants[member] = count - 1;
            }
        }

        private int CountAlive(TeamId team)
        {
            return occupants.Keys.Count(member =>
                member != null && member.isActiveAndEnabled && member.IsAlive && member.Team == team);
        }

        private void UpdateVisuals()
        {
            if (ring == null)
            {
                return;
            }

            Color color;
            if (State == BaseCaptureState.Contested)
            {
                color = new Color32(190, 158, 92, 255);
            }
            else if (State == BaseCaptureState.Capturing)
            {
                color = TeamPalette.ForRelation(CapturingTeam == TeamId.TeamA);
            }
            else if (owner == TeamId.TeamA)
            {
                color = TeamPalette.Ally;
            }
            else if (owner == TeamId.TeamB)
            {
                color = TeamPalette.Enemy;
            }
            else
            {
                color = TeamPalette.Neutral;
            }

            color.a = 0.9f;
            ring.startColor = color;
            ring.endColor = color;
            if (spawnMarkers == null)
            {
                return;
            }

            foreach (SpriteRenderer marker in spawnMarkers)
            {
                if (marker == null)
                {
                    continue;
                }

                Color markerColor = color;
                markerColor.a = 0.58f;
                marker.color = markerColor;
            }
        }
    }
}
