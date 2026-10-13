using System.Collections.Generic;

namespace LocalTanks
{
    /// <summary>What actually happened in the battle. The weight decides how much it tenses the music.</summary>
    public enum BattleAudioEventKind
    {
        Shot,
        Penetrated,
        Blocked,
        Ricochet,
        Destroyed,
        CaptureContested,
        Captured
    }

    public enum BattleMusicMood
    {
        Relaxed,
        Intensive
    }

    /// <summary>
    /// Deterministic battle intensity for the music layer. It knows nothing about the model or about
    /// chance: the same time and the same event sequence always produce the same decision, so the
    /// relaxed/intensive switch is proven by a test instead of by listening. The LLM is deliberately
    /// not consulted, because music must not wait for a model answer.
    /// </summary>
    public sealed class BattleMusicIntensity
    {
        /// <summary>How long a single event keeps contributing to the intensity.</summary>
        public const float WindowSeconds = 12f;

        /// <summary>Intensity that turns the calm layer into the tense one.</summary>
        public const float IntensiveThreshold = 1.6f;

        /// <summary>Intensity the tense layer falls back below. The gap is the hysteresis.</summary>
        public const float RelaxedThreshold = 0.7f;

        /// <summary>A mood is held at least this long, so a single exchange cannot flap the music.</summary>
        public const float MinimumMoodSeconds = 6f;

        // A battle can produce thousands of events; the window only ever needs the recent ones, and a
        // generous cap keeps a pathological stream from growing the list without bound.
        private const int MaximumEvents = 512;

        private readonly List<Entry> events = new List<Entry>();

        public BattleMusicMood Mood { get; private set; } = BattleMusicMood.Relaxed;
        public float Intensity { get; private set; }
        public int EventCount => events.Count;
        public string LastReason { get; private set; } = "бой начался";

        /// <summary>
        /// When the current mood began, in the same time base callers pass to
        /// <see cref="Register(BattleAudioEventKind, float)"/>. The mood is held for at least
        /// <see cref="MinimumMoodSeconds"/> so a single exchange cannot flap the music.
        /// </summary>
        private float moodSince;

        public static float WeightFor(BattleAudioEventKind kind)
        {
            switch (kind)
            {
                case BattleAudioEventKind.Shot: return 0.3f;
                case BattleAudioEventKind.Penetrated: return 0.55f;
                case BattleAudioEventKind.Blocked: return 0.4f;
                case BattleAudioEventKind.Ricochet: return 0.3f;
                case BattleAudioEventKind.Destroyed: return 1.1f;
                case BattleAudioEventKind.CaptureContested: return 0.5f;
                case BattleAudioEventKind.Captured: return 0.9f;
                default: return 0f;
            }
        }

        public void Register(BattleAudioEventKind kind, float time)
        {
            Register(kind, time, WeightFor(kind));
        }

        /// <summary>Registers one battle event. Weight is clamped, so a caller cannot invert the scale.</summary>
        public void Register(BattleAudioEventKind kind, float time, float weight)
        {
            Prune(time);
            if (weight <= 0f)
            {
                LastReason = $"{kind} без веса";
                return;
            }

            if (events.Count >= MaximumEvents)
            {
                events.RemoveAt(0);
            }

            events.Add(new Entry(time, weight));
            LastReason = $"{kind} +{weight:0.00}";
        }

        /// <summary>Weighted sum of the events still inside the window, with a linear decay.</summary>
        public float Evaluate(float time)
        {
            float total = 0f;
            for (int index = 0; index < events.Count; index++)
            {
                float age = time - events[index].Time;
                if (age < 0f) age = 0f;
                if (age > WindowSeconds) continue;
                total += events[index].Weight * (1f - age / WindowSeconds);
            }

            return total;
        }

        /// <summary>
        /// Recomputes the intensity and switches the mood when the thresholds and the minimum mood
        /// time allow it. Returns true when the mood changed.
        /// </summary>
        public bool Update(float time)
        {
            Prune(time);
            Intensity = Evaluate(time);
            float moodAge = time - moodSince;
            if (moodAge < MinimumMoodSeconds)
            {
                return false;
            }

            if (Mood == BattleMusicMood.Relaxed && Intensity >= IntensiveThreshold)
            {
                Mood = BattleMusicMood.Intensive;
                moodSince = time;
                LastReason = $"напряжённость {Intensity:0.00} ≥ {IntensiveThreshold:0.00}";
                return true;
            }

            if (Mood == BattleMusicMood.Intensive && Intensity <= RelaxedThreshold)
            {
                Mood = BattleMusicMood.Relaxed;
                moodSince = time;
                LastReason = $"напряжённость {Intensity:0.00} ≤ {RelaxedThreshold:0.00}";
                return true;
            }

            return false;
        }

        /// <summary>Starts a new battle: the mood is calm and the minimum mood time starts now.</summary>
        public void Reset(float time)
        {
            events.Clear();
            Intensity = 0f;
            Mood = BattleMusicMood.Relaxed;
            moodSince = time;
            LastReason = "бой начался";
        }

        private void Prune(float time)
        {
            for (int index = events.Count - 1; index >= 0; index--)
            {
                if (time - events[index].Time > WindowSeconds)
                {
                    events.RemoveAt(index);
                }
            }
        }

        private readonly struct Entry
        {
            public Entry(float time, float weight)
            {
                Time = time;
                Weight = weight;
            }

            public readonly float Time;
            public readonly float Weight;
        }
    }
}
