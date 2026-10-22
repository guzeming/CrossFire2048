using System;
using OperationBlacktide.Client.App;
using OperationBlacktide.Client.Network;
using OperationBlacktide.Client.UI;
using OperationBlacktide.Shared.Modes.Demolition;
using OperationBlacktide.Shared.Protocol;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OperationBlacktide.Client.Features.Demolition
{
    public sealed class DemolitionGateway : MonoBehaviour
    {
        TcpGameClient client;
        float nextPoll, pendingAt;
        int requestId, pendingId, appliedId;
        string entered = "";
        bool inBattle;
        public DemolitionResponse State { get; private set; } = new DemolitionResponse();
        public static MatchAssignment PendingMatch { get; private set; }
        public bool Busy { get; private set; }
        public LobbyAction PendingAction { get; private set; }
        public string Error { get; private set; } = "";
        public float StateAt { get; private set; }
        public float RequestSeconds => Busy ? Time.unscaledTime - pendingAt : 0;
        public event Action Changed;
        public static DemolitionGateway Get()
        {
            var flow = GameSceneFlow.Instance; if (flow == null) return null;
            var gateway = flow.GetComponent<DemolitionGateway>(); return gateway != null ? gateway : flow.gameObject.AddComponent<DemolitionGateway>();
        }
        void Awake() { client = GetComponent<TcpGameClient>(); client.MessageReceived += Receive; client.Disconnected += Disconnected; }
        public void Open() { UIManager.Instance?.Push(UIPanelId.Demolition, this); Send(new DemolitionRequest()); }
        public async void Send(DemolitionRequest request)
        {
            if (!client.IsConnected) { Fail("账号连接已断开，请重新登录"); return; }
            if (Busy && request.Action != LobbyAction.Poll && request.Action != LobbyAction.CancelQueue && request.Action != LobbyAction.DeclineMatch) return;
            request.RequestId = ++requestId;
            if (request.Action != LobbyAction.Poll)
            {
                Busy = true; PendingAction = request.Action; pendingId = request.RequestId;
                pendingAt = Time.unscaledTime; Error = ""; Changed?.Invoke();
            }
            try { await client.SendAsync(MessageType.DemolitionRequest, request); }
            catch (Exception) { Fail("请求发送失败，请重试"); }
        }
        void Fail(string message) { Busy = false; pendingId = 0; Error = message; UIManager.Instance?.ShowToast(message); Changed?.Invoke(); }
        void Receive(NetworkMessage message)
        {
            if (message.Type != MessageType.DemolitionResponse) return;
            var response = NetworkMessageCodec.DecodePayload<DemolitionResponse>(message); if (response == null) return;
            // JsonUtility may materialize JSON null nested classes as empty objects.
            // Stable IDs, not reference non-nullness, determine presence on the wire.
            if (response.Room != null && string.IsNullOrEmpty(response.Room.Code)) response.Room = null;
            if (response.Match != null && string.IsNullOrEmpty(response.Match.MatchId)) response.Match = null;
            response.Party = response.Party ?? Array.Empty<LobbyMember>();
            response.Rooms = response.Rooms ?? Array.Empty<RoomView>();
            if (response.RequestId == pendingId) { Busy = false; pendingId = 0; Error = response.Success ? "" : response.Message; }
            if (response.RequestId < appliedId) { Changed?.Invoke(); return; }
            appliedId = response.RequestId; State = response; StateAt = Time.unscaledTime;
            if (response.Success && !Busy) Error = "";
            if (!string.IsNullOrEmpty(response.Message)) UIManager.Instance?.ShowToast(response.Message);
            Changed?.Invoke();
            if (response.Match != null && response.Match.MatchId != entered && SceneManager.GetActiveScene().path == GameSceneFlow.LobbyScenePath && !GameSceneFlow.Instance.IsLoading)
            {
                entered = response.Match.MatchId; PendingMatch = response.Match;
                GameSceneFlow.Instance.EnterBattleArena(); inBattle = true;
            }
        }
        void Update()
        {
            if (!GameSceneFlow.Instance.Auth.Session.IsLoggedIn) return;
            if (Time.unscaledTime >= nextPoll) { nextPoll = Time.unscaledTime+1; Send(new DemolitionRequest()); }
            if (Busy && Time.unscaledTime-pendingAt > 8) Fail("匹配服务未响应，请确认本机服务器已更新后重试");
            if (inBattle && !GameSceneFlow.Instance.IsLoading && SceneManager.GetActiveScene().path == GameSceneFlow.LobbyScenePath)
            { inBattle = false; Open(); }
        }
        public void Reconnect() { entered = ""; Send(new DemolitionRequest { Action = LobbyAction.Reconnect }); }
        public static MatchAssignment Consume() { var result = PendingMatch; PendingMatch = null; return result; }
        void Disconnected(string reason) { State = new DemolitionResponse(); PendingMatch = null; entered = ""; Busy = false; pendingId = appliedId = 0; Error = "账号连接已断开，请重新登录"; Changed?.Invoke(); }
        void OnDestroy() { if (client != null) { client.MessageReceived -= Receive; client.Disconnected -= Disconnected; } }
    }
}
