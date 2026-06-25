using CrossFire2048.Client.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CrossFire2048.Client.Features.Account
{
    /// <summary>
    /// 登录成功后的占位大厅面板，后续扩展房间列表等功能。
    /// </summary>
    public sealed class LobbyPanel : UIPanel
    {
        [SerializeField] private Text welcomeText;
        [SerializeField] private Button logoutButton;
        [SerializeField] private AuthClient authClient;

        protected override void OnOpen(object args)
        {
            if (welcomeText != null && authClient != null && authClient.Session.IsLoggedIn)
            {
                welcomeText.text = $"欢迎，{authClient.Session.Username}";
            }

            if (logoutButton != null)
            {
                AddButton(logoutButton, OnLogoutClicked);
            }
        }

        private void OnLogoutClicked()
        {
            authClient?.Logout();
            UIManager.Instance?.PopTo(PanelId.Login);
        }
    }
}
