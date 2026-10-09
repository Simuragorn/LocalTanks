using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace LocalTanks.Tests
{
    public sealed class BotPersonalityAndDecisionTests
    {
        [Test]
        public void RosterGeneration_IsDeterministicUniqueAndBounded()
        {
            BotProfileContext[] contexts = BuildContexts();

            BotIdentity[] first = BotPersonalityGenerator.GenerateRoster("battle-001", contexts);
            BotIdentity[] second = BotPersonalityGenerator.GenerateRoster("battle-001", contexts.Reverse());

            Assert.That(JsonUtility.ToJson(new IdentityEnvelope { items = second }),
                Is.EqualTo(JsonUtility.ToJson(new IdentityEnvelope { items = first })));
            Assert.That(first.Select(item => item.nickname).Distinct(System.StringComparer.OrdinalIgnoreCase).Count(),
                Is.EqualTo(first.Length));
            Assert.That(first, Has.All.Matches<BotIdentity>(item =>
                item.nickname.Length > 0 && item.nickname.Length <= BotPersonalityGenerator.MaximumNicknameLength));
            Assert.That(first, Has.All.Matches<BotIdentity>(item => item.nickname.All(character =>
                char.IsLetterOrDigit(character) || character == '-' || character == '_')));
        }

        [Test]
        public void RoleBias_ChangesProfileWithoutChangingRandomBasis()
        {
            BotProfileContext assault = BuildContexts()[0];
            BotProfileContext support = BuildContexts()[0];
            support.role = "Support";

            BotPersonalityProfile assaultProfile = BotPersonalityGenerator.GenerateProfile(assault, 123456);
            BotPersonalityProfile supportProfile = BotPersonalityGenerator.GenerateProfile(support, 123456);

            Assert.That(assaultProfile.aggression, Is.GreaterThan(supportProfile.aggression));
            Assert.That(supportProfile.helpfulness, Is.GreaterThan(assaultProfile.helpfulness));
            AssertProfileBounds(assaultProfile);
            AssertProfileBounds(supportProfile);
        }

        [Test]
        public void StrategicDecision_ParsesStringEnumsAndValidatesKnownReferences()
        {
            const string json = "{" +
                                "\"schemaVersion\":1,\"requestId\":\"request-7\",\"botId\":\"bot-a\"," +
                                "\"intent\":\"AdvanceLane\",\"laneId\":\"lane_south\",\"zoneId\":\"\"," +
                                "\"targetId\":\"\",\"allyId\":\"\",\"desiredDistance\":12," +
                                "\"urgency\":0.7,\"durationSeconds\":15,\"explanation\":\"Обхожу по растительности\"," +
                                "\"message\":\"Иду на юг\",\"chatChannel\":\"Team\"}";

            bool parsed = BotStrategicDecisionSchema.TryParse(json, out BotStrategicDecision decision, out string error);
            string[] errors = BotStrategicDecisionValidator.Validate(decision, ValidationContext());

            Assert.That(parsed, Is.True, error);
            Assert.That(decision.intent, Is.EqualTo(BotStrategicIntentType.AdvanceLane));
            Assert.That(decision.chatChannel, Is.EqualTo(BotChatChannel.Team));
            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void StrategicDecision_RejectsInformationOutsideKnowledgeSnapshot()
        {
            BotStrategicDecision decision = new BotStrategicDecision
            {
                requestId = "request-7",
                botId = "bot-a",
                intent = BotStrategicIntentType.HuntKnownTarget,
                targetId = "hidden-enemy",
                desiredDistance = 10f,
                urgency = 0.5f,
                durationSeconds = 10f,
                explanation = "Вижу цель",
                chatChannel = BotChatChannel.None
            };

            string[] errors = BotStrategicDecisionValidator.Validate(decision, ValidationContext());

            Assert.That(errors.Any(item => item.Contains("target")), Is.True);
        }

        [Test]
        public void StrategicSchema_IsStrictAndContainsEveryIntent()
        {
            Assert.That(BotStrategicDecisionSchema.Json, Does.Contain("\"additionalProperties\":false"));
            foreach (BotStrategicIntentType intent in System.Enum.GetValues(typeof(BotStrategicIntentType)))
            {
                if (intent == BotStrategicIntentType.None) continue;
                Assert.That(BotStrategicDecisionSchema.Json, Does.Contain($"\"{intent}\""));
            }
        }

        private static BotProfileContext[] BuildContexts()
        {
            return new[]
            {
                new BotProfileContext
                {
                    entityId = "bot-a", team = "TeamA", tankId = "ms_1", vehicleClass = "LightTank",
                    nation = "USSR", lane = "South", role = "Assault"
                },
                new BotProfileContext
                {
                    entityId = "bot-b", team = "TeamB", tankId = "leichttraktor", vehicleClass = "LightTank",
                    nation = "Germany", lane = "North", role = "Flank"
                },
                new BotProfileContext
                {
                    entityId = "bot-c", team = "TeamA", tankId = "ms_1", vehicleClass = "LightTank",
                    nation = "USSR", lane = "Center", role = "Support"
                }
            };
        }

        private static BotDecisionValidationContext ValidationContext()
        {
            return new BotDecisionValidationContext
            {
                requestId = "request-7",
                botId = "bot-a",
                knownLaneIds = new[] { "lane_north", "lane_center", "lane_south" },
                knownZoneIds = new[] { "cover-1" },
                knownTargetIds = new[] { "visible-enemy" },
                allyIds = new[] { "ally-1" },
                maximumDesiredDistance = 50f
            };
        }

        private static void AssertProfileBounds(BotPersonalityProfile profile)
        {
            float[] values =
            {
                profile.aggression, profile.caution, profile.discipline, profile.initiative,
                profile.teamwork, profile.patience, profile.sociability, profile.humor,
                profile.resentment, profile.helpfulness, profile.planDefiance
            };
            foreach (float value in values) Assert.That(value, Is.InRange(0f, 1f));
        }

        [System.Serializable]
        private sealed class IdentityEnvelope
        {
            public BotIdentity[] items;
        }
    }
}
