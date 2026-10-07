using OperationBlacktide.Client.App;
using OperationBlacktide.Client.Common;
using OperationBlacktide.Client.UI;
using OperationBlacktide.Shared.Protocol;
using UnityEngine;
using UnityEngine.UI;

namespace OperationBlacktide.Client.Features.Account
{
    /// <summary>
    /// 登录/注册面板。场景跳转由 GameSceneFlow 在收到登录成功后处理。
    /// </summary>
    public sealed class LoginPanel : UIPanel
    {
        [SerializeField] private AuthClient authClient;
        [SerializeField] private InputField usernameInput;
        [SerializeField] private InputField passwordInput;
        [SerializeField] private Button loginButton;
        [SerializeField] private Button registerButton;
        [SerializeField] private Text statusText;

        protected override void OnOpen(object args)
        {
            if (authClient == null)
            {
                authClient = GameSceneFlow.Instance != null
                    ? GameSceneFlow.Instance.Auth : FindObjectOfType<AuthClient>();
            }

            string username = string.Empty, password = string.Empty;
            if (args is LoginOpenArgs loginArgs)
            {
                username = loginArgs.DefaultUsername ?? string.Empty;
                password = loginArgs.DefaultPassword ?? string.Empty;
            }
            else if (authClient != null)
                authClient.TryGetRememberedLogin(out username, out password);

            if (usernameInput != null) usernameInput.text = username;
            if (passwordInput != null) passwordInput.text = password;

            SetStatus(string.Empty);

            if (loginButton != null)
            {
                AddButton(loginButton, OnLoginClicked);
            }

            if (registerButton != null)
            {
                AddButton(registerButton, OnRegisterClicked);
            }

            AddGameEvent<string>(GameEventId.AccountStatusChanged, OnAccountStatusChanged);
            AddGameEvent<LoginResponse>(GameEventId.LoginCompleted, OnLoginCompleted);
            AddGameEvent<RegisterResponse>(GameEventId.RegisterCompleted, OnRegisterCompleted);
        }

        protected override void OnClose()
        {
            // Cached panels need not retain the plaintext password while the player is in the lobby.
            if (passwordInput != null) passwordInput.text = string.Empty;
        }

        private void OnLoginClicked()
        {
            if (authClient == null)
            {
                UIManager.Instance?.ShowToast("AuthClient 未绑定");
                return;
            }

            string username = usernameInput != null ? usernameInput.text : string.Empty;
            string password = passwordInput != null ? passwordInput.text : string.Empty;
            AddAsync(() => authClient.LoginAsync(username, password));
        }

        private void OnRegisterClicked()
        {
            if (authClient == null)
            {
                UIManager.Instance?.ShowToast("AuthClient 未绑定");
                return;
            }

            string username = usernameInput != null ? usernameInput.text : string.Empty;
            string password = passwordInput != null ? passwordInput.text : string.Empty;
            AddAsync(() => authClient.RegisterAsync(username, password));
        }

        private void OnAccountStatusChanged(string message)
        {
            SetStatus(message);
        }

        private void OnLoginCompleted(LoginResponse response)
        {
            if (response == null)
            {
                return;
            }

            if (response.Code == AuthResultCode.Ok)
            {
                UIManager.Instance?.ShowToast("登录成功");
                return;
            }

            UIManager.Instance?.ShowToast(response.Message);
        }

        private void OnRegisterCompleted(RegisterResponse response)
        {
            if (response == null)
            {
                return;
            }

            if (response.Code == AuthResultCode.Ok)
            {
                UIManager.Instance?.ShowToast("注册成功，请登录");
                return;
            }

            UIManager.Instance?.ShowToast(response.Message);
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message ?? string.Empty;
            }
        }
    }
}
