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
            Assert.That(profile.battleStartClipId, Is.Not.Empty);
            Assert.That(profile.captureProgressClipId, Is.Not.Empty);
            Assert.That(profile.baseCapturedClipId, Is.Not.Empty);
            Assert.That(profile.battleEndByLastKillClipId, Is.Not.Empty);
            Assert.That(profile.battleEndByCaptureClipId, Is.Not.Empty);
        }

        [Test]
        public void EveryReferencedClip_IsDeclaredByTheLocalPipeline()
        {
            string[] declared = BattleAudioProfileValidator.DeclaredClipIds();
            BattleAudioProfile profile = BattleAudioProfileValidator.LoadSource().Find(MinesArenaId);

            Assert.That(declared, Is.Not.Empty, "The clip catalog must exist and declare clips.");
            foreach (string clipId in profile.ReferencedClipIds())
            {
                Assert.That(declared, Does.Contain(clipId),
                    $"Clip '{clipId}' is referenced by the arena profile but not declared in " +
                    BattleAudioProfileValidator.ClipCatalogPath);
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
