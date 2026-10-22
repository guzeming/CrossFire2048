using OperationBlacktide.Client.App;
using OperationBlacktide.Client.UI;
using UnityEngine;
using UnityEngine.UI;

namespace OperationBlacktide.Client.Features.Lobby
{
    public sealed class LobbyMenuPanel : UIPanel
    {
        [SerializeField] private Button settingsButton, loginButton, quitButton, closeButton;

        protected override void OnOpen(object args)
        {
            AddButton(settingsButton, () => UIManager.Instance.Push(UIPanelId.Settings));
            AddButton(loginButton, () => GameSceneFlow.Instance?.Logout());
            AddButton(quitButton, GameSceneFlow.QuitGame);
            AddButton(closeButton, () => UIManager.Instance.Close(this));
        }
    }
}
