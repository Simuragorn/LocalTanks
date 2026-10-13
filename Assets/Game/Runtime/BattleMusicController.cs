using UnityEngine;

namespace LocalTanks
{
    public enum BattleMusicState
    {
        Silent,
        Loading,
        Battle,
        Result
    }

    /// <summary>
    /// The music layer of one battle: a loading track, a calm and a tense battle track, and the result
    /// stinger. It owns two sources so a change is a crossfade rather than a cut, and it takes every
    /// decision from <see cref="BattleMusicIntensity"/>, which is deterministic and never waits for the
    /// local model.
    /// </summary>
    public sealed class BattleMusicController : MonoBehaviour
    {
        public const float DefaultFadeSeconds = 2.5f;

        private const string SourceAName = "BattleMusicA";
        private const string SourceBName = "BattleMusicB";

        private BattleAudioProfile profile;
        private WotAudioClipLibrary library;
        private AudioSource sourceA;
        private AudioSource sourceB;
        private AudioSource active;
        private AudioSource fading;
        private float fadingFromVolume;
        private float fade = 1f;
        private float fadeDuration = DefaultFadeSeconds;
        private readonly BattleMusicIntensity intensity = new BattleMusicIntensity();

        public BattleMusicState State { get; private set; } = BattleMusicState.Silent;
        public BattleMusicMood Mood => intensity.Mood;
        public float Intensity => intensity.Intensity;
        public string CurrentClipId { get; private set; } = string.Empty;
        public string LastTransitionReason { get; private set; } = "музыка ещё не запускалась";
        public int TransitionCount { get; private set; }
        /// <summary>
        /// True while the layer carries music. It reports intent rather than the audio backend, so a
        /// headless run — where a source may never actually sound — still describes the same state.
        /// </summary>
        public bool IsPlaying => State != BattleMusicState.Silent && active != null && active.clip != null;
        public BattleMusicIntensity IntensityModel => intensity;

        /// <summary>The source currently carrying the music; diagnostics and tests read its volume.</summary>
        public AudioSource ActiveSource => active;

        /// <summary>The music volume the profile and the player settings currently ask for.</summary>
        public float TargetVolume => profile != null
            ? profile.musicVolume * PlayerSettingsStore.MusicMultiplier
            : 0f;

        public void Configure(BattleAudioProfile audioProfile, WotAudioClipLibrary clipLibrary)
        {
            profile = audioProfile;
            library = clipLibrary ?? WotAudioClipLibrary.Shared;
            EnsureSources();
        }

        /// <summary>Starts the loading track. The arena is built while this plays.</summary>
        public void BeginLoading(float fadeSeconds = DefaultFadeSeconds)
        {
            if (profile?.music == null)
            {
                LastTransitionReason = "у арены нет музыкального профиля";
                return;
            }

            Play(profile.music.loading, BattleMusicState.Loading, fadeSeconds, "загрузка арены", true);
        }

        /// <summary>Switches to the battle layer. The calm track is the entry point of every battle.</summary>
        public void BeginBattle(float gameTime, float fadeSeconds = DefaultFadeSeconds)
        {
            intensity.Reset(gameTime);
            Play(intensity.Mood == BattleMusicMood.Intensive ? profile?.music?.intensive : profile?.music?.relaxed,
                BattleMusicState.Battle, fadeSeconds, "бой начался: спокойный слой", true);
        }

        public void RegisterEvent(BattleAudioEventKind kind, float gameTime)
        {
            if (State != BattleMusicState.Battle)
            {
                return;
            }

            intensity.Register(kind, gameTime);
        }

