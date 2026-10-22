using System;
using System.IO;
using LiteNetLib;
using OperationBlacktide.Shared.Battle;
using OperationBlacktide.Shared.Modes.Demolition;

namespace OperationBlacktide.Client.Network
{
    public sealed class DemolitionConnection : IDisposable
    {
        readonly NetManager network;
        NetPeer peer;
        string matchId;
        bool disposed;
        public int PlayerId { get; private set; }
        public uint LastSequence { get; private set; }
        public bool Ready => PlayerId != 0 && peer?.ConnectionState == ConnectionState.Connected;
        public event Action<DemolitionSnapshot> Snapshot;
        public event Action<int,int,DemolitionPoint,DemolitionPoint> Shot;
        public event Action<string> Feedback, Disconnected;
        public DemolitionConnection()
        {
            var listener=new EventBasedNetListener();
            network=new NetManager(listener){AutoRecycle=true,IPv6Enabled=false,DisconnectTimeout=5000,MaxFragmentsCount=8};
            listener.PeerDisconnectedEvent+=(p,info)=>{PlayerId=0;if(!disposed)Disconnected?.Invoke(info.Reason.ToString());};
            listener.NetworkReceiveEvent+=(p,packet,channel,method)=>
            {
                try
                {
                    if(packet.AvailableBytes>DemolitionProtocol.MaxPacket)throw new InvalidDataException();
                    using var r=DemolitionProtocol.Read(packet.GetRemainingBytes(),out var kind);
                    switch(kind)
                    {
                        case DemolitionPacket.Welcome:
                            int id=r.ReadInt32();string match=r.ReadString();uint seq=r.ReadUInt32();BattleProtocol.End(r);
                            if(match!=matchId||id<1||id>10)throw new InvalidDataException("wrong_match");PlayerId=id;LastSequence=seq;break;
                        case DemolitionPacket.Snapshot:Snapshot?.Invoke(DemolitionProtocol.Snapshot(r));break;
                        case DemolitionPacket.Shot:
                            int shooter=r.ReadInt32(),hit=r.ReadInt32();var a=DemolitionProtocol.Point(r);var b=DemolitionProtocol.Point(r);BattleProtocol.End(r);Shot?.Invoke(shooter,hit,a,b);break;
                        case DemolitionPacket.Feedback:string text=r.ReadString();BattleProtocol.End(r);Feedback?.Invoke(text);break;
                        default:throw new InvalidDataException("wrong_packet");
                    }
                }
                catch(Exception e)when(e is IOException||e is ArgumentException){peer.Disconnect();Disconnected?.Invoke("协议错误："+e.Message);}
            };
        }
        public void Connect(MatchAssignment match)
        {matchId=match.MatchId;if(!network.Start())throw new IOException("Cannot open socket");peer=network.Connect(match.Host,match.Port,match.Ticket);}
        public void Poll()=>network.PollEvents();
        public void Input(BattleInput command,bool interact,DemolitionPoint? aimPoint=null)
        {if(Ready)peer.Send(DemolitionProtocol.Input(command,interact,aimPoint),DeliveryMethod.Sequenced);}
        public void Buy(BuyItem item)=>Send(DemolitionPacket.Buy,w=>w.Write((byte)item));
        public void Equip(int slot)=>Send(DemolitionPacket.Equip,w=>w.Write((byte)slot));
        public void Surrender()=>Send(DemolitionPacket.Surrender,w=>{});
        void Send(DemolitionPacket kind,Action<BinaryWriter> write){if(Ready)peer.Send(DemolitionProtocol.Write(kind,write),DeliveryMethod.ReliableOrdered);}
        public void Dispose(){if(disposed)return;disposed=true;network.Stop();PlayerId=0;}
    }
}
