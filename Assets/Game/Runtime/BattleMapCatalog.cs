using System;

namespace LocalTanks
{
    public readonly struct BattleMapOption
    {
        public BattleMapOption(string sceneName, string displayName)
        {
            SceneName = sceneName;
            DisplayName = displayName;
        }

        public string SceneName { get; }
        public string DisplayName { get; }
    }

    public static class BattleMapCatalog
    {
        public static readonly BattleMapOption[] Playable =
        {
            new BattleMapOption("Battle_RiverCrossing", "Речная переправа"),
            new BattleMapOption("Battle_Mines", "Рудники")
        };

        public static BattleMapOption FindByDisplayName(string displayName) =>
            Array.Find(Playable, item => string.Equals(item.DisplayName, displayName, StringComparison.Ordinal));
    }
}
