namespace CrossFire2048.Client.Common
{
    /// <summary>
    /// 全局游戏事件 ID。用于跨模块、跨 UI 的轻量事件广播。
    /// </summary>
    public enum GameEventId
    {
        None = 0,

        // 账户
        AccountStatusChanged = 100,
        RegisterCompleted = 101,
        LoginCompleted = 102,

        // 网络
        NetworkConnected = 200,
        NetworkDisconnected = 201,
    }
}
