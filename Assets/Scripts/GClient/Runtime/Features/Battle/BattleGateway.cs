using System;
using OperationBlacktide.Client.App;
using OperationBlacktide.Client.Network;
using OperationBlacktide.Client.UI;
using OperationBlacktide.Shared.Protocol;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OperationBlacktide.Client.Features.Battle
{
    public sealed class BattleGateway : MonoBehaviour
    {
        public static BattleJoinResponse Ticket { get; private set; }
        private TcpGameClient client;
        private bool waiting;
        private float deadline;
        private void Awake() { client = GetComponent<TcpGameClient>(); client.MessageReceived += Receive; }
        public async void Join()
        {
            var flow = GameSceneFlow.Instance;
            if (waiting || flow == null || flow.IsLoading || !flow.Auth.Session.IsLoggedIn ||
                SceneManager.GetActiveScene().path != GameSceneFlow.LobbyScenePath) return;
            waiting = true; deadline = Time.unscaledTime + 8;
            UIManager.Instance?.ShowToast("正在连接联机测试场…");
            try { await client.SendAsync(MessageType.BattleJoinRequest, new BattleJoinRequest()); }
            catch (Exception) { waiting = false; UIManager.Instance?.ShowToast("入房请求发送失败"); }
        }
        private void Receive(NetworkMessage message)
        {
            if (!waiting || message.Type != MessageType.BattleJoinResponse) return;
            waiting = false;
            var response = NetworkMessageCodec.DecodePayload<BattleJoinResponse>(message);
            if (response == null || !response.Success) { UIManager.Instance?.ShowToast(response?.Message ?? "入房失败"); return; }
            Ticket = response; GameSceneFlow.Instance.EnterBattleArena();
        }
        private void Update()
        {
            if (waiting && Time.unscaledTime > deadline) { waiting = false; UIManager.Instance?.ShowToast("入房超时，请检查账号服"); }
        }
        public static BattleJoinResponse ConsumeTicket() { var result = Ticket; Ticket = null; return result; }
        private void OnDestroy() { if (client != null) client.MessageReceived -= Receive; }
    }
}
