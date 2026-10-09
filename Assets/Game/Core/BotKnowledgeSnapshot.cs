using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace LocalTanks
{
    public enum BotContactState
    {
        Unknown,
        Remembered,
        Visible
    }

    [Serializable]
    public sealed class BotVehicleKnowledge
    {
        public string tankId;
        public string displayName;
        public string vehicleClass;
        public string nation;
        public int tier;
        public int currentHitPoints;
        public int maximumHitPoints;
        public float positionX;
        public float positionY;
        public float hullRotationDegrees;
        public float preferredDistance;
        public string tacticalState;
        public string fireBlockReason;
        public ArmorProfile armor;
        public float maximumForwardSpeed;
        public float maximumReverseSpeed;
        public float hullTurnSpeed;
        public float turretTurnSpeed;
        public float viewRange;
        public float stationaryConcealment;
        public float minimumDispersionDegrees;
        public float maximumDispersionDegrees;
        public float aimingTimeSeconds;
        public string weaponId;
        public float reloadSeconds;
        public string shellId;
        public int shellDamage;
        public float shellPenetration;
        public float shellSpeed;
    }

    [Serializable]
    public sealed class BotAllyKnowledge
    {
        public string memberId;
        public string nickname;
        public string tankId;
        public string vehicleClass;
        public bool isPlayer;
        public bool isAlive;
        public int currentHitPoints;
        public int maximumHitPoints;
        public float positionX;
        public float positionY;
    }

    [Serializable]
    public sealed class BotEnemyKnowledge
    {
        public string memberId;
        public string tankId;
        public string vehicleClass;
        public bool isAlive;
        public BotContactState contactState;
        public bool hasKnownPosition;
        public bool positionIsCurrent;
        public float positionX;
        public float positionY;
        public float secondsSinceSeen;
        public int currentHitPoints = -1;
        public int maximumHitPoints;
    }

    [Serializable]
    public sealed class BotBaseKnowledge
    {
        public string baseId;
        public string ownerTeamId;
        public string state;
        public string capturingTeamId;
        [Range(0f, 1f)] public float captureProgress;
        public int capturingTankCount;
        public float remainingSeconds;
    }

    [Serializable]
    public sealed class BotPlanKnowledge
    {
        public string teamSummary;
        public string primaryLaneId;
        public string secondaryLaneId;
        public string assignedLaneId;
        public string assignedTask;
        public float assignmentPriority;
        public string assignmentRationale;
        public bool isReserve;
    }

    [Serializable]
    public sealed class BotKnowledgeSnapshot
    {
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        public string snapshotId;
        public string battleId;
        public string botId;
        public string teamId;
        public float gameTimeSeconds;
        public int alliedAlive;
        public int enemyAlive;
        public BotIdentity identity;
        public BotMentalState mentalState;
        public BotVehicleKnowledge self;
        public BotAllyKnowledge[] allies;
        public BotEnemyKnowledge[] enemies;
        public BotBaseKnowledge[] bases;
        public BotPlanKnowledge plan;
        public string[] knownLaneIds;
        public string[] knownZoneIds;
        public string[] knownTargetIds;
        public string[] allyIds;
        public string snapshotHash;
    }

    public static class BotKnowledgeSnapshotValidator
    {
        public static string[] Validate(BotKnowledgeSnapshot snapshot)
        {
            List<string> errors = new List<string>();
            if (snapshot == null)
            {
                errors.Add("Bot knowledge snapshot is null.");
                return errors.ToArray();
            }

            if (snapshot.schemaVersion != BotKnowledgeSnapshot.CurrentSchemaVersion)
                errors.Add($"Unsupported bot knowledge schema version {snapshot.schemaVersion}.");
            if (string.IsNullOrWhiteSpace(snapshot.snapshotId)) errors.Add("Snapshot id is empty.");
            if (string.IsNullOrWhiteSpace(snapshot.battleId)) errors.Add("Snapshot battle id is empty.");
            if (string.IsNullOrWhiteSpace(snapshot.botId)) errors.Add("Snapshot bot id is empty.");
            if (snapshot.identity == null || snapshot.identity.personality == null)
                errors.Add("Snapshot identity or personality is missing.");
            if (snapshot.self == null) errors.Add("Snapshot own vehicle data is missing.");
            else
            {
                if (snapshot.self.currentHitPoints < 0 ||
                    snapshot.self.currentHitPoints > snapshot.self.maximumHitPoints)
                    errors.Add("Snapshot own hit points are invalid.");
                if (snapshot.self.maximumHitPoints <= 0) errors.Add("Snapshot own maximum hit points are invalid.");
            }

            HashSet<string> enemyIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (BotEnemyKnowledge enemy in snapshot.enemies ?? Array.Empty<BotEnemyKnowledge>())
            {
                if (enemy == null || string.IsNullOrWhiteSpace(enemy.memberId) || !enemyIds.Add(enemy.memberId))
                {
                    errors.Add("Snapshot contains an empty or duplicated enemy id.");
                    continue;
                }

                if (enemy.contactState == BotContactState.Visible)
                {
                    if (!enemy.hasKnownPosition || !enemy.positionIsCurrent)
                        errors.Add($"Visible enemy '{enemy.memberId}' must have a current position.");
                    if (enemy.isAlive && enemy.currentHitPoints < 0)
                        errors.Add($"Visible enemy '{enemy.memberId}' must expose current hit points.");
                }
                else
                {
                    if (enemy.positionIsCurrent)
                        errors.Add($"Non-visible enemy '{enemy.memberId}' cannot expose a current position.");
                    if (enemy.currentHitPoints != -1)
                        errors.Add($"Non-visible enemy '{enemy.memberId}' cannot expose current hit points.");
                    if (enemy.contactState == BotContactState.Unknown && enemy.hasKnownPosition)
                        errors.Add($"Unknown enemy '{enemy.memberId}' cannot expose a position.");
                    if (enemy.contactState == BotContactState.Remembered && !enemy.hasKnownPosition)
                        errors.Add($"Remembered enemy '{enemy.memberId}' needs a last known position.");
                }
            }

            string[] knownTargets = snapshot.knownTargetIds ?? Array.Empty<string>();
            HashSet<string> expectedTargets = new HashSet<string>(
                (snapshot.enemies ?? Array.Empty<BotEnemyKnowledge>())
                .Where(item => item != null && item.isAlive && item.contactState != BotContactState.Unknown)
                .Select(item => item.memberId), StringComparer.Ordinal);
            if (!expectedTargets.SetEquals(knownTargets))
                errors.Add("Snapshot known target ids do not match visible and remembered contacts.");

            string[] allies = snapshot.allyIds ?? Array.Empty<string>();
            HashSet<string> expectedAllies = new HashSet<string>(
                (snapshot.allies ?? Array.Empty<BotAllyKnowledge>())
                .Where(item => item != null && item.isAlive)
                .Select(item => item.memberId), StringComparer.Ordinal);
            if (!expectedAllies.SetEquals(allies))
                errors.Add("Snapshot ally ids do not match living allies.");
            return errors.ToArray();
        }
    }

    public static class BotKnowledgeSnapshotHasher
    {
        public static string Compute(BotKnowledgeSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            string previous = snapshot.snapshotHash;
            snapshot.snapshotHash = string.Empty;
            string json = JsonUtility.ToJson(snapshot);
            snapshot.snapshotHash = previous;
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(json));
                StringBuilder result = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) result.Append(value.ToString("x2"));
                return result.ToString();
            }
        }
    }
}
