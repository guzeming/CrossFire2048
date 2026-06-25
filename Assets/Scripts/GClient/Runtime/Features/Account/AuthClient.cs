using System;
using System.Threading.Tasks;
using CrossFire2048.Client.Network;
using CrossFire2048.Shared.Protocol;
using UnityEngine;

namespace CrossFire2048.Client.Features.Account
{
    /// <summary>
    /// 客户端账号业务封装。
    /// UI 或控制器只需要调用 RegisterAsync/LoginAsync，不直接处理网络消息细节。
    /// </summary>
    [RequireComponent(typeof(TcpGameClient))]
    public sealed class AuthClient : MonoBehaviour
    {
        [SerializeField] private TcpGameClient tcpGameClient;

        public AuthSession Session { get; } = new AuthSession();

        public event Action<RegisterResponse> RegisterCompleted;
        public event Action<LoginResponse> LoginCompleted;
        public event Action<string> StatusChanged;

        private string _lastLoginUsername = string.Empty;

        private void Awake()
        {
            if (tcpGameClient == null)
            {
                tcpGameClient = GetComponent<TcpGameClient>();
            }
        }

        private void OnEnable()
        {
            tcpGameClient.Connected += OnConnected;
            tcpGameClient.Disconnected += OnDisconnected;
            tcpGameClient.MessageReceived += OnMessageReceived;
            tcpGameClient.ErrorReceived += OnErrorReceived;
        }

        private void OnDisable()
        {
            tcpGameClient.Connected -= OnConnected;
            tcpGameClient.Disconnected -= OnDisconnected;
            tcpGameClient.MessageReceived -= OnMessageReceived;
            tcpGameClient.ErrorReceived -= OnErrorReceived;
        }

        public async Task RegisterAsync(string username, string password)
        {
            if (!ValidateInput(username, password))
            {
                return;
            }

            if (!await EnsureConnectedAsync())
            {
                return;
            }

            StatusChanged?.Invoke("正在发送注册请求...");
            await tcpGameClient.SendAsync(MessageType.RegisterRequest, new RegisterRequest
            {
                Username = username,
                Password = password,
            });
        }

        public async Task LoginAsync(string username, string password)
        {
            if (!ValidateInput(username, password))
            {
                return;
            }

            if (!await EnsureConnectedAsync())
            {
                return;
            }

            _lastLoginUsername = username;
            StatusChanged?.Invoke("正在发送登录请求...");
            await tcpGameClient.SendAsync(MessageType.LoginRequest, new LoginRequest
            {
                Username = username,
                Password = password,
            });
        }

        public void Logout()
        {
            Session.Clear();
            StatusChanged?.Invoke("已清空本地登录会话");
        }

        private bool ValidateInput(string username, string password)
        {
            if (!AccountRules.IsUsernameValid(username))
            {
                StatusChanged?.Invoke($"用户名需为 {AccountRules.UsernameMinLength}-{AccountRules.UsernameMaxLength} 位字母、数字或下划线");
                return false;
            }

            if (!AccountRules.IsPasswordValid(password))
            {
                StatusChanged?.Invoke($"密码需为 {AccountRules.PasswordMinLength}-{AccountRules.PasswordMaxLength} 位");
                return false;
            }

            return true;
        }

        private async Task<bool> EnsureConnectedAsync()
        {
            if (tcpGameClient.IsConnected)
            {
                return true;
            }

            StatusChanged?.Invoke($"正在连接服务器 {tcpGameClient.ServerHost}:{tcpGameClient.ServerPort}...");
            await tcpGameClient.ConnectConfiguredAsync();
            return tcpGameClient.IsConnected;
        }

        private void OnMessageReceived(NetworkMessage message)
        {
            switch (message.Type)
            {
                case MessageType.RegisterResponse:
                    HandleRegisterResponse(NetworkMessageCodec.DecodePayload<RegisterResponse>(message));
                    break;

                case MessageType.LoginResponse:
                    HandleLoginResponse(NetworkMessageCodec.DecodePayload<LoginResponse>(message));
                    break;

                case MessageType.Error:
                    ErrorResponse error = NetworkMessageCodec.DecodePayload<ErrorResponse>(message);
                    StatusChanged?.Invoke(error != null ? error.Message : "服务器返回未知错误");
                    break;
            }
        }

        private void HandleRegisterResponse(RegisterResponse response)
        {
            if (response == null)
            {
                StatusChanged?.Invoke("注册响应解析失败");
                return;
            }

            StatusChanged?.Invoke(response.Message);
            RegisterCompleted?.Invoke(response);
        }

        private void HandleLoginResponse(LoginResponse response)
        {
            if (response == null)
            {
                StatusChanged?.Invoke("登录响应解析失败");
                return;
            }

            if (response.Code == AuthResultCode.Ok)
            {
                Session.Set(response.UserId, _lastLoginUsername, response.SessionToken);
            }

            StatusChanged?.Invoke(response.Message);
            LoginCompleted?.Invoke(response);
        }

        private void OnConnected()
        {
            StatusChanged?.Invoke("已连接服务器");
        }

        private void OnDisconnected(string reason)
        {
            StatusChanged?.Invoke($"服务器连接已断开：{reason}");
        }

        private void OnErrorReceived(string message)
        {
            StatusChanged?.Invoke(message);
        }
    }
}
