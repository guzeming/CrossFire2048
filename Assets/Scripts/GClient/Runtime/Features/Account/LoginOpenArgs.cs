using CrossFire2048.Client.UI;

namespace CrossFire2048.Client.Features.Account
{
    /// <summary>
    /// 打开登录面板时可传入的初始参数。
    /// </summary>
    public sealed class LoginOpenArgs : UIPanelOpenArgs
    {
        public string DefaultUsername = string.Empty;
        public string DefaultPassword = string.Empty;
    }
}
