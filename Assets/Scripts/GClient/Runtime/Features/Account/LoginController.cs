using CrossFire2048.Shared.Protocol;
using UnityEngine;

namespace CrossFire2048.Client.Features.Account
{
    /// <summary>
    /// 登录注册流程控制器。
    /// 当前阶段先通过 Inspector 字段和右键菜单验证链路，后续 UI 按钮可直接调用 Register/Login。
    /// </summary>
    [RequireComponent(typeof(AuthClient))]
    public sealed class LoginController : MonoBehaviour
    {
        [SerializeField] private AuthClient authClient;
        [SerializeField] private string username = "test_user";
        [SerializeField] private string password = "123456";

        [Header("Runtime")]
        [SerializeField] private string statusText = "未连接";

        public string StatusText => statusText;

        private void Awake()
        {
            if (authClient == null)
            {
                authClient = GetComponent<AuthClient>();
            }
        }

        private void OnEnable()
        {
            authClient.StatusChanged += OnStatusChanged;
            authClient.RegisterCompleted += OnRegisterCompleted;
            authClient.LoginCompleted += OnLoginCompleted;
        }

        private void OnDisable()
        {
            authClient.StatusChanged -= OnStatusChanged;
            authClient.RegisterCompleted -= OnRegisterCompleted;
            authClient.LoginCompleted -= OnLoginCompleted;
        }

        [ContextMenu("Register")]
        public async void Register()
        {
            await authClient.RegisterAsync(username, password);
        }

        [ContextMenu("Login")]
        public async void Login()
        {
            await authClient.LoginAsync(username, password);
        }

        public void SetCredentials(string newUsername, string newPassword)
        {
            username = newUsername;
            password = newPassword;
        }

        private void OnStatusChanged(string message)
        {
            statusText = message;
            Debug.Log($"[Login] {message}");
        }

        private void OnRegisterCompleted(RegisterResponse response)
        {
            Debug.Log($"[Login] Register result: {response.Code}, {response.Message}");
        }

        private void OnLoginCompleted(LoginResponse response)
        {
            Debug.Log($"[Login] Login result: {response.Code}, {response.Message}");

            if (response.Code == AuthResultCode.Ok)
            {
                Debug.Log($"[Login] UserId={response.UserId}, Token={response.SessionToken}");
            }
        }
    }
}
