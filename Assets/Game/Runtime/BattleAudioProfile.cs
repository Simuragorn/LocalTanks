using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace LocalTanks
{
    [Serializable]
    public sealed class BattleMusicClipIds
    {
        public string loading;
        public string relaxed;
        public string intensive;
        public string victory;
        public string draw;
        public string defeat;
    }

    [Serializable]
    public sealed class BattleOutcomeClipIds
    {
        public string victory;
        public string draw;
        public string defeat;

        public IEnumerable<string> ReferencedClipIds()
        {
            foreach (string clipId in new[] { victory, draw, defeat })
                if (!string.IsNullOrWhiteSpace(clipId)) yield return clipId;
        }

        public string[] Validate()
        {
            List<string> errors = new List<string>();
            if (string.IsNullOrWhiteSpace(victory)) errors.Add("Global outcome signals have no victory clip.");
            if (string.IsNullOrWhiteSpace(draw)) errors.Add("Global outcome signals have no draw clip.");
            if (string.IsNullOrWhiteSpace(defeat)) errors.Add("Global outcome signals have no defeat clip.");
            foreach (string clipId in ReferencedClipIds())
                if (!Regex.IsMatch(clipId, "^[a-z0-9_]+$")) errors.Add($"Global outcome signal '{clipId}' is invalid.");
            return errors.ToArray();
        }
    }

    /// <summary>
    /// Which local clips belong to one arena, and how loud each battle layer is. The scene names no
    /// Wwise event and no file path: it only knows its map ID, and the profile supplies the stable clip
    /// IDs that <see cref="WotAudioClipLibrary"/> resolves.
    /// </summary>
    [Serializable]
    public sealed class BattleAudioProfile
    {
        public string mapId;
        public string displayName;
        public BattleMusicClipIds music = new BattleMusicClipIds();
        public string ambienceClipId;

        /// <summary>Loop that runs while a base is being captured; stopped by the runtime, not by a clip.</summary>
        public string captureProgressClipId;

        /// <summary>Signal for a battle that ended with the last enemy destroyed.</summary>
        public string battleEndByLastKillClipId;

        /// <summary>
        /// Signal for a battle that ended by capturing a base. It stays declared until the victory
        /// condition itself is decided (`PROJECT_PLAN.md` §22.1), so the profile does not have to change
        /// when that decision lands.
        /// </summary>
        public string battleEndByCaptureClipId;

        [Range(0f, 1f)] public float musicVolume = 0.5f;
        [Range(0f, 1f)] public float ambienceVolume = 0.3f;

        /// <summary>Battle signals: capture notices and end-of-battle stingers.</summary>
        [Range(0f, 1f)] public float signalVolume = 0.65f;

        public string[] Validate()
        {
            List<string> errors = new List<string>();
            if (string.IsNullOrWhiteSpace(mapId))
            {
                errors.Add("Battle audio profile has no mapId.");
            }

            if (music == null)
            {
                errors.Add($"Battle audio profile '{mapId}' has no music block.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(music.relaxed))
                    errors.Add($"Battle audio profile '{mapId}' has no relaxed battle music.");
                if (string.IsNullOrWhiteSpace(music.intensive))
                    errors.Add($"Battle audio profile '{mapId}' has no intensive battle music.");
            }

            foreach (string clipId in ReferencedClipIds())
            {
                if (clipId != null && !ClipIdPattern.IsMatch(clipId))
                    errors.Add($"Battle audio profile '{mapId}' uses the invalid clip ID '{clipId}'.");
            }

            CheckVolume(errors, "musicVolume", musicVolume);
            CheckVolume(errors, "ambienceVolume", ambienceVolume);
            CheckVolume(errors, "signalVolume", signalVolume);
            return errors.ToArray();
        }

        /// <summary>Every clip ID this profile names, in a stable order. Empty entries are skipped.</summary>
        public IEnumerable<string> ReferencedClipIds()
        {
            if (music != null)
            {
                foreach (string clipId in new[] { music.loading, music.relaxed, music.intensive, music.victory, music.draw, music.defeat })
                {
                    if (!string.IsNullOrWhiteSpace(clipId)) yield return clipId;
                }
            }

            foreach (string clipId in new[]
                     {
                         ambienceClipId, captureProgressClipId, battleEndByLastKillClipId, battleEndByCaptureClipId
                     })
            {
                if (!string.IsNullOrWhiteSpace(clipId)) yield return clipId;
            }
        }

        private static readonly Regex ClipIdPattern = new Regex("^[a-z0-9_]+$", RegexOptions.Compiled);

        private void CheckVolume(List<string> errors, string field, float value)
        {
            if (value < 0f || value > 1f)
                errors.Add($"Battle audio profile '{mapId}' has {field} outside 0..1 ({value}).");
        }
    }

    /// <summary>
    /// All arena audio profiles in one committed JSON resource. One editable list, read at runtime,
    /// validated by an Editor command and by an Edit Mode test, so a profile cannot name a clip that the
    /// local pipeline never declared.
    /// </summary>
    [Serializable]
    public sealed class BattleAudioProfileCatalog
    {
        public const string ResourceName = "BattleAudioProfiles";
        public const string SourcePath = "Assets/Game/Resources/BattleAudioProfiles.json";

        public int schemaVersion = 2;
        public BattleOutcomeClipIds outcomeSignals = new BattleOutcomeClipIds();
        [Range(0f, 1f)] public float outcomeSignalVolume = 0.65f;
        public BattleAudioProfile[] profiles = Array.Empty<BattleAudioProfile>();

        public static BattleAudioProfileCatalog Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new BattleAudioProfileCatalog();
            }

            return JsonUtility.FromJson<BattleAudioProfileCatalog>(json) ?? new BattleAudioProfileCatalog();
        }

        public static BattleAudioProfileCatalog Load()
        {
            TextAsset asset = Resources.Load<TextAsset>(ResourceName);
            if (asset == null)
            {
                Debug.LogWarning($"Battle audio profile resource '{ResourceName}' is missing; battles stay without music.");
                return new BattleAudioProfileCatalog();
            }

            return Parse(asset.text);
        }

        /// <summary>The profile for one map, or null when the map has no battle audio.</summary>
        public BattleAudioProfile Find(string mapId)
        {
            if (string.IsNullOrWhiteSpace(mapId) || profiles == null)
            {
                return null;
            }

            foreach (BattleAudioProfile profile in profiles)
            {
                if (profile != null && string.Equals(profile.mapId, mapId, StringComparison.Ordinal))
                {
                    return profile;
                }
            }

            return null;
        }

        public string[] Validate()
        {
            List<string> errors = new List<string>();
            if (schemaVersion != 2)
                errors.Add($"Battle audio profile catalog has unsupported schemaVersion {schemaVersion}.");
            if (outcomeSignals == null)
                errors.Add("Battle audio profile catalog has no global outcome signals.");
            else
                errors.AddRange(outcomeSignals.Validate());
            if (outcomeSignalVolume < 0f || outcomeSignalVolume > 1f)
                errors.Add($"Battle audio profile catalog has outcomeSignalVolume outside 0..1 ({outcomeSignalVolume}).");
            if (profiles == null || profiles.Length == 0)
            {
                errors.Add("Battle audio profile catalog declares no profiles.");
                return errors.ToArray();
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (BattleAudioProfile profile in profiles)
            {
                if (profile == null)
                {
                    errors.Add("Battle audio profile catalog contains an empty entry.");
                    continue;
                }

                errors.AddRange(profile.Validate());
                if (!string.IsNullOrWhiteSpace(profile.mapId) && !seen.Add(profile.mapId))
                {
                    errors.Add($"Battle audio profile '{profile.mapId}' is declared twice.");
                }
            }

            return errors.ToArray();
        }
    }
}
