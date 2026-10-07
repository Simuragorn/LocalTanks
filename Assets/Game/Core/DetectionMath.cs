using System;

namespace LocalTanks
{
    public enum DetectionReason
    {
        Detected,
        GuaranteedRange,
        HardBlocker,
        OutsideViewArc,
        OutsideViewRange,
        Concealed
    }

    public readonly struct DetectionInput
    {
        public DetectionInput(
            float distance,
            float viewRange,
            float stationaryConcealment,
            float bushBonus,
            float movementRevealPenalty,
            float firingRevealPenalty,
            float maximumConcealment,
            float minimumVisibilityFactor,
            float guaranteedDetectionRange,
            bool hasHardBlocker,
            bool isWithinViewArc = true)
        {
            Distance = distance;
            ViewRange = viewRange;
            StationaryConcealment = stationaryConcealment;
            BushBonus = bushBonus;
            MovementRevealPenalty = movementRevealPenalty;
            FiringRevealPenalty = firingRevealPenalty;
            MaximumConcealment = maximumConcealment;
            MinimumVisibilityFactor = minimumVisibilityFactor;
            GuaranteedDetectionRange = guaranteedDetectionRange;
            HasHardBlocker = hasHardBlocker;
            IsWithinViewArc = isWithinViewArc;
        }

        public float Distance { get; }
        public float ViewRange { get; }
        public float StationaryConcealment { get; }
        public float BushBonus { get; }
        public float MovementRevealPenalty { get; }
        public float FiringRevealPenalty { get; }
        public float MaximumConcealment { get; }
        public float MinimumVisibilityFactor { get; }
        public float GuaranteedDetectionRange { get; }
        public bool HasHardBlocker { get; }
        public bool IsWithinViewArc { get; }
    }

    public readonly struct DetectionResult
    {
        public DetectionResult(bool detected, DetectionReason reason, float concealment, float detectionDistance)
        {
            Detected = detected;
            Reason = reason;
            Concealment = concealment;
            DetectionDistance = detectionDistance;
        }

        public bool Detected { get; }
        public DetectionReason Reason { get; }
        public float Concealment { get; }
        public float DetectionDistance { get; }
    }

    public static class DetectionMath
    {
        public static DetectionResult Resolve(DetectionInput input)
        {
            float maximumConcealment = Clamp01(input.MaximumConcealment);
            float concealment = Clamp(
                input.StationaryConcealment + input.BushBonus -
                input.MovementRevealPenalty - input.FiringRevealPenalty,
                0f,
                maximumConcealment);
            float visibilityFactor = Math.Max(1f - concealment, Clamp01(input.MinimumVisibilityFactor));
            float detectionDistance = Math.Max(0f, input.ViewRange) * visibilityFactor;

            if (input.HasHardBlocker)
            {
                return new DetectionResult(false, DetectionReason.HardBlocker, concealment, detectionDistance);
            }

            if (!input.IsWithinViewArc)
            {
                return new DetectionResult(false, DetectionReason.OutsideViewArc, concealment, detectionDistance);
            }

            if (input.Distance > input.ViewRange)
            {
                return new DetectionResult(false, DetectionReason.OutsideViewRange, concealment, detectionDistance);
            }

            if (input.Distance <= input.GuaranteedDetectionRange)
            {
                return new DetectionResult(true, DetectionReason.GuaranteedRange, concealment, detectionDistance);
            }

            bool detected = input.Distance <= detectionDistance;
            return new DetectionResult(
                detected,
                detected ? DetectionReason.Detected : DetectionReason.Concealed,
                concealment,
                detectionDistance);
        }

        public static bool IsWithinViewArc(float forwardDot, float totalAngleDegrees)
        {
            if (totalAngleDegrees <= 0f)
            {
                return false;
            }

            if (totalAngleDegrees >= 360f)
            {
                return true;
            }

            double halfAngleRadians = totalAngleDegrees * 0.5d * Math.PI / 180d;
            double threshold = Math.Cos(halfAngleRadians);
            return Clamp(forwardDot, -1f, 1f) >= threshold;
        }

        private static float Clamp01(float value)
        {
            return Clamp(value, 0f, 1f);
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
