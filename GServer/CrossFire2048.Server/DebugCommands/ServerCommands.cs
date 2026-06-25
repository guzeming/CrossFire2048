using CrossFire2048.Server.Network;

namespace CrossFire2048.Server.DebugCommands
{
    public sealed class HelpCommand : IDebugCommand
    {
        private readonly DebugCommandRegistry _registry;

        public HelpCommand(DebugCommandRegistry registry)
        {
            _registry = registry;
        }

        public string Name => "help";
        public string Description => "显示命令列表";

        public void Execute(string[] args)
        {
            Console.WriteLine("可用命令：");
            foreach (IDebugCommand command in _registry.Commands.OrderBy(c => c.Name))
            {
                Console.WriteLine($"  {command.Name,-12} {command.Description}");
            }
        }
    }

    public sealed class AccountsCommand : IDebugCommand
    {
        private readonly GameServer _server;

        public AccountsCommand(GameServer server)
        {
            _server = server;
        }

        public string Name => "accounts";
        public string Description => "查看已注册账号数量";

        public void Execute(string[] args)
        {
            Console.WriteLine($"已注册账号数：{_server.AccountCount}");
        }
    }

    public sealed class StatusCommand : IDebugCommand
    {
        private readonly GameServer _server;

        public StatusCommand(GameServer server)
        {
            _server = server;
        }

        public string Name => "status";
        public string Description => "查看服务器运行状态";

        public void Execute(string[] args)
        {
            Console.WriteLine("Server: Running");
            Console.WriteLine($"Port: {_server.Port}");
            Console.WriteLine($"Accounts: {_server.AccountCount}");
            Console.WriteLine($"Connections: {_server.ConnectionCount}");
            Console.WriteLine($"Sessions: {_server.SessionCount}");
        }
    }

    public sealed class SessionsCommand : IDebugCommand
    {
        private readonly GameServer _server;

        public SessionsCommand(GameServer server)
        {
            _server = server;
        }

        public string Name => "sessions";
        public string Description => "查看当前登录会话";

        public void Execute(string[] args)
        {
            IReadOnlyCollection<ClientConnection> sessions = _server.GetSessions();
            Console.WriteLine($"当前登录会话数：{sessions.Count}");
            foreach (ClientConnection session in sessions)
            {
                Console.WriteLine($"  用户名={session.Username} UserId={session.UserId} 连接=#{session.ConnectionId}");
            }
        }
    }

    public sealed class ClientsCommand : IDebugCommand
    {
        private readonly GameServer _server;

        public ClientsCommand(GameServer server)
        {
            _server = server;
        }

        public string Name => "clients";
        public string Description => "查看当前连接";

        public void Execute(string[] args)
        {
            IReadOnlyCollection<ClientConnection> connections = _server.GetConnections();
            Console.WriteLine($"当前连接数：{connections.Count}");
            foreach (ClientConnection connection in connections)
            {
                string state = connection.IsAuthenticated ? $"已登录({connection.Username})" : "未登录";
                Console.WriteLine($"  #{connection.ConnectionId} {connection.RemoteEndPoint} {state}");
            }
        }
    }

    public sealed class KickCommand : IDebugCommand
    {
        private readonly GameServer _server;

        public KickCommand(GameServer server)
        {
            _server = server;
        }

        public string Name => "kick";
        public string Description => "踢出指定用户：kick <userId>";

        public void Execute(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("用法：kick <userId>");
                return;
            }

            bool kicked = _server.Kick(args[0]);
            Console.WriteLine(kicked ? "已踢出该用户" : "未找到该用户的会话");
        }
    }

    public sealed class SaveCommand : IDebugCommand
    {
        private readonly GameServer _server;

        public SaveCommand(GameServer server)
        {
            _server = server;
        }

        public string Name => "save";
        public string Description => "手动保存账号数据";

        public void Execute(string[] args)
        {
            bool ok = _server.SaveAccounts();
            Console.WriteLine(ok ? "账号数据已保存" : "账号数据保存失败");
        }
    }

    public sealed class StopCommand : IDebugCommand
    {
        private readonly Action _stopAction;

        public StopCommand(Action stopAction)
        {
            _stopAction = stopAction;
        }

        public string Name => "stop";
        public string Description => "关闭服务器";

        public void Execute(string[] args)
        {
            _stopAction();
        }
    }
}
