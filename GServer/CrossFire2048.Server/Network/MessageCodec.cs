using System.Text.Json;
using CrossFire2048.Shared.Protocol;

namespace CrossFire2048.Server.Network
{
    /// <summary>
    /// 消息编解码。约定：每条消息是一行紧凑 JSON 的 NetworkMessage 信封，
    /// 以换行符分隔。负载本身再序列化为信封里的 Json 字段。
    /// </summary>
    public static class MessageCodec
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true,
            WriteIndented = false,
        };

        public static string Encode<TPayload>(MessageType type, TPayload payload)
        {
            string payloadJson = JsonSerializer.Serialize(payload, Options);
            NetworkMessage envelope = new NetworkMessage(type, payloadJson);
            return JsonSerializer.Serialize(envelope, Options);
        }

        public static NetworkMessage? DecodeEnvelope(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            return JsonSerializer.Deserialize<NetworkMessage>(line, Options);
        }

        public static TPayload? DecodePayload<TPayload>(NetworkMessage envelope)
        {
            if (string.IsNullOrEmpty(envelope.Json))
            {
                return default;
            }

            return JsonSerializer.Deserialize<TPayload>(envelope.Json, Options);
        }
    }
}
