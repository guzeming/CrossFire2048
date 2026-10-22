namespace OperationBlacktide.Shared.Protocol
{
    /// <summary>
    /// TCP 控制面消息：账户、心跳与入房。战斗输入/快照使用独立 BattleProtocol。
    /// </summary>
    public enum MessageType
    {
        None = 0,

        // 通用
        Heartbeat = 1,
        Error = 2,

        // 账户：注册
        RegisterRequest = 10,
        RegisterResponse = 11,

        // 账户：登录
        LoginRequest = 12,
        LoginResponse = 13,
        BattleJoinRequest = 20,
        BattleJoinResponse = 21,
        DemolitionRequest = 30,
        DemolitionResponse = 31,
    }
}
