namespace CrossFire2048.Shared.Protocol
{
    /// <summary>
    /// 客户端与服务端之间的消息类型。第一阶段只覆盖连接与账户相关消息。
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
    }
}
