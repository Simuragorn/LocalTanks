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

    /// <summary>Which language a nation's crew speaks. Changing an entry is all a new language needs.</summary>
    [Serializable]
    public sealed class CrewVoiceNationLanguage
    {
        public string nation;
        public string language;
    }

    /// <summary>Maps a role to the clip ID that carries it; `{lang}` becomes the language suffix.</summary>
    [Serializable]
    public sealed class CrewVoiceRoleBinding
    {
        public string role;
        public string clipIdPattern;
        public string description;
    }

    /// <summary>
    /// Crew voice is nation-dependent by design: the same role is a different recording per language, so
    /// the clip ID carries the language suffix and only the data decides which suffix a vehicle asks for.
    /// Every nation currently points at the Russian set, because that is the only one extracted — turning
    /// on German or English is a one-line data change once those clips exist, and the validator refuses to
    /// let a nation point at a language whose clips were never declared.
    /// </summary>
    [Serializable]
    public sealed class CrewVoiceProfileCatalog
    {
        public const string ResourceName = "CrewVoiceProfiles";
        public const string SourcePath = "Assets/Game/Resources/CrewVoiceProfiles.json";
        public const string LanguagePlaceholder = "{lang}";

        public int schemaVersion = 1;
        public string defaultLanguage = "ru";

        /// <summary>Level of every crew line; the player's `Voice` channel scales it further.</summary>
        [Range(0f, 1f)] public float volume = 0.9f;

        public CrewVoiceLanguage[] languages = Array.Empty<CrewVoiceLanguage>();
        public CrewVoiceNationLanguage[] nations = Array.Empty<CrewVoiceNationLanguage>();
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
            foreach (CrewVoiceNationLanguage entry in nations ?? Array.Empty<CrewVoiceNationLanguage>())
            {
                if (entry != null && string.Equals(entry.nation, name, StringComparison.Ordinal))
                {
                    return string.IsNullOrWhiteSpace(entry.language) ? defaultLanguage : entry.language;
                }
            }

            return defaultLanguage;
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

        /// <summary>The clip ID for one role in the nation's language, or an empty string when unknown.</summary>
        public string ClipIdFor(CrewVoiceRole role, TankNation nation)
        {
            return ClipIdForLanguage(role, LanguageFor(nation));
        }

        public string ClipIdForLanguage(CrewVoiceRole role, string language)
        {
            string pattern = PatternFor(role);
            return string.IsNullOrWhiteSpace(pattern)
                ? string.Empty
                : pattern.Replace(LanguagePlaceholder, language ?? defaultLanguage);
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
            foreach (CrewVoiceNationLanguage entry in nations ?? Array.Empty<CrewVoiceNationLanguage>())
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

        /// <summary>Every clip ID the currently used languages ask for, in a stable order.</summary>
        public string[] ReferencedClipIds()
        {
            List<string> ids = new List<string>();
            foreach (string language in UsedLanguages())
            {
                foreach (CrewVoiceRole role in Enum.GetValues(typeof(CrewVoiceRole)))
                {
                    string clipId = ClipIdForLanguage(role, language);
                    if (!string.IsNullOrWhiteSpace(clipId) && !ids.Contains(clipId))
                    {
                        ids.Add(clipId);
                    }
                }
            }

            ids.Sort(StringComparer.Ordinal);
            return ids.ToArray();
        }

        public string[] Validate()
        {
            List<string> errors = new List<string>();
            if (schemaVersion != 1)
                errors.Add($"Crew voice profile has unsupported schemaVersion {schemaVersion}.");
            if (volume < 0f || volume > 1f)
                errors.Add($"Crew voice volume is outside 0..1 ({volume}).");
            if (string.IsNullOrWhiteSpace(defaultLanguage))
                errors.Add("Crew voice profile has no default language.");
            else if (!TryGetLanguage(defaultLanguage, out CrewVoiceLanguage _))
                errors.Add($"Crew voice default language '{defaultLanguage}' is not declared in languages.");

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
                foreach (CrewVoiceNationLanguage entry in nations ?? Array.Empty<CrewVoiceNationLanguage>())
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

            foreach (CrewVoiceNationLanguage entry in nations ?? Array.Empty<CrewVoiceNationLanguage>())
            {
                if (entry == null) continue;
                if (Array.IndexOf(nationNames, entry.nation) < 0)
                    errors.Add($"Crew voice profile declares unknown nation '{entry.nation}'.");
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
    }
}
