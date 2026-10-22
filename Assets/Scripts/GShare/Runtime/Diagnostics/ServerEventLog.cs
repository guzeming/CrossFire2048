#nullable enable
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace OperationBlacktide.Shared.Diagnostics
{
    // Bounded asynchronous JSONL writer: simulation never waits for disk or the viewer.
    public sealed class ServerEventLog : IDisposable
    {
        private readonly ConcurrentQueue<string> queue = new ConcurrentQueue<string>();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly Thread thread;
        private readonly string directory, source, prefix;
        private volatile bool stopping;
        private int pending, dropped;
        public string Failure { get; private set; } = "";
        public ServerEventLog(string directory, string source)
        {
            this.directory = Path.GetFullPath(directory); this.source = source;
            Directory.CreateDirectory(this.directory);
            prefix = source + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Process.GetCurrentProcess().Id;
            thread = new Thread(Run) { IsBackground = true, Name = "Server event log" }; thread.Start();
        }
        public void Write(string action, string message, string player = "", string userId = "", int tick = 0, uint sequence = 0, string level = "INFO", string room = "", int entity = 0)
        {
            if (stopping) return;
            if (Interlocked.Increment(ref pending) > 8192) { Interlocked.Decrement(ref pending); Interlocked.Increment(ref dropped); return; }
            queue.Enqueue("{\"utc\":" + Quote(DateTime.UtcNow.ToString("O")) + ",\"level\":" + Quote(level) + ",\"source\":" + Quote(source) +
                ",\"room\":" + Quote(room) + ",\"entity\":" + entity.ToString(CultureInfo.InvariantCulture) + ",\"player\":" + Quote(player) + ",\"userId\":" + Quote(userId) + ",\"tick\":" + tick.ToString(CultureInfo.InvariantCulture) +
                ",\"sequence\":" + sequence.ToString(CultureInfo.InvariantCulture) + ",\"action\":" + Quote(action) + ",\"message\":" + Quote(message) + "}");
            wake.Set();
        }
        public static string Quote(string value)
        {
            var b = new StringBuilder("\"");
            foreach (char c in value ?? "")
            {
                if (c == '"' || c == '\\') b.Append('\\').Append(c);
                else if (char.IsControl(c)) b.Append("\\u").Append(((int)c).ToString("x4"));
                else b.Append(c);
            }
            return b.Append('"').ToString();
        }
        private void Run()
        {
            try
            {
                int part = 0;
                while (!stopping || !queue.IsEmpty)
                {
                    using var stream = new FileStream(Path.Combine(directory, prefix + "-" + part++ + ".jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
                    using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                    while ((!stopping || !queue.IsEmpty) && stream.Length < 10 * 1024 * 1024)
                    {
                        while (queue.TryDequeue(out string? line)) { Interlocked.Decrement(ref pending); writer.WriteLine(line); if (stream.Position >= 10 * 1024 * 1024) break; }
                        int lost = Interlocked.Exchange(ref dropped, 0);
                        if (lost > 0) writer.WriteLine("{\"utc\":" + Quote(DateTime.UtcNow.ToString("O")) + ",\"level\":\"WARN\",\"source\":" + Quote(source) + ",\"action\":\"log_overflow\",\"message\":" + Quote("Dropped " + lost + " events") + "}");
                        writer.Flush();
                        if (!stopping) wake.WaitOne(200);
                    }
                }
            }
            catch (Exception ex) { Failure = ex.Message; Console.Error.WriteLine("Server event log failed: " + ex.Message); }
        }
        public void Dispose() { if (stopping) return; stopping = true; wake.Set(); if (thread.Join(3000)) wake.Dispose(); }
    }
}
