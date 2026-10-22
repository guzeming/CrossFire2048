using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OperationBlacktide.Client.Features.Lobby
{
    /// <summary>Only an explicit download click starts this process. It survives closing settings.</summary>
    public sealed class LocalAssistantInstallation : MonoBehaviour
    {
        public const string SpeechModel = "sherpa-onnx-streaming-zipformer-small-ctc-zh-int8-2025-04-01";
        private static LocalAssistantInstallation instance;
        private LocalAssistantProcess process;
        public static string InstallPath => Path.Combine(Application.persistentDataPath, "LocalAssistant");
        public bool IsBusy => process != null;
        public bool IsCancelling { get; private set; }
        public float Progress { get; private set; }
        public string Status { get; private set; } = "";
        public event Action Changed;
        public static bool Supported => Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;
        public static LocalAssistantInstallation Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new GameObject("LocalAssistantDownloads").AddComponent<LocalAssistantInstallation>();
                    DontDestroyOnLoad(instance.gameObject);
                }
                return instance;
            }
        }
        [Serializable] private sealed class Message { public string type, text; public float progress; }

        public static bool ModelsInstalled(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                return new FileInfo(Path.Combine(path, "dialogue.gguf")).Length == 1117320736L
                    && File.Exists(Path.Combine(path, "llama", "llama-server.exe"))
                    && new FileInfo(Path.Combine(path, SpeechModel, "model.int8.onnx")).Length == 26342340L
                    && File.Exists(Path.Combine(path, SpeechModel, "tokens.txt"));
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
        private static IEnumerable<string> ModelCandidates()
        {
            yield return InstallPath;
            string configured = Environment.GetEnvironmentVariable("BLACKTIDE_ASSISTANT_HOME");
            if (!string.IsNullOrWhiteSpace(configured)) yield return configured;
            yield return Path.Combine(Path.GetDirectoryName(Application.dataPath), "LocalAssistant");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var directory = new DirectoryInfo(Application.dataPath);
            for (int i = 0; directory != null && i < 6; i++, directory = directory.Parent)
                yield return Path.Combine(directory.FullName, "LocalAssistant");
#endif
        }
        public static string FindModels()
        {
            foreach (string path in ModelCandidates()) if (ModelsInstalled(path)) return Path.GetFullPath(path);
            return null;
        }
        public static string FindHost(string modelRoot = null)
        {
            const string executable = "Blacktide.LocalAssistant.exe";
            string bundled = Path.Combine(Path.GetDirectoryName(Application.dataPath), "AssistantRuntime", executable);
            if (File.Exists(bundled)) return bundled;
            if (modelRoot != null && File.Exists(Path.Combine(modelRoot, "host", executable))) return Path.Combine(modelRoot, "host", executable);
            foreach (string path in ModelCandidates())
                if (File.Exists(Path.Combine(path, "host", executable))) return Path.Combine(path, "host", executable);
            return null;
        }
        public void Download()
        {
            if (IsBusy || !Supported || FindModels() != null) return;
            string host = FindHost();
            if (host == null) { Status = "助手安装程序缺失，请重新安装游戏"; Changed?.Invoke(); return; }
            try
            {
                Progress = 0; IsCancelling = false; Status = "正在准备下载…";
                process = new LocalAssistantProcess(host, InstallPath, true);
            }
            catch (Exception) { Status = "无法开始下载，请检查磁盘权限后重试"; }
            Changed?.Invoke();
        }
        public void Cancel()
        {
            if (!IsBusy || IsCancelling) return;
            IsCancelling = true; Status = "正在取消…";
            process.Send("{\"type\":\"cancel\"}"); Changed?.Invoke();
        }
        private void Update()
        {
            if (process == null) return;
            while (process != null && process.TryRead(out string line))
            {
                Message message;
                try { message = JsonUtility.FromJson<Message>(line); } catch (Exception) { continue; }
                if (message == null) continue;
                if (message.type == "progress")
                {
                    Progress = Mathf.Clamp01(message.progress);
                    if (!IsCancelling) Status = message.text;
                }
                else if (message.type == "installed" || message.type == "cancelled" || message.type == "error")
                {
                    Status = message.text;
                    if (message.type == "installed") Progress = 1;
                    Stop();
                }
                Changed?.Invoke();
            }
            if (process != null && process.HasExited)
            {
                Status = "下载已中断，可重新下载"; Stop(); Changed?.Invoke();
            }
        }
        private void Stop() { process?.Dispose(); process = null; IsCancelling = false; }
        private void OnApplicationQuit() { Stop(); }
        private void OnDestroy() { Stop(); if (instance == this) instance = null; }
    }
}
