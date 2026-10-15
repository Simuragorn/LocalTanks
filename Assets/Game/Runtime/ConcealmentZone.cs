using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    public sealed class ConcealmentZone : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float concealmentBonus = 0.18f;
        [SerializeField, Min(0.1f)] private float fadeDistance = 2f;
        [SerializeField, Range(0.05f, 1f)] private float fadedAlpha = 0.3f;
        [SerializeField, Min(0.1f)] private float fadeSpeed = 4f;

        private SpriteRenderer[] renderers;
        private float[] originalAlphas;
        private float targetAlpha = 1f;
        private bool queuedForFade;

        public float ConcealmentBonus => concealmentBonus;
        public float CurrentAlpha { get; private set; } = 1f;

        private void Awake()
        {
            renderers = GetComponentsInChildren<SpriteRenderer>(true);
            originalAlphas = new float[renderers.Length];
            for (int index = 0; index < renderers.Length; index++)
            {
                originalAlphas[index] = renderers[index] != null ? renderers[index].color.a : 0f;
            }
        }

        private void OnEnable()
        {
            if (Application.isPlaying)
            {
                ConcealmentFadeScheduler.Register(this);
            }
        }

        private void OnDisable()
        {
            if (Application.isPlaying)
            {
                ConcealmentFadeScheduler.Unregister(this);
            }
        }

        public void Configure(float bonus)
        {
            concealmentBonus = Mathf.Clamp01(bonus);
        }

        internal void RefreshFadeTarget(TeamMember player)
        {
            float nextTarget = 1f;
            if (player != null)
            {
                Vector2 offset = (Vector2)transform.position - (Vector2)player.transform.position;
                if (offset.sqrMagnitude <= fadeDistance * fadeDistance)
                {
                    nextTarget = fadedAlpha;
                }
            }

            if (Mathf.Approximately(targetAlpha, nextTarget))
            {
                return;
            }

            targetAlpha = nextTarget;
            ConcealmentFadeScheduler.QueueFade(this);
        }

        internal bool StepFade(float deltaTime)
        {
            float nextAlpha = Mathf.MoveTowards(CurrentAlpha, targetAlpha, fadeSpeed * deltaTime);
            if (!Mathf.Approximately(nextAlpha, CurrentAlpha))
            {
                CurrentAlpha = nextAlpha;
                ApplyAlpha();
            }

            if (!Mathf.Approximately(CurrentAlpha, targetAlpha))
            {
                return false;
            }

            CurrentAlpha = targetAlpha;
            queuedForFade = false;
            return true;
        }

        internal bool TryMarkQueued()
        {
            if (queuedForFade)
            {
                return false;
            }

            queuedForFade = true;
            return true;
        }

        internal void ClearQueuedFlag()
        {
            queuedForFade = false;
        }

        private void ApplyAlpha()
        {
            if (renderers == null)
            {
                return;
            }

            for (int index = 0; index < renderers.Length; index++)
            {
                SpriteRenderer renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                Color color = renderer.color;
                color.a = originalAlphas[index] * CurrentAlpha;
                renderer.color = color;
            }
        }
    }

    /// <summary>
    /// Updates every bush through one Unity callback. Mines contains thousands of concealment objects;
    /// making each object run Update and rewrite its renderer every frame caused regular main-thread
    /// spikes. Proximity checks are spread across frames, while the handful of zones currently fading
    /// still animate every frame.
    /// </summary>
    internal sealed class ConcealmentFadeScheduler : MonoBehaviour
    {
        private const int ProximityChecksPerFrame = 256;
        private const float PlayerSearchInterval = 0.5f;

        private static ConcealmentFadeScheduler instance;

        private readonly List<ConcealmentZone> zones = new List<ConcealmentZone>(512);
        private readonly List<ConcealmentZone> fadingZones = new List<ConcealmentZone>(32);
        private TeamMember player;
        private int nextZoneIndex;
        private float nextPlayerSearchTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
        }

        internal static void Register(ConcealmentZone zone)
        {
            if (zone == null)
            {
                return;
            }

            EnsureInstance();
            if (!instance.zones.Contains(zone))
            {
                instance.zones.Add(zone);
            }
        }

        internal static void Unregister(ConcealmentZone zone)
        {
            if (instance == null || zone == null)
            {
                return;
            }

            instance.zones.Remove(zone);
            instance.fadingZones.Remove(zone);
            zone.ClearQueuedFlag();
            instance.nextZoneIndex = Mathf.Min(instance.nextZoneIndex, instance.zones.Count);
        }

        internal static void QueueFade(ConcealmentZone zone)
        {
            if (zone == null)
            {
                return;
            }

            EnsureInstance();
            if (zone.TryMarkQueued())
            {
                instance.fadingZones.Add(zone);
            }
        }

        private static void EnsureInstance()
        {
            if (instance != null)
            {
                return;
            }

            GameObject root = new GameObject("Concealment Fade Scheduler");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<ConcealmentFadeScheduler>();
        }

        private void Update()
        {
            RefreshPlayer();
            RefreshZoneTargets();
            AnimateFades();
        }

        private void RefreshPlayer()
        {
            if (player != null && player.isActiveAndEnabled && player.IsAlive)
            {
                return;
            }

            if (Time.unscaledTime < nextPlayerSearchTime)
            {
                return;
            }

            player = null;
            foreach (TeamMember member in TeamMember.ActiveMembers)
            {
                if (member != null && member.isActiveAndEnabled && member.IsPlayerControlled && member.IsAlive)
                {
                    player = member;
                    break;
                }
            }

            nextPlayerSearchTime = Time.unscaledTime + PlayerSearchInterval;
        }

        private void RefreshZoneTargets()
        {
            int checks = Mathf.Min(ProximityChecksPerFrame, zones.Count);
            while (checks-- > 0 && zones.Count > 0)
            {
                if (nextZoneIndex >= zones.Count)
                {
                    nextZoneIndex = 0;
                }

                ConcealmentZone zone = zones[nextZoneIndex++];
                if (zone != null && zone.isActiveAndEnabled)
                {
                    zone.RefreshFadeTarget(player);
                }
            }
        }

        private void AnimateFades()
        {
            for (int index = fadingZones.Count - 1; index >= 0; index--)
            {
                ConcealmentZone zone = fadingZones[index];
                if (zone == null || !zone.isActiveAndEnabled || zone.StepFade(Time.deltaTime))
                {
                    if (zone != null)
                    {
                        zone.ClearQueuedFlag();
                    }

                    fadingZones.RemoveAt(index);
                }
            }
        }

        private void OnDestroy()
        {
            if (instance != this)
            {
                return;
            }

            foreach (ConcealmentZone zone in fadingZones)
            {
                zone?.ClearQueuedFlag();
            }

            instance = null;
        }
    }
}
