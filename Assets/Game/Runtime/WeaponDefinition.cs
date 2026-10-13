using UnityEngine;

namespace LocalTanks
{
    public sealed class WeaponDefinition : ScriptableObject
    {
        public string id;
        public string displayName;

        /// <summary>
        /// Bore diameter in millimetres, taken from the same World of Tanks configuration as everything
        /// else about the gun. It is the one honest reason a 45 mm and an 88 mm shot look different, so
        /// the visual layer selects its effect band by caliber instead of keeping a per-machine copy.
        /// </summary>
        [Min(0.01f)] public float caliberMm = 1f;

        [Min(0.01f)] public float reloadSeconds = 1f;
        [Min(1)] public int magazineSize = 1;
        [Min(0f)] public float shotIntervalSeconds;
        public ShellDefinition shell;
    }
}
