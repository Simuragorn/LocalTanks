using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LocalTanks;
using UnityEditor;
using UnityEngine;

namespace LocalTanks.Editor
{
    /// <summary>
    /// Keeps the arena audio profiles honest.
    /// </summary>
    /// <remarks>
    /// The profiles name clips that only exist after the local WoT pipeline ran, and they key themselves
    /// by the map ID the tactical descriptor reports. Both are easy to get subtly wrong — `10_hills`
    /// instead of `10_hills_tactical`, or a clip ID that was never declared — so the checks run over the
    /// committed catalog itself and fail loudly with the list of IDs that would have been correct.
    /// </remarks>
    public static class BattleAudioProfileValidator
    {
        public const string SourcePath = BattleAudioProfileCatalog.SourcePath;
        public const string CrewVoiceSourcePath = CrewVoiceProfileCatalog.SourcePath;
        public const string ClipCatalogPath = "Automation/pipelines/WoTAudioPipeline/audio_clips.json";
        public const string TacticalSourceFolder = "Assets/Game/GameData/Maps";

        [MenuItem("Local Tanks/Data/Validate Battle Audio Profiles")]
        public static void ValidateMenu()
        {
            BattleAudioProfileCatalog catalog = LoadSource();
            string[] errors = CollectErrors();
            if (errors.Length > 0)
            {
                throw new InvalidDataException(
                    "Battle audio profiles are invalid:\n- " + string.Join("\n- ", errors));
            }

            string[] clipIds = catalog.profiles.SelectMany(profile => profile.ReferencedClipIds())
                .Concat(LoadCrewVoiceSource().ReferencedClipIds())
                .Distinct()
                .ToArray();
            Debug.Log($"Local Tanks: battle audio profiles are valid " +
                      $"({catalog.profiles.Length} arena(s), {clipIds.Length} referenced clips).");

            // Where each language in use comes from, so extending the set later is a data edit plus one
            // pipeline run with a known bank and language ID instead of a new investigation.
            CrewVoiceProfileCatalog crew = LoadCrewVoiceSource();
            foreach (string language in crew.UsedLanguages())
            {
                if (crew.TryGetLanguage(language, out CrewVoiceLanguage entry))
                {
                    Debug.Log($"Local Tanks: crew voice language '{entry.id}' ({entry.displayName}) comes from " +
                              $"{entry.bankEntry} with Wwise language ID {entry.wwiseLanguageId}.");
                }
            }
        }

        public static CrewVoiceProfileCatalog LoadCrewVoiceSource()
        {
            string path = Path.GetFullPath(CrewVoiceSourcePath);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Crew voice profile JSON was not found.", CrewVoiceSourcePath);
            }

            return CrewVoiceProfileCatalog.Parse(File.ReadAllText(path));
        }

        public static BattleAudioProfileCatalog LoadSource()
        {
            string path = Path.GetFullPath(SourcePath);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Battle audio profile JSON was not found.", SourcePath);
            }

