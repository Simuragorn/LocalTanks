using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    public static class CombatAudioEmitter
    {
        private const float SpatialBlend = 0.82f;
        private const float MinimumDistance = 5f;
        private const float MaximumDistance = 32f;

        private static readonly Dictionary<string, AudioClip> Cache = new Dictionary<string, AudioClip>();
        private static ITankAudioClipProvider clipProvider = new ResourcesTankAudioClipProvider();

        public static int PlayCount { get; private set; }
        public static string LastClipId { get; private set; }

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
            if (Application.isBatchMode && clipProvider is ResourcesTankAudioClipProvider)
            {
                return;
            }

            if (!Cache.TryGetValue(clipId, out AudioClip clip))
            {
                clip = clipProvider.Load(clipId);
                Cache[clipId] = clip;
            }

            if (clip == null)
            {
                return;
            }

            GameObject emitter = new GameObject($"CombatAudio_{clipId}");
            emitter.transform.position = position;
            AudioSource source = emitter.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = SpatialBlend;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = MinimumDistance;
            source.maxDistance = MaximumDistance;
            source.dopplerLevel = 0f;
            source.clip = clip;
            source.volume = Mathf.Clamp01(volume);
            source.pitch = Mathf.Clamp(pitch, 0.25f, 3f);
            source.Play();
            Object.Destroy(emitter, clip.length / source.pitch + 0.1f);
            PlayCount++;
            LastClipId = clipId;
        }

        public static void SetClipProvider(ITankAudioClipProvider provider)
        {
            clipProvider = provider ?? new ResourcesTankAudioClipProvider();
            Cache.Clear();
            PlayCount = 0;
            LastClipId = null;
        }

        public static void ResetClipProvider()
        {
            SetClipProvider(new ResourcesTankAudioClipProvider());
        }
    }
}
