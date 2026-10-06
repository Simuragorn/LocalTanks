using UnityEngine;

namespace LocalTanks
{
    public sealed class WeaponDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        [Min(0.01f)] public float reloadSeconds = 1f;
        public ShellDefinition shell;
    }
}
