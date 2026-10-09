using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LocalTanks
{
    public static class BotDecisionPromptBuilder
    {
        public static LocalLlmRequest Build(
            BotKnowledgeSnapshot snapshot,
            IEnumerable<BotStrategicIntentType> allowedIntents)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            BotStrategicIntentType[] intents = (allowedIntents ?? Array.Empty<BotStrategicIntentType>())
                .Where(item => item != BotStrategicIntentType.None)
                .Distinct()
                .ToArray();
            string allowed = string.Join(", ", intents.Select(item => item.ToString()));
            BotPersonalityProfile personality = snapshot.identity?.personality;
            float temperature = personality == null
                ? 0.25f
                : Mathf.Clamp(0.12f + personality.initiative * 0.18f + personality.humor * 0.12f, 0.12f, 0.5f);
            return new LocalLlmRequest
            {
                purpose = $"bot-decision:{snapshot.battleId}:{snapshot.botId}:{snapshot.snapshotId}",
                systemPrompt =
                    "Ты отыгрываешь одного игрока-бота в одиночной танковой игре. Выбери одно редкое стратегическое намерение, " +
                    "опираясь только на переданный снимок знаний и характер. Не придумывай координаты, контакты, зоны, союзников или направления. " +
                    "Unknown-противник существует в составе, но его местоположение и HP тебе неизвестны. Remembered содержит только последнюю известную позицию. " +
                    "Не управляй мотором, башней и выстрелом: Unity исполнит намерение детерминированно. " +
                    "Используй только разрешённые намерения и ID из knownLaneIds, knownZoneIds, knownTargetIds и allyIds. " +
                    "requestId и botId скопируй без изменений. Ответ должен строго соответствовать JSON-схеме.",
                userPrompt = $"Разрешённые намерения: {allowed}\nЧестный снимок знаний:\n" +
                             JsonUtility.ToJson(snapshot) +
                             "\nВерни ровно одно решение без Markdown и дополнительного текста.",
                jsonSchema = BotStrategicDecisionSchema.BuildJsonSchema(intents),
                temperature = temperature
            };
        }
    }
}
