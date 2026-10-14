using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace LocalTanks
{
    /// <summary>Every crew line the game can ask for. The name is the stable ID used by the data.</summary>
    public enum CrewVoiceRole
    {
        BattleStart,
        BaseCaptured,
        BaseLost,
        Penetrated,
        NotPenetrated,
        Ricochet,
        EnemyDestroyedByPlayer,
        EnemyDestroyed,
        AllyDestroyed,
        VehicleDestroyed
    }

    /// <summary>
    /// One localized crew voice set the client ships. The language ID is the Wwise language of that
    /// bank and is what the extraction pipeline passes as `languageId`, so adding a language is a data
    /// edit plus one pipeline run.
    /// </summary>
    [Serializable]
    public sealed class CrewVoiceLanguage
    {
        public string id;
        public string displayName;
        public string wwiseLanguageId;
        public string bankEntry;
    }

    /// <summary>
    /// Which language a nation's crew speaks and which voice it uses. Both are one data edit: the same
    /// role is a separate recording per language and per voice, and nothing else in the game changes.
    /// </summary>
    [Serializable]
    public sealed class CrewVoiceNationBinding
    {
        public string nation;
        public string language;

        /// <summary>`male` or `female`; empty means the catalog's default voice.</summary>
        public string voice;
    }

    /// <summary>
    /// Maps a role to the clip ID that carries it; `{lang}` becomes the language suffix and `{voice}`
    /// the voice suffix, so one pattern covers every extracted set.
    /// </summary>
    [Serializable]
    public sealed class CrewVoiceRoleBinding
    {
        public string role;
        public string clipIdPattern;
        public string description;
    }

    /// <summary>
    /// Crew voice is nation-dependent by design: the same role is a different recording per language
    /// and per voice, so the clip ID carries both suffixes and only the data decides which ones a
    /// vehicle asks for.
    /// </summary>
    /// <remarks>
    /// Each national bank of the installed client branches every battle line on one Wwise switch with
    /// two values — the speaking crew's sex — so the male and the female take of a line are two
    /// declarations of the same event, told apart by `variantMatch` in the clip catalog. The runtime
    /// never sees a switch: it only names a clip ID, which keeps the extracted library independent of
    /// how the client happens to store its branches.
    /// </remarks>
    [Serializable]
    public sealed class CrewVoiceProfileCatalog
    {
        public const string ResourceName = "CrewVoiceProfiles";
        public const string SourcePath = "Assets/Game/Resources/CrewVoiceProfiles.json";
        public const string LanguagePlaceholder = "{lang}";
        public const string VoicePlaceholder = "{voice}";

        public int schemaVersion = 2;
        public string defaultLanguage = "ru";

        /// <summary>The voice a nation speaks with when its entry names none.</summary>
        public string defaultVoice = "male";

        /// <summary>Level of every crew line; the player's `Voice` channel scales it further.</summary>
        [Range(0f, 1f)] public float volume = 0.9f;

        /// <summary>
        /// The same phrase sooner than this after the previous one is skipped. An automatic gun puts a
        /// dozen shells into a target within seconds, and a crew that repeats "не пробил" for every one
        /// of them says nothing useful.
        /// </summary>
        [Min(0f)] public float repeatCooldownSeconds = 3f;

        /// <summary>
        /// Any line sooner than this after the previous one is skipped, so the crew never talks over
        /// itself. The loss of the player's own machine ignores both limits: that is one thing the crew
        /// must always report.
        /// </summary>
        [Min(0f)] public float speechCooldownSeconds = 1.2f;

        public CrewVoiceLanguage[] languages = Array.Empty<CrewVoiceLanguage>();
        public CrewVoiceNationBinding[] nations = Array.Empty<CrewVoiceNationBinding>();
        public CrewVoiceRoleBinding[] roles = Array.Empty<CrewVoiceRoleBinding>();

        public static CrewVoiceProfileCatalog Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new CrewVoiceProfileCatalog();
            }

            return JsonUtility.FromJson<CrewVoiceProfileCatalog>(json) ?? new CrewVoiceProfileCatalog();
        }

        public static CrewVoiceProfileCatalog Load()
        {
            TextAsset asset = Resources.Load<TextAsset>(ResourceName);
            if (asset == null)
            {
                // No crew voice at all is a valid state: the battle stays playable and silent.
                Debug.LogWarning($"Crew voice profile resource '{ResourceName}' is missing; crew voice stays silent.");
                return new CrewVoiceProfileCatalog();
            }

            return Parse(asset.text);
        }

        /// <summary>The language a nation's crew speaks, falling back to the default language.</summary>
        public string LanguageFor(TankNation nation)
        {
            string name = nation.ToString();
            foreach (CrewVoiceNationBinding entry in nations ?? Array.Empty<CrewVoiceNationBinding>())
            {
                if (entry != null && string.Equals(entry.nation, name, StringComparison.Ordinal))
                {
                    return string.IsNullOrWhiteSpace(entry.language) ? defaultLanguage : entry.language;
                }
            }

            return defaultLanguage;
        }

        /// <summary>The voice a nation's crew speaks with, falling back to the default voice.</summary>
        public string VoiceFor(TankNation nation)
        {
            string name = nation.ToString();
            foreach (CrewVoiceNationBinding entry in nations ?? Array.Empty<CrewVoiceNationBinding>())
            {
                if (entry != null && string.Equals(entry.nation, name, StringComparison.Ordinal))
                {
                    return string.IsNullOrWhiteSpace(entry.voice) ? defaultVoice : entry.voice;
                }
            }

            return defaultVoice;
        }

        public bool TryGetLanguage(string id, out CrewVoiceLanguage language)
        {
            foreach (CrewVoiceLanguage entry in languages ?? Array.Empty<CrewVoiceLanguage>())
            {
                if (entry != null && string.Equals(entry.id, id, StringComparison.Ordinal))
                {
                    language = entry;
                    return true;
                }
            }

            language = null;
            return false;
        }

        /// <summary>The clip ID for one role in the nation's language and voice, or an empty string.</summary>
        public string ClipIdFor(CrewVoiceRole role, TankNation nation)
        {
            return ClipIdForLanguage(role, LanguageFor(nation), VoiceFor(nation));
        }

        public string ClipIdForLanguage(CrewVoiceRole role, string language)
        {
            return ClipIdForLanguage(role, language, defaultVoice);
        }

        public string ClipIdForLanguage(CrewVoiceRole role, string language, string voice)
        {
            string pattern = PatternFor(role);
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return string.Empty;
            }

            return pattern
                .Replace(LanguagePlaceholder, language ?? defaultLanguage)
                .Replace(VoicePlaceholder, string.IsNullOrWhiteSpace(voice) ? defaultVoice : voice);
        }

        public string DescriptionFor(CrewVoiceRole role)
        {
            foreach (CrewVoiceRoleBinding binding in roles ?? Array.Empty<CrewVoiceRoleBinding>())
            {
                if (binding != null && string.Equals(binding.role, role.ToString(), StringComparison.Ordinal))
                {
                    return binding.description;
                }
            }

            return string.Empty;
        }

        public string PatternFor(CrewVoiceRole role)
        {
            foreach (CrewVoiceRoleBinding binding in roles ?? Array.Empty<CrewVoiceRoleBinding>())
            {
                if (binding != null && string.Equals(binding.role, role.ToString(), StringComparison.Ordinal))
                {
                    return binding.clipIdPattern;
                }
            }

            return string.Empty;
        }

        /// <summary>Every language some nation actually uses. Only these have to exist locally.</summary>
        public string[] UsedLanguages()
        {
            HashSet<string> used = new HashSet<string>(StringComparer.Ordinal) { defaultLanguage };
            foreach (CrewVoiceNationBinding entry in nations ?? Array.Empty<CrewVoiceNationBinding>())
            {
                if (entry != null && !string.IsNullOrWhiteSpace(entry.language))
                {
                    used.Add(entry.language);
                }
            }

            used.Remove(string.Empty);
            string[] result = new string[used.Count];
            used.CopyTo(result);
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }

        /// <summary>Every voice some nation actually uses. Only these have to exist locally.</summary>
        public string[] UsedVoices()
        {
            HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(defaultVoice))
            {
                used.Add(defaultVoice);
            }

            foreach (CrewVoiceNationBinding entry in nations ?? Array.Empty<CrewVoiceNationBinding>())
            {
                if (entry != null && !string.IsNullOrWhiteSpace(entry.voice))
                {
                    used.Add(entry.voice);
                }
            }

            used.Remove(string.Empty);
            string[] result = new string[used.Count];
            used.CopyTo(result);
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }

        /// <summary>Every clip ID the currently used languages and voices ask for, in a stable order.</summary>
        public string[] ReferencedClipIds()
        {
            List<string> ids = new List<string>();
            foreach (string language in UsedLanguages())
            {
                foreach (string voice in UsedVoices())
                {
                    foreach (CrewVoiceRole role in Enum.GetValues(typeof(CrewVoiceRole)))
                    {
                        string clipId = ClipIdForLanguage(role, language, voice);
                        if (!string.IsNullOrWhiteSpace(clipId) && !ids.Contains(clipId))
                        {
                            ids.Add(clipId);
                        }
                    }
                }
            }

            ids.Sort(StringComparer.Ordinal);
            return ids.ToArray();
        }

        public string[] Validate()
        {
            List<string> errors = new List<string>();
            if (schemaVersion != 2)
                errors.Add($"Crew voice profile has unsupported schemaVersion {schemaVersion}.");
            if (volume < 0f || volume > 1f)
                errors.Add($"Crew voice volume is outside 0..1 ({volume}).");
            if (repeatCooldownSeconds < 0f)
                errors.Add($"Crew voice repeat cooldown cannot be negative ({repeatCooldownSeconds}).");
            if (speechCooldownSeconds < 0f)
                errors.Add($"Crew voice speech cooldown cannot be negative ({speechCooldownSeconds}).");
            if (string.IsNullOrWhiteSpace(defaultLanguage))
                errors.Add("Crew voice profile has no default language.");
            else if (!TryGetLanguage(defaultLanguage, out CrewVoiceLanguage _))
                errors.Add($"Crew voice default language '{defaultLanguage}' is not declared in languages.");
            if (string.IsNullOrWhiteSpace(defaultVoice))
                errors.Add("Crew voice profile has no default voice.");

            string[] roleNames = Enum.GetNames(typeof(CrewVoiceRole));
            foreach (string role in roleNames)
            {
                int matches = 0;
                foreach (CrewVoiceRoleBinding binding in roles ?? Array.Empty<CrewVoiceRoleBinding>())
                {
                    if (binding != null && string.Equals(binding.role, role, StringComparison.Ordinal))
                    {
                        matches++;
                        if (string.IsNullOrWhiteSpace(binding.clipIdPattern) ||
                            !binding.clipIdPattern.Contains(LanguagePlaceholder))
                        {
                            errors.Add($"Crew voice role '{role}' must use a clip ID pattern containing " +
                                       $"'{LanguagePlaceholder}'.");
                        }

                        if (string.IsNullOrWhiteSpace(binding.clipIdPattern) ||
                            !binding.clipIdPattern.Contains(VoicePlaceholder))
                        {
                            errors.Add($"Crew voice role '{role}' must use a clip ID pattern containing " +
                                       $"'{VoicePlaceholder}'; a hardcoded voice would defeat the scaffold.");
                        }
                    }
                }

                if (matches == 0)
                    errors.Add($"Crew voice role '{role}' has no clip ID pattern.");
                else if (matches > 1)
                    errors.Add($"Crew voice role '{role}' is declared {matches} times.");
            }

            foreach (CrewVoiceRoleBinding binding in roles ?? Array.Empty<CrewVoiceRoleBinding>())
            {
                if (binding == null) continue;
                if (Array.IndexOf(roleNames, binding.role) < 0)
                    errors.Add($"Crew voice profile declares unknown role '{binding.role}'.");
            }

            string[] nationNames = Enum.GetNames(typeof(TankNation));
            foreach (string nation in nationNames)
            {
                int matches = 0;
                string language = null;
                foreach (CrewVoiceNationBinding entry in nations ?? Array.Empty<CrewVoiceNationBinding>())
                {
                    if (entry != null && string.Equals(entry.nation, nation, StringComparison.Ordinal))
                    {
                        matches++;
                        language = entry.language;
                    }
                }

                if (matches == 0)
                {
                    // Adding a nation to the game must force a decision about its crew voice, so the gap
                    // is an error rather than an accidental fallback.
                    errors.Add($"Crew voice profile has no language for nation '{nation}'.");
                }
                else if (matches > 1)
                {
                    errors.Add($"Crew voice profile declares nation '{nation}' {matches} times.");
                }
                else if (!string.IsNullOrWhiteSpace(language) && !TryGetLanguage(language, out CrewVoiceLanguage _))
                {
                    errors.Add($"Crew voice nation '{nation}' uses undeclared language '{language}'.");
                }
            }

            foreach (CrewVoiceNationBinding entry in nations ?? Array.Empty<CrewVoiceNationBinding>())
            {
                if (entry == null) continue;
                if (Array.IndexOf(nationNames, entry.nation) < 0)
                    errors.Add($"Crew voice profile declares unknown nation '{entry.nation}'.");
                if (!string.IsNullOrWhiteSpace(entry.voice) && !VoiceIdPattern.IsMatch(entry.voice))
                    errors.Add($"Crew voice nation '{entry.nation}' uses invalid voice '{entry.voice}'; " +
                               "use lowercase letters or digits, for example 'male' or 'female'.");
            }

            foreach (CrewVoiceLanguage language in languages ?? Array.Empty<CrewVoiceLanguage>())
            {
                if (language == null) continue;
                if (string.IsNullOrWhiteSpace(language.id) || !LanguageIdPattern.IsMatch(language.id))
                    errors.Add($"Crew voice language ID '{language.id}' must be lowercase letters or digits.");
                if (string.IsNullOrWhiteSpace(language.wwiseLanguageId))
                    errors.Add($"Crew voice language '{language.id}' has no Wwise language ID.");
                if (string.IsNullOrWhiteSpace(language.bankEntry))
                    errors.Add($"Crew voice language '{language.id}' names no bank entry.");
            }

            return errors.ToArray();
        }

        private static readonly Regex LanguageIdPattern = new Regex("^[a-z0-9_]+$", RegexOptions.Compiled);
        private static readonly Regex VoiceIdPattern = new Regex("^[a-z0-9_]+$", RegexOptions.Compiled);
    }
}
