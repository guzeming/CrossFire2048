using System;

namespace OperationBlacktide.Shared.Protocol
{
    [Serializable] public sealed class BattleJoinRequest { }
    [Serializable] public sealed class BattleJoinResponse
    {
        public bool Success;
        public string Message = "";
        public string Host = "127.0.0.1";
        public int Port = 7778;
        public string Ticket = "";
    }
}
