using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace LocalTanks
{
    [Serializable]
    public sealed class LocalLlmRequest
    {
        public string purpose;
        public string systemPrompt;
        public string userPrompt;
        public string jsonSchema;
        [Range(0f, 2f)] public float temperature = 0.2f;
    }

    public sealed class LocalLlmResponse
    {
        private LocalLlmResponse(bool succeeded, string content, string error, long durationMilliseconds)
        {
            Succeeded = succeeded;
            Content = content ?? string.Empty;
            Error = error ?? string.Empty;
            DurationMilliseconds = Math.Max(0L, durationMilliseconds);
        }

        public bool Succeeded { get; }
        public string Content { get; }
        public string Error { get; }
        public long DurationMilliseconds { get; }

        public static LocalLlmResponse Success(string content, long durationMilliseconds = 0L)
        {
            return new LocalLlmResponse(true, content, string.Empty, durationMilliseconds);
        }

        public static LocalLlmResponse Failure(string error, long durationMilliseconds = 0L)
        {
            return new LocalLlmResponse(false, string.Empty, error, durationMilliseconds);
        }
    }

    public interface ILocalLlmProvider
    {
        string ProviderName { get; }
        IEnumerator Generate(LocalLlmRequest request, Action<LocalLlmResponse> completed);
    }

    public sealed class FakeLocalLlmProvider : ILocalLlmProvider
    {
        private readonly Queue<LocalLlmResponse> responses = new Queue<LocalLlmResponse>();

        public string ProviderName => "Fake";
        public IReadOnlyList<LocalLlmRequest> Requests => requests;

        private readonly List<LocalLlmRequest> requests = new List<LocalLlmRequest>();

        public void Enqueue(LocalLlmResponse response)
        {
            responses.Enqueue(response ?? LocalLlmResponse.Failure("Fake response was null."));
        }

        public IEnumerator Generate(LocalLlmRequest request, Action<LocalLlmResponse> completed)
        {
            requests.Add(request);
            LocalLlmResponse response = responses.Count > 0
                ? responses.Dequeue()
                : LocalLlmResponse.Failure("Fake provider has no queued response.");
            completed?.Invoke(response);
            yield break;
        }
    }

    public sealed class OllamaLlmProvider : ILocalLlmProvider
    {
        [Serializable]
        private sealed class OllamaMessage
        {
            public string role;
            public string content;
        }

        [Serializable]
        private sealed class OllamaEnvelope
        {
            public OllamaMessage message;
            public bool done;
            public string done_reason;
            public long total_duration;
        }

        private readonly LocalLlmSettings settings;

        public OllamaLlmProvider(LocalLlmSettings providerSettings)
        {
            settings = providerSettings ?? throw new ArgumentNullException(nameof(providerSettings));
        }

        public string ProviderName => "Ollama";

        public IEnumerator Generate(LocalLlmRequest request, Action<LocalLlmResponse> completed)
        {
            if (request == null)
            {
                completed?.Invoke(LocalLlmResponse.Failure("Local LLM request is null."));
                yield break;
            }

            string endpoint = settings.baseUrl.TrimEnd('/') + "/api/chat";
            string json = BuildRequestJson(settings, request);
            byte[] body = Encoding.UTF8.GetBytes(json);
            using (UnityWebRequest webRequest = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                webRequest.uploadHandler = new UploadHandlerRaw(body);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
                webRequest.timeout = Math.Max(1, settings.requestTimeoutSeconds);
                float startedAt = Time.realtimeSinceStartup;
                yield return webRequest.SendWebRequest();
                long duration = (long)((Time.realtimeSinceStartup - startedAt) * 1000f);
                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    completed?.Invoke(LocalLlmResponse.Failure(
                        $"Ollama request failed ({webRequest.responseCode}): {webRequest.error}",
                        duration));
                    yield break;
                }

                OllamaEnvelope envelope;
                try
                {
                    envelope = JsonUtility.FromJson<OllamaEnvelope>(webRequest.downloadHandler.text);
                }
                catch (Exception exception)
                {
                    completed?.Invoke(LocalLlmResponse.Failure(
                        $"Ollama returned invalid response JSON: {exception.Message}", duration));
                    yield break;
                }

                if (envelope?.message == null || string.IsNullOrWhiteSpace(envelope.message.content))
                {
                    completed?.Invoke(LocalLlmResponse.Failure("Ollama response has no message content.", duration));
                    yield break;
                }

                long reportedDuration = envelope.total_duration > 0
                    ? envelope.total_duration / 1_000_000L
                    : duration;
                completed?.Invoke(LocalLlmResponse.Success(envelope.message.content, reportedDuration));
            }
        }

        public static string BuildRequestJson(LocalLlmSettings settings, LocalLlmRequest request)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (request == null) throw new ArgumentNullException(nameof(request));
            string schema = string.IsNullOrWhiteSpace(request.jsonSchema) ? "\"json\"" : request.jsonSchema.Trim();
            return "{" +
                   "\"model\":" + Quote(settings.model) + "," +
                   "\"messages\":[" +
                   "{\"role\":\"system\",\"content\":" + Quote(request.systemPrompt) + "}," +
                   "{\"role\":\"user\",\"content\":" + Quote(request.userPrompt) + "}" +
                   "]," +
                   "\"stream\":false," +
                   "\"think\":false," +
                   "\"format\":" + schema + "," +
                   "\"keep_alive\":" + Quote(settings.keepAlive) + "," +
                   "\"options\":{\"temperature\":" +
                   Mathf.Clamp(request.temperature, 0f, 2f).ToString("0.###", CultureInfo.InvariantCulture) + "}" +
                   "}";
        }

        private static string Quote(string value)
        {
            if (value == null) return "\"\"";
            StringBuilder result = new StringBuilder(value.Length + 8);
            result.Append('"');
            foreach (char character in value)
            {
                switch (character)
                {
                    case '"': result.Append("\\\""); break;
                    case '\\': result.Append("\\\\"); break;
                    case '\n': result.Append("\\n"); break;
                    case '\r': result.Append("\\r"); break;
                    case '\t': result.Append("\\t"); break;
                    default:
                        if (character < 32) result.Append("\\u").Append(((int)character).ToString("x4"));
                        else result.Append(character);
                        break;
                }
            }

            result.Append('"');
            return result.ToString();
        }
    }
}
