using System.IO;
using System.Linq;
using LocalTanks.Editor;
using NUnit.Framework;

namespace LocalTanks.Tests
{
    /// <summary>
    /// The committed arena audio profile is data, and data goes wrong quietly: a clip ID that the local
    /// pipeline never declared, a map key that no arena reports, a volume outside the slider range. These
    /// tests read the real file, so the failure appears here instead of as a silent battle.
    /// </summary>
    public sealed class BattleAudioProfileTests
    {
        private const string MinesArenaId = "Battle_Mines";

        [Test]
        public void CommittedProfiles_AreValid()
        {
            string[] errors = BattleAudioProfileValidator.CollectErrors();

            Assert.That(errors, Is.Empty,
                "The committed battle audio profiles must name declared clips and known arenas:\n- " +
                string.Join("\n- ", errors));
        }

        [Test]
        public void Mines_DeclaresTheWholeBattleLayer()
        {
            BattleAudioProfile profile = BattleAudioProfileValidator.LoadSource().Find(MinesArenaId);

            Assert.That(profile, Is.Not.Null, "The only arena with extracted music must have a profile.");
            Assert.That(profile.music.loading, Is.Not.Empty);
            Assert.That(profile.music.relaxed, Is.Not.Empty);
            Assert.That(profile.music.intensive, Is.Not.Empty);
            Assert.That(profile.music.victory, Is.Not.Empty);
            Assert.That(profile.music.draw, Is.Not.Empty);
            Assert.That(profile.music.defeat, Is.Not.Empty);
            Assert.That(profile.ambienceClipId, Is.Not.Empty);
            Assert.That(profile.captureProgressClipId, Is.Not.Empty);
            Assert.That(profile.battleEndByLastKillClipId, Is.Not.Empty);
            Assert.That(profile.battleEndByCaptureClipId, Is.Not.Empty);
        }

        [Test]
        public void EveryArenaHasGlobalVictoryDrawAndDefeatSignals()
        {
            BattleAudioProfileCatalog catalog = BattleAudioProfileValidator.LoadSource();
            Assert.That(catalog.outcomeSignals.victory, Is.EqualTo("mines_music_result_victory"));
            Assert.That(catalog.outcomeSignals.draw, Is.EqualTo("mines_music_result_draw"));
            Assert.That(catalog.outcomeSignals.defeat, Is.EqualTo("mines_music_result_defeat"));
            Assert.That(BattleAudioProfileValidator.KnownArenaIds(), Does.Contain("Battle_RiverCrossing"),
                "River Crossing has no music profile but must still receive the global outcome signals.");
        }

        [Test]
        public void CrewVoice_CoversEveryRoleAndNation()
        {
            CrewVoiceProfileCatalog crew = BattleAudioProfileValidator.LoadCrewVoiceSource();

            Assert.That(crew.Validate(), Is.Empty);
            foreach (string role in System.Enum.GetNames(typeof(CrewVoiceRole)))
            {
                Assert.That(crew.PatternFor((CrewVoiceRole)System.Enum.Parse(typeof(CrewVoiceRole), role)),
                    Is.Not.Empty, $"Role '{role}' must have a clip ID pattern.");
            }

            // Adding a nation to the game must force a decision about its crew voice instead of silently
            // falling back to whatever language happens to be default.
            foreach (string nation in System.Enum.GetNames(typeof(TankNation)))
            {
                TankNation parsed = (TankNation)System.Enum.Parse(typeof(TankNation), nation);
                Assert.That(crew.LanguageFor(parsed), Is.Not.Empty,
                    $"Nation '{nation}' must map to a voice language.");
                Assert.That(crew.VoiceFor(parsed), Is.Not.Empty,
                    $"Nation '{nation}' must map to a voice.");
            }

            // Pacing is data too: an automatic gun fires several shells a second, and a crew that repeats
            // one phrase for each of them says nothing useful.
            Assert.That(crew.repeatCooldownSeconds, Is.GreaterThan(0f),
                "The same phrase must not be repeated immediately.");
            Assert.That(crew.speechCooldownSeconds, Is.GreaterThan(0f),
                "The crew must not talk over itself.");
        }

