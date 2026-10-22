using System;
using System.IO;
using OperationBlacktide.Shared.Battle;

namespace OperationBlacktide.Shared.Modes.Demolition
{
    public enum DemolitionPacket : byte { Input = 1, Welcome, Snapshot, Shot, Buy, Equip, Surrender, Feedback }
    public struct DemolitionInput
    {
        public BattleInput Command;
        public bool Interact, HasAim;
        public DemolitionPoint AimPoint;
    }
    public sealed class DemolitionActorState
    {
        public BattleState Motion;
        public string Weapon = "";
        public int Money, Armor, Kills, Deaths;
        public bool Kit, Connected;
    }
    public sealed class DemolitionSnapshot
    {
        public int Tick, Round, ScoreA, ScoreB, Carrier, Site, Interactor;
        public RoundPhase Phase;
        public BombPhase Bomb;
        public float Remaining, Progress;
        public DemolitionPoint BombPosition;
        public string Reason = "";
        public DemolitionActorState[] Actors = Array.Empty<DemolitionActorState>();
    }
    public static class DemolitionProtocol
    {
        public const int MaxPacket = 4096;
        public const byte Version = 2;
        public static byte[] Input(BattleInput command, bool interact, DemolitionPoint? aimPoint = null) => Write(DemolitionPacket.Input, w =>
        { BattleProtocol.WriteInput(w, command); w.Write(interact); w.Write(aimPoint.HasValue); if (aimPoint.HasValue) Point(w, aimPoint.Value); });
        public static DemolitionInput Input(BinaryReader r)
        {
            var input = new DemolitionInput { Command = BattleProtocol.ReadInput(r), Interact = r.ReadBoolean(), HasAim = r.ReadBoolean() };
            if (input.HasAim)
            {
                input.AimPoint = Point(r);
                if (!ValidCoordinate(input.AimPoint.X) || !ValidCoordinate(input.AimPoint.Y) || !ValidCoordinate(input.AimPoint.Z))
                    throw new InvalidDataException("invalid_aim_point");
            }
            BattleProtocol.End(r); return input;
        }
        static bool ValidCoordinate(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) <= 10000;
        public static byte[] Write(DemolitionPacket kind, Action<BinaryWriter> body)
        {
            using var stream = new MemoryStream();
            using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) { w.Write((byte)0xD5); w.Write(Version); w.Write((byte)kind); body(w); }
            if (stream.Length > MaxPacket) throw new InvalidDataException("demolition_packet_size"); return stream.ToArray();
        }
        public static BinaryReader Read(byte[] data, out DemolitionPacket kind)
        {
            if (data.Length < 3 || data.Length > MaxPacket) throw new InvalidDataException("demolition_packet_size");
            var r = new BinaryReader(new MemoryStream(data));
            if (r.ReadByte() != 0xD5 || r.ReadByte() != Version) { r.Dispose(); throw new InvalidDataException("demolition_version"); }
            kind = (DemolitionPacket)r.ReadByte(); return r;
        }
        public static void Point(BinaryWriter w, DemolitionPoint p) { w.Write(p.X); w.Write(p.Y); w.Write(p.Z); }
        public static DemolitionPoint Point(BinaryReader r) => new DemolitionPoint(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        public static byte[] Snapshot(DemolitionSnapshot s) => Write(DemolitionPacket.Snapshot, w =>
        {
            w.Write(s.Tick); w.Write((byte)s.Phase); w.Write(s.Round); w.Write(s.ScoreA); w.Write(s.ScoreB); w.Write(s.Remaining); w.Write(s.Reason);
            w.Write((byte)s.Bomb); w.Write(s.Carrier); w.Write(s.Site); w.Write(s.Interactor); w.Write(s.Progress); Point(w, s.BombPosition);
            w.Write((byte)s.Actors.Length);
            foreach (var a in s.Actors)
            {
                BattleProtocol.WriteState(w, a.Motion); w.Write(a.Weapon); w.Write(a.Money); w.Write(a.Armor); w.Write(a.Kills); w.Write(a.Deaths); w.Write(a.Kit); w.Write(a.Connected);
            }
        });
        public static DemolitionSnapshot Snapshot(BinaryReader r)
        {
            var s = new DemolitionSnapshot { Tick = r.ReadInt32(), Phase = (RoundPhase)r.ReadByte(), Round = r.ReadInt32(), ScoreA = r.ReadInt32(), ScoreB = r.ReadInt32(),
                Remaining = r.ReadSingle(), Reason = r.ReadString(), Bomb = (BombPhase)r.ReadByte(), Carrier = r.ReadInt32(), Site = r.ReadInt32(), Interactor = r.ReadInt32(), Progress = r.ReadSingle(), BombPosition = Point(r) };
            int count = r.ReadByte(); if (count != 10) throw new InvalidDataException("roster_count");
            s.Actors = new DemolitionActorState[count];
            for (int i = 0; i < count; i++) s.Actors[i] = new DemolitionActorState { Motion = BattleProtocol.ReadState(r), Weapon = r.ReadString(), Money = r.ReadInt32(), Armor = r.ReadInt32(), Kills = r.ReadInt32(), Deaths = r.ReadInt32(), Kit = r.ReadBoolean(), Connected = r.ReadBoolean() };
            BattleProtocol.End(r); return s;
        }
    }
}
