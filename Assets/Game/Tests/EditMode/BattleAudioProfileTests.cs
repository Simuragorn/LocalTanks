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
        private const string MinesArenaId = "10_hills_tactical";

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
                Assert.That(crew.LanguageFor((TankNation)System.Enum.Parse(typeof(TankNation), nation)),
                    Is.Not.Empty, $"Nation '{nation}' must map to a voice language.");
            }

            // Pacing is data too: an automatic gun fires several shells a second, and a crew that repeats
            // one phrase for each of them says nothing useful.
            Assert.That(crew.repeatCooldownSeconds, Is.GreaterThan(0f),
                "The same phrase must not be repeated immediately.");
            Assert.That(crew.speechCooldownSeconds, Is.GreaterThan(0f),
                "The crew must not talk over itself.");
        }

        [Test]
        public void CrewVoice_CurrentlySpeaksRussianForEveryNation()
        {
            CrewVoiceProfileCatalog crew = BattleAudioProfileValidator.LoadCrewVoiceSource();

            foreach (TankNation nation in System.Enum.GetValues(typeof(TankNation)))
            {
                Assert.That(crew.LanguageFor(nation), Is.EqualTo("ru"),
                    "Only the Russian set is extracted, so every nation must point at it for now.");
            }

            Assert.That(crew.ReferencedClipIds(), Does.Contain("crew_not_penetrated_ru"));
            Assert.That(crew.ReferencedClipIds(), Does.Contain("crew_vehicle_destroyed_ru"));
            Assert.That(crew.ReferencedClipIds(), Does.Contain("battle_start_ru"),
                "Battle start and base capture are crew lines, so they resolve through the same nation map.");
            Assert.That(crew.ReferencedClipIds(), Does.Contain("base_captured_ru"));
        }

        [Test]
        public void CrewVoice_ResolvesTheLanguageSuffixFromTheNation()
        {
            CrewVoiceProfileCatalog crew = BattleAudioProfileValidator.LoadCrewVoiceSource();

            Assert.That(crew.ClipIdFor(CrewVoiceRole.Ricochet, TankNation.Germany), Is.EqualTo("crew_ricochet_ru"));

            // Point Germany at German and the very same role asks for a different recording; nothing else
            // in the game changes, which is the whole point of the scaffolding.
            CrewVoiceProfileCatalog german = new CrewVoiceProfileCatalog
            {
                defaultLanguage = "ru",
                languages = crew.languages,
                roles = crew.roles,
                nations = new[]
                {
                    new CrewVoiceNationLanguage { nation = "Germany", language = "de" },
                    new CrewVoiceNationLanguage { nation = "USSR", language = "ru" },
                    new CrewVoiceNationLanguage { nation = "USA", language = "ru" }
                }
            };

            Assert.That(german.ClipIdFor(CrewVoiceRole.Ricochet, TankNation.Germany), Is.EqualTo("crew_ricochet_de"));
            Assert.That(german.ClipIdFor(CrewVoiceRole.Ricochet, TankNation.USSR), Is.EqualTo("crew_ricochet_ru"));
        }

        [Test]
        public void CrewVoice_RejectsAnUnknownNationOrLanguage()
        {
            CrewVoiceProfileCatalog crew = new CrewVoiceProfileCatalog
            {
                defaultLanguage = "ru",
                languages = new[] { new CrewVoiceLanguage { id = "ru", wwiseLanguageId = "1", bankEntry = "ussr/voiceover.bnk" } },
                roles = new[] { new CrewVoiceRoleBinding { role = "Ricochet", clipIdPattern = "crew_ricochet_{lang}" } },
                nations = new[]
                {
                    new CrewVoiceNationLanguage { nation = "Germany", language = "ru" },
                    new CrewVoiceNationLanguage { nation = "USSR", language = "ru" },
                    new CrewVoiceNationLanguage { nation = "USA", language = "fr" }
                }
            };

            string[] errors = crew.Validate();

            Assert.That(errors.Any(error => error.Contains("undeclared language 'fr'")), Is.True);
            Assert.That(errors.Any(error => error.Contains("role 'BaseLost' has no clip ID pattern")), Is.True,
                "Every role must be declared, or a battle would silently lose a line.");
        }

        [Test]
        public void CrewVoice_RejectsAPatternWithoutTheLanguagePlaceholder()
        {
            CrewVoiceProfileCatalog crew = new CrewVoiceProfileCatalog
            {
                defaultLanguage = "ru",
                languages = new[] { new CrewVoiceLanguage { id = "ru", wwiseLanguageId = "1", bankEntry = "ussr/voiceover.bnk" } },
                roles = new[] { new CrewVoiceRoleBinding { role = "Ricochet", clipIdPattern = "crew_ricochet_ru" } },
                nations = new[]
                {
                    new CrewVoiceNationLanguage { nation = "Germany", language = "ru" },
                    new CrewVoiceNationLanguage { nation = "USSR", language = "ru" },
                    new CrewVoiceNationLanguage { nation = "USA", language = "ru" }
                }
            };

            Assert.That(crew.Validate().Any(error => error.Contains("{lang}")), Is.True,
                "A hardcoded language in the pattern would defeat the whole scaffold.");
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
                "The arena list comes from the tactical sources and must include the arena the profile keys on.");
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
                "The descriptor reports the tactical source ID, not the raw map name.");
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
