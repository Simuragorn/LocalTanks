using System;
using UnityEngine;

namespace LocalTanks
{
    public sealed class TankHealth : MonoBehaviour
    {
        [SerializeField] private TankDefinition definition;

        private HealthState state;

        public event Action<TankHealth> Destroyed;

        public int CurrentHitPoints => state != null ? state.CurrentHitPoints : 0;
        public int MaximumHitPoints => state != null ? state.MaximumHitPoints : 0;
        public bool IsDestroyed => state != null && state.IsDestroyed;

        private void Awake()
        {
            InitializeState();
        }

        public void Configure(TankDefinition newDefinition)
        {
            definition = newDefinition;
            InitializeState();
        }

        public DamageResult ApplyDamage(int amount)
        {
            InitializeState();
            DamageResult result = state.ApplyDamage(amount);
            if (result.WasDestroyed)
            {
                Destroyed?.Invoke(this);
            }

            return result;
        }

        private void InitializeState()
        {
            if (state == null && definition != null)
            {
                state = new HealthState(Mathf.Max(1, definition.maxHitPoints));
            }
        }
    }
}
