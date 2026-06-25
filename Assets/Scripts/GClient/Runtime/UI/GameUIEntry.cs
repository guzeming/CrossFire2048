using UnityEngine;

namespace CrossFire2048.Client.UI
{
    /// <summary>
    /// 场景启动时自动打开登录面板。挂到 UIRoot 同物体或任意启动对象上。
    /// </summary>
    public sealed class GameUIEntry : MonoBehaviour
    {
        [SerializeField] private PanelId startPanel = PanelId.Login;
        [SerializeField] private bool openOnStart = true;

        private void Start()
        {
            if (!openOnStart || UIManager.Instance == null)
            {
                return;
            }

            // DontDestroyOnLoad 的 UIRoot 跨场景时避免重复 Push 起始面板
            if (UIManager.Instance.GetStackCount(UILayer.Normal) > 0)
            {
                return;
            }

            UIManager.Instance.Push(startPanel);
        }
    }
}
