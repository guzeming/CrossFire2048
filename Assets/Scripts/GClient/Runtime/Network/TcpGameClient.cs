using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CrossFire2048.Client.App;
using CrossFire2048.Shared.Protocol;
using UnityEngine;

namespace CrossFire2048.Client.Network
{
    public enum ClientConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
    }

    /// <summary>
    /// Unity 客户端 TCP 网络组件。
    /// 第一阶段用于登录注册这类低频可靠消息；后续战斗同步可单独增加 UDP 通道。
    /// </summary>
    public sealed class TcpGameClient : MonoBehaviour
    {
        [SerializeField] private AppConfig appConfig;
        [SerializeField] private string serverHost = "127.0.0.1";
        [SerializeField] private int serverPort = 7777;

        private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();

        private TcpClient _tcpClient;
        private StreamReader _reader;
        private StreamWriter _writer;
        private CancellationTokenSource _cts;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        public ClientConnectionState State { get; private set; } = ClientConnectionState.Disconnected;
        public bool IsConnected => State == ClientConnectionState.Connected;
        public string ServerHost => ResolveServerHost();
        public int ServerPort => ResolveServerPort();

        public event Action Connected;
        public event Action<string> Disconnected;
        public event Action<NetworkMessage> MessageReceived;
        public event Action<string> ErrorReceived;

        private void Update()
        {
            while (_mainThreadActions.TryDequeue(out Action action))
            {
                action.Invoke();
            }
        }

        private void OnDestroy()
        {
            Disconnect("组件销毁");
        }

        public async void Connect()
        {
            await ConnectConfiguredAsync();
        }

        public Task ConnectConfiguredAsync()
        {
            return ConnectAsync(ResolveServerHost(), ResolveServerPort());
        }

        public async Task ConnectAsync(string host, int port)
        {
            if (State != ClientConnectionState.Disconnected)
            {
                return;
            }

            State = ClientConnectionState.Connecting;
            serverHost = host;
            serverPort = port;

            try
            {
                _cts = new CancellationTokenSource();
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(host, port);

                NetworkStream stream = _tcpClient.GetStream();
                _reader = new StreamReader(stream, Encoding.UTF8);
                _writer = new StreamWriter(stream, new UTF8Encoding(false))
                {
                    AutoFlush = true,
                    NewLine = "\n",
                };

                State = ClientConnectionState.Connected;
                EnqueueMainThread(() => Connected?.Invoke());

                _ = ReceiveLoopAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                Cleanup();
                EnqueueMainThread(() => ErrorReceived?.Invoke($"连接服务器失败：{ex.Message}"));
            }
        }

        public async Task SendAsync<TPayload>(MessageType type, TPayload payload)
        {
            if (!IsConnected || _writer == null)
            {
                EnqueueMainThread(() => ErrorReceived?.Invoke("尚未连接服务器"));
                return;
            }

            string line = NetworkMessageCodec.Encode(type, payload);

            await _sendLock.WaitAsync();
            try
            {
                await _writer.WriteLineAsync(line);
            }
            catch (Exception ex)
            {
                EnqueueMainThread(() => ErrorReceived?.Invoke($"发送消息失败：{ex.Message}"));
                Disconnect("发送失败");
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public void Disconnect(string reason = "主动断开")
        {
            if (State == ClientConnectionState.Disconnected)
            {
                return;
            }

            Cleanup();
            EnqueueMainThread(() => Disconnected?.Invoke(reason));
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    string line = await _reader.ReadLineAsync();
                    if (line == null)
                    {
                        break;
                    }

                    NetworkMessage message = NetworkMessageCodec.DecodeEnvelope(line);
                    if (message != null)
                    {
                        EnqueueMainThread(() => MessageReceived?.Invoke(message));
                    }
                }

                EnqueueMainThread(() => Disconnect("服务器断开连接"));
            }
            catch (Exception ex)
            {
                EnqueueMainThread(() =>
                {
                    ErrorReceived?.Invoke($"接收消息失败：{ex.Message}");
                    Disconnect("接收失败");
                });
            }
        }

        private void Cleanup()
        {
            State = ClientConnectionState.Disconnected;

            try
            {
                _cts?.Cancel();
            }
            catch
            {
                // 忽略取消异常。
            }

            _reader?.Dispose();
            _writer?.Dispose();
            _tcpClient?.Close();

            _reader = null;
            _writer = null;
            _tcpClient = null;
            _cts = null;
        }

        private void EnqueueMainThread(Action action)
        {
            _mainThreadActions.Enqueue(action);
        }

        private string ResolveServerHost()
        {
            return appConfig != null ? appConfig.ServerHost : serverHost;
        }

        private int ResolveServerPort()
        {
            return appConfig != null ? appConfig.ServerPort : serverPort;
        }
    }
}
