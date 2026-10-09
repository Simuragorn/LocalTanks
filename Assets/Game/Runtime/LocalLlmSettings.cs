using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    [Serializable]
    public sealed class LocalLlmSettings
    {
        public const string ResourceName = "LocalLlmSettings";

        public string provider = "Ollama";
        public bool autoStart = true;
        public string baseUrl = "http://127.0.0.1:11434";
        public string model = "qwen3:8b";
        public string relativeExecutablePath = "LocalData/Ollama/ollama.exe";
        public string relativeModelPath = "LocalData/OllamaModels";
        public int startupTimeoutSeconds = 45;
        public int requestTimeoutSeconds = 30;
        public string keepAlive = "10m";

        public static LocalLlmSettings Load()
        {
            TextAsset asset = Resources.Load<TextAsset>(ResourceName);
            if (asset == null)
            {
                Debug.LogWarning($"Local LLM settings resource '{ResourceName}' is missing. Defaults will be used.");
                return new LocalLlmSettings();
            }

            LocalLlmSettings settings = JsonUtility.FromJson<LocalLlmSettings>(asset.text);
            return settings ?? new LocalLlmSettings();
        }

        public string[] Validate()
        {
            List<string> errors = new List<string>();
            if (!string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Unsupported local LLM provider '{provider}'.");
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri endpoint) ||
                endpoint.Scheme != Uri.UriSchemeHttp || !endpoint.IsLoopback)
                errors.Add("Local LLM baseUrl must be an absolute loopback HTTP URL.");
            if (string.IsNullOrWhiteSpace(model)) errors.Add("Local LLM model is empty.");
            if (string.IsNullOrWhiteSpace(relativeExecutablePath))
                errors.Add("Local LLM executable path is empty.");
            if (string.IsNullOrWhiteSpace(relativeModelPath)) errors.Add("Local LLM model path is empty.");
            if (startupTimeoutSeconds < 1) errors.Add("Local LLM startup timeout must be positive.");
            if (requestTimeoutSeconds < 1) errors.Add("Local LLM request timeout must be positive.");
            if (string.IsNullOrWhiteSpace(keepAlive)) errors.Add("Local LLM keepAlive is empty.");
            return errors.ToArray();
        }
    }
}
