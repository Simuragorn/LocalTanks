using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LocalTanks
{
    public static class CombatAudioEmitter
    {
        public const int DefaultVoiceLimit = 24;
        private const float SpatialBlend = 0.82f;
        private const float MinimumDistance = 5f;
        private const float MaximumDistance = 32f;

        private static readonly Dictionary<string, AudioClip> Cache = new Dictionary<string, AudioClip>();
        private static readonly List<AudioSource> Voices = new List<AudioSource>();
        private static ITankAudioClipProvider clipProvider = new ResourcesTankAudioClipProvider();
        private static GameObject poolRoot;
        private static int nextVoice;

        public static int PlayCount { get; private set; }
        public static string LastClipId { get; private set; }
        public static int VoiceCount => Voices.Count(source => source != null);
        public static int VoiceStealCount { get; private set; }

        /// <summary>
        /// Builds the bounded impact-audio pool and resolves the clips during battle loading. Machine guns
        /// can produce dozens of impacts in a few seconds; creating and destroying a GameObject and an
        /// AudioSource for every bullet made those hits coincide with large main-thread and GC spikes.
        /// </summary>
        public static void PrepareForBattle(
            IEnumerable<ShellDefinition> shells,
            Transform parent,
            int voiceLimit = DefaultVoiceLimit)
        {
            if (AudioRuntimePolicy.IsSuppressed && clipProvider is ResourcesTankAudioClipProvider)
            {
                return;
            }

            EnsureVoices(parent, voiceLimit);
            foreach (ShellAudioProfile profile in (shells ?? System.Array.Empty<ShellDefinition>())
                         .Where(shell => shell != null && shell.audio != null)
                         .Select(shell => shell.audio))
            {
                LoadClip(profile.penetrationClipId);
                LoadClip(profile.blockedClipId);
                LoadClip(profile.ricochetClipId);
            }
        }

        public static void PlayImpact(ShellAudioProfile profile, ImpactOutcome outcome, Vector3 position)
        {
            if (profile == null)
            {
                return;
            }

            PlayAtPoint(profile.ClipIdFor(outcome), position, profile.VolumeFor(outcome), profile.pitch);
        }

        public static void PlayAtPoint(string clipId, Vector3 position, float volume, float pitch = 1f)
        {
            if (string.IsNullOrWhiteSpace(clipId) || clipProvider == null)
            {
                return;
            }

            // Automated suites run in batchmode; they must not create audio objects or play clips.
            if (AudioRuntimePolicy.IsSuppressed && clipProvider is ResourcesTankAudioClipProvider)
            {
                return;
            }

            AudioClip clip = LoadClip(clipId);
            if (clip == null)
            {
                return;
            }

            if (Voices.Count == 0)
            {
                EnsureVoices(null, DefaultVoiceLimit);
            }
            if (Voices.Count == 0)
            {
                return;
            }

            AudioSource source = Voices[nextVoice % Voices.Count];
            nextVoice = (nextVoice + 1) % Voices.Count;
            if (source == null)
            {
                return;
            }

            if (source.isPlaying)
            {
                source.Stop();
                VoiceStealCount++;
            }

            source.transform.position = position;
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = SpatialBlend;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = MinimumDistance;
            source.maxDistance = MaximumDistance;
            source.dopplerLevel = 0f;
            source.clip = clip;
            source.volume = Mathf.Clamp01(volume * PlayerSettingsStore.EffectsMultiplier);
            source.pitch = Mathf.Clamp(pitch, 0.25f, 3f);
            source.Play();
            PlayCount++;
            LastClipId = clipId;
        }

        private static AudioClip LoadClip(string clipId)
        {
            if (string.IsNullOrWhiteSpace(clipId))
            {
                return null;
            }

            if (!Cache.TryGetValue(clipId, out AudioClip clip))
            {
                clip = clipProvider.Load(clipId);
                Cache[clipId] = clip;
            }

            return clip;
        }

        private static void EnsureVoices(Transform parent, int voiceLimit)
        {
            int limit = Mathf.Clamp(voiceLimit, 1, 64);
            if (poolRoot == null)
            {
                Voices.Clear();
                poolRoot = new GameObject("CombatAudioPool");
                if (parent != null)
                {
                    poolRoot.transform.SetParent(parent, false);
                }
            }
            else if (parent != null && poolRoot.transform.parent != parent)
            {
                poolRoot.transform.SetParent(parent, false);
            }

            Voices.RemoveAll(source => source == null);
            while (Voices.Count < limit)
            {
                GameObject voice = new GameObject($"Voice_{Voices.Count:00}");
                voice.transform.SetParent(poolRoot.transform, false);
                AudioSource source = voice.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                Voices.Add(source);
            }
        }

        public static void SetClipProvider(ITankAudioClipProvider provider)
        {
            clipProvider = provider ?? new ResourcesTankAudioClipProvider();
            Cache.Clear();
            PlayCount = 0;
            LastClipId = null;
            VoiceStealCount = 0;
            nextVoice = 0;
            foreach (AudioSource source in Voices)
            {
                if (source == null) continue;
                source.Stop();
                source.clip = null;
            }
        }

        public static void ResetClipProvider()
        {
            SetClipProvider(new ResourcesTankAudioClipProvider());
        }

        public static void ShutdownPool()
        {
            if (poolRoot != null)
            {
                UnityEngine.Object.Destroy(poolRoot);
            }

            poolRoot = null;
            Voices.Clear();
            nextVoice = 0;
        }
    }
}