        /// <summary>
        /// Plays the result stinger. Victory, draw and defeat are relative to the player, so the same
        /// battle outcome is heard differently by the two sides.
        /// </summary>
        public void EnterResult(TeamId playerTeam, TeamId survivingTeam, float fadeSeconds = DefaultFadeSeconds)
        {
            if (profile?.music == null)
            {
                return;
            }

            string clipId;
            string reason;
            if (survivingTeam == TeamId.Neutral || playerTeam == TeamId.Neutral)
            {
                clipId = profile.music.draw;
                reason = "результат: ничья";
            }
            else if (survivingTeam == playerTeam)
            {
                clipId = profile.music.victory;
                reason = "результат: победа";
            }
            else
            {
                clipId = profile.music.defeat;
                reason = "результат: поражение";
            }

            Play(clipId, BattleMusicState.Result, fadeSeconds, reason, false);
        }

        /// <summary>Advances the crossfade and, during a battle, the deterministic mood decision.</summary>
        public void Tick(float gameTime, float deltaTime)
        {
            if (State == BattleMusicState.Battle && intensity.Update(gameTime))
            {
                string clipId = intensity.Mood == BattleMusicMood.Intensive
                    ? profile?.music?.intensive
                    : profile?.music?.relaxed;
                Play(clipId, BattleMusicState.Battle, DefaultFadeSeconds, intensity.LastReason, true);
            }

            AdvanceFade(deltaTime);
        }

        /// <summary>Stops everything, for example when the scene is torn down or a test ends.</summary>
        public void Stop()
        {
            if (sourceA != null) sourceA.Stop();
            if (sourceB != null) sourceB.Stop();
            active = null;
            fading = null;
            fade = 1f;
            State = BattleMusicState.Silent;
            CurrentClipId = string.Empty;
            LastTransitionReason = "музыка остановлена";
        }

        private void Play(string clipId, BattleMusicState state, float fadeSeconds, string reason, bool loop)
        {
            EnsureSources();
            State = state;
            LastTransitionReason = reason;

            if (string.IsNullOrWhiteSpace(clipId))
            {
                // A map may declare music it cannot resolve locally. The state still changes, so the
                // developer overlay explains why nothing is heard instead of pretending to play. The
                // fade is left alone: whatever is already playing must not duck for a missing clip.
                CurrentClipId = string.Empty;
                return;
            }

            AudioClip clip = library != null ? library.Load(clipId) : null;
            CurrentClipId = clipId;
            if (clip == null)
            {
                return;
            }

            fadeDuration = Mathf.Max(0.01f, fadeSeconds);
            fade = 0f;
            AudioSource incoming = active == sourceB ? sourceA : sourceB;
            if (active != null && active.isPlaying)
            {
                fading = active;
                fadingFromVolume = active.volume;
            }

            active = incoming;
            active.clip = clip;
            active.loop = loop;
            active.volume = 0f;
            active.Play();
            TransitionCount++;
        }

        private void AdvanceFade(float deltaTime)
        {
            if (active == null)
            {
                return;
            }

            if (fade < 1f)
            {
                fade = Mathf.Min(1f, fade + Mathf.Max(0f, deltaTime) / fadeDuration);
            }

            active.volume = Mathf.Clamp01(TargetVolume * fade);
            if (fading == null)
            {
                return;
            }

            fading.volume = Mathf.Clamp01(fadingFromVolume * (1f - fade));
            if (fade >= 1f)
            {
                fading.Stop();
                fading = null;
            }
        }

        private void EnsureSources()
        {
            if (sourceA == null) sourceA = EnsureSource(SourceAName);
            if (sourceB == null) sourceB = EnsureSource(SourceBName);
            if (active == null) active = sourceA;
        }

        private AudioSource EnsureSource(string sourceName)
        {
            Transform existing = transform.Find(sourceName);
            if (existing != null)
            {
                AudioSource found = existing.GetComponent<AudioSource>();
                if (found != null)
                {
                    return found;
                }
            }

            GameObject child = new GameObject(sourceName);
            child.transform.SetParent(transform, false);
            AudioSource source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            return source;
        }

        private void Update()
        {
            Tick(Time.time, Time.unscaledDeltaTime);
        }
    }
}
