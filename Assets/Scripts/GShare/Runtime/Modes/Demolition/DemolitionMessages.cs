using System;

namespace OperationBlacktide.Shared.Modes.Demolition
{
    public enum LobbyAction
    {
        Poll, Queue, CancelQueue, CreateParty, JoinParty, LeaveParty, TransferParty,
        CreateRoom, JoinRoom, LeaveRoom, ConfigureRoom, SelectTeam, Ready, StartRoom, Kick, TransferHost, Reconnect, AcceptMatch, DeclineMatch
    }
    public enum LobbyPhase { Idle, Queued, Allocating, Loading, Playing, Confirming }
    [Serializable] public sealed class DemolitionRequest
    {
        public int RequestId;
        public LobbyAction Action;
        public string Code = "", Password = "", Name = "", Target = "";
        public bool AllowAI, IsPrivate, Ready, ChangePassword;
        public int Team;
    }
    [Serializable] public sealed class LobbyMember
    {
        public string UserId = "", Name = "";
        public int Team;
        public bool Ready, Online = true, IsBot;
    }
    [Serializable] public sealed class RoomView
    {
        public string Code = "", Name = "", HostId = "";
        public bool IsPrivate, HasPassword, AllowAI, Started;
        public float StartsIn = -1;
        public LobbyMember[] Members = Array.Empty<LobbyMember>();
    }
    [Serializable] public sealed class DemolitionResponse
    {
        public int RequestId;
        public bool Success = true;
        public string Message = "", PartyCode = "", LeaderId = "";
        public LobbyPhase Phase;
        public bool AllowAI;
        public float WaitSeconds;
        public float ConfirmSeconds;
        public bool Confirmed;
        public LobbyMember[] Party = Array.Empty<LobbyMember>();
        public RoomView Room;
        public RoomView[] Rooms = Array.Empty<RoomView>();
        public MatchAssignment Match;
    }
    [Serializable] public sealed class MatchSeat
    {
        public int Id, Team;
        public string UserId = "", Name = "";
        public bool IsBot;
    }
    [Serializable] public sealed class MatchAssignment
    {
        public string MatchId = "", Host = "127.0.0.1", Ticket = "";
        public int Port;
        public bool HasAI;
        public MatchSeat[] Seats = Array.Empty<MatchSeat>();
    }
    // Server-only manifest, never sent to clients. A separate process owns each match/world.
    [Serializable] public sealed class DemolitionManifest
    {
        public string MatchId = "", KeyFile = "", ReadyFile = "", ResultFile = "";
        public int Port;
        public MatchSeat[] Seats = Array.Empty<MatchSeat>();
    }
    [Serializable] public sealed class DemolitionResult
    {
        public string MatchId = "", Reason = "";
        public bool Completed, HasAI;
        public int ScoreA, ScoreB;
    }
}
