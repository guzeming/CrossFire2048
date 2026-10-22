#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace OperationBlacktide.Shared.Battle
{
    // Keys live outside Assets/builds, shared only by the account and battle processes.
    public sealed class BattleTicket
    {
        public string UserId = "", Username = "", Nonce = "";
        public long Expires;
        public const string Room = "arena-1";

        public static string Issue(byte[] key, string userId, string username)
        {
            using var stream = new MemoryStream();
            using (var w = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                w.Write(1); w.Write(Room); w.Write(userId); w.Write(username);
                w.Write(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 30);
                w.Write(Guid.NewGuid().ToString("N"));
            }
            byte[] data = stream.ToArray();
            using var mac = new HMACSHA256(key);
            return Convert.ToBase64String(data) + "." + Convert.ToBase64String(mac.ComputeHash(data));
        }

        public static bool TryRead(byte[] key, string token, out BattleTicket? ticket)
        {
            ticket = null;
            if (token == null || token.Length > 1024) return false;
            try
            {
                string[] parts = token.Split('.');
                if (parts.Length != 2) return false;
                byte[] data = Convert.FromBase64String(parts[0]), signature = Convert.FromBase64String(parts[1]);
                using var mac = new HMACSHA256(key);
                byte[] expected = mac.ComputeHash(data);
                if (signature.Length != expected.Length) return false;
                int difference = 0;
                for (int i = 0; i < expected.Length; i++) difference |= signature[i] ^ expected[i];
                if (difference != 0) return false;
                using var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
                if (r.ReadInt32() != 1 || r.ReadString() != Room) return false;
                var value = new BattleTicket { UserId = r.ReadString(), Username = r.ReadString(),
                    Expires = r.ReadInt64(), Nonce = r.ReadString() };
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (r.BaseStream.Position != data.Length || value.Expires < now || value.Expires > now + 35 ||
                    value.UserId.Length == 0 || value.UserId.Length > 64 || value.Username.Length > 32 || value.Nonce.Length != 32) return false;
                ticket = value;
                return true;
            }
            catch (Exception ex) when (ex is FormatException || ex is IOException || ex is ArgumentException) { return false; }
        }
    }
}
