using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LocalTanks.Tests
{
    /// <summary>
    /// The controllers are driven with their own clips, so the test proves the wiring rather than the
    /// presence of the local WoT files: which clip plays in which state, when the capture loop starts and
    /// stops, and that the two new volume channels reach sounds that are already playing.
    /// </summary>
    public sealed class BattleAudioPlayModeTests
    {
        private const string ManifestJson = @"{
            ""schemaVersion"": 2,
            ""clips"": [
                { ""clipId"": ""mines_music_loading"", ""relativePath"": ""Clips/Mines/Music/mines_music_loading.wav"" },
                { ""clipId"": ""mines_music_battle_relaxed"", ""relativePath"": ""Clips/Mines/Music/mines_music_battle_relaxed.wav"" },
                { ""clipId"": ""mines_music_battle_intensive"", ""relativePath"": ""Clips/Mines/Music/mines_music_battle_intensive.wav"" },
                { ""clipId"": ""mines_music_result_victory"", ""relativePath"": ""Clips/Mines/Music/mines_music_result_victory.wav"" },
                { ""clipId"": ""mines_music_result_draw"", ""relativePath"": ""Clips/Mines/Music/mines_music_result_draw.wav"" },
                { ""clipId"": ""mines_music_result_defeat"", ""relativePath"": ""Clips/Mines/Music/mines_music_result_defeat.wav"" },
                { ""clipId"": ""mines_ambient_wind_summer"", ""relativePath"": ""Clips/Mines/Ambience/mines_ambient_wind_summer.wav"" },
                { ""clipId"": ""battle_start_ru"", ""relativePath"": ""Clips/Universal/BattleFlow/Russian/battle_start_ru.wav"" },
                { ""clipId"": ""base_captured_ru"", ""relativePath"": ""Clips/Universal/BaseCapture/Russian/base_captured_ru.wav"" },
                { ""clipId"": ""base_capture_progress_loop"", ""relativePath"": ""Clips/Universal/BaseCapture/base_capture_progress_loop.wav"" },
                { ""clipId"": ""battle_end_by_last_kill"", ""relativePath"": ""Clips/Universal/BattleFlow/battle_end_by_last_kill.wav"" }
            ]
        }";

        private float previousMaster;
        private float previousMusic;
        private float previousVoice;

        [SetUp]
        public void CaptureVolumes()
        {
            previousMaster = PlayerSettingsStore.MasterVolume;
            previousMusic = PlayerSettingsStore.MusicVolume;
            previousVoice = PlayerSettingsStore.VoiceVolume;
            PlayerSettingsStore.MasterVolume = 1f;
            PlayerSettingsStore.MusicVolume = 1f;
            PlayerSettingsStore.VoiceVolume = 1f;
        }

        [TearDown]
        public void RestoreVolumes()
        {
            PlayerSettingsStore.MasterVolume = previousMaster;
            PlayerSettingsStore.MusicVolume = previousMusic;
            PlayerSettingsStore.VoiceVolume = previousVoice;
        }

        [UnityTest]
        public IEnumerator Music_FollowsTheBattleAndCrossfadesBetweenMoods()
        {
            GameObject host = new GameObject("BattleMusicHost");
            List<AudioClip> clips = new List<AudioClip>();
            try
            {
                // A real battle scene has a listener on the main camera; without one Unity complains for
                // every source that actually starts.
                host.AddComponent<AudioListener>();
                BattleMusicController music = host.AddComponent<BattleMusicController>();
                music.Configure(BuildProfile(), BuildLibrary(clips));
                yield return null;

                music.BeginBattle(0f);
                yield return null;

                Assert.That(music.State, Is.EqualTo(BattleMusicState.Battle));
                Assert.That(music.CurrentClipId, Is.EqualTo("mines_music_battle_relaxed"));
                Assert.That(music.IsPlaying, Is.True);
                Assert.That(music.ActiveSource.loop, Is.True, "Battle music must loop until the mood changes.");

                // A heavy exchange inside the minimum mood time must not flip the layer yet.
                music.RegisterEvent(BattleAudioEventKind.Destroyed, 1f);
                music.RegisterEvent(BattleAudioEventKind.Destroyed, 2f);
                music.Tick(2f, 3f);
                Assert.That(music.Mood, Is.EqualTo(BattleMusicMood.Relaxed));

                music.RegisterEvent(BattleAudioEventKind.Destroyed, 8f);
                music.RegisterEvent(BattleAudioEventKind.Destroyed, 8f);
                music.Tick(9f, 0.5f);

                Assert.That(music.Mood, Is.EqualTo(BattleMusicMood.Intensive));
                Assert.That(music.CurrentClipId, Is.EqualTo("mines_music_battle_intensive"));
                Assert.That(music.TransitionCount, Is.EqualTo(2));

                music.EnterResult(TeamId.TeamA, TeamId.TeamA);
                yield return null;
                Assert.That(music.State, Is.EqualTo(BattleMusicState.Result));
                Assert.That(music.CurrentClipId, Is.EqualTo("mines_music_result_victory"));
                Assert.That(music.ActiveSource.loop, Is.False, "A result stinger must not loop forever.");
            }
            finally
            {
                Object.Destroy(host);
                foreach (AudioClip clip in clips)
                {
                    Object.Destroy(clip);
                }
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator MusicVolume_ReachesThePlayingSource()
        {
            GameObject host = new GameObject("BattleMusicVolumeHost");
            List<AudioClip> clips = new List<AudioClip>();
            try
            {
                host.AddComponent<AudioListener>();
                BattleMusicController music = host.AddComponent<BattleMusicController>();
                BattleAudioProfile profile = BuildProfile();
                music.Configure(profile, BuildLibrary(clips));
                music.BeginBattle(0f);
                yield return null;

                music.Tick(1f, 5f);
                float expected = profile.musicVolume * PlayerSettingsStore.MusicMultiplier;
                Assert.That(music.ActiveSource.volume, Is.EqualTo(expected).Within(0.001f));

                PlayerSettingsStore.MusicVolume = 0.25f;
                music.Tick(2f, 0.1f);

                Assert.That(music.ActiveSource.volume,
                    Is.EqualTo(profile.musicVolume * 0.25f).Within(0.001f),
                    "The music slider must change a track that is already playing.");

                PlayerSettingsStore.MusicVolume = 0f;
                music.Tick(3f, 0.1f);
                Assert.That(music.ActiveSource.volume, Is.Zero);
            }
            finally
            {
                Object.Destroy(host);
                foreach (AudioClip clip in clips)
                {
                    Object.Destroy(clip);
                }
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Flow_StartsAmbienceAndRunsTheCaptureLoopOnlyWhileCapturing()
        {
            GameObject host = new GameObject("BattleFlowHost");
            List<AudioClip> clips = new List<AudioClip>();
            try
            {
                host.AddComponent<AudioListener>();
                BattleFlowAudioController flow = host.AddComponent<BattleFlowAudioController>();
                flow.Configure(BuildProfile(), BuildLibrary(clips));

                flow.PlayBattleStart();
                yield return null;

                Assert.That(flow.IsAmbiencePlaying, Is.True, "The map ambience starts with the battle.");
                Assert.That(flow.AmbienceClipId, Is.EqualTo("mines_ambient_wind_summer"));
                Assert.That(flow.VoicePlayCount, Is.EqualTo(1));
                Assert.That(flow.LastClipId, Is.EqualTo("battle_start_ru"));

                flow.ApplyCaptureState("base_b", BaseCaptureState.Capturing);
                yield return null;
                Assert.That(flow.IsCaptureLoopPlaying, Is.True);
                Assert.That(flow.CaptureLoopStartCount, Is.EqualTo(1));

                flow.ApplyCaptureState("base_b", BaseCaptureState.Contested);
                yield return null;
                Assert.That(flow.IsCaptureLoopPlaying, Is.False, "A contested base stops the capture signal.");
                Assert.That(flow.CaptureLoopStopCount, Is.EqualTo(1));

                flow.ApplyCaptureState("base_b", BaseCaptureState.Capturing);
                flow.ApplyCaptureState("base_b", BaseCaptureState.Owned);
                yield return null;
                Assert.That(flow.IsCaptureLoopPlaying, Is.False,
                    "Taking the base must stop the loop even when the state jumps straight to owned.");
                Assert.That(flow.CaptureLoopStartCount, Is.EqualTo(2));
                Assert.That(flow.CaptureLoopStopCount, Is.EqualTo(2));

                flow.PlayBaseCaptured();
                Assert.That(flow.VoicePlayCount, Is.EqualTo(2));
                Assert.That(flow.LastClipId, Is.EqualTo("base_captured_ru"));

                flow.PlayBattleEnd(false);
                Assert.That(flow.SignalPlayCount, Is.EqualTo(1));
                Assert.That(flow.LastClipId, Is.EqualTo("battle_end_by_last_kill"));

                PlayerSettingsStore.VoiceVolume = 0.5f;
                flow.PlayBattleEnd(false);
                Assert.That(flow.SignalVolume, Is.EqualTo(BuildProfile().signalVolume * 0.5f).Within(0.001f));
            }
            finally
            {
                Object.Destroy(host);
                foreach (AudioClip clip in clips)
                {
                    Object.Destroy(clip);
                }
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator DirectorFollowsTheWholeBattle()
        {
            GameObject host = new GameObject("BattleAudioDirectorHost");
            List<AudioClip> clips = new List<AudioClip>();
            try
            {
                BattleAudioDirector audio = host.AddComponent<BattleAudioDirector>();
                audio.Configure(null, false);

                Assert.That(audio.HasProfile, Is.False, "Without a map there is no profile and no sound.");
                Assert.That(audio.Status, Does.Contain("нет профиля звука"));

                // Registering combat events while no battle is running must not throw or accumulate.
                audio.RegisterEvent(BattleAudioEventKind.Shot);
                audio.RegisterEvent(BattleAudioEventKind.Destroyed);
                audio.StopAll();
                Assert.That(audio.Music, Is.Null);
            }
            finally
            {
                Object.Destroy(host);
                foreach (AudioClip clip in clips)
                {
                    Object.Destroy(clip);
                }
            }

            yield return null;
        }

        private static BattleAudioProfile BuildProfile()
        {
            return new BattleAudioProfile
            {
                mapId = "10_hills_tactical",
                displayName = "Рудники",
                music = new BattleMusicClipIds
                {
                    loading = "mines_music_loading",
                    relaxed = "mines_music_battle_relaxed",
                    intensive = "mines_music_battle_intensive",
                    victory = "mines_music_result_victory",
                    draw = "mines_music_result_draw",
                    defeat = "mines_music_result_defeat"
                },
                ambienceClipId = "mines_ambient_wind_summer",
                battleStartClipId = "battle_start_ru",
                captureProgressClipId = "base_capture_progress_loop",
                baseCapturedClipId = "base_captured_ru",
                battleEndByLastKillClipId = "battle_end_by_last_kill",
                musicVolume = 0.45f,
                ambienceVolume = 0.3f,
                signalVolume = 0.6f,
                voiceVolume = 0.85f
            };
        }

        private static WotAudioClipLibrary BuildLibrary(List<AudioClip> created)
        {
            Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
            foreach (string clipId in new[]
                     {
                         "mines_music_loading", "mines_music_battle_relaxed", "mines_music_battle_intensive",
                         "mines_music_result_victory", "mines_music_result_draw", "mines_music_result_defeat",
                         "mines_ambient_wind_summer", "battle_start_ru", "base_captured_ru",
                         "base_capture_progress_loop", "battle_end_by_last_kill"
                     })
            {
                AudioClip clip = AudioClip.Create(clipId, 4800, 1, 48000, false);
                clips[clipId] = clip;
                created.Add(clip);
            }

            return new WotAudioClipLibrary(
                ManifestJson,
                path => clips.TryGetValue(path.Substring(path.LastIndexOf('/') + 1), out AudioClip clip) ? clip : null,
                false,
                _ => { });
        }
    }
}
