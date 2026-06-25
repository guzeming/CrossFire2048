using CrossFire2048.Server;
using CrossFire2048.Server.DebugCommands;

// 解析启动参数：--port <端口>，默认 7777；账户数据默认存到 data/accounts.json。
int port = 7777;
string accountFile = Path.Combine(AppContext.BaseDirectory, "data", "accounts.json");

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
}

Console.WriteLine("CrossFire2048 服务端");
Console.WriteLine($"端口：{port}");
Console.WriteLine($"账户文件：{accountFile}");

CancellationTokenSource cts = new CancellationTokenSource();
GameServer server = new GameServer(port, accountFile);

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
console.Run();

server.Stop();
