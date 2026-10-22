using System;
using System.IO;
using LiteNetLib;
using OperationBlacktide.Shared.Battle;

namespace OperationBlacktide.Client.Network
{
    // Transport adapter is also exercised by the headless integration clients.
    public sealed class BattleConnection : IDisposable
    {
        private readonly NetManager network;
        private NetPeer peer;
        private bool disposed, disconnectReported;
        public int PlayerId { get; private set; }
        public int Ping => peer?.Ping ?? 0;
        public bool Ready => PlayerId != 0 && peer?.ConnectionState == ConnectionState.Connected;
        public event Action<int, BattleState[]> Snapshot;
        public event Action<int, int, float[]> Shot;
        public event Action<string> Disconnected;
        public BattleConnection()
        {
            var listener = new EventBasedNetListener();
            network = new NetManager(listener) { AutoRecycle = true, IPv6Enabled = false, PingInterval = 500, DisconnectTimeout = 5000 };
            listener.PeerDisconnectedEvent += (p, reason) => ReportDisconnect(reason.Reason.ToString());
            listener.NetworkReceiveEvent += (p, packet, channel, method) =>
            {
                Action dispatch;
                try
                {
                    using var r = BattleProtocol.Read(packet.GetRemainingBytes(), out var kind);
                    switch (kind)
                    {
                        case BattlePacket.Welcome:
                            int playerId = r.ReadInt32(); r.ReadInt32();
                            if (r.ReadString() != BattleProtocol.MapId) throw new InvalidDataException("Map version mismatch");
                            BattleProtocol.End(r); dispatch = () => PlayerId = playerId; break;
                        case BattlePacket.Snapshot:
                            int tick = r.ReadInt32(), count = r.ReadByte();
                            if (count > BattleProtocol.MaxPlayers) throw new InvalidDataException();
                            var states = new BattleState[count];
                            for (int i = 0; i < count; i++) states[i] = BattleProtocol.ReadState(r);
                            BattleProtocol.End(r); dispatch = () => Snapshot?.Invoke(tick, states); break;
                        case BattlePacket.Shot:
                            int shooter = r.ReadInt32(), target = r.ReadInt32();
                            var points = new float[6]; for (int i = 0; i < 6; i++) points[i] = r.ReadSingle();
                            BattleProtocol.End(r); dispatch = () => Shot?.Invoke(shooter, target, points); break;
                        default: throw new InvalidDataException();
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is ArgumentException)
                { ReportDisconnect("Invalid server packet: " + ex.Message); peer?.Disconnect(); return; }
                // Presentation callbacks are application code, not packet decoding. Let their errors
                // reach the application log without misclassifying them as corrupt server traffic.
                dispatch();
            };
        }
        public void Connect(string host, int port, string ticket)
        {
            if (!network.Start()) throw new IOException("Cannot open battle socket.");
            peer = network.Connect(host, port, ticket);
        }
        public void Poll() => network.PollEvents();
        // LiteNetLib activates these knobs only in DEBUG / SIMULATE_NETWORK builds.
        public void ConfigureSimulation(int roundTripMs, int lossPercent)
        {
            network.SimulateLatency = roundTripMs > 0;
            network.SimulationMinLatency = roundTripMs; network.SimulationMaxLatency = roundTripMs + 1;
            network.SimulatePacketLoss = lossPercent > 0; network.SimulationPacketLossChance = lossPercent;
        }
        public void Send(BattleInput[] commands)
        {
            if (!Ready || commands.Length == 0) return;
            if (commands.Length > 3) throw new ArgumentException("At most three redundant input samples per packet.");
            peer.Send(BattleProtocol.Write(BattlePacket.Inputs, w =>
            { w.Write((byte)commands.Length); foreach (var input in commands) BattleProtocol.WriteInput(w, input); }), DeliveryMethod.Sequenced);
        }
        private void ReportDisconnect(string reason)
        {
            PlayerId = 0;
            if (disposed || disconnectReported) return;
            disconnectReported = true; Disconnected?.Invoke(reason);
        }
        public void Dispose() { if (disposed) return; disposed = true; network.Stop(); PlayerId = 0; }
    }
}
