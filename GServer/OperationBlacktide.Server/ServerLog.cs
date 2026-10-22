namespace OperationBlacktide.Server
{
    /// <summary>
    /// 简单的控制台日志。带时间戳和级别，方便在调试窗口里观察服务器行为。
    /// </summary>
    public static class ServerLog
    {
        public static OperationBlacktide.Shared.Diagnostics.ServerEventLog? Events { get; set; }
        private static readonly object Gate = new object();

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);
        public static void Error(string message) => Write("ERROR", message);

        private static void Write(string level, string message)
        {
            Events?.Write("server", message, level: level.Trim());
            string line = $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}";
            lock (Gate)
            {
                Console.WriteLine(line);
            }
        }
    }
}
