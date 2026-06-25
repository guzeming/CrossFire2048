namespace CrossFire2048.Server.DebugCommands
{
    /// <summary>
    /// 一个调试命令。命令通过名字触发，args 是名字之后的参数列表。
    /// </summary>
    public interface IDebugCommand
    {
        string Name { get; }
        string Description { get; }
        void Execute(string[] args);
    }
}
