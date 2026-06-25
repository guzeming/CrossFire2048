namespace CrossFire2048.Server.DebugCommands
{
    /// <summary>
    /// 调试命令注册表。负责保存命令、按名字查找并执行。
    /// 新增命令只需实现 IDebugCommand 并注册即可。
    /// </summary>
    public sealed class DebugCommandRegistry
    {
        private readonly Dictionary<string, IDebugCommand> _commands =
            new Dictionary<string, IDebugCommand>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<IDebugCommand> Commands => _commands.Values.ToList();

        public void Register(IDebugCommand command)
        {
            _commands[command.Name] = command;
        }

        public bool TryGet(string name, out IDebugCommand command)
        {
            return _commands.TryGetValue(name, out command!);
        }

        /// <summary>
        /// 解析并执行一行输入。返回 false 表示命令未找到。
        /// </summary>
        public bool Execute(string input)
        {
            string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return true;
            }

            string name = parts[0];
            string[] args = parts.Skip(1).ToArray();

            if (!_commands.TryGetValue(name, out IDebugCommand? command))
            {
                return false;
            }

            command.Execute(args);
            return true;
        }
    }
}
