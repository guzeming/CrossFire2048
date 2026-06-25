using CrossFire2048.Server.DebugCommands;

namespace CrossFire2048.Server
{
    /// <summary>
    /// 命令行调试窗口。在主线程循环读取输入，把命令交给注册表执行。
    /// 这是“无需打开 Unity 也能观察和控制服务器”的入口。
    /// </summary>
    public sealed class ServerConsole
    {
        private readonly DebugCommandRegistry _registry;
        private readonly CancellationTokenSource _cts;

        public ServerConsole(DebugCommandRegistry registry, CancellationTokenSource cts)
        {
            _registry = registry;
            _cts = cts;
        }

        public void Run()
        {
            Console.WriteLine("输入 help 查看可用命令。");

            while (!_cts.IsCancellationRequested)
            {
                Console.Write("server> ");
                string? input = Console.ReadLine();

                if (input == null)
                {
                    // 输入流结束（例如管道关闭），退出循环。
                    break;
                }

                input = input.Trim();
                if (input.Length == 0)
                {
                    continue;
                }

                try
                {
                    bool found = _registry.Execute(input);
                    if (!found)
                    {
                        Console.WriteLine($"未知命令：{input}，输入 help 查看可用命令。");
                    }
                }
                catch (Exception ex)
                {
                    ServerLog.Error($"命令执行异常：{ex.Message}");
                }
            }
        }
    }
}
