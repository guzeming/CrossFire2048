namespace OperationBlacktide.Client.UI
{
    /// <summary>
    /// 全项目 UI 面板 ID 枚举。与 UIManager 注册表中的 panelId 字符串保持一致。
    /// 命名 UIPanelId，避免与 UIPanel.PanelId（string）属性冲突。
    /// </summary>
    public enum UIPanelId
    {
        None = 0,
        Login = 1,
        Lobby = 2,
        Toast = 3,
        Training = 4,
        ReturnToLobby = 5,
        TrainingMenu = 6,
    }
}
