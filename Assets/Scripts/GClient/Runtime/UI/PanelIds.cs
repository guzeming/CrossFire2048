using System;

namespace CrossFire2048.Client.UI
{
    /// <summary>
    /// PanelId 与字符串 panelId 的转换与校验。
    /// UIManager 注册表、预制体命名、代码调用都通过这里统一维护。
    /// </summary>
    public static class PanelIds
    {
        /// <summary>所有已定义的面板 ID，便于编辑器工具或批量校验。</summary>
        public static readonly PanelId[] All =
        {
            PanelId.Login,
            PanelId.Lobby,
            PanelId.Toast,
        };

        /// <summary>不参与栈管理的 Overlay 面板。</summary>
        public static readonly PanelId[] OverlayOnly =
        {
            PanelId.Toast,
        };

        public static string Key(PanelId panelId)
        {
            if (panelId == PanelId.None)
            {
                return string.Empty;
            }

            return panelId.ToString();
        }

        public static bool TryParse(string key, out PanelId panelId)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                panelId = PanelId.None;
                return false;
            }

            return Enum.TryParse(key, out panelId) && panelId != PanelId.None;
        }

        public static bool IsOverlayOnly(PanelId panelId)
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
