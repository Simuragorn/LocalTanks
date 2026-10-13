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
                { ""clipId"": ""crew_ricochet_ru"", ""relativePath"": ""Clips/Universal/CrewVoice/Russian/crew_ricochet_ru.wav"" },
                { ""clipId"": ""crew_not_penetrated_ru"", ""relativePath"": ""Clips/Universal/CrewVoice/Russian/crew_not_penetrated_ru.wav"" },
                { ""clipId"": ""crew_penetrated_ru"", ""relativePath"": ""Clips/Universal/CrewVoice/Russian/crew_penetrated_ru.wav"" },
                { ""clipId"": ""crew_enemy_destroyed_ru"", ""relativePath"": ""Clips/Universal/CrewVoice/Russian/crew_enemy_destroyed_ru.wav"" },
                { ""clipId"": ""crew_enemy_destroyed_by_ally_ru"", ""relativePath"": ""Clips/Universal/CrewVoice/Russian/crew_enemy_destroyed_by_ally_ru.wav"" },
                { ""clipId"": ""crew_ally_destroyed_ru"", ""relativePath"": ""Clips/Universal/CrewVoice/Russian/crew_ally_destroyed_ru.wav"" },
                { ""clipId"": ""crew_vehicle_destroyed_ru"", ""relativePath"": ""Clips/Universal/CrewVoice/Russian/crew_vehicle_destroyed_ru.wav"" },
                { ""clipId"": ""crew_base_lost_ru"", ""relativePath"": ""Clips/Universal/CrewVoice/Russian/crew_base_lost_ru.wav"" },
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

                flow.StartAmbience();
                yield return null;

                Assert.That(flow.IsAmbiencePlaying, Is.True, "The map ambience starts with the battle.");
                Assert.That(flow.AmbienceClipId, Is.EqualTo("mines_ambient_wind_summer"));

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

        [UnityTest]
        public IEnumerator CrewVoice_FollowsThePlayersOwnBattleInTheNationsLanguage()
        {
            GameObject host = new GameObject("CrewVoiceHost");
            List<AudioClip> clips = new List<AudioClip>();
            List<Object> spawned = new List<Object>();
            try
            {
                host.AddComponent<AudioListener>();
                CrewVoiceController crew = host.AddComponent<CrewVoiceController>();
                CrewVoiceProfileCatalog catalog = CrewVoiceProfileCatalog.Load();
                Assert.That(catalog.roles.Length, Is.EqualTo(10),
                    "The committed crew voice catalog must declare every role.");

                // The player drives a German machine, which still speaks Russian because that is the only
                // extracted set: this is exactly the scaffolding the project asked for.
                TeamMember player = BuildMember("PlayerTank", TeamId.TeamA, TankNation.Germany, spawned);
                TeamMember enemy = BuildMember("EnemyTank", TeamId.TeamB, TankNation.USSR, spawned);
                TeamMember ally = BuildMember("AllyTank", TeamId.TeamA, TankNation.USSR, spawned);
                crew.Configure(player, BuildLibrary(clips), catalog, new[] { player, enemy, ally });

                Assert.That(crew.IsActive, Is.True);
                Assert.That(crew.Language, Is.EqualTo("ru"));
                Assert.That(crew.Volume, Is.EqualTo(catalog.volume * PlayerSettingsStore.VoiceMultiplier).Within(0.001f));

                crew.PlayBattleStart();
                Assert.That(crew.LastClipId, Is.EqualTo("battle_start_ru"));

                TankArmor enemyArmor = enemy.GetComponent<TankArmor>();
                crew.ReportShotResult(enemyArmor, ImpactOutcome.Blocked, false);
                Assert.That(crew.LastClipId, Is.EqualTo("crew_not_penetrated_ru"));
                crew.ReportShotResult(enemyArmor, ImpactOutcome.Ricocheted, false);
                Assert.That(crew.LastClipId, Is.EqualTo("crew_ricochet_ru"));
                crew.ReportShotResult(enemyArmor, ImpactOutcome.Penetrated, false);
                Assert.That(crew.LastClipId, Is.EqualTo("crew_penetrated_ru"));

                // Hitting a friendly machine is not a line of its own; destroying one is.
                TankArmor allyArmor = ally.GetComponent<TankArmor>();
                string afterHit = crew.LastClipId;
                crew.ReportShotResult(allyArmor, ImpactOutcome.Penetrated, false);
                Assert.That(crew.LastClipId, Is.EqualTo(afterHit));
                crew.ReportShotResult(allyArmor, ImpactOutcome.Penetrated, true);
                Assert.That(crew.LastClipId, Is.EqualTo("crew_ally_destroyed_ru"));

                // The player's own kill: the victim reports itself first inside ResolveImpact, so the
                // generic line has to yield to the player's own line for the same machine.
                crew.MarkIncomingHit(enemyArmor);
                enemy.GetComponent<TankHealth>().ApplyDamage(100000);
                Assert.That(crew.SuppressedDuplicates, Is.EqualTo(1));
                crew.ReportShotResult(enemyArmor, ImpactOutcome.Penetrated, true);
                Assert.That(crew.LastClipId, Is.EqualTo("crew_enemy_destroyed_ru"));

                // An enemy destroyed by somebody else still reaches the player's crew.
                TeamMember other = BuildMember("OtherEnemy", TeamId.TeamB, TankNation.USSR, spawned);
                crew.Configure(player, BuildLibrary(clips), catalog, new[] { player, other });
                other.GetComponent<TankHealth>().ApplyDamage(100000);
                Assert.That(crew.LastClipId, Is.EqualTo("crew_enemy_destroyed_by_ally_ru"));

                crew.ReportBaseCaptured(TeamId.TeamB, TeamId.TeamA);
                Assert.That(crew.LastClipId, Is.EqualTo("base_captured_ru"));
                crew.ReportBaseCaptured(TeamId.TeamA, TeamId.TeamB);
                Assert.That(crew.LastClipId, Is.EqualTo("crew_base_lost_ru"));

                player.GetComponent<TankHealth>().ApplyDamage(100000);
                Assert.That(crew.LastClipId, Is.EqualTo("crew_vehicle_destroyed_ru"));
            }
            finally
            {
                Object.Destroy(host);
                foreach (Object spawnedObject in spawned)
                {
                    Object.Destroy(spawnedObject);
                }

                foreach (AudioClip clip in clips)
                {
                    Object.Destroy(clip);
                }
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator CrewVoice_AsksForTheLanguageTheNationPointsAt()
        {
            GameObject host = new GameObject("CrewVoiceLanguageHost");
            List<AudioClip> clips = new List<AudioClip>();
            List<Object> spawned = new List<Object>();
            try
            {
                host.AddComponent<AudioListener>();
                CrewVoiceController crew = host.AddComponent<CrewVoiceController>();
                CrewVoiceProfileCatalog catalog = CrewVoiceProfileCatalog.Load();
                TeamMember player = BuildMember("GermanTank", TeamId.TeamA, TankNation.Germany, spawned);

                // Flip the data only: the German machine now asks for German clips. They are not extracted
                // yet, so the line stays silent, but the request itself is already language-correct.
                CrewVoiceProfileCatalog german = new CrewVoiceProfileCatalog
                {
                    defaultLanguage = catalog.defaultLanguage,
                    volume = catalog.volume,
                    languages = catalog.languages,
                    roles = catalog.roles,
                    nations = new[]
                    {
                        new CrewVoiceNationLanguage { nation = "Germany", language = "de" },
                        new CrewVoiceNationLanguage { nation = "USSR", language = "ru" },
                        new CrewVoiceNationLanguage { nation = "USA", language = "ru" }
                    }
                };

                crew.Configure(player, BuildLibrary(clips), german, new[] { player });
                Assert.That(crew.Language, Is.EqualTo("de"));
                Assert.That(crew.Play(CrewVoiceRole.Ricochet), Is.False,
                    "A language whose clips are missing must stay silent rather than block the battle.");
                Assert.That(crew.LastClipId, Is.EqualTo("crew_ricochet_de"));
            }
            finally
            {
                Object.Destroy(host);
                foreach (Object spawnedObject in spawned)
                {
                    Object.Destroy(spawnedObject);
                }

                foreach (AudioClip clip in clips)
                {
                    Object.Destroy(clip);
                }
            }

            yield return null;
        }

        private static TeamMember BuildMember(string name, TeamId team, TankNation nation, List<Object> spawned)
        {
            GameObject tank = new GameObject(name);
            TankDefinition definition = ScriptableObject.CreateInstance<TankDefinition>();
            definition.nation = nation;
            definition.maxHitPoints = 100;
            TankHealth health = tank.AddComponent<TankHealth>();
            health.Configure(definition);
            TeamMember member = tank.AddComponent<TeamMember>();
            member.Configure(team, false);
            tank.AddComponent<TankArmor>();
            spawned.Add(tank);
            spawned.Add(definition);
            return member;
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
                captureProgressClipId = "base_capture_progress_loop",
                battleEndByLastKillClipId = "battle_end_by_last_kill",
                musicVolume = 0.45f,
                ambienceVolume = 0.3f,
                signalVolume = 0.6f
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
                         "crew_ricochet_ru", "crew_not_penetrated_ru", "crew_penetrated_ru",
                         "crew_enemy_destroyed_ru", "crew_enemy_destroyed_by_ally_ru", "crew_ally_destroyed_ru",
                         "crew_vehicle_destroyed_ru", "crew_base_lost_ru",
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
