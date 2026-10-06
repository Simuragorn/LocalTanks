using UnityEngine;

namespace LocalTanks
{
    public sealed class CombatDatabase : ScriptableObject
    {
        public TankDefinition[] tanks;
        public WeaponDefinition[] weapons;
        public ShellDefinition[] shells;

        public TankDefinition FindTank(string definitionId)
        {
            if (tanks == null)
            {
                return null;
            }

            foreach (TankDefinition tank in tanks)
            {
                if (tank != null && tank.id == definitionId)
                {
                    return tank;
                }
            }

            return null;
        }
    }
}
