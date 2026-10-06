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
            float minimumCosine = ArmorMath.DefaultMinimumCosine)
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
    }

    public readonly struct ImpactResult
    {
        public ImpactResult(
            ImpactOutcome outcome,
            ArmorZone zone,
            float impactAngle,
            float effectiveArmor,
            Vector2 outgoingDirection,
            float remainingPenetration,
            float outgoingSpeed)
        {
            Outcome = outcome;
            Zone = zone;
            ImpactAngle = impactAngle;
            EffectiveArmor = effectiveArmor;
            OutgoingDirection = outgoingDirection;
            RemainingPenetration = remainingPenetration;
            OutgoingSpeed = outgoingSpeed;
        }

        public ImpactOutcome Outcome { get; }
        public ArmorZone Zone { get; }
        public float ImpactAngle { get; }
        public float EffectiveArmor { get; }
        public Vector2 OutgoingDirection { get; }
        public float RemainingPenetration { get; }
        public float OutgoingSpeed { get; }
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

    public static class ImpactResolver
    {
        public static ImpactResult Resolve(ImpactRequest request)
        {
            ArmorZone zone = ArmorMath.SelectZone(request.LocalSurfaceNormal);
            float cosine = ArmorMath.IncidenceCosine(request.ProjectileDirection, request.SurfaceNormal);
            float angle = Mathf.Acos(cosine) * Mathf.Rad2Deg;
            float effectiveArmor = ArmorMath.EffectiveArmor(
                request.Armor.ThicknessFor(zone),
                cosine,
                request.MinimumCosine);

            if (Mathf.Max(0f, request.Penetration) >= effectiveArmor)
            {
                return new ImpactResult(
                    ImpactOutcome.Penetrated,
                    zone,
                    angle,
                    effectiveArmor,
                    Vector2.zero,
                    0f,
                    0f);
            }

            if (request.RemainingRicochets > 0 && angle >= Mathf.Clamp(request.RicochetAngle, 0f, 90f))
            {
                return new ImpactResult(
                    ImpactOutcome.Ricocheted,
                    zone,
                    angle,
                    effectiveArmor,
                    RicochetMath.Reflect(request.ProjectileDirection, request.SurfaceNormal),
                    Mathf.Max(0f, request.Penetration) * Mathf.Clamp01(request.RicochetPenetrationMultiplier),
                    Mathf.Max(0f, request.Speed) * Mathf.Clamp01(request.RicochetSpeedMultiplier));
            }

            return new ImpactResult(
                ImpactOutcome.Blocked,
                zone,
                angle,
                effectiveArmor,
                Vector2.zero,
                0f,
                0f);
        }
    }
}
