using System;
using UnityEngine;

namespace LocalTanks
{
    public enum ArmorZone
    {
        Front,
        Left,
        Right,
        Rear
    }

    public enum ImpactOutcome
    {
        Penetrated,
        Blocked,
        Ricocheted
    }

    [Serializable]
    public struct ArmorProfile
    {
        [Min(0f)] public float front;
        [Min(0f)] public float left;
        [Min(0f)] public float right;
        [Min(0f)] public float rear;

        public ArmorProfile(float front, float left, float right, float rear)
        {
            this.front = front;
            this.left = left;
            this.right = right;
            this.rear = rear;
        }

        public float ThicknessFor(ArmorZone zone)
        {
            switch (zone)
            {
                case ArmorZone.Front:
                    return front;
                case ArmorZone.Left:
                    return left;
                case ArmorZone.Right:
                    return right;
                case ArmorZone.Rear:
                    return rear;
                default:
                    throw new ArgumentOutOfRangeException(nameof(zone), zone, null);
            }
        }
    }

    public readonly struct ImpactRequest
    {
        public ImpactRequest(
            Vector2 projectileDirection,
            Vector2 surfaceNormal,
            Vector2 localSurfaceNormal,
            ArmorProfile armor,
            float penetration,
            float speed,
            float ricochetAngle,
            float ricochetSpeedMultiplier,
            float ricochetPenetrationMultiplier,
            int remainingRicochets,
            float minimumCosine = ArmorMath.DefaultMinimumCosine,
            float basePenetration = -1f,
            float penetrationRandomMinimum = 1f,
            float penetrationRandomMaximum = 1f)
        {
            ProjectileDirection = projectileDirection;
            SurfaceNormal = surfaceNormal;
            LocalSurfaceNormal = localSurfaceNormal;
            Armor = armor;
            Penetration = penetration;
            Speed = speed;
            RicochetAngle = ricochetAngle;
            RicochetSpeedMultiplier = ricochetSpeedMultiplier;
            RicochetPenetrationMultiplier = ricochetPenetrationMultiplier;
            RemainingRicochets = remainingRicochets;
            MinimumCosine = minimumCosine;
            BasePenetration = basePenetration >= 0f ? basePenetration : penetration;
            PenetrationRandomMinimum = penetrationRandomMinimum;
            PenetrationRandomMaximum = penetrationRandomMaximum;
        }

        public Vector2 ProjectileDirection { get; }
        public Vector2 SurfaceNormal { get; }
        public Vector2 LocalSurfaceNormal { get; }
        public ArmorProfile Armor { get; }
        public float Penetration { get; }
        public float Speed { get; }
        public float RicochetAngle { get; }
        public float RicochetSpeedMultiplier { get; }
        public float RicochetPenetrationMultiplier { get; }
        public int RemainingRicochets { get; }
        public float MinimumCosine { get; }
        public float BasePenetration { get; }
        public float PenetrationRandomMinimum { get; }
        public float PenetrationRandomMaximum { get; }
    }

    public readonly struct ImpactResult
    {
        public ImpactResult(
            ImpactOutcome outcome,
            ArmorZone zone,
            float impactAngle,
            float effectiveArmor,
            float penetrationChance,
            Vector2 outgoingDirection,
            float remainingPenetration,
            float outgoingSpeed)
        {
            Outcome = outcome;
            Zone = zone;
            ImpactAngle = impactAngle;
            EffectiveArmor = effectiveArmor;
            PenetrationChance = penetrationChance;
            OutgoingDirection = outgoingDirection;
            RemainingPenetration = remainingPenetration;
            OutgoingSpeed = outgoingSpeed;
        }

        public ImpactOutcome Outcome { get; }
        public ArmorZone Zone { get; }
        public float ImpactAngle { get; }
        public float EffectiveArmor { get; }
        public float PenetrationChance { get; }
        public Vector2 OutgoingDirection { get; }
        public float RemainingPenetration { get; }
        public float OutgoingSpeed { get; }
    }

    public readonly struct ArmorProjection
    {
        public ArmorProjection(ArmorZone zone, float impactAngle, float effectiveArmor)
        {
            Zone = zone;
            ImpactAngle = impactAngle;
            EffectiveArmor = effectiveArmor;
        }

        public ArmorZone Zone { get; }
        public float ImpactAngle { get; }
        public float EffectiveArmor { get; }
    }

    public static class ArmorMath
    {
        public const float DefaultMinimumCosine = 0.1f;

        public static ArmorZone SelectZone(Vector2 localSurfaceNormal)
        {
            Vector2 normal = localSurfaceNormal.normalized;
            if (Mathf.Abs(normal.y) >= Mathf.Abs(normal.x))
            {
                return normal.y >= 0f ? ArmorZone.Front : ArmorZone.Rear;
            }

            return normal.x < 0f ? ArmorZone.Left : ArmorZone.Right;
        }

