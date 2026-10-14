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
            Vector2 delta = destination - origin;
            float distance = delta.magnitude;
            if (distance <= 0.001f) return false;

            return TryEvaluateAlongDirection(
                ownerRoot,
                intendedTank,
                origin,
                delta / distance,
                distance,
                basePenetration,
                rules,
                out solution);
        }

        /// <summary>
        /// Forecasts the plate intersected by the direction the barrel actually faces. The hovered tank
        /// is supplied separately: cursor position selects a vehicle, but it cannot bend the ballistic
        /// ray while the turret is still traversing toward it.
        /// </summary>
        public static bool TryEvaluateAlongDirection(
            Transform ownerRoot,
            Transform intendedTank,
            Vector2 origin,
            Vector2 shotDirection,
            float maximumDistance,
            float basePenetration,
            CombatRules rules,
            out ArmorAimSolution solution)
        {
            solution = default;
            if (intendedTank == null || BelongsTo(intendedTank, ownerRoot)) return false;

            TankHealth health = intendedTank.GetComponent<TankHealth>();
            if (health == null || health.IsDestroyed) return false;
            TankVisibilityPresenter visibility = intendedTank.GetComponent<TankVisibilityPresenter>();
            if (visibility != null && !visibility.IsVisible) return false;

            float distance = Mathf.Max(0f, maximumDistance);
            if (distance <= 0.001f || shotDirection.sqrMagnitude <= 0.0001f) return false;
            Vector2 direction = shotDirection.normalized;

            RaycastHit2D nearest = default;
            float nearestDistance = float.PositiveInfinity;
            foreach (RaycastHit2D hit in Physics2D.RaycastAll(origin, direction, distance))
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
                : -direction;
            Vector2 localNormal = armor.transform.InverseTransformDirection(surfaceNormal).normalized;
            ArmorProjection projection = ArmorCombatMath.EvaluateProjection(
                direction,
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
