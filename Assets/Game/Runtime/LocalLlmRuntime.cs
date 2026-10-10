using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace LocalTanks
{
    public enum LocalLlmRuntimeState
    {
        Disabled,
        Checking,
        Starting,
        Ready,
        Unavailable
    }

    public sealed class LocalLlmRuntime : MonoBehaviour
    {
        [Serializable]
        private sealed class ModelProbeRequest
        {
            public string model;
        }

        private static LocalLlmRuntime instance;

        private Process ownedProcess;
        private bool shuttingDown;

        public static LocalLlmRuntime Instance => instance;
        public LocalLlmSettings Settings { get; private set; }
        public LocalLlmRuntimeState State { get; private set; } = LocalLlmRuntimeState.Disabled;
        public string StatusMessage { get; private set; } = "Local LLM is disabled.";
        public bool OwnsServerProcess => ownedProcess != null;
        public ILocalLlmProvider Provider { get; private set; }

        public event Action<LocalLlmRuntimeState, string> StateChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateRuntime()
        {
            if (Application.isBatchMode || instance != null)
            {
                return;
            }

            GameObject root = new GameObject("LocalLlmRuntime");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<LocalLlmRuntime>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            Settings = LocalLlmSettings.Load();
            Provider = new OllamaLlmProvider(Settings);
        }

        private IEnumerator Start()
        {
            string[] errors = Settings.Validate();
            if (errors.Length > 0)
            {
                SetState(LocalLlmRuntimeState.Unavailable, string.Join(" ", errors));
                yield break;
            }

            if (!Settings.autoStart)
            {
                SetState(LocalLlmRuntimeState.Disabled, "Automatic local LLM startup is disabled.");
                yield break;
            }

            SetState(LocalLlmRuntimeState.Checking, "Checking local Ollama server.");
            bool available = false;
            yield return Probe(result => available = result);
            if (available)
            {
                bool modelAvailable = false;
                yield return ProbeModel(result => modelAvailable = result);
                if (!modelAvailable)
                {
                    SetState(LocalLlmRuntimeState.Unavailable,
                        $"Ollama is running, but model '{Settings.model}' is unavailable. Deterministic AI fallback remains active.");
                    yield break;
                }

                SetState(LocalLlmRuntimeState.Ready,
                    $"Using an already running Ollama server with model '{Settings.model}'.");
                yield break;
            }

            string executablePath = ResolveProjectPath(Settings.relativeExecutablePath);
            if (!File.Exists(executablePath))
            {
                SetState(LocalLlmRuntimeState.Unavailable,
                    $"Ollama executable was not found at '{executablePath}'. Deterministic AI fallback remains active.");
                yield break;
            }

            string modelPath = ResolveProjectPath(Settings.relativeModelPath);
            Directory.CreateDirectory(modelPath);
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = "serve",
                    WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                startInfo.EnvironmentVariables["OLLAMA_HOST"] = new Uri(Settings.baseUrl).Authority;
                startInfo.EnvironmentVariables["OLLAMA_MODELS"] = modelPath;
                startInfo.EnvironmentVariables["OLLAMA_KEEP_ALIVE"] = Settings.keepAlive;
                startInfo.EnvironmentVariables["OLLAMA_NUM_PARALLEL"] = "1";
                startInfo.EnvironmentVariables["OLLAMA_MAX_LOADED_MODELS"] = "1";
                ownedProcess = Process.Start(startInfo);
                TryLowerProcessPriority(ownedProcess);
            }
            catch (Exception exception)
            {
                SetState(LocalLlmRuntimeState.Unavailable,
                    $"Could not start Ollama: {exception.Message}. Deterministic AI fallback remains active.");
                yield break;
            }

            SetState(LocalLlmRuntimeState.Starting, "Starting project-owned Ollama server.");
            float deadline = Time.realtimeSinceStartup + Settings.startupTimeoutSeconds;
            while (!shuttingDown && Time.realtimeSinceStartup < deadline)
            {
                if (ownedProcess == null || ownedProcess.HasExited)
                {
                    SetState(LocalLlmRuntimeState.Unavailable,
                        "Project-owned Ollama process exited during startup. Deterministic AI fallback remains active.");
                    yield break;
                }

                yield return Probe(result => available = result);
                if (available)
                {
                    bool modelAvailable = false;
                    yield return ProbeModel(result => modelAvailable = result);
                    if (!modelAvailable)
                    {
                        SetState(LocalLlmRuntimeState.Unavailable,
                            $"Project-owned Ollama started, but model '{Settings.model}' is unavailable. Deterministic AI fallback remains active.");
                        StopOwnedProcess();
                        yield break;
                    }

                    SetState(LocalLlmRuntimeState.Ready,
                        $"Project-owned Ollama server is ready with model '{Settings.model}'.");
                    yield break;
                }

                yield return new WaitForSecondsRealtime(0.25f);
            }

            SetState(LocalLlmRuntimeState.Unavailable,
                "Ollama startup timed out. Deterministic AI fallback remains active.");
            StopOwnedProcess();
        }

        private IEnumerator Probe(Action<bool> completed)
        {
            string endpoint = Settings.baseUrl.TrimEnd('/') + "/api/version";
            using (UnityWebRequest request = UnityWebRequest.Get(endpoint))
            {
                request.timeout = 2;
                yield return request.SendWebRequest();
                completed?.Invoke(request.result == UnityWebRequest.Result.Success);
            }
        }

        private IEnumerator ProbeModel(Action<bool> completed)
        {
            string endpoint = Settings.baseUrl.TrimEnd('/') + "/api/show";
            string json = JsonUtility.ToJson(new ModelProbeRequest { model = Settings.model });
            byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
            using (UnityWebRequest request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 5;
                yield return request.SendWebRequest();
                completed?.Invoke(request.result == UnityWebRequest.Result.Success);
            }
        }

        private void SetState(LocalLlmRuntimeState state, string message)
        {
            State = state;
            StatusMessage = message ?? string.Empty;
            StateChanged?.Invoke(State, StatusMessage);
            if (state == LocalLlmRuntimeState.Unavailable) Debug.LogWarning(StatusMessage, this);
            else Debug.Log(StatusMessage, this);
        }

        private static string ResolveProjectPath(string relativePath)
        {
            string overriddenRoot = Environment.GetEnvironmentVariable("LOCALTANKS_ROOT");
            string root = string.IsNullOrWhiteSpace(overriddenRoot)
                ? Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
                : Path.GetFullPath(overriddenRoot);
            return Path.GetFullPath(Path.Combine(root, relativePath ?? string.Empty));
        }

        private void OnApplicationQuit()
        {
            shuttingDown = true;
            StopOwnedProcess();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            StopOwnedProcess();
        }

        private void StopOwnedProcess()
        {
            Process process = ownedProcess;
            ownedProcess = null;
            if (process == null)
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    KillProcessTree(process);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not stop project-owned Ollama process: {exception.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }

        private static void KillProcessTree(Process process)
        {
            if (Application.platform == RuntimePlatform.WindowsEditor ||
                Application.platform == RuntimePlatform.WindowsPlayer)
            {
                try
                {
                    using (Process treeKiller = Process.Start(new ProcessStartInfo
                           {
                               FileName = "taskkill.exe",
                               Arguments = $"/PID {process.Id} /T /F",
                               UseShellExecute = false,
                               CreateNoWindow = true,
                               WindowStyle = ProcessWindowStyle.Hidden
                           }))
                    {
                        treeKiller?.WaitForExit(2000);
                    }
                }
                catch (Exception)
                {
                    // The direct kill below is still safe for platforms without taskkill.
                }
            }

            if (!process.HasExited)
            {
                process.Kill();
            }

            process.WaitForExit(2000);
        }

        private static void TryLowerProcessPriority(Process process)
        {
            if (process == null) return;
            try
            {
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not lower Ollama process priority: {exception.Message}");
            }
        }
    }
}
