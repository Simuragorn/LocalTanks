using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LocalTanks
{
    [Serializable]
    public sealed class BotProfileContext
    {
        public string entityId;
        public string team;
        public string tankId;
        public string vehicleClass;
        public string nation;
        public string lane;
        public string role;
    }

    [Serializable]
    public sealed class BotPersonalityProfile
    {
        public int seed;
        [Range(0f, 1f)] public float aggression;
        [Range(0f, 1f)] public float caution;
        [Range(0f, 1f)] public float discipline;
        [Range(0f, 1f)] public float initiative;
        [Range(0f, 1f)] public float teamwork;
        [Range(0f, 1f)] public float patience;
        [Range(0f, 1f)] public float sociability;
        [Range(0f, 1f)] public float humor;
        [Range(0f, 1f)] public float resentment;
        [Range(0f, 1f)] public float helpfulness;
        [Range(0f, 1f)] public float planDefiance;

        public string DominantTrait
        {
            get
            {
                KeyValuePair<string, float>[] traits =
                {
                    new KeyValuePair<string, float>("Агрессивный", aggression),
                    new KeyValuePair<string, float>("Осторожный", caution),
                    new KeyValuePair<string, float>("Дисциплинированный", discipline),
                    new KeyValuePair<string, float>("Инициативный", initiative),
                    new KeyValuePair<string, float>("Командный", teamwork),
                    new KeyValuePair<string, float>("Терпеливый", patience),
                    new KeyValuePair<string, float>("Общительный", sociability),
                    new KeyValuePair<string, float>("Шутливый", humor),
                    new KeyValuePair<string, float>("Обидчивый", resentment),
                    new KeyValuePair<string, float>("Отзывчивый", helpfulness),
                    new KeyValuePair<string, float>("Своевольный", planDefiance)
                };
                return traits.OrderByDescending(item => item.Value).ThenBy(item => item.Key, StringComparer.Ordinal)
                    .First().Key;
            }
        }
    }

    [Serializable]
    public sealed class BotIdentity
    {
        public string entityId;
        public string nickname;
        public BotPersonalityProfile personality;
    }

    [Serializable]
    public sealed class BotMentalState
    {
        [Range(0f, 1f)] public float morale = 0.65f;
        [Range(0f, 1f)] public float stress = 0.1f;
        [Range(0f, 1f)] public float boredom = 0.05f;
        [Range(0f, 1f)] public float planConfidence = 0.7f;
    }

    public static class BotPersonalityGenerator
    {
        public const int MaximumNicknameLength = 18;

        private static readonly string[] AggressiveNames =
            { "Zloy", "Kaban", "Volk", "Taran", "Berserk", "Nagibator" };
        private static readonly string[] CautiousNames =
            { "Stalker", "Tihiy", "Lesnik", "Sibiryak", "Bober", "Filin" };
        private static readonly string[] DisciplinedNames =
            { "Starshina", "Kapral", "Komandir", "Tankist", "Sanych", "Veteran" };
        private static readonly string[] InitiativeNames =
            { "Dimon", "Vovan", "Sanya", "Kolyan", "Dron", "Vitek" };
        private static readonly string[] TeamNames =
            { "Batya", "Brat", "Naparnik", "Serega", "Ruslan", "Maxon" };
        private static readonly string[] PatientNames =
            { "DedMazai", "Kremen", "Sever", "Ural", "Taezhnik", "Chasovoy" };
        private static readonly string[] SocialNames =
            { "Sanek", "Leha", "Mihalych", "Petrovich", "Andryuha", "Slavik" };
        private static readonly string[] HumorNames =
            { "Bublik", "Kote", "Pivovar", "Barmaley", "Pelmen", "Baton" };
        private static readonly string[] ResentfulNames =
            { "Hmuriy", "Kolyuchiy", "Zlopamyat", "Grumpy", "Obida", "Yazva" };
        private static readonly string[] HelpfulNames =
            { "Buksir", "Doktor", "Spasatel", "Dobryak", "Sanitar", "Podmoga" };
        private static readonly string[] DefiantNames =
            { "Buntar", "Upryamiy", "Volniy", "Ataman", "Brodyaga", "SamPoSebe" };
        private static readonly string[] Suffixes =
            { "RUS", "BY", "KZ", "UA", "Pro", "Top", "Old", "Live" };

        public static BotIdentity[] GenerateRoster(string battleId, IEnumerable<BotProfileContext> contexts)
        {
            if (string.IsNullOrWhiteSpace(battleId)) throw new ArgumentException("Battle id is empty.", nameof(battleId));
            BotProfileContext[] ordered = (contexts ?? throw new ArgumentNullException(nameof(contexts)))
                .OrderBy(item => item?.entityId, StringComparer.Ordinal)
                .ToArray();
            HashSet<string> entityIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> nicknames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<BotIdentity> result = new List<BotIdentity>(ordered.Length);
            foreach (BotProfileContext context in ordered)
            {
                if (context == null || string.IsNullOrWhiteSpace(context.entityId) || !entityIds.Add(context.entityId))
                    throw new ArgumentException("Bot profile context has an empty or duplicated entity id.", nameof(contexts));
                int seed = StableSeed(battleId + "|" + ContextKey(context));
                BotPersonalityProfile profile = GenerateProfile(context, seed);
                string nickname = MakeUniqueNickname(GenerateNickname(context, profile, seed), nicknames);
                result.Add(new BotIdentity
                {
                    entityId = context.entityId,
                    nickname = nickname,
                    personality = profile
                });
            }

            return result.ToArray();
        }

        public static BotPersonalityProfile GenerateProfile(BotProfileContext context, int seed)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            DeterministicRandom random = new DeterministicRandom(seed);
            BotPersonalityProfile profile = new BotPersonalityProfile
            {
                seed = seed,
                aggression = random.Trait(),
                caution = random.Trait(),
                discipline = random.Trait(),
                initiative = random.Trait(),
                teamwork = random.Trait(),
                patience = random.Trait(),
                sociability = random.Trait(),
                humor = random.Trait(),
                resentment = random.Trait(),
                helpfulness = random.Trait(),
                planDefiance = random.Trait()
            };

            ApplyRoleBias(profile, context.role);
            ApplyClassBias(profile, context.vehicleClass);
            Normalize(profile);
            return profile;
        }

        public static int StableSeed(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char character in value ?? string.Empty)
                {
                    hash ^= character;
                    hash *= 16777619;
                }

                return (int)(hash == 0 ? 1 : hash);
            }
        }

        private static void ApplyRoleBias(BotPersonalityProfile profile, string role)
        {
            switch (role)
            {
                case "Assault":
                    profile.aggression += 0.18f;
                    profile.discipline += 0.08f;
                    profile.patience -= 0.08f;
                    break;
                case "Support":
                    profile.teamwork += 0.18f;
                    profile.helpfulness += 0.16f;
                    profile.caution += 0.08f;
                    break;
                case "Flank":
                    profile.initiative += 0.18f;
                    profile.planDefiance += 0.08f;
                    profile.patience -= 0.06f;
                    break;
            }
        }

        private static void ApplyClassBias(BotPersonalityProfile profile, string vehicleClass)
        {
            switch (vehicleClass)
            {
                case "LightTank":
                    profile.initiative += 0.14f;
                    profile.caution -= 0.05f;
                    break;
                case "MediumTank":
                    profile.teamwork += 0.08f;
                    profile.initiative += 0.07f;
                    break;
                case "HeavyTank":
                    profile.discipline += 0.1f;
                    profile.patience += 0.1f;
                    break;
                case "TankDestroyer":
                    profile.caution += 0.12f;
                    profile.patience += 0.16f;
                    break;
                case "Artillery":
                    profile.patience += 0.18f;
                    profile.teamwork += 0.08f;
                    break;
            }
        }

        private static void Normalize(BotPersonalityProfile profile)
        {
            profile.aggression = Mathf.Clamp01(profile.aggression);
            profile.caution = Mathf.Clamp01(profile.caution);
            profile.discipline = Mathf.Clamp01(profile.discipline);
            profile.initiative = Mathf.Clamp01(profile.initiative);
            profile.teamwork = Mathf.Clamp01(profile.teamwork);
            profile.patience = Mathf.Clamp01(profile.patience);
            profile.sociability = Mathf.Clamp01(profile.sociability);
            profile.humor = Mathf.Clamp01(profile.humor);
            profile.resentment = Mathf.Clamp01(profile.resentment);
            profile.helpfulness = Mathf.Clamp01(profile.helpfulness);
            profile.planDefiance = Mathf.Clamp01(profile.planDefiance);
        }

        private static string GenerateNickname(BotProfileContext context, BotPersonalityProfile profile, int seed)
        {
            string[] firstParts = DominantNameSet(profile);
            string[] classParts = ClassNameSet(context.vehicleClass);
            uint value = unchecked((uint)seed);
            string first = firstParts[value % firstParts.Length];
            string second = classParts[(value >> 8) % classParts.Length];
            string suffix = Suffixes[(value >> 12) % Suffixes.Length];
            string number = ((value >> 16) % 90 + 10).ToString();
            string year = ((value >> 20) % 30 + 70).ToString();
            switch ((value >> 28) % 6)
            {
                case 0: return TrimNickname($"{first}{number}");
                case 1: return TrimNickname($"{first}_{suffix}");
                case 2: return TrimNickname($"xX_{first}_Xx");
                case 3: return TrimNickname($"{first}{year}");
                case 4: return TrimNickname($"{first}_{second}");
                default: return TrimNickname($"Real_{first}{number[1]}");
            }
        }

        private static string[] DominantNameSet(BotPersonalityProfile profile)
        {
            string dominant = profile.DominantTrait;
            switch (dominant)
            {
                case "Агрессивный": return AggressiveNames;
                case "Осторожный": return CautiousNames;
                case "Дисциплинированный": return DisciplinedNames;
                case "Инициативный": return InitiativeNames;
                case "Командный": return TeamNames;
                case "Терпеливый": return PatientNames;
                case "Общительный": return SocialNames;
                case "Шутливый": return HumorNames;
                case "Обидчивый": return ResentfulNames;
                case "Отзывчивый": return HelpfulNames;
                default: return DefiantNames;
            }
        }

        private static string[] ClassNameSet(string vehicleClass)
        {
            switch (vehicleClass)
            {
                case "HeavyTank": return new[] { "Heavy", "Stal", "Bronya" };
                case "MediumTank": return new[] { "Flang", "Manevr", "CT" };
                case "TankDestroyer": return new[] { "Sniper", "Kust", "PT" };
                case "Artillery": return new[] { "Arta", "Grom", "SPG" };
                default: return new[] { "Scout", "Veter", "LT" };
            }
        }

        private static string MakeUniqueNickname(string proposed, ISet<string> used)
        {
            string nickname = TrimNickname(proposed);
            int suffix = 2;
            while (!used.Add(nickname))
            {
                string postfix = "-" + suffix++;
                nickname = TrimNickname(proposed, MaximumNicknameLength - postfix.Length) + postfix;
            }

            return nickname;
        }

        private static string TrimNickname(string value, int maximumLength = MaximumNicknameLength)
        {
            string clean = new string((value ?? string.Empty)
                .Where(character => char.IsLetterOrDigit(character) || character == '-' || character == '_')
                .ToArray());
            return clean.Length <= maximumLength ? clean : clean.Substring(0, maximumLength);
        }

        private static string ContextKey(BotProfileContext context)
        {
            return $"{context.entityId}|{context.team}|{context.tankId}|{context.vehicleClass}|" +
                   $"{context.nation}|{context.lane}|{context.role}";
        }

        private struct DeterministicRandom
        {
            private uint state;

            public DeterministicRandom(int seed)
            {
                state = unchecked((uint)seed);
                if (state == 0) state = 0x9E3779B9;
            }

            public float Trait()
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return 0.22f + (state & 0x00FFFFFF) / 16777215f * 0.58f;
            }
        }
    }
}
