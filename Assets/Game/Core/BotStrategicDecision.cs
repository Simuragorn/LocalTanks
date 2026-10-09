using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    public enum BotStrategicIntentType
    {
        None,
        AdvanceLane,
        HoldZone,
        ScoutLane,
        SupportAlly,
        DefendBase,
        Reposition,
        HuntKnownTarget,
        TakeCover,
        AngleArmor,
        WithdrawUnderFire,
        BreakCrossfire,
        PressAttack,
        Retreat,
        Wait
    }

    public enum BotChatChannel
    {
        None,
        Team,
        Global
    }

    [Serializable]
    public sealed class BotStrategicDecision
    {
        public int schemaVersion = BotStrategicDecisionSchema.SchemaVersion;
        public string requestId;
        public string botId;
        public BotStrategicIntentType intent;
        public string laneId;
        public string zoneId;
        public string targetId;
        public string allyId;
        public float desiredDistance;
        public float urgency;
        public float durationSeconds;
        public string explanation;
        public string message;
        public BotChatChannel chatChannel;
    }

    [Serializable]
    public sealed class BotStrategicDecisionJson
    {
        public int schemaVersion;
        public string requestId;
        public string botId;
        public string intent;
        public string laneId;
        public string zoneId;
        public string targetId;
        public string allyId;
        public float desiredDistance;
        public float urgency;
        public float durationSeconds;
        public string explanation;
        public string message;
        public string chatChannel;
    }

    [Serializable]
    public sealed class BotDecisionValidationContext
    {
        public string requestId;
        public string botId;
        public string[] knownLaneIds;
        public string[] knownZoneIds;
        public string[] knownTargetIds;
        public string[] allyIds;
        public float maximumDesiredDistance = 100f;
    }

    public static class BotStrategicDecisionSchema
    {
        public const int SchemaVersion = 1;
        public const string Json = "{" +
            "\"type\":\"object\"," +
            "\"additionalProperties\":false," +
            "\"properties\":{" +
            "\"schemaVersion\":{\"type\":\"integer\",\"const\":1}," +
            "\"requestId\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":80}," +
            "\"botId\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":80}," +
            "\"intent\":{\"type\":\"string\",\"enum\":[\"AdvanceLane\",\"HoldZone\",\"ScoutLane\",\"SupportAlly\",\"DefendBase\",\"Reposition\",\"HuntKnownTarget\",\"TakeCover\",\"AngleArmor\",\"WithdrawUnderFire\",\"BreakCrossfire\",\"PressAttack\",\"Retreat\",\"Wait\"]}," +
            "\"laneId\":{\"type\":\"string\"}," +
            "\"zoneId\":{\"type\":\"string\"}," +
            "\"targetId\":{\"type\":\"string\"}," +
            "\"allyId\":{\"type\":\"string\"}," +
            "\"desiredDistance\":{\"type\":\"number\",\"minimum\":0}," +
            "\"urgency\":{\"type\":\"number\",\"minimum\":0,\"maximum\":1}," +
            "\"durationSeconds\":{\"type\":\"number\",\"minimum\":1,\"maximum\":60}," +
            "\"explanation\":{\"type\":\"string\",\"maxLength\":240}," +
            "\"message\":{\"type\":\"string\",\"maxLength\":140}," +
            "\"chatChannel\":{\"type\":\"string\",\"enum\":[\"None\",\"Team\",\"Global\"]}" +
            "}," +
            "\"required\":[\"schemaVersion\",\"requestId\",\"botId\",\"intent\",\"laneId\",\"zoneId\",\"targetId\",\"allyId\",\"desiredDistance\",\"urgency\",\"durationSeconds\",\"explanation\",\"message\",\"chatChannel\"]" +
            "}";

        public static bool TryParse(string json, out BotStrategicDecision decision, out string error)
        {
            decision = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Decision JSON is empty.";
                return false;
            }

            BotStrategicDecisionJson raw;
            try
            {
                raw = JsonUtility.FromJson<BotStrategicDecisionJson>(json);
            }
            catch (Exception exception)
            {
                error = $"Decision JSON is invalid: {exception.Message}";
                return false;
            }

            if (raw == null || !Enum.TryParse(raw.intent, false, out BotStrategicIntentType intent) ||
                intent == BotStrategicIntentType.None ||
                !Enum.TryParse(raw.chatChannel, false, out BotChatChannel chatChannel))
            {
                error = "Decision contains an unknown intent or chat channel.";
                return false;
            }

            decision = new BotStrategicDecision
            {
                schemaVersion = raw.schemaVersion,
                requestId = raw.requestId,
                botId = raw.botId,
                intent = intent,
                laneId = raw.laneId,
                zoneId = raw.zoneId,
                targetId = raw.targetId,
                allyId = raw.allyId,
                desiredDistance = raw.desiredDistance,
                urgency = raw.urgency,
                durationSeconds = raw.durationSeconds,
                explanation = raw.explanation,
                message = raw.message,
                chatChannel = chatChannel
            };
            return true;
        }
    }

    public static class BotStrategicDecisionValidator
    {
        public static string[] Validate(BotStrategicDecision decision, BotDecisionValidationContext context)
        {
            List<string> errors = new List<string>();
            if (decision == null)
            {
                errors.Add("Decision is null.");
                return errors.ToArray();
            }

            if (context == null)
            {
                errors.Add("Validation context is null.");
                return errors.ToArray();
            }

            if (decision.schemaVersion != BotStrategicDecisionSchema.SchemaVersion)
                errors.Add($"Unsupported decision schema version {decision.schemaVersion}.");
            if (!string.Equals(decision.requestId, context.requestId, StringComparison.Ordinal))
                errors.Add("Decision requestId does not match the active request.");
            if (!string.Equals(decision.botId, context.botId, StringComparison.Ordinal))
                errors.Add("Decision botId does not match the active bot.");
            if (decision.intent == BotStrategicIntentType.None || !Enum.IsDefined(typeof(BotStrategicIntentType), decision.intent))
                errors.Add("Decision intent is unknown.");
            if (decision.urgency < 0f || decision.urgency > 1f) errors.Add("Decision urgency is outside 0..1.");
            if (decision.durationSeconds < 1f || decision.durationSeconds > 60f)
                errors.Add("Decision duration is outside 1..60 seconds.");
            if (decision.desiredDistance < 0f || decision.desiredDistance > context.maximumDesiredDistance)
                errors.Add("Decision desired distance is outside the allowed range.");
            if ((decision.explanation ?? string.Empty).Length > 240) errors.Add("Decision explanation is too long.");
            if ((decision.message ?? string.Empty).Length > 140) errors.Add("Decision message is too long.");
            if (decision.chatChannel == BotChatChannel.None && !string.IsNullOrWhiteSpace(decision.message))
                errors.Add("Decision contains a message without a chat channel.");
            if (decision.chatChannel != BotChatChannel.None && string.IsNullOrWhiteSpace(decision.message))
                errors.Add("Decision selects a chat channel without a message.");

            bool laneRequired = decision.intent == BotStrategicIntentType.AdvanceLane ||
                                decision.intent == BotStrategicIntentType.ScoutLane;
            bool zoneRequired = decision.intent == BotStrategicIntentType.HoldZone ||
                                decision.intent == BotStrategicIntentType.TakeCover ||
                                decision.intent == BotStrategicIntentType.Reposition;
            ValidateReference(errors, "lane", decision.laneId, context.knownLaneIds, laneRequired);
            ValidateReference(errors, "zone", decision.zoneId, context.knownZoneIds, zoneRequired);
            ValidateReference(errors, "target", decision.targetId, context.knownTargetIds,
                decision.intent == BotStrategicIntentType.HuntKnownTarget);
            ValidateReference(errors, "ally", decision.allyId, context.allyIds,
                decision.intent == BotStrategicIntentType.SupportAlly);
            return errors.ToArray();
        }

        private static void ValidateReference(
            ICollection<string> errors,
            string label,
            string value,
            string[] allowed,
            bool required)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (required) errors.Add($"Decision requires a {label} reference.");
                return;
            }

            if (!Contains(allowed, value)) errors.Add($"Decision {label} is not available in the knowledge snapshot.");
        }

        private static bool Contains(string[] values, string expected)
        {
            if (string.IsNullOrWhiteSpace(expected)) return false;
            foreach (string value in values ?? Array.Empty<string>())
            {
                if (string.Equals(value, expected, StringComparison.Ordinal)) return true;
            }

            return false;
        }
    }
}
