using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using CrossFire2048.Server.Accounts;
using CrossFire2048.Server.Network;
using CrossFire2048.Server.Storage;
using CrossFire2048.Shared.Protocol;

namespace CrossFire2048.Server
{
    /// <summary>
    /// 服务端核心。负责监听 TCP、接受连接、读取消息并分发到账户逻辑，
    /// 同时维护当前连接和登录会话，供命令行调试窗口查询和控制。
    /// </summary>
    public sealed class GameServer
    {
        private readonly int _port;
        private readonly AccountStore _accountStore;
        private readonly AccountService _accountService;

        private readonly ConcurrentDictionary<int, ClientConnection> _connections =
            new ConcurrentDictionary<int, ClientConnection>();

        // 已登录用户：UserId -> 连接，用于查重登录与踢人。
        private readonly ConcurrentDictionary<string, ClientConnection> _sessions =
            new ConcurrentDictionary<string, ClientConnection>();

        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private int _nextConnectionId;

        public GameServer(int port, string accountFilePath)
        {
            _port = port;
            _accountStore = new AccountStore(accountFilePath);
            _accountService = new AccountService(_accountStore);
        }

        public int AccountCount => _accountService.AccountCount;
        public int Port => _port;
        public int ConnectionCount => _connections.Count;
        public int SessionCount => _sessions.Count;

        public IReadOnlyCollection<ClientConnection> GetConnections() => _connections.Values.ToList();
        public IReadOnlyCollection<ClientConnection> GetSessions() => _sessions.Values.ToList();

        public void Start(CancellationTokenSource cts)
        {
            _cts = cts;
            _accountStore.Load();
            ServerLog.Info($"已加载账户数据，当前账户数：{_accountService.AccountCount}");

            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            ServerLog.Info($"服务器已启动，监听端口 {_port}");

            _ = AcceptLoopAsync(cts.Token);
        }

        public void Stop()
        {
            try
            {
                _accountStore.Save();
                ServerLog.Info("账户数据已保存");
            }
            catch (Exception ex)
            {
                ServerLog.Error($"保存账户数据失败：{ex.Message}");
            }

            foreach (ClientConnection connection in _connections.Values)
            {
                connection.Close();
            }

            _connections.Clear();
            _sessions.Clear();

            try
            {
                _listener?.Stop();
            }
            catch
            {
                // 忽略停止监听时的异常。
            }

            ServerLog.Info("服务器已停止");
        }

        public bool SaveAccounts()
        {
            try
            {
                _accountStore.Save();
                return true;
            }
            catch (Exception ex)
            {
                ServerLog.Error($"保存账户数据失败：{ex.Message}");
                return false;
            }
        }

        public bool Kick(string userId)
        {
            if (_sessions.TryGetValue(userId, out ClientConnection? connection))
            {
                connection.Close();
                return true;
            }

            return false;
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            if (_listener == null)
            {
                return;
            }

            while (!token.IsCancellationRequested)
            {
                TcpClient tcpClient;
                try
                {
                    tcpClient = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    ServerLog.Error($"接受连接失败：{ex.Message}");
                    continue;
                }

                int connectionId = Interlocked.Increment(ref _nextConnectionId);
                ClientConnection connection = new ClientConnection(connectionId, tcpClient);
                _connections[connectionId] = connection;
                ServerLog.Info($"客户端接入 #{connectionId} ({connection.RemoteEndPoint})");

                _ = HandleClientAsync(connection, token);
            }
        }

        private async Task HandleClientAsync(ClientConnection connection, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    string? line = await connection.ReadLineAsync().ConfigureAwait(false);
                    if (line == null)
                    {
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    await DispatchAsync(connection, line).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ServerLog.Warn($"连接 #{connection.ConnectionId} 读取异常：{ex.Message}");
            }
            finally
            {
                RemoveConnection(connection);
            }
        }

