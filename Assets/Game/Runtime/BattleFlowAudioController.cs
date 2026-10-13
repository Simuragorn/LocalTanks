using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
        /// <summary>
        /// Battle flow audio that is not music and not crew voice: the capture loop, its completion
        /// signal and the end-of-battle stinger. Crew lines belong to <see cref="CrewVoiceController"/>,
        /// because the language of a line depends on the nation of the machine that hears it.
        /// </summary>
        /// <remarks>
        /// Every trigger is an event the battle already raises, so this controller adds no new game rule.
        /// The capture loop deserves a note: in World of Tanks `base_capture_1` starts the loop and
        /// `base_capture_2` stops it, and the latter is a control event with digital silence rather than a
        /// clip. The runtime therefore stops its own source instead of waiting for a stop clip.
        /// </remarks>
        public sealed class BattleFlowAudioController : MonoBehaviour
    {
        private readonly HashSet<string> capturingBases = new HashSet<string>();

        private BattleAudioProfile profile;
        private WotAudioClipLibrary library;
        private AudioSource signalSource;
        private AudioSource captureSource;
        private AudioSource ambienceSource;
        private bool captureLoopActive;
        private bool ambienceActive;

        public string LastClipId { get; private set; } = string.Empty;
        public string LastReason { get; private set; } = "сигналов ещё не было";
        public int SignalPlayCount { get; private set; }
        public int CaptureLoopStartCount { get; private set; }
        public int CaptureLoopStopCount { get; private set; }

        /// <summary>
        /// The capture loop is running for the battle. Like the music state this reports intent, not the
        /// audio backend, so a headless run and a played battle agree on what is happening.
        /// </summary>
        public bool IsCaptureLoopPlaying => captureLoopActive;

        public bool IsAmbiencePlaying => ambienceActive;
        public string AmbienceClipId { get; private set; } = string.Empty;
        public int CapturingBaseCount => capturingBases.Count;

        /// <summary>Battle signals share the `Voice` channel with crew lines, so both follow one slider.</summary>
        public float SignalVolume => profile != null
            ? profile.signalVolume * PlayerSettingsStore.VoiceMultiplier
            : 0f;

        /// <summary>The map ambience belongs to the music bus: it is atmosphere, not a battle signal.</summary>
        public float AmbienceVolume => profile != null
            ? profile.ambienceVolume * PlayerSettingsStore.MusicMultiplier
            : 0f;

        public void Configure(BattleAudioProfile audioProfile, WotAudioClipLibrary clipLibrary)
        {
            profile = audioProfile;
            library = clipLibrary ?? WotAudioClipLibrary.Shared;
            signalSource ??= CreateSource("BattleSignal", false);
            captureSource ??= CreateSource("BattleCaptureLoop", true);
            ambienceSource ??= CreateSource("BattleAmbience", true);
        }

        /// <summary>Starts the map ambience loop, once per battle.</summary>
        public void StartAmbience()
        {
            if (ambienceActive || ambienceSource == null || string.IsNullOrWhiteSpace(profile?.ambienceClipId))
            {
                return;
            }

            AudioClip clip = Resolve(profile.ambienceClipId);
            if (clip == null)
            {
                return;
            }

            ambienceSource.clip = clip;
            ambienceSource.volume = AmbienceVolume;
            ambienceSource.Play();
            ambienceActive = true;
            AmbienceClipId = profile.ambienceClipId;
        }

        /// <summary>
        /// Reports the capture state of one base. The loop runs while at least one base is being captured
        /// and stops as soon as none is: contested and owned bases both clear their own contribution.
        /// </summary>
        public void ApplyCaptureState(string baseId, BaseCaptureState state)
        {
            if (string.IsNullOrWhiteSpace(baseId))
            {
                return;
            }

            if (state == BaseCaptureState.Capturing)
            {
                capturingBases.Add(baseId);
            }
            else
            {
                capturingBases.Remove(baseId);
            }

            SyncCaptureLoop();
        }

        /// <summary>Played when a battle ends. The signal differs for a capture and for the last kill.</summary>
        public void PlayBattleEnd(bool byBaseCapture)
        {
            PlaySignal(byBaseCapture ? profile?.battleEndByCaptureClipId : profile?.battleEndByLastKillClipId,
                byBaseCapture ? "бой окончен захватом базы" : "бой окончен уничтожением");
        }

        public void StopAll()
        {
            if (signalSource != null) signalSource.Stop();
            if (captureSource != null) captureSource.Stop();
            if (ambienceSource != null) ambienceSource.Stop();
            capturingBases.Clear();
            captureLoopActive = false;
            ambienceActive = false;
        }

        private void SyncCaptureLoop()
        {
            if (captureSource == null)
            {
                return;
            }

            if (capturingBases.Count == 0)
            {
                if (captureLoopActive)
                {
                    captureSource.Stop();
                    captureLoopActive = false;
                    CaptureLoopStopCount++;
                    LastReason = "захват прекращён";
                }

                return;
            }

            if (captureLoopActive)
            {
                return;
            }

            AudioClip clip = Resolve(profile?.captureProgressClipId);
            if (clip == null)
            {
                return;
            }

            captureSource.clip = clip;
            captureSource.volume = SignalVolume;
            captureSource.Play();
            captureLoopActive = true;
            CaptureLoopStartCount++;
            LastClipId = profile.captureProgressClipId;
            LastReason = "идёт захват базы";
        }

        private void PlaySignal(string clipId, string reason)
        {
            if (signalSource == null || !PlayOneShot(signalSource, clipId, SignalVolume))
            {
                return;
            }

            SignalPlayCount++;
            LastClipId = clipId;
            LastReason = reason;
        }

        private bool PlayOneShot(AudioSource source, string clipId, float volume)
        {
            AudioClip clip = Resolve(clipId);
            if (clip == null)
            {
                return false;
            }

            source.PlayOneShot(clip, Mathf.Clamp01(volume));
            return true;
        }

        private AudioClip Resolve(string clipId)
        {
            return library != null ? library.Load(clipId) : null;
        }

        private AudioSource CreateSource(string sourceName, bool loop)
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
            source.loop = loop;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            return source;
        }

        private void Update()
        {
            // The sliders must reach the sounds that are already playing, exactly like the tank and
            // effect channels: a preference that only applies after a restart is not a setting.
            if (captureLoopActive && captureSource != null)
            {
                captureSource.volume = SignalVolume;
            }

            if (ambienceActive && ambienceSource != null)
            {
                ambienceSource.volume = AmbienceVolume;
            }
        }
    }
}
