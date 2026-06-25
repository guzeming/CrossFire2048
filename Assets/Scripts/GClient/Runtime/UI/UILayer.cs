namespace CrossFire2048.Client.UI
{
    /// <summary>
    /// UI 显示层级。数值越大，显示越靠前。
    /// </summary>
    public enum UILayer
    {
        Background = 0,
        Normal = 100,
        Popup = 200,
        Overlay = 300,
    }
}
