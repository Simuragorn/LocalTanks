using UnityEngine;

namespace LocalTanks
{
    public sealed class WeaponDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        [Min(0.01f)] public float reloadSeconds = 1f;
        [Min(1)] public int magazineSize = 1;
        [Min(0f)] public float shotIntervalSeconds;
        public ShellDefinition shell;
    }
}
