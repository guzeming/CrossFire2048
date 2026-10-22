using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace OperationBlacktide.Client.Features.Lobby
{
    /// <summary>Only local child-process pipes. Audio and transcripts never leave this machine.</summary>
    internal sealed class LocalAssistantProcess : IDisposable
    {
        private readonly Process process;
        private readonly BlockingCollection<string> outgoing = new BlockingCollection<string>(64);
        private readonly ConcurrentQueue<string> incoming = new ConcurrentQueue<string>();
        private bool disposed;
        public bool HasExited => process.HasExited;

        public LocalAssistantProcess(string executable, string root, bool install = false)
        {
            if (!File.Exists(executable)) throw new FileNotFoundException("请先安装本地助手模型");
            process = new Process { StartInfo = new ProcessStartInfo
            {
                FileName = executable, Arguments = (install ? "--install " : "") + "\"" + root.TrimEnd('\\', '/') + "\"",
                WorkingDirectory = Path.GetDirectoryName(executable), UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false)
            }};
            process.OutputDataReceived += OnOutput;
            // Drain native diagnostics without logging the player's speech or filling a pipe.
            process.ErrorDataReceived += OnError;
            process.Start(); process.StandardInput.AutoFlush = true;
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            Task.Run(WriteLoop);
        }
        private void OnOutput(object sender, DataReceivedEventArgs e) { if (!disposed && e.Data != null) incoming.Enqueue(e.Data); }
        private void OnError(object sender, DataReceivedEventArgs e) { }
        public bool TryRead(out string message) => incoming.TryDequeue(out message);
        public bool Send(string message) => !disposed && !outgoing.IsAddingCompleted && outgoing.TryAdd(message);
        private void WriteLoop()
        {
            try { foreach (string message in outgoing.GetConsumingEnumerable()) process.StandardInput.WriteLine(message); }
            catch (Exception) { if (!disposed) incoming.Enqueue("{\"type\":\"fatal\",\"text\":\"本地助手连接中断，请按 T 重试\"}"); }
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            outgoing.CompleteAdding();
            // Never wait for process shutdown on Unity's frame thread.
            Task.Run(() =>
            {
                try
                {
                    process.StandardInput.Close();
                    if (!process.WaitForExit(1500)) process.Kill();
                }
                catch (Exception) { }
                finally { process.Dispose(); }
            });
        }
    }
}
