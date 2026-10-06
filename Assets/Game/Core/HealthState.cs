using System;

namespace LocalTanks
{
    public readonly struct DamageResult
    {
        public DamageResult(int appliedDamage, int remainingHitPoints, bool wasDestroyed)
        {
            AppliedDamage = appliedDamage;
            RemainingHitPoints = remainingHitPoints;
            WasDestroyed = wasDestroyed;
        }

        public int AppliedDamage { get; }
        public int RemainingHitPoints { get; }
        public bool WasDestroyed { get; }
    }

    public sealed class HealthState
    {
        public HealthState(int maximumHitPoints)
        {
            if (maximumHitPoints <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumHitPoints));
            }

            MaximumHitPoints = maximumHitPoints;
            CurrentHitPoints = maximumHitPoints;
        }

        public int MaximumHitPoints { get; }
        public int CurrentHitPoints { get; private set; }
        public bool IsDestroyed => CurrentHitPoints == 0;

        public DamageResult ApplyDamage(int damage)
        {
            if (damage <= 0 || IsDestroyed)
            {
                return new DamageResult(0, CurrentHitPoints, false);
            }

            int previous = CurrentHitPoints;
            CurrentHitPoints = Math.Max(0, CurrentHitPoints - damage);
            return new DamageResult(previous - CurrentHitPoints, CurrentHitPoints, CurrentHitPoints == 0);
        }
    }
}
