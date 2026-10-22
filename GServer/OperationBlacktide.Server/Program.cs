using OperationBlacktide.Server;
using OperationBlacktide.Server.DebugCommands;
using OperationBlacktide.Shared.Diagnostics;
using System.Security.Cryptography;

// 解析启动参数：--port <端口>，默认 7777；账户数据默认存到 data/accounts.json。
int port = 7777;
string accountFile = Path.Combine(AppContext.BaseDirectory, "data", "accounts.json");
string keyFile = Path.GetFullPath("data/battle.key");
string logDirectory = Path.GetFullPath("Logs/Server");
int battlePort = 7778;
string? stopFile = null;

for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--port" && int.TryParse(args[i + 1], out int parsedPort))
    {
        port = parsedPort;
    }
    else if (args[i] == "--accounts")
    {
        accountFile = args[i + 1];
    }
    else if (args[i] == "--battle-key") keyFile = Path.GetFullPath(args[i + 1]);
    else if (args[i] == "--logs") logDirectory = Path.GetFullPath(args[i + 1]);
    else if (args[i] == "--battle-port") battlePort = int.Parse(args[i + 1]);
    else if (args[i] == "--stop-file") stopFile = Path.GetFullPath(args[i + 1]);
}

Directory.CreateDirectory(Path.GetDirectoryName(keyFile)!);
if (!File.Exists(keyFile))
{
    using var created = new FileStream(keyFile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    created.Write(RandomNumberGenerator.GetBytes(32));
}
byte[] battleKey = File.ReadAllBytes(keyFile);
if (battleKey.Length != 32) throw new InvalidDataException("Battle signing key must contain 32 bytes.");
using var eventLog = new ServerEventLog(logDirectory, "account");
ServerLog.Events = eventLog;

Console.WriteLine("黑潮行动 / OPERATION BLACKTIDE 服务端");
Console.WriteLine($"端口：{port}");
Console.WriteLine($"账户文件：{accountFile}");

CancellationTokenSource cts = new CancellationTokenSource();
int executableArg = Array.IndexOf(args, "--demolition-server");
string? demolitionExecutable = executableArg >= 0 && executableArg + 1 < args.Length ? args[executableArg + 1] : null;
GameServer server = new GameServer(port, accountFile, battleKey, battlePort, keyFile, demolitionExecutable);

// 注册调试命令。
DebugCommandRegistry registry = new DebugCommandRegistry();
registry.Register(new HelpCommand(registry));
registry.Register(new AccountsCommand(server));
registry.Register(new StatusCommand(server));
registry.Register(new SessionsCommand(server));
registry.Register(new ClientsCommand(server));
registry.Register(new KickCommand(server));
registry.Register(new SaveCommand(server));
registry.Register(new StopCommand(() => cts.Cancel()));

// Ctrl+C 也触发优雅停止。
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

server.Start(cts);

// 命令行调试窗口在主线程运行，直到 stop 或 Ctrl+C。
ServerConsole console = new ServerConsole(registry, cts);
if (args.Contains("--no-console"))
{
    try
    {
        while (!cts.IsCancellationRequested && (stopFile == null || !File.Exists(stopFile)))
            await Task.Delay(200, cts.Token);
    }
    catch (OperationCanceledException) { }
}
else console.Run();

server.Stop();