        private async Task DispatchAsync(ClientConnection connection, string line)
        {
            NetworkMessage? envelope;
            try
            {
                envelope = MessageCodec.DecodeEnvelope(line);
            }
            catch (Exception ex)
            {
                ServerLog.Warn($"连接 #{connection.ConnectionId} 消息解析失败：{ex.Message}");
                await connection.SendAsync(MessageType.Error, new ErrorResponse { Message = "消息格式错误" })
                    .ConfigureAwait(false);
                return;
            }

            if (envelope == null)
            {
                return;
            }

            switch (envelope.Type)
            {
                case MessageType.RegisterRequest:
                    await HandleRegisterAsync(connection, envelope).ConfigureAwait(false);
                    break;

                case MessageType.LoginRequest:
                    await HandleLoginAsync(connection, envelope).ConfigureAwait(false);
                    break;

                case MessageType.Heartbeat:
                    await connection.SendAsync(MessageType.Heartbeat, new Heartbeat
                    {
                        ClientTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    }).ConfigureAwait(false);
                    break;

                default:
                    ServerLog.Warn($"连接 #{connection.ConnectionId} 发送了未知消息类型：{envelope.Type}");
                    break;
            }
        }

        private async Task HandleRegisterAsync(ClientConnection connection, NetworkMessage envelope)
        {
            RegisterRequest? request = MessageCodec.DecodePayload<RegisterRequest>(envelope);
            if (request == null)
            {
                await connection.SendAsync(MessageType.RegisterResponse, new RegisterResponse
                {
                    Code = AuthResultCode.InvalidFormat,
                    Message = "注册请求为空",
                }).ConfigureAwait(false);
                return;
            }

            RegisterResult result = _accountService.Register(request.Username, request.Password);
            ServerLog.Info($"注册请求 用户名={request.Username} 结果={result.Code}");

            await connection.SendAsync(MessageType.RegisterResponse, new RegisterResponse
            {
                Code = result.Code,
                Message = result.Message,
            }).ConfigureAwait(false);
        }

        private async Task HandleLoginAsync(ClientConnection connection, NetworkMessage envelope)
        {
            LoginRequest? request = MessageCodec.DecodePayload<LoginRequest>(envelope);
            if (request == null)
            {
                await connection.SendAsync(MessageType.LoginResponse, new LoginResponse
                {
                    Code = AuthResultCode.InvalidFormat,
                    Message = "登录请求为空",
                }).ConfigureAwait(false);
                return;
            }

            LoginResult result = _accountService.Login(request.Username, request.Password);
            ServerLog.Info($"登录请求 用户名={request.Username} 结果={result.Code}");

            if (result.Code != AuthResultCode.Ok || result.Account == null)
            {
                await connection.SendAsync(MessageType.LoginResponse, new LoginResponse
                {
                    Code = result.Code,
                    Message = result.Message,
                }).ConfigureAwait(false);
                return;
            }

            // 同一账号重复登录：踢掉旧连接，保留新连接。
            if (_sessions.TryGetValue(result.Account.UserId, out ClientConnection? existing))
            {
                ServerLog.Info($"账号 {result.Account.Username} 重复登录，断开旧连接 #{existing.ConnectionId}");
                existing.Close();
            }

            string sessionToken = Guid.NewGuid().ToString("N");
            connection.MarkAuthenticated(result.Account.UserId, result.Account.Username, sessionToken);
            _sessions[result.Account.UserId] = connection;

            await connection.SendAsync(MessageType.LoginResponse, new LoginResponse
            {
                Code = AuthResultCode.Ok,
                Message = result.Message,
                UserId = result.Account.UserId,
                SessionToken = sessionToken,
            }).ConfigureAwait(false);
        }

        private void RemoveConnection(ClientConnection connection)
        {
            _connections.TryRemove(connection.ConnectionId, out _);

            if (connection.IsAuthenticated)
            {
                // 仅当会话仍指向该连接时才移除，避免误删被顶号后的新连接。
                if (_sessions.TryGetValue(connection.UserId, out ClientConnection? current) &&
                    current.ConnectionId == connection.ConnectionId)
                {
                    _sessions.TryRemove(connection.UserId, out _);
                }
            }

            connection.Close();
            ServerLog.Info($"客户端断开 #{connection.ConnectionId}");
        }
    }
}