        public static float IncidenceCosine(Vector2 projectileDirection, Vector2 surfaceNormal)
        {
            Vector2 direction = projectileDirection.normalized;
            Vector2 normal = surfaceNormal.normalized;
            return Mathf.Clamp01(Vector2.Dot(-direction, normal));
        }

        public static float ImpactAngle(Vector2 projectileDirection, Vector2 surfaceNormal)
        {
            return Mathf.Acos(IncidenceCosine(projectileDirection, surfaceNormal)) * Mathf.Rad2Deg;
        }

        public static float EffectiveArmor(float thickness, float incidenceCosine, float minimumCosine)
        {
            float safeCosine = Mathf.Max(Mathf.Clamp01(incidenceCosine), Mathf.Max(0.0001f, minimumCosine));
            return Mathf.Max(0f, thickness) / safeCosine;
        }
    }

    public static class RicochetMath
    {
        public static Vector2 Reflect(Vector2 direction, Vector2 surfaceNormal)
        {
            return Vector2.Reflect(direction.normalized, surfaceNormal.normalized).normalized;
        }
    }

    public static class ArmorCombatMath
    {
        public static ArmorProjection EvaluateProjection(
            Vector2 projectileDirection,
            Vector2 surfaceNormal,
            Vector2 localSurfaceNormal,
            ArmorProfile armor,
            float minimumCosine)
        {
            ArmorZone zone = ArmorMath.SelectZone(localSurfaceNormal);
            float cosine = ArmorMath.IncidenceCosine(projectileDirection, surfaceNormal);
            return new ArmorProjection(
                zone,
                Mathf.Acos(cosine) * Mathf.Rad2Deg,
                ArmorMath.EffectiveArmor(armor.ThicknessFor(zone), cosine, minimumCosine));
        }

        /// <summary>Chance that a uniform penetration roll reaches the effective plate thickness.</summary>
        public static float PenetrationChance(
            float basePenetration,
            float effectiveArmor,
            float minimumMultiplier,
            float maximumMultiplier)
        {
            float safePenetration = Mathf.Max(0f, basePenetration);
            float minimum = Mathf.Max(0f, Mathf.Min(minimumMultiplier, maximumMultiplier));
            float maximum = Mathf.Max(minimum, Mathf.Max(minimumMultiplier, maximumMultiplier));
            if (safePenetration <= 0f) return effectiveArmor <= 0f ? 1f : 0f;
            if (Mathf.Approximately(minimum, maximum))
                return safePenetration * minimum >= effectiveArmor ? 1f : 0f;

            float requiredMultiplier = Mathf.Max(0f, effectiveArmor) / safePenetration;
            return Mathf.Clamp01((maximum - requiredMultiplier) / (maximum - minimum));
        }
    }

    public static class ImpactResolver
    {
        public static ImpactResult Resolve(ImpactRequest request)
        {
            ArmorProjection projection = ArmorCombatMath.EvaluateProjection(
                request.ProjectileDirection,
                request.SurfaceNormal,
                request.LocalSurfaceNormal,
                request.Armor,
                request.MinimumCosine);
            float chance = ArmorCombatMath.PenetrationChance(
                request.BasePenetration,
                projection.EffectiveArmor,
                request.PenetrationRandomMinimum,
                request.PenetrationRandomMaximum);

            if (Mathf.Max(0f, request.Penetration) >= projection.EffectiveArmor)
            {
                return new ImpactResult(
                    ImpactOutcome.Penetrated,
                    projection.Zone,
                    projection.ImpactAngle,
                    projection.EffectiveArmor,
                    chance,
                    Vector2.zero,
                    0f,
                    0f);
            }

            if (request.RemainingRicochets > 0 &&
                projection.ImpactAngle >= Mathf.Clamp(request.RicochetAngle, 0f, 90f))
            {
                return new ImpactResult(
                    ImpactOutcome.Ricocheted,
                    projection.Zone,
                    projection.ImpactAngle,
                    projection.EffectiveArmor,
                    chance,
                    RicochetMath.Reflect(request.ProjectileDirection, request.SurfaceNormal),
                    Mathf.Max(0f, request.Penetration) * Mathf.Clamp01(request.RicochetPenetrationMultiplier),
                    Mathf.Max(0f, request.Speed) * Mathf.Clamp01(request.RicochetSpeedMultiplier));
            }

            return new ImpactResult(
                ImpactOutcome.Blocked,
                projection.Zone,
                projection.ImpactAngle,
                projection.EffectiveArmor,
                chance,
                Vector2.zero,
                0f,
                0f);
        }
    }
}
