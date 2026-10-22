using System.Net.Sockets;
using System.Text;
using OperationBlacktide.Shared.Protocol;

namespace OperationBlacktide.Server.Network
{
    /// <summary>
    /// 表示一个已连接的客户端。封装底层 TcpClient 的读写，
    /// 并保存该连接的登录状态（登录前 UserId 为空）。
    /// </summary>
    public sealed class ClientConnection
    {
        private readonly TcpClient _tcpClient;
        private readonly NetworkStream _stream;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        public int ConnectionId { get; }
        public DateTime NextBattleTicketAt { get; set; }
        public string RemoteEndPoint { get; }

        public bool IsAuthenticated => !string.IsNullOrEmpty(UserId);
        public string UserId { get; private set; } = string.Empty;
        public string Username { get; private set; } = string.Empty;
        public string SessionToken { get; private set; } = string.Empty;

        public ClientConnection(int connectionId, TcpClient tcpClient)
        {
            ConnectionId = connectionId;
            _tcpClient = tcpClient;
            _stream = tcpClient.GetStream();
            _reader = new StreamReader(_stream, Encoding.UTF8);
            _writer = new StreamWriter(_stream, new UTF8Encoding(false))
            {
                AutoFlush = true,
                NewLine = "\n",
            };

            RemoteEndPoint = tcpClient.Client.RemoteEndPoint?.ToString() ?? "unknown";
        }

        public void MarkAuthenticated(string userId, string username, string sessionToken)
        {
            UserId = userId;
            Username = username;
            SessionToken = sessionToken;
        }

        public async Task<string?> ReadLineAsync()
        {
            // Bound allocation before accepting untrusted JSON (ReadLineAsync has no limit).
            var line = new StringBuilder();
            char[] character = new char[1];
            while (await _reader.ReadAsync(character, 0, 1).ConfigureAwait(false) != 0)
            {
                if (character[0] == '\n') return line.ToString().TrimEnd('\r');
                if (line.Length >= 8192) throw new InvalidDataException("Message exceeds 8192 characters.");
                line.Append(character[0]);
            }
            return null;
        }

        public async Task SendAsync<TPayload>(MessageType type, TPayload payload)
        {
            string line = MessageCodec.Encode(type, payload);
            await _sendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await _writer.WriteLineAsync(line).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public void Close()
        {
            try
            {
                _tcpClient.Close();
            }
            catch
            {
                // 关闭时忽略异常，连接清理由上层负责。
            }
        }
    }
}
