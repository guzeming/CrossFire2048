using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text;

namespace OperationBlacktide.Launcher;

internal static class SelfTest
{
    public static void Run(string root)
    {
        var paths = new ProjectPaths(root); var commands = new Commands(paths);
        Check(ProjectPaths.FindRoot(paths.At("GTools/Launcher/bin/Release/net8.0-windows")) == paths.Root, "root discovery from binary directory");
        foreach (var tool in ToolCatalog.All)
        {
            if (tool.File == "viewer") continue;
            string path = paths.At(tool.File.EndsWith(".ps1") ? "GTools/" + tool.File : tool.File);
            Check(File.Exists(path) || Directory.Exists(path), "tool target: " + tool.Name);
        }
        Check(ToolCatalog.All.Select(t => t.Name).Distinct().Count() == ToolCatalog.All.Count, "unique tool names");
        string hostilePath = @"D:\A space\literal $name & x\Unity.exe";
        var command = commands.Script("build-battle.ps1", hostilePath, "Client");
        Check(command.CreateStartInfo().ArgumentList.Contains(hostilePath), "paths remain one literal argument");
        Check(!command.CreateStartInfo().UseShellExecute && command.CreateStartInfo().CreateNoWindow, "helpers hidden and shell-free");
        using var current = Process.GetCurrentProcess();
        Check(StatusReader.IsSameProcess(current.Id, current.StartTime.ToUniversalTime().Ticks, Environment.ProcessPath!), "managed process identity");
        Check(!StatusReader.IsSameProcess(current.Id, current.StartTime.ToUniversalTime().Ticks - 1, Environment.ProcessPath!), "reused process ID rejected");
        Check(!StatusReader.IsSameProcess(current.Id, current.StartTime.ToUniversalTime().Ticks, @"C:\wrong.exe"), "wrong process path rejected");
        Check(new ServiceStatus(true, false, false, false, 0).HasForeignPort, "occupied port is not claimed as managed");
        Check(new ServiceStatus(true, true, true, true, 0).Ready, "ready requires both managed services");
        VerifyProgress();

        string directory = paths.At(".build-check/launcher-selftest-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        string script = Path.Combine(directory, "script with spaces.ps1");
        File.WriteAllText(script, "[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)\n[Console]::WriteLine('UTF8: 玩家操作')\n[Console]::Error.WriteLine('stderr captured')\nexit 7\n", new UTF8Encoding(true));
        var output = new List<string>();
        string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell/v1.0/powershell.exe");
        int exit = ProcessRunner.RunAsync(new Command(powershell, root, new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script }), line => { lock (output) output.Add(line); }).GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(directory, "process-output.json"), System.Text.Json.JsonSerializer.Serialize(new { exit, output }), Encoding.UTF8);
        Check(exit == 7 && output.Contains("UTF8: 玩家操作") && output.Contains("stderr captured"), "real process UTF8 stdout/stderr and failure exit");
        VerifyCancellation(root, directory, powershell);
        var snapshot = StatusReader.Read(paths);
        Console.WriteLine("STATUS tcp=" + snapshot.TcpListening + " udp=" + snapshot.UdpListening + " managed=" + snapshot.Managed);
        Console.WriteLine("LAUNCHER_SELF_TEST_PASS");
    }
    private static void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); Console.WriteLine("PASS " + name); }

    private static void VerifyProgress()
    {
        var progress = new TaskProgressTracker();
        progress.Reset(); progress.BeginStep(0, 50);
        progress.Consume("[进度] 60 编译脚本 6/10");
        Check(progress.Read() == (30, "编译脚本 6/10"), "reported progress is mapped to the current command range");
        progress.Consume("[进度] 10 准备依赖包");
        progress.Consume("[进度] 999 invalid");
        progress.Consume("[阶段] 等待新日志");
        Check(progress.Read() == (30, "等待新日志"), "late phases, invalid values and waiting do not change the percentage");
        progress.CompleteStep(); progress.BeginStep(50, 100);
        progress.Consume("[进度] 40 检查场景");
        Check(progress.Read().Percent == 70, "a following command continues the total percentage");
        progress.Finish(false); progress.Consume("[进度] 100 late output");
        Check(progress.Read().Percent == 70, "failure or cancellation retains progress and ignores late reports");
        progress.Reset(); progress.BeginStep(0, 100);
        Check(progress.Read().Percent == 0, "a new job starts at zero");
        progress.Consume("[进度] 100 子任务完成"); progress.CompleteStep();
        Check(progress.Read().Percent == 99, "100 percent is reserved for overall job success");
        progress.Finish(true);
        Check(progress.Read().Percent == 100, "confirmed job success reaches 100 percent");
    }

    private static void VerifyCancellation(string root, string directory, string powershell)
    {
        string script = Path.Combine(directory, "cancel parent.ps1");
        File.WriteAllText(script, "[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)\n" +
            "$child=Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList @('-NoProfile','-Command','Start-Sleep -Seconds 60') -WindowStyle Hidden -PassThru\n" +
            "[Console]::WriteLine('CHILD:'+$child.Id)\nStart-Sleep -Seconds 60\n", new UTF8Encoding(true));
        using var unrelated = Process.Start(new Command(powershell, root, new[] { "-NoProfile", "-Command", "Start-Sleep -Seconds 60" }).CreateStartInfo())!;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Process? child = null;
        bool cancelled = false;
        try
        {
            try
            {
                ProcessRunner.RunAsync(new Command(powershell, root, new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script }), line =>
                {
                    if (!line.StartsWith("CHILD:")) return;
                    child = Process.GetProcessById(int.Parse(line[6..]));
                    cancellation.Cancel();
                }, cancellation.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && child != null && child.WaitForExit(5000), "cancellation stops the tool and its real child process");
            Check(!unrelated.HasExited, "cancellation leaves unrelated processes running");
            using var alreadyCancelled = new CancellationTokenSource(); alreadyCancelled.Cancel();
            bool rejected = false;
            try { ProcessRunner.RunAsync(new Command("must-not-start.exe", root, Array.Empty<string>()), _ => { }, alreadyCancelled.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { rejected = true; }
            Check(rejected, "cancelled jobs cannot start another command");
        }
        finally
        {
            if (child != null) { if (!child.HasExited) child.Kill(entireProcessTree: true); child.Dispose(); }
            if (!unrelated.HasExited) { unrelated.Kill(); unrelated.WaitForExit(5000); }
        }
    }

    public static void RunServices(string root)
    {
        var paths = new ProjectPaths(root);
        string relative = ".build-check/launcher-services-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string directory = paths.At(relative); Directory.CreateDirectory(directory);
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int accountPort = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        using var socket = new UdpClient(0); int battlePort = ((IPEndPoint)socket.Client.LocalEndPoint!).Port; socket.Close();
        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        string start = Path.Combine(directory, "start fixture.ps1"), stop = Path.Combine(directory, "stop fixture.ps1");
        File.WriteAllText(start, "$ErrorActionPreference='Stop'\n& " + Quote(paths.At("GTools/start-local-server.ps1")) + " -NoViewer -AccountPort " + accountPort + " -BattlePort " + battlePort +
            " -DataDirectory " + Quote(relative) + " -LogDirectory " + Quote(relative + "/logs") + "\n", new UTF8Encoding(true));
        File.WriteAllText(stop, "$ErrorActionPreference='Stop'\n& " + Quote(paths.At("GTools/stop-local-server.ps1")) + " -DataDirectory " + Quote(relative) + "\n", new UTF8Encoding(true));
        string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell/v1.0/powershell.exe");
        Command Invocation(string file) => new(powershell, root, new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", paths.At("GTools/Launcher/invoke-tool.ps1"), "-ScriptPath", file });
        var ids = new List<(int id, long time, string path)>();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            int result = ProcessRunner.RunAsync(Invocation(start), Console.WriteLine, timeout.Token).GetAwaiter().GetResult();
            Check(result == 0, "launcher process runner starts actual servers and completes while services remain alive");
            using var records = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "local-server-processes.json")));
            File.Copy(Path.Combine(directory, "local-server-processes.json"), Path.Combine(directory, "started-processes.json"));
            foreach (var record in records.RootElement.EnumerateArray())
            {
                var identity = (record.GetProperty("Id").GetInt32(), record.GetProperty("Started").GetInt64(), record.GetProperty("Path").GetString()!);
                ids.Add(identity);
                Check(StatusReader.IsSameProcess(identity.Item1, identity.Item2, identity.Item3), "recorded service is live");
            }
            using var client = new TcpClient("127.0.0.1", accountPort);
            Check(Directory.GetFiles(Path.Combine(directory, "logs"), "battle-*.jsonl").Length > 0, "battle log available to viewer");
        }
        finally
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
            int result = ProcessRunner.RunAsync(Invocation(stop), Console.WriteLine, timeout.Token).GetAwaiter().GetResult();
            Check(result == 0, "launcher process runner stops actual servers");
        }
        foreach (var identity in ids)
            Console.WriteLine("STOP_STATUS " + identity.id + " live=" + StatusReader.IsSameProcess(identity.id, identity.time, identity.path));
        Check(ids.Count == 2 && ids.All(p => !StatusReader.IsSameProcess(p.id, p.time, p.path)), "both managed services exited before the stop command completed");
        File.WriteAllText(Path.Combine(directory, "result.txt"), "LAUNCHER_SERVICES_VERIFY_PASS");
        Console.WriteLine("LAUNCHER_SERVICES_VERIFY_PASS " + directory);
    }
}