        [Test]
        public void CrewVoice_SpeaksTheLanguageOfEveryExtractedNation()
        {
            CrewVoiceProfileCatalog crew = BattleAudioProfileValidator.LoadCrewVoiceSource();

            // Five national sets are extracted, and each tank nation asks for its own; the British and
            // French sets are declared and extracted ahead of the nations that will need them.
            Assert.That(crew.LanguageFor(TankNation.USSR), Is.EqualTo("ru"));
            Assert.That(crew.LanguageFor(TankNation.Germany), Is.EqualTo("de"));
            Assert.That(crew.LanguageFor(TankNation.USA), Is.EqualTo("en_us"));
            foreach (string language in new[] { "ru", "de", "en_gb", "fr", "en_us" })
            {
                Assert.That(crew.TryGetLanguage(language, out CrewVoiceLanguage entry), Is.True,
                    $"Language '{language}' must be declared in the catalog.");
                Assert.That(entry.bankEntry, Is.Not.Empty);
            }

            Assert.That(crew.ReferencedClipIds(), Does.Contain("crew_not_penetrated_de_male"));
            Assert.That(crew.ReferencedClipIds(), Does.Contain("crew_vehicle_destroyed_en_us_male"));
            Assert.That(crew.ReferencedClipIds(), Does.Contain("battle_start_ru_male"),
                "Battle start and base capture are crew lines, so they resolve through the same nation map.");
            Assert.That(crew.ReferencedClipIds(), Does.Contain("base_captured_ru_male"));
        }

        [Test]
        public void CrewVoice_ResolvesTheLanguageAndVoiceSuffixFromTheNation()
        {
            CrewVoiceProfileCatalog crew = BattleAudioProfileValidator.LoadCrewVoiceSource();

            Assert.That(crew.ClipIdFor(CrewVoiceRole.Ricochet, TankNation.Germany),
                Is.EqualTo("crew_ricochet_de_male"));

            // Point a nation at another language and voice and the very same role asks for a different
            // recording; nothing else in the game changes, which is the whole point of the scaffolding.
            CrewVoiceProfileCatalog french = new CrewVoiceProfileCatalog
            {
                defaultLanguage = "ru",
                defaultVoice = "male",
                languages = crew.languages,
                roles = crew.roles,
                nations = new[]
                {
                    new CrewVoiceNationBinding { nation = "Germany", language = "fr", voice = "female" },
                    new CrewVoiceNationBinding { nation = "USSR", language = "ru", voice = "male" },
                    new CrewVoiceNationBinding { nation = "USA", language = "en_us", voice = "male" }
                }
            };

            Assert.That(french.ClipIdFor(CrewVoiceRole.Ricochet, TankNation.Germany),
                Is.EqualTo("crew_ricochet_fr_female"));
            Assert.That(french.ClipIdFor(CrewVoiceRole.Ricochet, TankNation.USSR),
                Is.EqualTo("crew_ricochet_ru_male"));
            Assert.That(french.ReferencedClipIds(), Does.Contain("crew_ricochet_fr_female"));
        }

        [Test]
        public void CrewVoice_RejectsAnUnknownNationOrLanguage()
        {
            CrewVoiceProfileCatalog crew = new CrewVoiceProfileCatalog
            {
                defaultLanguage = "ru",
                defaultVoice = "male",
                languages = new[] { new CrewVoiceLanguage { id = "ru", wwiseLanguageId = "1", bankEntry = "ussr/voiceover.bnk" } },
                roles = new[] { new CrewVoiceRoleBinding { role = "Ricochet", clipIdPattern = "crew_ricochet_{lang}_{voice}" } },
                nations = new[]
                {
                    new CrewVoiceNationBinding { nation = "Germany", language = "ru", voice = "male" },
                    new CrewVoiceNationBinding { nation = "USSR", language = "ru", voice = "male" },
                    new CrewVoiceNationBinding { nation = "USA", language = "fr", voice = "male" }
                }
            };

            string[] errors = crew.Validate();

            Assert.That(errors.Any(error => error.Contains("undeclared language 'fr'")), Is.True);
            Assert.That(errors.Any(error => error.Contains("role 'BaseLost' has no clip ID pattern")), Is.True,
                "Every role must be declared, or a battle would silently lose a line.");
        }

        [Test]
        public void CrewVoice_RejectsAPatternWithoutTheLanguageOrVoicePlaceholder()
        {
            CrewVoiceProfileCatalog noLanguage = new CrewVoiceProfileCatalog
            {
                defaultLanguage = "ru",
                defaultVoice = "male",
                languages = new[] { new CrewVoiceLanguage { id = "ru", wwiseLanguageId = "1", bankEntry = "ussr/voiceover.bnk" } },
                roles = new[] { new CrewVoiceRoleBinding { role = "Ricochet", clipIdPattern = "crew_ricochet_ru_{voice}" } },
                nations = new[]
                {
                    new CrewVoiceNationBinding { nation = "Germany", language = "ru", voice = "male" },
                    new CrewVoiceNationBinding { nation = "USSR", language = "ru", voice = "male" },
                    new CrewVoiceNationBinding { nation = "USA", language = "ru", voice = "male" }
                }
            };

            Assert.That(noLanguage.Validate().Any(error => error.Contains("{lang}")), Is.True,
                "A hardcoded language in the pattern would defeat the whole scaffold.");

            CrewVoiceProfileCatalog noVoice = new CrewVoiceProfileCatalog
            {
                defaultLanguage = "ru",
                defaultVoice = "male",
                languages = new[] { new CrewVoiceLanguage { id = "ru", wwiseLanguageId = "1", bankEntry = "ussr/voiceover.bnk" } },
                roles = new[] { new CrewVoiceRoleBinding { role = "Ricochet", clipIdPattern = "crew_ricochet_{lang}_male" } },
                nations = new[]
                {
                    new CrewVoiceNationBinding { nation = "Germany", language = "ru", voice = "male" },
                    new CrewVoiceNationBinding { nation = "USSR", language = "ru", voice = "male" },
                    new CrewVoiceNationBinding { nation = "USA", language = "ru", voice = "male" }
                }
            };

            Assert.That(noVoice.Validate().Any(error => error.Contains("{voice}")), Is.True,
                "A hardcoded voice would make the extracted female set unreachable.");
        }

