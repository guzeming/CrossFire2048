using System;

namespace CrossFire2048.Client.Features.Account
{
    /// <summary>
    /// 客户端当前登录会话。
    /// 登录成功后由 AuthClient 写入，后续大厅、房间、战斗请求都会依赖这里的身份信息。
    /// </summary>
    [Serializable]
    public sealed class AuthSession
    {
        public string UserId { get; private set; } = string.Empty;
        public string Username { get; private set; } = string.Empty;
        public string SessionToken { get; private set; } = string.Empty;

        public bool IsLoggedIn => !string.IsNullOrEmpty(UserId) && !string.IsNullOrEmpty(SessionToken);

        public void Set(string userId, string username, string sessionToken)
        {
            UserId = userId ?? string.Empty;
            Username = username ?? string.Empty;
            SessionToken = sessionToken ?? string.Empty;
        }

        public void Clear()
        {
            UserId = string.Empty;
            Username = string.Empty;
            SessionToken = string.Empty;
        }
    }
}
