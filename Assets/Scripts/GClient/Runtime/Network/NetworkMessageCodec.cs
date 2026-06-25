using CrossFire2048.Shared.Protocol;
using UnityEngine;

namespace CrossFire2048.Client.Network
{
    /// <summary>
    /// Unity 客户端消息编解码。
    /// 传输格式与服务端保持一致：每行一个 NetworkMessage JSON，
    /// NetworkMessage.Json 字段保存具体消息负载的 JSON。
    /// </summary>
    public static class NetworkMessageCodec
    {
        public static string Encode<TPayload>(MessageType type, TPayload payload)
        {
            string payloadJson = JsonUtility.ToJson(payload);
            NetworkMessage envelope = new NetworkMessage(type, payloadJson);
            return JsonUtility.ToJson(envelope);
        }

        public static NetworkMessage DecodeEnvelope(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            return JsonUtility.FromJson<NetworkMessage>(line);
        }

        public static TPayload DecodePayload<TPayload>(NetworkMessage envelope)
        {
            if (envelope == null || string.IsNullOrEmpty(envelope.Json))
            {
                return default;
            }

            return JsonUtility.FromJson<TPayload>(envelope.Json);
        }
    }
}
