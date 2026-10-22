using System.Text;
using System.Text.Json;

namespace OperationBlacktide.LogViewer;

public sealed record LogEntry(string Utc, string Level, string Source, string Room, string Player,
    string UserId, int Tick, uint Sequence, string Action, string Message, int Entity)
{
    public static LogEntry Parse(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        string Get(string key) => root.TryGetProperty(key, out var v) ? v.ToString() : "";
        int.TryParse(Get("tick"), out int tick); uint.TryParse(Get("sequence"), out uint sequence);
        int.TryParse(Get("entity"), out int entity);
        return new LogEntry(Get("utc"), Get("level"), Get("source"), Get("room"), Get("player"), Get("userId"), tick, sequence, Get("action"), Get("message"), entity);
    }
    public string Time => DateTimeOffset.TryParse(Utc, out var time) ? time.ToLocalTime().ToString("HH:mm:ss.fff") : Utc;
}

public sealed class LogTailer : IDisposable
{
    private sealed class Cursor : IDisposable
    {
        public readonly FileStream Stream;
        public Decoder Decoder = Encoding.UTF8.GetDecoder();
        public StringBuilder Partial = new();
        public bool SkipFirst;
        public Cursor(string path)
        {
            Stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (Stream.Length > 2 * 1024 * 1024) { Stream.Seek(-2 * 1024 * 1024, SeekOrigin.End); SkipFirst = true; }
        }
        public void Dispose() => Stream.Dispose();
    }
    private readonly Dictionary<string, Cursor> cursors = new(StringComparer.OrdinalIgnoreCase);
    private readonly byte[] bytes = new byte[65536];
    private readonly char[] chars = new char[65536];
    public string DirectoryPath { get; }
    public string Status { get; private set; } = "等待日志文件";
    public long InvalidLines { get; private set; }
    public LogTailer(string path) { DirectoryPath = Path.GetFullPath(path); }
    public List<LogEntry> Poll()
    {
        var result = new List<LogEntry>();
        try
        {
            if (!Directory.Exists(DirectoryPath)) { Status = "目录尚未创建，等待服务器启动"; return result; }
            var files = new DirectoryInfo(DirectoryPath).EnumerateFiles("*.jsonl").OrderByDescending(f => f.LastWriteTimeUtc).Take(64).Select(f => f.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string old in cursors.Keys.Where(p => !files.Contains(p)).ToArray()) { cursors[old].Dispose(); cursors.Remove(old); }
            foreach (string file in files.OrderBy(p => p))
            {
                try
                {
                    if (!cursors.TryGetValue(file, out var cursor)) cursors[file] = cursor = new Cursor(file);
                    if (cursor.Stream.Length < cursor.Stream.Position)
                    { cursor.Stream.Position = 0; cursor.Decoder.Reset(); cursor.Partial.Clear(); cursor.SkipFirst = false; }
                    // Limit work per refresh, preserving partial UTF-8 and partial lines until next poll.
                    for (int chunk = 0; chunk < 4 && cursor.Stream.Position < cursor.Stream.Length; chunk++)
                    {
                        int count = cursor.Stream.Read(bytes, 0, bytes.Length);
                        if (count == 0) break;
                        int decoded = cursor.Decoder.GetChars(bytes, 0, count, chars, 0, false);
                        for (int i = 0; i < decoded; i++)
                        {
                            if (chars[i] == '\n')
                            {
                                if (!cursor.SkipFirst && cursor.Partial.Length > 0)
                                {
                                    try { result.Add(LogEntry.Parse(cursor.Partial.ToString().TrimEnd('\r'))); }
                                    catch (JsonException) { InvalidLines++; }
                                    catch (InvalidOperationException) { InvalidLines++; }
                                }
                                cursor.Partial.Clear(); cursor.SkipFirst = false;
                            }
                            else if (!cursor.SkipFirst)
                            {
                                if (cursor.Partial.Length >= 65536) { InvalidLines++; cursor.Partial.Clear(); cursor.SkipFirst = true; }
                                else cursor.Partial.Append(chars[i]);
                            }
                        }
                    }
                }
                catch (IOException) { /* Rotation or writer opening a file: retry on next poll. */ }
            }
            Status = $"跟踪 {cursors.Count} 个文件 · 无效行 {InvalidLines}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = ex.Message; }
        return result.OrderBy(e => e.Utc, StringComparer.Ordinal).ToList();
    }
    public void Dispose() { foreach (var c in cursors.Values) c.Dispose(); cursors.Clear(); }
}
