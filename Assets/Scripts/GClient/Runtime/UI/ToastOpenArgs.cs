namespace CrossFire2048.Client.UI
{
    /// <summary>
    /// Toast 面板参数（也可直接传 string 给 ShowToast）。
    /// </summary>
    public sealed class ToastOpenArgs : UIPanelOpenArgs
    {
        public string Message = string.Empty;
        public float Duration = 2f;
    }
}
