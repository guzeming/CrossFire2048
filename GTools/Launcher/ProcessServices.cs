using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace OperationBlacktide.Launcher;

public sealed record Command(string FileName, string WorkingDirectory, IReadOnlyList<string> Arguments)
{
    public ProcessStartInfo CreateStartInfo(bool capture = true)
    {
        var info = new ProcessStartInfo(FileName) { WorkingDirectory = WorkingDirectory, UseShellExecute = false,
            CreateNoWindow = capture, RedirectStandardOutput = capture, RedirectStandardError = capture };
        if (capture) { info.StandardOutputEncoding = Encoding.UTF8; info.StandardErrorEncoding = Encoding.UTF8; }
        foreach (string argument in Arguments) info.ArgumentList.Add(argument);
        return info;
    }
}

public static class ProcessRunner
{
    public static async Task<int> RunAsync(Command command, Action<string> output, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = command.CreateStartInfo() };
        if (!process.Start()) throw new IOException("无法启动：" + command.FileName);
        using var drain = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        using var cancel = cancellation.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        });
        async Task Read(StreamReader reader)
        { while (await reader.ReadLineAsync(drain.Token).ConfigureAwait(false) is { } line) output(line); }
        Task readers = Task.WhenAll(Read(process.StandardOutput), Read(process.StandardError));
        try
        {
            await process.WaitForExitAsync(cancellation).ConfigureAwait(false);
            try { await readers.WaitAsync(TimeSpan.FromSeconds(3), cancellation).ConfigureAwait(false); }
            catch (TimeoutException)
            {
                // A detached descendant may inherit a pipe. Its lifetime must not hold the launcher job open.
                drain.Cancel();
                try { await readers.ConfigureAwait(false); } catch (OperationCanceledException) { }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Do not report completion while the cancelled tool is still running.
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            throw;
        }
        finally
        {
            drain.Cancel();
            // A later job must never receive a cancelled command's last progress report.
            try { await readers.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        cancellation.ThrowIfCancellationRequested();
        return process.ExitCode;
    }

    public static Process Open(string path, string directory, params string[] args) =>
        Process.Start(new Command(path, directory, args).CreateStartInfo(false)) ?? throw new IOException("无法启动：" + path);
}

public sealed class Commands
{
    private readonly ProjectPaths paths;
    public Commands(ProjectPaths paths) { this.paths = paths; }
    public Command Dotnet(params string[] arguments) => new("dotnet", paths.Root, arguments);
    public Command Build(string relative) => Script("Launcher/build-dotnet.ps1", target: relative);
    public Command Script(string name, string? unity = null, string? target = null, bool noViewer = false)
    {
        // ArgumentList passes each value as data; no shell string concatenation or cmd /c.
        var args = new List<string> { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", paths.At("GTools/Launcher/invoke-tool.ps1"), "-ScriptPath", paths.At("GTools/" + name) };
        if (unity != null) { args.Add("-UnityPath"); args.Add(unity); }
        if (target != null) { args.Add("-Target"); args.Add(target); }
        if (noViewer) args.Add("-NoViewer");
        string shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell/v1.0/powershell.exe");
        return new Command(shell, paths.Root, args);
    }
}

public sealed record ServiceStatus(bool TcpListening, bool UdpListening, bool AccountManaged, bool BattleManaged, int Clients)
{
    public bool Ready => TcpListening && UdpListening && AccountManaged && BattleManaged;
    public bool Managed => AccountManaged || BattleManaged;
    public bool HasForeignPort => TcpListening && !AccountManaged || UdpListening && !BattleManaged;
    public string AccountLabel => TcpListening ? AccountManaged ? "运行中" : "端口已占用" : "未启动";
    public string BattleLabel => UdpListening ? BattleManaged ? "运行中" : "端口已占用" : "未启动";
}

public static class StatusReader
{
    public static ServiceStatus Read(ProjectPaths paths)
    {
        var ip = IPGlobalProperties.GetIPGlobalProperties();
        bool tcp = ip.GetActiveTcpListeners().Any(e => e.Port == 7777), udp = ip.GetActiveUdpListeners().Any(e => e.Port == 7778);
        bool account = false, battle = false;
        string file = paths.At("data/local-server-processes.json");
        if (File.Exists(file))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var records = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToArray() : new[] { doc.RootElement };
                foreach (var record in records)
                {
                    int id = record.GetProperty("Id").GetInt32();
                    long ticks = record.GetProperty("Started").GetInt64();
                    string expected = record.GetProperty("Path").GetString() ?? "";
                    if (!IsSameProcess(id, ticks, expected)) continue;
                    if (Path.GetFileName(expected).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)) account = true;
                    if (expected.Equals(paths.BattleServer, StringComparison.OrdinalIgnoreCase)) battle = true;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException or FormatException) { }
        }
        int clients = 0;
        foreach (var process in Process.GetProcessesByName("Blacktide"))
        {
            using (process) { if (PathMatches(process, paths.Client)) clients++; }
        }
        return new ServiceStatus(tcp, udp, account, battle, clients);
    }
    public static bool IsSameProcess(int id, long ticks, string expected)
    {
        try { using var process = Process.GetProcessById(id); return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == ticks && PathMatches(process, expected); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }
    private static bool PathMatches(Process process, string path)
    {
        try { return string.Equals(process.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }
    public static bool FocusExisting(string executable)
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
        {
            using (process) if (PathMatches(process, executable) && process.MainWindowHandle != IntPtr.Zero)
            { WindowActivation.Focus(process.MainWindowHandle); return true; }
        }
        return false;
    }
}

internal static class WindowActivation
{
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr handle, int command);
    public static void Focus(IntPtr handle) { ShowWindow(handle, 9); SetForegroundWindow(handle); }
}
