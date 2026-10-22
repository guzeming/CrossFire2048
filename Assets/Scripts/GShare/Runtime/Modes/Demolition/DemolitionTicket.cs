using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace OperationBlacktide.Shared.Modes.Demolition
{
    public static class DemolitionTicket
    {
        public static string Issue(byte[] key, string match, string user, long now)
        {
            using var stream = new MemoryStream();
            using (var w = new BinaryWriter(stream, Encoding.UTF8, true))
            { w.Write(1); w.Write(match); w.Write(user); w.Write(now + 180); }
            var data = stream.ToArray(); using var hmac = new HMACSHA256(key);
            return Convert.ToBase64String(data) + "." + Convert.ToBase64String(hmac.ComputeHash(data));
        }
        public static bool Validate(byte[] key, string ticket, string match, long now, out string user)
        {
            user = "";
            if (ticket == null || ticket.Length > 512) return false;
            try
            {
                var parts = ticket.Split('.'); if (parts.Length != 2) return false;
                byte[] data = Convert.FromBase64String(parts[0]), sig = Convert.FromBase64String(parts[1]);
                using var hmac = new HMACSHA256(key); var expected = hmac.ComputeHash(data);
                if (sig.Length != expected.Length) return false;
                int diff = 0; for (int i = 0; i < sig.Length; i++) diff |= sig[i] ^ expected[i];
                if (diff != 0) return false;
                using var r = new BinaryReader(new MemoryStream(data));
                if (r.ReadInt32() != 1 || r.ReadString() != match) return false;
                string id = r.ReadString(); long expiry = r.ReadInt64();
                if (id.Length < 1 || id.Length > 64 || expiry < now || expiry > now + 180 || r.BaseStream.Position != data.Length) return false;
                user = id; return true;
            }
            catch (Exception ex) when (ex is IOException || ex is FormatException || ex is ArgumentException) { return false; }
        }
    }
}
