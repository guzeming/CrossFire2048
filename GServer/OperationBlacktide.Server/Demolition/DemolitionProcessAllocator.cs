using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using OperationBlacktide.Shared.Modes.Demolition;

namespace OperationBlacktide.Server.Demolition;

/// <summary>Local deployment: one headless Unity process per room provides complete physics isolation.</summary>
public sealed class DemolitionProcessAllocator : IDemolitionAllocator
{
    sealed record Instance(Process Process, string Ready, string Result, string Stop);
    readonly Dictionary<string, Instance> processes = new();
    readonly string executable, keyFile, directory;
    static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = true };
    public DemolitionProcessAllocator(string executable, string keyFile, string directory)
    { this.executable = Path.GetFullPath(executable); this.keyFile = Path.GetFullPath(keyFile); this.directory = Path.GetFullPath(directory); }
    public int Start(string id, MatchSeat[] seats)
    {
        if (!File.Exists(executable)) throw new FileNotFoundException("Build the Unity battle server first.", executable);
        if (processes.Count >= 4) throw new InvalidOperationException("Local battle process capacity reached.");
        using var portProbe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)portProbe.Client.LocalEndPoint!).Port; portProbe.Close();
        string folder = Path.Combine(directory, id); Directory.CreateDirectory(folder);
        string ready = Path.Combine(folder, "ready"), result = Path.Combine(folder, "result.json"), stop = Path.Combine(folder, "stop");
        string manifest = Path.Combine(folder, "manifest.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new DemolitionManifest { MatchId = id, KeyFile = keyFile, Port = port,
            ReadyFile = ready, ResultFile = result, Seats = seats }, Json));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(executable)! };
        foreach (string arg in new[] { "-batchmode", "-nographics", "--demolition-manifest", manifest, "--stop-file", stop,
            "-logFile", Path.Combine(folder, "unity.log") }) start.ArgumentList.Add(arg);
        var process = Process.Start(start) ?? throw new IOException("Could not launch battle process.");
        processes.Add(id, new Instance(process, ready, result, stop));
        return port;
    }
    public AllocationState Status(string id)
    {
        if (!processes.TryGetValue(id, out var p)) return AllocationState.Failed;
        if (File.Exists(p.Result)) return AllocationState.Completed;
        if (p.Process.HasExited) return AllocationState.Failed;
        return File.Exists(p.Ready) ? AllocationState.Ready : AllocationState.Starting;
    }
    public string Result(string id)
    {
        try
        {
            var value = JsonSerializer.Deserialize<DemolitionResult>(File.ReadAllText(processes[id].Result), Json);
            return value?.Completed == true ? $"对局结束 {value.ScoreA}:{value.ScoreB}" + (value.HasAI ? " · 含 AI 对局" : "") : "对局未完成，不计胜负";
        }
        catch { return "对局已结束"; }
    }
    public DemolitionResult Outcome(string id)
    {
        try { return JsonSerializer.Deserialize<DemolitionResult>(File.ReadAllText(processes[id].Result), Json) ?? new DemolitionResult(); }
        catch { return new DemolitionResult(); }
    }
    public void Release(string id)
    {
        if (!processes.Remove(id, out var p)) return;
        try { if (!p.Process.HasExited) { File.WriteAllText(p.Stop, "stop"); _ = StopLater(p.Process); } else p.Process.Dispose(); }
        catch { p.Process.Dispose(); }
    }
    static async Task StopLater(Process process)
    {
        try { using var timeout = new CancellationTokenSource(5000); await process.WaitForExitAsync(timeout.Token); }
        catch { try { if (!process.HasExited) process.Kill(true); } catch { } }
        finally { process.Dispose(); }
    }
    public void Dispose() { foreach (string id in processes.Keys.ToArray()) Release(id); }
}
