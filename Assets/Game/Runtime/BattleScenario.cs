using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LocalTanks
{
    public enum BattleScenarioKind
    {
        StandardBattle,
        AiDuelLab,
        AiSquadLab,
        PlayerAiTestLab
    }

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
    }

    public sealed class BattleScenario : ScriptableObject
    {
        public const int TeamSize = 7;
        public const int SpawnSlotCount = 15;

        public string id;
        public string sceneId;
        public BattleScenarioKind kind;

        /// <summary>
        /// Match rules: the battle length, what the time limit means and whether friendly fire is allowed.
        /// They travel with the scenario so a battle cannot end because of a value hidden in a component.
        /// </summary>
        public MatchRules rules = new MatchRules();

        public BattleScenarioEntry[] entries;

        public int ExpectedTeamSize => kind switch
        {
            BattleScenarioKind.AiDuelLab => 1,
            BattleScenarioKind.AiSquadLab => 3,
            _ => TeamSize
        };
        public int RequiredPlayerCount =>
            kind == BattleScenarioKind.AiDuelLab || kind == BattleScenarioKind.AiSquadLab ? 0 : 1;
        public bool UsesRemoteStrategy => kind == BattleScenarioKind.StandardBattle;
        public bool UsesStrategicCoordinator =>
            kind != BattleScenarioKind.AiDuelLab && kind != BattleScenarioKind.AiSquadLab;

        public string[] Validate(CombatDatabase database)
        {
            List<string> errors = new List<string>();
            BattleScenarioEntry[] roster = entries ?? Array.Empty<BattleScenarioEntry>();
            if (string.IsNullOrWhiteSpace(id)) errors.Add("Scenario id is empty.");
            if (string.IsNullOrWhiteSpace(sceneId)) errors.Add("Scenario sceneId is empty.");
            if (rules == null)
            {
                errors.Add("Scenario has no match rules.");
            }
            else
            {
                errors.AddRange(rules.Validate($"Scenario '{id}' match rules:"));
            }
            int teamACount = roster.Count(item => item != null && item.team == TeamId.TeamA);
            int teamBCount = roster.Count(item => item != null && item.team == TeamId.TeamB);
            if (kind == BattleScenarioKind.PlayerAiTestLab)
            {
                if (teamACount < 1 || teamACount > TeamSize)
                    errors.Add($"Player AI Test Lab needs 1-{TeamSize} Team A entries.");
                if (teamBCount < 1 || teamBCount > TeamSize)
                    errors.Add($"Player AI Test Lab needs 1-{TeamSize} Team B entries.");
            }
            else
            {
                if (teamACount != ExpectedTeamSize)
                    errors.Add($"Scenario must contain exactly {ExpectedTeamSize} Team A entries.");
                if (teamBCount != ExpectedTeamSize)
                    errors.Add($"Scenario must contain exactly {ExpectedTeamSize} Team B entries.");
            }
            if (roster.Count(item => item != null && item.playerControlled) != RequiredPlayerCount)
                errors.Add($"Scenario must contain exactly {RequiredPlayerCount} player-controlled entries.");

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
                TankDefinition definition = database?.FindTank(entry.tankId);
                if (definition == null)
                    errors.Add($"Scenario entry '{entry.id}' references missing tank '{entry.tankId}'.");
                else if (!definition.availableInGame)
                    errors.Add($"Scenario entry '{entry.id}' references unavailable tank '{entry.tankId}'.");
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
            }

            foreach (TeamId team in new[] { TeamId.TeamA, TeamId.TeamB })
            {
                TankNation[] nations = roster
                    .Where(item => item != null && item.team == team)
                    .Select(item => database?.FindTank(item.tankId))
                    .Where(definition => definition != null)
                    .Select(definition => definition.nation)
                    .Distinct()
                    .ToArray();
                if (nations.Length > 1)
                    errors.Add($"{team} must contain tanks from exactly one nation.");

                if (kind != BattleScenarioKind.StandardBattle)
                {
                    continue;
                }

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