            return BattleAudioProfileCatalog.Parse(File.ReadAllText(path));
        }

        /// <summary>Every problem found in the committed profile file, in a stable order.</summary>
        public static string[] CollectErrors()
        {
            List<string> errors = new List<string>();
            BattleAudioProfileCatalog catalog = LoadSource();
            errors.AddRange(catalog.Validate());

            string[] declared = DeclaredClipIds();
            HashSet<string> declaredSet = new HashSet<string>(declared, StringComparer.Ordinal);
            string[] arenas = KnownArenaIds();
            HashSet<string> arenaSet = new HashSet<string>(arenas, StringComparer.Ordinal);

            foreach (BattleAudioProfile profile in catalog.profiles ?? Array.Empty<BattleAudioProfile>())
            {
                if (profile == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(profile.mapId) && !arenaSet.Contains(profile.mapId))
                {
                    errors.Add($"Battle audio profile '{profile.mapId}' matches no arena. Known arenas: " +
                               $"{string.Join(", ", arenas)}.");
                }

                foreach (string clipId in profile.ReferencedClipIds())
                {
                    if (!declaredSet.Contains(clipId))
                    {
                        errors.Add($"Battle audio profile '{profile.mapId}' references the undeclared clip " +
                                   $"'{clipId}'; declare it in {ClipCatalogPath} first.");
                    }
                }
            }

            foreach (IGrouping<string, string> duplicate in DeclaredClipEntries()
                         .Where(entry => !string.IsNullOrWhiteSpace(entry))
                         .GroupBy(entry => entry, StringComparer.Ordinal)
                         .Where(group => group.Count() > 1))
            {
                errors.Add($"Clip catalog declares '{duplicate.Key}' {duplicate.Count()} times; " +
                           "one clip ID must name exactly one event.");
            }

            CrewVoiceProfileCatalog crew = LoadCrewVoiceSource();
            errors.AddRange(crew.Validate());
            foreach (string clipId in crew.ReferencedClipIds())
            {
                if (!declaredSet.Contains(clipId))
                {
                    // This is the guard that keeps a nation from pointing at a language whose clips were
                    // never extracted: flipping Germany to `de` fails here until the German set is declared
                    // and the pipeline ran.
                    errors.Add($"Crew voice references the undeclared clip '{clipId}'; declare it in " +
                               $"{ClipCatalogPath} and run the pipeline, or point the nation at a language " +
                               $"that already exists. Languages in use: {string.Join(", ", crew.UsedLanguages())}.");
                }
            }

            foreach (string language in crew.UsedLanguages())
            {
                if (!crew.TryGetLanguage(language, out _))
                {
                    errors.Add($"Crew voice language '{language}' is used by a nation but not declared.");
                }
            }

            return errors.ToArray();
        }

        /// <summary>Every clip ID the local pipeline declares, vehicle and arena clips alike.</summary>
        public static string[] DeclaredClipIds()
        {
            return DeclaredClipEntries().Where(entry => !string.IsNullOrWhiteSpace(entry)).ToArray();
        }

        /// <summary>The arena IDs the tactical sources use, which is what a profile must be keyed by.</summary>
        public static string[] KnownArenaIds()
        {
            string folder = Path.GetFullPath(TacticalSourceFolder);
            if (!Directory.Exists(folder))
            {
                return Array.Empty<string>();
            }

            return Directory.GetFiles(folder, "*_tactical.json")
                .Select(path => TacticalMapSource.Parse(File.ReadAllText(path)).id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
        }

        private static string[] DeclaredClipEntries()
        {
            string path = Path.GetFullPath(ClipCatalogPath);
            if (!File.Exists(path))
            {
                return Array.Empty<string>();
            }

            ClipCatalogJson catalog = JsonUtility.FromJson<ClipCatalogJson>(File.ReadAllText(path));
            return catalog?.clips == null
                ? Array.Empty<string>()
                : catalog.clips.Where(clip => clip != null).Select(clip => clip.clipId).ToArray();
        }

        [Serializable]
        private sealed class ClipCatalogJson
        {
            public int schemaVersion;
            public ClipEntryJson[] clips;
        }

        [Serializable]
        private sealed class ClipEntryJson
        {
            // Only the ID matters here; the rest of the entry belongs to the extraction pipeline.
            public string clipId;
        }
    }

    /// <summary>Revalidates the profiles whenever the committed file changes, so a bad edit is reported at once.</summary>
    internal sealed class BattleAudioProfilePostprocessor : AssetPostprocessor
    {
        private static bool scheduled;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (scheduled || !importedAssets.Concat(deletedAssets).Concat(movedAssets).Concat(movedFromAssetPaths)
                    .Any(path => path == BattleAudioProfileValidator.SourcePath))
            {
                return;
            }

            scheduled = true;
            EditorApplication.delayCall += () =>
            {
                scheduled = false;
                string[] errors = BattleAudioProfileValidator.CollectErrors();
                if (errors.Length > 0)
                {
                    Debug.LogError("Battle audio profiles are invalid:\n- " + string.Join("\n- ", errors));
                }
            };
        }
    }
}
