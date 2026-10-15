using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LocalTanks
{
    [Serializable]
    public sealed class BattleLaunchSelection
    {
        public string sceneName;
        public string[] teamATanks = Array.Empty<string>();
        public string[] teamBTanks = Array.Empty<string>();

        public BattleLaunchSelection Clone() => new BattleLaunchSelection
        {
            sceneName = sceneName,
            teamATanks = (teamATanks ?? Array.Empty<string>()).ToArray(),
            teamBTanks = (teamBTanks ?? Array.Empty<string>()).ToArray()
        };
    }

    /// <summary>
    /// One transient launch request shared by the menu and the selected battle scene. The authored scenario
    /// remains the map's source of rules and spawn geometry; only its roster is cloned and replaced.
    /// </summary>
    public static class BattleLaunchConfiguration
    {
        private static readonly int[] SpawnSlots = { 7, 5, 9, 0, 14, 1, 10 };
        private static BattleLaunchSelection pending;
        private static BattleLaunchSelection last;

        public static BattleLaunchSelection Pending => pending?.Clone();
        public static BattleLaunchSelection Last => last?.Clone();

        public static void Set(string sceneName, IEnumerable<string> teamATanks, IEnumerable<string> teamBTanks)
        {
            BattleLaunchSelection selection = new BattleLaunchSelection
            {
                sceneName = sceneName?.Trim(),
                teamATanks = teamATanks?.ToArray() ?? Array.Empty<string>(),
                teamBTanks = teamBTanks?.ToArray() ?? Array.Empty<string>()
            };
            ValidateSelection(selection, null);
            pending = selection;
            last = selection.Clone();
        }

        public static void ClearPending() => pending = null;

        public static void RepeatLast()
        {
            pending = last?.Clone();
        }

        public static BattleScenario CreateRuntimeScenario(
            BattleScenario source,
            string activeSceneName,
            CombatDatabase database)
        {
            if (source == null || pending == null ||
                !string.Equals(pending.sceneName, activeSceneName, StringComparison.Ordinal))
            {
                return source;
            }

            ValidateSelection(pending, database);
            BattleScenario runtime = UnityEngine.Object.Instantiate(source);
            runtime.name = source.name;
            runtime.hideFlags = HideFlags.DontSave;
            runtime.allowsCustomRoster = true;
            runtime.entries = BuildEntries(pending).ToArray();
            pending = null;
            return runtime;
        }

        private static IEnumerable<BattleScenarioEntry> BuildEntries(BattleLaunchSelection selection)
        {
            for (int index = 0; index < selection.teamATanks.Length; index++)
                yield return Entry(TeamId.TeamA, index, selection.teamATanks[index], index == 0);
            for (int index = 0; index < selection.teamBTanks.Length; index++)
                yield return Entry(TeamId.TeamB, index, selection.teamBTanks[index], false);
        }

        private static BattleScenarioEntry Entry(TeamId team, int index, string tankId, bool player)
        {
            int slot = SpawnSlots[index];
            BattleLane lane = slot <= 1 ? BattleLane.South : slot >= 10 ? BattleLane.North : BattleLane.Center;
            CombatAiRole role = lane == BattleLane.Center
                ? (index % 2 == 0 ? CombatAiRole.Assault : CombatAiRole.Support)
                : CombatAiRole.Flank;
            return new BattleScenarioEntry
            {
                id = $"menu_{(team == TeamId.TeamA ? "a" : "b")}_{index + 1}",
                tankId = tankId,
                team = team,
                playerControlled = player,
                baseId = team == TeamId.TeamA ? "A" : "B",
                spawnSlot = slot,
                lane = lane,
                role = role
            };
        }

        private static void ValidateSelection(BattleLaunchSelection selection, CombatDatabase database)
        {
            if (selection == null || string.IsNullOrWhiteSpace(selection.sceneName))
                throw new ArgumentException("A battle launch needs a scene name.");
            ValidateTeam(selection.teamATanks, "Team A", database);
            ValidateTeam(selection.teamBTanks, "Team B", database);
        }

        private static void ValidateTeam(string[] tanks, string teamName, CombatDatabase database)
        {
            if (tanks == null || tanks.Length < 1 || tanks.Length > BattleScenario.TeamSize)
                throw new ArgumentException($"{teamName} must contain 1-{BattleScenario.TeamSize} tanks.");
            for (int index = 0; index < tanks.Length; index++)
            {
                string tankId = tanks[index];
                if (string.IsNullOrWhiteSpace(tankId))
                    throw new ArgumentException($"{teamName} slot {index + 1} has no tank.");
                TankDefinition definition = database?.FindTank(tankId);
                if (database != null && (definition == null || !definition.availableInGame))
                    throw new ArgumentException($"{teamName} slot {index + 1} uses unavailable tank '{tankId}'.");
            }
        }
    }

    /// <summary>
    /// The last player-authored battle setup. It deliberately stores the same stable scene and tank ids
    /// as <see cref="BattleLaunchConfiguration"/>, so the menu has no second roster format to maintain.
    /// Availability is resolved by the menu on load because content may have changed between launches.
    /// </summary>
    public static class BattleSetupPreferencesStore
    {
        public const string PreferencesKey = "localtanks.battleSetup.v1";

        public static BattleLaunchSelection Load()
        {
            string json = PlayerPrefs.GetString(PreferencesKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                BattleLaunchSelection selection = JsonUtility.FromJson<BattleLaunchSelection>(json);
                if (!HasValidShape(selection)) return null;
                selection.sceneName = selection.sceneName.Trim();
                return selection.Clone();
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        public static void Save(
            string sceneName,
            IEnumerable<string> teamATanks,
            IEnumerable<string> teamBTanks)
        {
            BattleLaunchSelection selection = new BattleLaunchSelection
            {
                sceneName = sceneName?.Trim(),
                teamATanks = teamATanks?.ToArray() ?? Array.Empty<string>(),
                teamBTanks = teamBTanks?.ToArray() ?? Array.Empty<string>()
            };
            if (!HasValidShape(selection))
                throw new ArgumentException("Battle setup preferences need a map and 1-7 tanks per team.");

            PlayerPrefs.SetString(PreferencesKey, JsonUtility.ToJson(selection));
            PlayerPrefs.Save();
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(PreferencesKey);
            PlayerPrefs.Save();
        }

        private static bool HasValidShape(BattleLaunchSelection selection)
        {
            return selection != null &&
                   !string.IsNullOrWhiteSpace(selection.sceneName) &&
                   HasValidTeam(selection.teamATanks) &&
                   HasValidTeam(selection.teamBTanks);
        }

        private static bool HasValidTeam(string[] tanks)
        {
            return tanks != null &&
                   tanks.Length >= 1 &&
                   tanks.Length <= BattleScenario.TeamSize;
        }
    }
}