        [Test]
        public void EveryReferencedClip_IsDeclaredByTheLocalPipeline()
        {
            string[] declared = BattleAudioProfileValidator.DeclaredClipIds();
            Assert.That(declared, Is.Not.Empty, "The clip catalog must exist and declare clips.");

            BattleAudioProfile profile = BattleAudioProfileValidator.LoadSource().Find(MinesArenaId);
            foreach (string clipId in profile.ReferencedClipIds())
            {
                Assert.That(declared, Does.Contain(clipId),
                    $"Clip '{clipId}' is referenced by the arena profile but not declared in " +
                    BattleAudioProfileValidator.ClipCatalogPath);
            }

            foreach (string clipId in BattleAudioProfileValidator.LoadSource().outcomeSignals.ReferencedClipIds())
            {
                Assert.That(declared, Does.Contain(clipId),
                    $"Global result clip '{clipId}' must already be extracted and declared.");
            }

            foreach (string clipId in BattleAudioProfileValidator.LoadCrewVoiceSource().ReferencedClipIds())
            {
                Assert.That(declared, Does.Contain(clipId),
                    $"Crew voice clip '{clipId}' is not declared in {BattleAudioProfileValidator.ClipCatalogPath}; " +
                    "extract it before pointing a nation at that language.");
            }
        }

        [Test]
        public void KnownArenaIds_IncludeTheArenaTheProfileKeysOn()
        {
            string[] arenas = BattleAudioProfileValidator.KnownArenaIds();

            Assert.That(arenas, Does.Contain(MinesArenaId),
                "The arena list comes from battle map scenes and must include the arena the profile keys on.");
        }

        [Test]
        public void Catalog_RejectsDuplicateArenaIds()
        {
            BattleAudioProfileCatalog catalog = new BattleAudioProfileCatalog
            {
                profiles = new[]
                {
                    new BattleAudioProfile { mapId = MinesArenaId, music = new BattleMusicClipIds { relaxed = "a", intensive = "b" } },
                    new BattleAudioProfile { mapId = MinesArenaId, music = new BattleMusicClipIds { relaxed = "a", intensive = "b" } }
                }
            };

            string[] errors = catalog.Validate();

            Assert.That(errors.Any(error => error.Contains("declared twice")), Is.True,
                "Two profiles for one arena would make the winner depend on file order.");
        }

        [Test]
        public void Profile_RejectsStructuralMistakes()
        {
            BattleAudioProfile incomplete = new BattleAudioProfile
            {
                mapId = "arena",
                music = new BattleMusicClipIds { relaxed = "mines_music_battle_relaxed" }
            };
            Assert.That(incomplete.Validate().Any(error => error.Contains("intensive")), Is.True);

            BattleAudioProfile badId = new BattleAudioProfile
            {
                mapId = "arena",
                music = new BattleMusicClipIds { relaxed = "Mines/Music/x.wav", intensive = "b" }
            };
            Assert.That(badId.Validate().Any(error => error.Contains("invalid clip ID")), Is.True,
                "A path must never be stored where a stable clip ID belongs.");

            BattleAudioProfile badVolume = new BattleAudioProfile
            {
                mapId = "arena",
                music = new BattleMusicClipIds { relaxed = "a", intensive = "b" },
                musicVolume = 1.4f
            };
            Assert.That(badVolume.Validate().Any(error => error.Contains("musicVolume")), Is.True);
        }

        [Test]
        public void Find_MatchesTheExactArenaId()
        {
            BattleAudioProfileCatalog catalog = BattleAudioProfileValidator.LoadSource();

            Assert.That(catalog.Find("10_hills"), Is.Null,
                "The descriptor reports the battle scene ID, not the source map name.");
            Assert.That(catalog.Find(MinesArenaId), Is.Not.Null);
            Assert.That(catalog.Find(string.Empty), Is.Null);
        }

        [Test]
        public void SourceFile_IsWhereTheRuntimeLooks()
        {
            Assert.That(File.Exists(Path.GetFullPath(BattleAudioProfileCatalog.SourcePath)), Is.True);
        }
    }
}
