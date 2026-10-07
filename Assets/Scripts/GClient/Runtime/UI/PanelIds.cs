using System;

namespace OperationBlacktide.Client.UI
{
    /// <summary>
    /// UIPanelId 与字符串 panelId 的转换与校验。
    /// UIManager 注册表、预制体命名、代码调用都通过这里统一维护。
    /// </summary>
    public static class PanelIds
    {
        /// <summary>所有已定义的面板 ID，便于编辑器工具或批量校验。</summary>
        public static readonly UIPanelId[] All =
        {
            UIPanelId.Login,
            UIPanelId.Lobby,
            UIPanelId.Toast,
            UIPanelId.Training,
            UIPanelId.ReturnToLobby,
            UIPanelId.TrainingMenu,
        };

        /// <summary>不参与栈管理的 Overlay 面板。</summary>
        public static readonly UIPanelId[] OverlayOnly =
        {
            UIPanelId.Toast,
        };

        public static string Key(UIPanelId panelId)
        {
            if (panelId == UIPanelId.None)
            {
                return string.Empty;
            }

            return panelId.ToString();
        }

        public static bool TryParse(string key, out UIPanelId panelId)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                panelId = UIPanelId.None;
                return false;
            }

            return Enum.TryParse(key, out panelId) && panelId != UIPanelId.None;
        }

        public static bool IsOverlayOnly(UIPanelId panelId)
        {
            for (int i = 0; i < OverlayOnly.Length; i++)
            {
                if (OverlayOnly[i] == panelId)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
