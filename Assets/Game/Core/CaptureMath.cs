using System;

namespace LocalTanks
{
    public static class CaptureMath
    {
        public static float CaptureDuration(float baseCaptureSeconds, int tankCount, int maximumContributors)
        {
            if (baseCaptureSeconds <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(baseCaptureSeconds));
            }

            if (maximumContributors <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumContributors));
            }

            int contributors = Math.Max(1, Math.Min(tankCount, maximumContributors));
            return baseCaptureSeconds / contributors;
        }

        public static float Advance(float progress, float deltaTime, float baseCaptureSeconds, int tankCount, int maximumContributors)
        {
            if (tankCount <= 0 || deltaTime <= 0f)
            {
                return Clamp01(progress);
            }

            return Clamp01(progress + deltaTime / CaptureDuration(baseCaptureSeconds, tankCount, maximumContributors));
        }

        public static float Recover(float progress, float deltaTime, float fullRecoverySeconds)
        {
            if (deltaTime <= 0f || fullRecoverySeconds <= 0f)
            {
                return Clamp01(progress);
            }

            return Clamp01(progress - deltaTime / fullRecoverySeconds);
        }

        public static float RemainingSeconds(float progress, float baseCaptureSeconds, int tankCount, int maximumContributors)
        {
            return (1f - Clamp01(progress)) * CaptureDuration(baseCaptureSeconds, tankCount, maximumContributors);
        }

        private static float Clamp01(float value)
        {
            return Math.Max(0f, Math.Min(1f, value));
        }
    }
}
