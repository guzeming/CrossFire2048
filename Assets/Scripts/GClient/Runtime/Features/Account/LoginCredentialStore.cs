using System;
using System.Runtime.InteropServices;
using OperationBlacktide.Shared.Protocol;

namespace OperationBlacktide.Client.Features.Account
{
    /// <summary>Windows 用户凭据库中的上次成功登录信息；不把密码写入 PlayerPrefs 或项目文件。</summary>
    public static class LoginCredentialStore
    {
        // Scope to the server, and persist for this Windows user on this machine only.
        private static string Target(string host, int port) =>
            "OperationBlacktide/Login/v1/" + host.Trim().ToLowerInvariant() + ":" + port;

        public static bool TryLoad(string host, int port, out string username, out string password)
        {
            username = password = string.Empty;
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            if (!CredRead(Target(host, port), Generic, 0, out var pointer)) return false;
            try
            {
                var entry = Marshal.PtrToStructure<Credential>(pointer);
                if (entry.Blob == IntPtr.Zero || entry.BlobSize == 0 ||
                    entry.BlobSize > AccountRules.PasswordMaxLength * 2 || entry.BlobSize % 2 != 0)
                    return false;
                string value = Marshal.PtrToStringUni(entry.Blob, (int)entry.BlobSize / 2);
                if (!AccountRules.IsUsernameValid(entry.UserName) || !AccountRules.IsPasswordValid(value))
                    return false;
                username = entry.UserName;
                password = value;
                return true;
            }
            finally { CredFree(pointer); }
#else
            return false; // Other platforms need their own secure credential provider.
#endif
        }

        public static bool TrySave(string host, int port, string username, string password)
        {
            if (!AccountRules.IsUsernameValid(username) || !AccountRules.IsPasswordValid(password)) return false;
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            var secret = Marshal.StringToCoTaskMemUni(password);
            try
            {
                var entry = new Credential
                {
                    Type = Generic,
                    TargetName = Target(host, port),
                    UserName = username,
                    Blob = secret,
                    BlobSize = (uint)(password.Length * 2),
                    Persist = 2 // CRED_PERSIST_LOCAL_MACHINE: this user, future logons, same machine.
                };
                return CredWrite(ref entry, 0);
            }
            finally { Marshal.ZeroFreeCoTaskMemUnicode(secret); }
#else
            return false;
#endif
        }

        public static bool Forget(string host, int port)
        {
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            return CredDelete(Target(host, port), Generic, 0) || Marshal.GetLastWin32Error() == 1168;
#else
            return true;
#endif
        }

#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
        private const uint Generic = 1;

        // Native layout from wincred.h. The credential blob contains UTF-16 without a terminator.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Credential
        {
            public uint Flags, Type;
            public string TargetName, Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint BlobSize;
            public IntPtr Blob;
            public uint Persist, AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias, UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredWrite(ref Credential credential, uint flags);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredDelete(string target, uint type, uint flags);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr credential);
#endif
    }
}
