namespace CrossFire2048.Server.Storage
{
    /// <summary>
    /// 持久化的账户记录。密码永不明文存储，只保存盐和哈希。
    /// </summary>
    public sealed class AccountRecord
    {
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string PasswordSalt { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public long CreatedAtUnixMs { get; set; }
    }
}
