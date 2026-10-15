using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    [Serializable]
    public sealed class WotAudioManifestEntry
    {
        public string clipId;
        public string group;
        public string relativePath;
        public string usage;
        public int variantCount;
        public string sha256;
    }

    [Serializable]
    public sealed class WotAudioManifest
    {
        public int schemaVersion;
        public WotAudioManifestEntry[] clips;
    }

    /// <summary>
    /// Resolves a stable WoT clip ID to the local WAV the pipeline produced.
    /// </summary>
    /// <remarks>
    /// The pipeline writes clips into group folders (`Clips/Mines/Music/...`), while the first vehicle
    /// clips stayed flat in `Clips/`. The manifest it writes next to them is therefore the single source
    /// of truth for the real path: declaring a clip in `Automation/pipelines/WoTAudioPipeline/audio_clips.json` and
    /// running the pipeline is enough, and no code has to learn a second table of paths. A missing
    /// manifest, a missing clip and a batchmode run all degrade to silence instead of an exception,
    /// because a battle has to stay playable without the local WoT files.
    /// </remarks>
    public sealed class WotAudioClipLibrary : ITankAudioClipProvider
    {
        public const string PerformanceAudioSwitch = "-localtanksPerformanceAudio";
        public const string ClipResourceRoot = "WoTAudio";
        public const string ManifestResourceName = "WoTAudio/audio_manifest";

        private readonly Dictionary<string, WotAudioManifestEntry> entries =
            new Dictionary<string, WotAudioManifestEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>(StringComparer.Ordinal);
        private readonly HashSet<string> reported = new HashSet<string>(StringComparer.Ordinal);
        private readonly Func<string, AudioClip> loader;
        private readonly Action<string> warn;
        private readonly bool manifestAvailable;
        private static WotAudioClipLibrary shared;

        public WotAudioClipLibrary(string manifestJson, Func<string, AudioClip> clipLoader, bool silent, Action<string> warningSink = null)
        {
            loader = clipLoader;
            warn = warningSink ?? (message => Debug.LogWarning(message));
            IsSilent = silent;
            if (string.IsNullOrWhiteSpace(manifestJson))
            {
                // A batchmode run passes no manifest on purpose. Warning about it there would report the
                // test harness's own decision as a missing file.
                if (!silent)
                {
                    Report("manifest-missing", "Local WoT audio manifest was not found; every clip resolves to silence.");
                }

                return;
            }

            WotAudioManifest manifest = null;
            try
            {
                manifest = JsonUtility.FromJson<WotAudioManifest>(manifestJson);
            }
            catch (Exception exception)
            {
                Report("manifest-unreadable", $"Local WoT audio manifest could not be parsed: {exception.Message}");
                return;
            }

            manifestAvailable = true;
            foreach (WotAudioManifestEntry entry in manifest != null && manifest.clips != null
                         ? manifest.clips
                         : Array.Empty<WotAudioManifestEntry>())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.clipId) ||
                    string.IsNullOrWhiteSpace(entry.relativePath))
                {
                    continue;
                }

                entries[entry.clipId] = entry;
            }
        }

        /// <summary>True when nothing can play: a batchmode run, or a missing manifest.</summary>
        public bool IsSilent { get; }

        public int EntryCount => entries.Count;
        public int UnresolvedClipCount { get; private set; }
        public string LastIssue { get; private set; } = string.Empty;

        /// <summary>The library used by the runtime, created once and shared by every audio controller.</summary>
        public static WotAudioClipLibrary Shared => shared ??= CreateRuntime();

        public static WotAudioClipLibrary CreateRuntime()
        {
            // Batchmode suites must stay silent: the extracted WoT clips are usually absent on a build
            // machine and a test run that suddenly plays music would be a surprise for whoever runs it.
            bool silent = AudioRuntimePolicy.IsSuppressed;
            string manifestJson = silent ? null : LoadManifestText();
            return new WotAudioClipLibrary(
                manifestJson,
                clipId => silent ? null : Resources.Load<AudioClip>(clipId),
                silent,
                message => Debug.LogWarning(message));
        }

        /// <summary>Drops the shared library so a test can observe a fresh resolution.</summary>
        public static void ResetShared()
        {
            shared = null;
        }

        public AudioClip Load(string clipId)
        {
            if (string.IsNullOrWhiteSpace(clipId))
            {
                return null;
            }

            if (cache.TryGetValue(clipId, out AudioClip cached))
            {
                return cached;
            }

            if (IsSilent)
            {
                // A batchmode run resolves nothing at all. That is a deliberate silence rather than a
                // missing clip, so it is neither counted nor reported per clip.
                cache[clipId] = null;
                return null;
            }

            AudioClip clip = loader(BuildResourcePath(clipId));
            cache[clipId] = clip;
            if (clip == null && manifestAvailable)
            {
                UnresolvedClipCount++;
                Report(clipId, $"Local WoT clip '{clipId}' has no WAV under {ClipResourceRoot}; it will stay silent.");
            }

            return clip;
        }

        /// <summary>The Resources path for a clip ID, without extension. Exposed so a test can pin the rules.</summary>
        public string BuildResourcePath(string clipId)
        {
            if (entries.TryGetValue(clipId, out WotAudioManifestEntry entry))
            {
                string relative = StripExtension(entry.relativePath).Trim('/');
                if (relative.StartsWith("Clips/", StringComparison.Ordinal))
                {
                    return $"{ClipResourceRoot}/{relative}";
                }

                return $"{ClipResourceRoot}/Clips/{relative}";
            }

            // A clip the manifest does not know keeps the historical flat path, so an older local cache
            // stays usable until the pipeline is rerun.
            return $"{ClipResourceRoot}/Clips/{clipId}";
        }

        public bool TryGetEntry(string clipId, out WotAudioManifestEntry entry)
        {
            return entries.TryGetValue(clipId, out entry);
        }

        /// <summary>One line for the developer overlay: what the library knows and what it cannot find.</summary>
        public string DescribeState()
        {
            if (IsSilent)
            {
                return "звук выключен (batchmode или отсутствует manifest)";
            }

            return $"{entries.Count} клипов в manifest, {UnresolvedClipCount} не найдено";
        }

        private static string LoadManifestText()
        {
            TextAsset manifest = Resources.Load<TextAsset>(ManifestResourceName);
            return manifest != null ? manifest.text : null;
        }

        private static string StripExtension(string path)
        {
            int dot = path.LastIndexOf('.');
            int slash = path.LastIndexOf('/');
            return dot > slash ? path.Substring(0, dot) : path;
        }

        private void Report(string key, string message)
        {
            LastIssue = message;
            if (reported.Add(key))
            {
                warn(message);
            }
        }
    }

    /// <summary>One batch-mode audio rule shared by runtime providers and the performance diagnostic.</summary>
    public static class AudioRuntimePolicy
    {
        public static bool IsSuppressed => Application.isBatchMode &&
                                           !Array.Exists(
                                               Environment.GetCommandLineArgs(),
                                               argument => argument == WotAudioClipLibrary.PerformanceAudioSwitch);
    }
}
