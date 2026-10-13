using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace LocalTanks.Tests
{
    /// <summary>
    /// The resolver is the piece that makes the grouped local library reachable at all: vehicle clips are
    /// flat, map and battle clips live in group folders, and every failure has to end in silence rather
    /// than in an exception, because a battle must stay playable without the extracted WoT files.
    /// </summary>
    public sealed class WotAudioClipLibraryTests
    {
        private const string ManifestJson = @"{
            ""schemaVersion"": 2,
            ""clips"": [
                {
                    ""clipId"": ""engine_eng_gaz_aa_pc"",
                    ""group"": ""Vehicles"",
                    ""relativePath"": ""Clips/engine_eng_gaz_aa_pc.wav"",
                    ""usage"": ""loop""
                },
                {
                    ""clipId"": ""mines_music_battle_relaxed"",
                    ""group"": ""Mines/Music"",
                    ""relativePath"": ""Clips/Mines/Music/mines_music_battle_relaxed.wav"",
                    ""usage"": ""music""
                }
            ]
        }";

        [Test]
        public void GroupedAndFlatClips_ResolveFromTheManifest()
        {
            WotAudioClipLibrary library = new WotAudioClipLibrary(ManifestJson, _ => null, false, _ => { });

            Assert.That(library.BuildResourcePath("engine_eng_gaz_aa_pc"),
                Is.EqualTo("WoTAudio/Clips/engine_eng_gaz_aa_pc"));
            Assert.That(library.BuildResourcePath("mines_music_battle_relaxed"),
                Is.EqualTo("WoTAudio/Clips/Mines/Music/mines_music_battle_relaxed"));
        }

        [Test]
        public void UnknownClipId_KeepsTheFlatPathAsFallback()
        {
            WotAudioClipLibrary library = new WotAudioClipLibrary(ManifestJson, _ => null, false, _ => { });

            Assert.That(library.BuildResourcePath("impact_small_pierced"),
                Is.EqualTo("WoTAudio/Clips/impact_small_pierced"),
                "An older local cache without a manifest entry must stay usable.");
        }

        [Test]
        public void Load_ResolvesOnceAndCachesTheResult()
        {
            List<string> requested = new List<string>();
            AudioClip clip = AudioClip.Create("music", 480, 1, 48000, false);
            WotAudioClipLibrary library = new WotAudioClipLibrary(
                ManifestJson,
                path =>
                {
                    requested.Add(path);
                    return path.EndsWith("mines_music_battle_relaxed") ? clip : null;
                },
                false,
                _ => { });

            Assert.That(library.Load("mines_music_battle_relaxed"), Is.SameAs(clip));
            Assert.That(library.Load("mines_music_battle_relaxed"), Is.SameAs(clip));
            Assert.That(requested.Count, Is.EqualTo(1), "A clip is loaded once and then cached.");
            Assert.That(library.UnresolvedClipCount, Is.Zero);

            Object.DestroyImmediate(clip);
        }

        [Test]
        public void MissingClip_ReportsOnceAndStaysSilent()
        {
            List<string> warnings = new List<string>();
            WotAudioClipLibrary library = new WotAudioClipLibrary(ManifestJson, _ => null, false, warnings.Add);

            Assert.That(library.Load("mines_music_battle_relaxed"), Is.Null);
            Assert.That(library.Load("mines_music_battle_relaxed"), Is.Null);

            Assert.That(library.UnresolvedClipCount, Is.EqualTo(1));
            Assert.That(warnings.Count, Is.EqualTo(1), "A missing clip must not flood the console every frame.");
            Assert.That(warnings[0], Does.Contain("mines_music_battle_relaxed"));
        }

        [Test]
        public void EmptyClipId_IsNotAnError()
        {
            List<string> warnings = new List<string>();
            WotAudioClipLibrary library = new WotAudioClipLibrary(ManifestJson, _ => null, false, warnings.Add);

            Assert.That(library.Load(null), Is.Null);
            Assert.That(library.Load("   "), Is.Null);
            Assert.That(warnings, Is.Empty);
            Assert.That(library.UnresolvedClipCount, Is.Zero);
        }

        [Test]
        public void MissingManifest_IsReportedOnceAndKeepsTheFlatFallback()
        {
            List<string> warnings = new List<string>();
            List<string> requested = new List<string>();
            WotAudioClipLibrary library = new WotAudioClipLibrary(
                null,
                path => { requested.Add(path); return null; },
                false,
                warnings.Add);

            Assert.That(library.EntryCount, Is.Zero);
            Assert.That(library.Load("mines_music_battle_relaxed"), Is.Null);
            Assert.That(library.Load("engine_eng_gaz_aa_pc"), Is.Null);

            Assert.That(warnings.Count, Is.EqualTo(1), "A whole missing library is one problem, not one per clip.");
            Assert.That(warnings[0], Does.Contain("manifest"));
            Assert.That(requested, Is.EqualTo(new[]
            {
                "WoTAudio/Clips/mines_music_battle_relaxed",
                "WoTAudio/Clips/engine_eng_gaz_aa_pc"
            }), "Without a manifest an older flat cache must still be reachable.");
            Assert.That(library.UnresolvedClipCount, Is.Zero, "A missing library is not a missing clip.");
        }

        [Test]
        public void SilentLibrary_NeverTouchesTheLoader()
        {
            int loads = 0;
            WotAudioClipLibrary library = new WotAudioClipLibrary(ManifestJson, _ => { loads++; return null; }, true, _ => { });

            Assert.That(library.IsSilent, Is.True);
            Assert.That(library.Load("mines_music_battle_relaxed"), Is.Null);
            Assert.That(loads, Is.Zero, "A batchmode run must not load local clips.");
            Assert.That(library.UnresolvedClipCount, Is.Zero, "A deliberate silence is not a missing clip.");
        }

        [Test]
        public void UnreadableManifest_ReportsAndStaysSilent()
        {
            List<string> warnings = new List<string>();
            WotAudioClipLibrary library = new WotAudioClipLibrary("{ not json", _ => null, false, warnings.Add);

            Assert.That(library.EntryCount, Is.Zero);
            Assert.That(warnings.Count, Is.EqualTo(1));
        }

        [Test]
        public void DescribeState_ExplainsSilence()
        {
            WotAudioClipLibrary silent = new WotAudioClipLibrary(null, _ => null, true, _ => { });
            WotAudioClipLibrary loaded = new WotAudioClipLibrary(ManifestJson, _ => null, false, _ => { });

            Assert.That(silent.DescribeState(), Does.Contain("выключен"));
            Assert.That(loaded.DescribeState(), Does.Contain("2"));
        }
    }
}
