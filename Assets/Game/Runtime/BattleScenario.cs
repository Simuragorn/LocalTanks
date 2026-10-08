using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LocalTanks
{
    public enum BattleLane
    {
        North,
        Center,
        South
    }

    public enum CombatAiRole
    {
        Assault,
        Support,
        Flank
    }

    [Serializable]
    public sealed class BattleScenarioEntry
    {
        public string id;
        public string tankId;
        public TeamId team;
        public bool playerControlled;
        public string baseId;
        [Range(0, BattleScenario.SpawnSlotCount - 1)] public int spawnSlot;
        public BattleLane lane;
        public CombatAiRole role;
        public Vector2Int[] routeCells;
    }

    public sealed class BattleScenario : ScriptableObject
    {
        public const int TeamSize = 7;
        public const int SpawnSlotCount = 15;

        public string id;
        public string sceneId;
        public BattleScenarioEntry[] entries;

        public string[] Validate(CombatDatabase database)
        {
            List<string> errors = new List<string>();
            BattleScenarioEntry[] roster = entries ?? Array.Empty<BattleScenarioEntry>();
            if (string.IsNullOrWhiteSpace(id)) errors.Add("Scenario id is empty.");
            if (string.IsNullOrWhiteSpace(sceneId)) errors.Add("Scenario sceneId is empty.");
            if (roster.Count(item => item != null && item.team == TeamId.TeamA) != TeamSize)
                errors.Add($"Scenario must contain exactly {TeamSize} Team A entries.");
            if (roster.Count(item => item != null && item.team == TeamId.TeamB) != TeamSize)
                errors.Add($"Scenario must contain exactly {TeamSize} Team B entries.");
            if (roster.Count(item => item != null && item.playerControlled) != 1)
                errors.Add("Scenario must contain exactly one player-controlled entry.");

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> spawnKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (BattleScenarioEntry entry in roster)
            {
                if (entry == null)
                {
                    errors.Add("Scenario contains a null entry.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.id) || !ids.Add(entry.id))
                    errors.Add($"Scenario entry id '{entry.id}' is empty or duplicated.");
                if (database == null || database.FindTank(entry.tankId) == null)
                    errors.Add($"Scenario entry '{entry.id}' references missing tank '{entry.tankId}'.");
                if (entry.team == TeamId.Neutral)
                    errors.Add($"Scenario entry '{entry.id}' cannot be neutral.");
                if (entry.playerControlled && entry.team != TeamId.TeamA)
                    errors.Add($"Player entry '{entry.id}' must belong to Team A.");
                if (entry.baseId != "A" && entry.baseId != "B")
                    errors.Add($"Scenario entry '{entry.id}' has unknown base '{entry.baseId}'.");
                if (entry.team == TeamId.TeamA && entry.baseId != "A" ||
                    entry.team == TeamId.TeamB && entry.baseId != "B")
                    errors.Add($"Scenario entry '{entry.id}' uses the opposing base.");
                if (entry.spawnSlot < 0 || entry.spawnSlot >= SpawnSlotCount)
                    errors.Add($"Scenario entry '{entry.id}' has invalid spawn slot {entry.spawnSlot}.");
                string spawnKey = $"{entry.baseId}:{entry.spawnSlot}";
                if (!spawnKeys.Add(spawnKey))
                    errors.Add($"Spawn point '{spawnKey}' is assigned more than once.");
                if (!entry.playerControlled && (entry.routeCells == null || entry.routeCells.Length < 2))
                    errors.Add($"AI entry '{entry.id}' needs at least two route cells.");
            }

            foreach (TeamId team in new[] { TeamId.TeamA, TeamId.TeamB })
            {
                foreach (BattleLane lane in Enum.GetValues(typeof(BattleLane)))
                {
                    int count = roster.Count(item => item != null && item.team == team && item.lane == lane);
                    int expectedCount = lane == BattleLane.Center ? 3 : 2;
                    if (count != expectedCount)
                        errors.Add($"{team} lane {lane} must contain exactly {expectedCount} entries, found {count}.");
                }
            }

            return errors.ToArray();
        }
    }
}
