using System;
using System.IO;

namespace OperationBlacktide.Shared.Battle
{
    [Flags] public enum InputButtons : byte { None = 0, Fire = 1, Reload = 2, Jump = 4, Sprint = 8 }
    public enum BattlePacket : byte { Inputs = 1, Welcome = 2, Snapshot = 3, Shot = 4 }
    public enum BattleTeam : byte { CT = 1, T = 2 }
    public struct BattleInput
    {
        public uint Sequence;
        public float X, Z, Yaw;
        public InputButtons Buttons;
        public bool Has(InputButtons button) => (Buttons & button) != 0;
    }
    public struct BattleState
    {
        public int Id, Life, Health, Ammo, Reserve, ReloadTicks;
        public uint Ack;
        public float X, Y, Z, Yaw, VerticalSpeed;
        public BattleTeam Team;
        public bool IsBot;
    }
    public static class BattleProtocol
    {
        public const int Version = 3, TickRate = 60, SnapshotInterval = 3, MaxPlayers = 8;
        public const string MapId = "dust2-v1";
        public const float Step = 1f / TickRate;
        public const int Capacity = 30, Reserve = 90, ReloadTicks = 144, FireTicks = 6, Damage = 25;
        public const int MaxPacket = 1100;
        public static byte[] Write(BattlePacket type, Action<BinaryWriter> write)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            { writer.Write((byte)type); writer.Write((byte)Version); write(writer); }
            return stream.ToArray();
        }
        public static BinaryReader Read(byte[] data, out BattlePacket type)
        {
            if (data.Length < 2 || data.Length > MaxPacket) throw new InvalidDataException("packet_size");
            var reader = new BinaryReader(new MemoryStream(data));
            type = (BattlePacket)reader.ReadByte();
            if (reader.ReadByte() != Version) { reader.Dispose(); throw new InvalidDataException("version"); }
            return reader;
        }
        public static void End(BinaryReader reader)
        { if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("trailing_data"); }
        public static void WriteInput(BinaryWriter w, BattleInput v)
        { w.Write(v.Sequence); w.Write(v.X); w.Write(v.Z); w.Write(v.Yaw); w.Write((byte)v.Buttons); }
        public static BattleInput ReadInput(BinaryReader r)
        {
            var v = new BattleInput { Sequence = r.ReadUInt32(), X = r.ReadSingle(), Z = r.ReadSingle(), Yaw = r.ReadSingle(), Buttons = (InputButtons)r.ReadByte() };
            if (!Finite(v.X) || !Finite(v.Z) || !Finite(v.Yaw) || Math.Abs(v.X) > 1.01f || Math.Abs(v.Z) > 1.01f ||
                v.Yaw < -180 || v.Yaw > 180 || ((byte)v.Buttons & ~15) != 0 || v.Sequence == 0)
                throw new InvalidDataException("invalid_input");
            return v;
        }
        public static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        public static void WriteState(BinaryWriter w, BattleState v)
        {
            w.Write(v.Id); w.Write(v.Life); w.Write(v.Ack); w.Write(v.X); w.Write(v.Y); w.Write(v.Z); w.Write(v.Yaw); w.Write(v.VerticalSpeed);
            w.Write(v.Health); w.Write(v.Ammo); w.Write(v.Reserve); w.Write(v.ReloadTicks);
            w.Write((byte)v.Team); w.Write(v.IsBot);
        }
        public static BattleState ReadState(BinaryReader r) => new BattleState { Id = r.ReadInt32(), Life = r.ReadInt32(), Ack = r.ReadUInt32(),
            X = r.ReadSingle(), Y = r.ReadSingle(), Z = r.ReadSingle(), Yaw = r.ReadSingle(), VerticalSpeed = r.ReadSingle(),
            Health = r.ReadInt32(), Ammo = r.ReadInt32(), Reserve = r.ReadInt32(), ReloadTicks = r.ReadInt32(),
            Team = (BattleTeam)r.ReadByte(), IsBot = r.ReadBoolean() };
    }
}
