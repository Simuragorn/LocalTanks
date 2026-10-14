using UnityEngine;

namespace LocalTanks
{
    public readonly struct ArmorAimSolution
    {
        public ArmorAimSolution(TankArmor armor, ArmorProjection projection, float penetrationChance)
        {
            Armor = armor;
            Projection = projection;
            PenetrationChance = penetrationChance;
        }

        public TankArmor Armor { get; }
        public ArmorProjection Projection { get; }
        public float PenetrationChance { get; }
    }

    /// <summary>
    /// Reads the plate actually presented under the cursor and delegates all armor math to Core. This is a
    /// query only: hidden enemies are rejected before their armor can affect the reticle.
    /// </summary>
    public static class ArmorAimProbe
    {
        public static bool TryEvaluate(
            Transform ownerRoot,
            Vector2 origin,
            Vector2 destination,
            float basePenetration,
            CombatRules rules,
            out ArmorAimSolution solution)
        {
            solution = default;
            Transform intendedTank = LineOfFireProbe.FindLivingTankAt(destination);
            if (intendedTank == null || BelongsTo(intendedTank, ownerRoot)) return false;

            TankVisibilityPresenter visibility = intendedTank.GetComponent<TankVisibilityPresenter>();
            if (visibility != null && !visibility.IsVisible) return false;

            Vector2 delta = destination - origin;
            float distance = delta.magnitude;
            if (distance <= 0.001f) return false;

            RaycastHit2D nearest = default;
            float nearestDistance = float.PositiveInfinity;
            foreach (RaycastHit2D hit in Physics2D.RaycastAll(origin, delta / distance, distance))
            {
                if (hit.collider == null || hit.collider.isTrigger ||
                    hit.collider.GetComponentInParent<FirePassThroughObstacle>() != null ||
                    BelongsTo(hit.collider.transform, ownerRoot))
                {
                    continue;
                }

                if (hit.distance < nearestDistance)
                {
                    nearest = hit;
                    nearestDistance = hit.distance;
                }
            }

            if (nearest.collider == null || !BelongsTo(nearest.collider.transform, intendedTank)) return false;
            TankArmor armor = nearest.collider.GetComponentInParent<TankArmor>();
            if (armor == null || armor.Definition == null) return false;

            CombatRules effectiveRules = rules ?? new CombatRules();
            Vector2 surfaceNormal = nearest.normal.sqrMagnitude > 0.0001f
                ? nearest.normal.normalized
                : -(delta / distance);
            Vector2 localNormal = armor.transform.InverseTransformDirection(surfaceNormal).normalized;
            ArmorProjection projection = ArmorCombatMath.EvaluateProjection(
                delta / distance,
                surfaceNormal,
                localNormal,
                armor.Definition.armor,
                effectiveRules.minimumArmorCosine);
            float chance = ArmorCombatMath.PenetrationChance(
                basePenetration,
                projection.EffectiveArmor,
                effectiveRules.penetrationRandomMinimum,
                effectiveRules.penetrationRandomMaximum);
            solution = new ArmorAimSolution(armor, projection, chance);
            return true;
        }

        private static bool BelongsTo(Transform candidate, Transform root)
        {
            return root != null && (candidate == root || candidate.IsChildOf(root));
        }
    }
}
