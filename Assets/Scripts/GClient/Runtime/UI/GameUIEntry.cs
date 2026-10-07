using OperationBlacktide.Client.App;
using UnityEngine;

namespace OperationBlacktide.Client.UI
{
    /// <summary>
    /// 场景 UI 入口。挂到场景自己的 SceneUI 对象上，不随 UIRoot 跨场景保留。
    /// </summary>
    public sealed class GameUIEntry : MonoBehaviour
    {
        [SerializeField] private UIPanelId startPanel = UIPanelId.Login;
        [SerializeField] private bool openOnStart = true;

        private void Start()
        {
            if (!openOnStart || UIManager.Instance == null)
            {
                return;
            }

            if (startPanel == UIPanelId.Lobby &&
                (GameSceneFlow.Instance == null || !GameSceneFlow.Instance.Auth.Session.IsLoggedIn))
            {
                return;
            }

            // 新场景从自己的主界面开始，不保留上一个场景的返回历史。
            UIManager.Instance.CloseAll(UILayer.Popup);
            UIManager.Instance.CloseAll(UILayer.Normal);
            UIManager.Instance.Push(startPanel);
        }
    }
}
