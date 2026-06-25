using CrossFire2048.Client.Common;
using CrossFire2048.Client.UI;
using CrossFire2048.Shared.Protocol;
using UnityEngine;
using UnityEngine.UI;

namespace CrossFire2048.Client.Features.Account
{
    /// <summary>
    /// 登录/注册面板。登录成功后 Push Lobby。
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
            if (args is LoginOpenArgs loginArgs)
            {
                if (usernameInput != null)
                {
                    usernameInput.text = loginArgs.DefaultUsername ?? string.Empty;
                }

                if (passwordInput != null)
                {
                    passwordInput.text = loginArgs.DefaultPassword ?? string.Empty;
                }
            }

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
                UIManager.Instance?.Push(PanelId.Lobby);
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
