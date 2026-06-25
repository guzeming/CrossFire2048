using System;

namespace CrossFire2048.Shared.Protocol
{
    /// <summary>
    /// 网络消息信封。所有消息在传输时都包成 NetworkMessage，
    /// Type 表示消息类型，Json 表示具体负载序列化后的内容。
    /// 这样客户端和服务端只需先解析信封，再按 Type 解析负载。
    /// </summary>
    [Serializable]
    public class NetworkMessage
    {
        public MessageType Type;
        public string Json;

        public NetworkMessage()
        {
            Type = MessageType.None;
            Json = string.Empty;
        }

        public NetworkMessage(MessageType type, string json)
        {
            Type = type;
            Json = json ?? string.Empty;
        }
    }
}
