using System.Collections;
using NUnit.Framework;
using UnityEngine;

namespace LocalTanks.Tests
{
    public sealed class LocalLlmFoundationTests
    {
        [Test]
        public void DefaultSettings_AreValidAndDoNotUseCloudEndpoint()
        {
            LocalLlmSettings settings = new LocalLlmSettings();

            Assert.That(settings.Validate(), Is.Empty);
            Assert.That(settings.baseUrl, Does.StartWith("http://127.0.0.1"));
            Assert.That(settings.model, Is.EqualTo("qwen3:8b"));
            Assert.That(settings.relativeExecutablePath, Does.StartWith("LocalData/"));
            Assert.That(settings.gpuLayerCount, Is.Zero);
            Assert.That(settings.cpuThreadCount, Is.EqualTo(4));
            Assert.That(settings.requestInitialBotDecisions, Is.False);
            Assert.That(settings.enableDynamicBotDecisions, Is.False,
                "Live LLM replanning is opt-in so inference cannot cause periodic battle hitches.");
            Assert.That(settings.requestOnContactChanges, Is.False);
        }

        [Test]
        public void Settings_RejectRemoteServer()
        {
            LocalLlmSettings settings = new LocalLlmSettings
            {
                baseUrl = "https://example.com"
            };

            string[] errors = settings.Validate();
            Assert.That(errors, Has.Length.EqualTo(1));
            Assert.That(errors[0], Does.Contain("loopback"));
        }

        [Test]
        public void OllamaRequest_UsesStructuredSchemaAndEscapesPrompts()
        {
            LocalLlmSettings settings = new LocalLlmSettings();
            LocalLlmRequest request = new LocalLlmRequest
            {
                systemPrompt = "Ты бот \"Резкий\".",
                userPrompt = "Выбери путь.\nТолько JSON.",
                jsonSchema = "{\"type\":\"object\",\"properties\":{\"lane\":{\"type\":\"string\"}},\"required\":[\"lane\"]}",
                temperature = 0.35f
            };

            string json = OllamaLlmProvider.BuildRequestJson(settings, request);

            Assert.That(json, Does.Contain("\"model\":\"qwen3:8b\""));
            Assert.That(json, Does.Contain("\"format\":{\"type\":\"object\""));
            Assert.That(json, Does.Contain("\\\"Резкий\\\""));
            Assert.That(json, Does.Contain("Выбери путь.\\nТолько JSON."));
            Assert.That(json, Does.Contain("\"temperature\":0.35"));
            Assert.That(json, Does.Contain("\"num_gpu\":0"));
            Assert.That(json, Does.Contain("\"num_thread\":4"));
            Assert.That(json, Does.Contain("\"num_ctx\":4096"));
            Assert.That(json, Does.Contain("\"num_predict\":320"));
            Assert.That(json, Does.Contain("\"think\":false"));
        }

        [Test]
        public void RuntimePolicy_SkipsRedundantInitialPerBotRequests()
        {
            LocalLlmSettings settings = new LocalLlmSettings { requestInitialBotDecisions = false };

            Assert.That(LocalLlmRequestPolicy.ShouldRunInitialBotRequests(settings), Is.False);
            settings.requestInitialBotDecisions = true;
            Assert.That(LocalLlmRequestPolicy.ShouldRunInitialBotRequests(settings), Is.True);
        }

        [Test]
        public void RuntimePolicy_DropsContactNoiseAndRequestsDuringCooldown()
        {
            LocalLlmSettings settings = new LocalLlmSettings
            {
                enableDynamicBotDecisions = true,
                requestOnContactChanges = false
            };

            Assert.That(LocalLlmRequestPolicy.CanQueueDynamic(settings, "contact-spotted", 100f, 0f, false), Is.False);
            Assert.That(LocalLlmRequestPolicy.CanQueueDynamic(settings, "significant-damage", 100f, 120f, false), Is.False);
            Assert.That(LocalLlmRequestPolicy.CanQueueDynamic(settings, "significant-damage", 130f, 120f, true), Is.False);
            Assert.That(LocalLlmRequestPolicy.CanQueueDynamic(settings, "significant-damage", 130f, 120f, false), Is.True);
        }

        [Test]
        public void FakeProvider_ReturnsQueuedResponseAndRecordsRequest()
        {
            FakeLocalLlmProvider provider = new FakeLocalLlmProvider();
            provider.Enqueue(LocalLlmResponse.Success("{\"lane\":\"north\"}"));
            LocalLlmRequest request = new LocalLlmRequest { purpose = "map-plan" };
            LocalLlmResponse received = null;

            IEnumerator routine = provider.Generate(request, response => received = response);
            while (routine.MoveNext()) { }

            Assert.That(received, Is.Not.Null);
            Assert.That(received.Succeeded, Is.True);
            Assert.That(received.Content, Does.Contain("north"));
            Assert.That(provider.Requests, Has.Count.EqualTo(1));
            Assert.That(provider.Requests[0], Is.SameAs(request));
        }

        [Test]
        public void InvalidSettings_ReportEveryBlockingProblem()
        {
            LocalLlmSettings settings = new LocalLlmSettings
            {
                provider = "CloudOnly",
                baseUrl = "not-a-url",
                model = string.Empty,
                relativeExecutablePath = string.Empty,
                relativeModelPath = string.Empty,
                startupTimeoutSeconds = 0,
                requestTimeoutSeconds = 0,
                keepAlive = string.Empty,
                gpuLayerCount = -1,
                cpuThreadCount = 0,
                contextTokens = 0,
                maxOutputTokens = 0,
                dynamicDecisionCooldownSeconds = 0f
            };

            Assert.That(settings.Validate(), Has.Length.EqualTo(13));
        }
    }
}
