using UnityEngine;

namespace CrossFire2048.Client.App
{
    /// <summary>
    /// 客户端应用配置。
    /// 当前先保存默认服务器地址，后续可以扩展测试服/正式服、超时时间、日志开关等配置。
    /// </summary>
    [CreateAssetMenu(fileName = "AppConfig", menuName = "CrossFire2048/App Config")]
    public sealed class AppConfig : ScriptableObject
    {
        [SerializeField] private string serverHost = "127.0.0.1";
        [SerializeField] private int serverPort = 7777;

        public string ServerHost => string.IsNullOrWhiteSpace(serverHost) ? "127.0.0.1" : serverHost;
        public int ServerPort => serverPort > 0 ? serverPort : 7777;
    }
}
