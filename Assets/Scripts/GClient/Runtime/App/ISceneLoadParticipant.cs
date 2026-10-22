namespace OperationBlacktide.Client.App
{
    /// <summary>场景 Start 之后仍有异步初始化时，延长加载遮罩至可展示游戏或错误界面。</summary>
    public interface ISceneLoadParticipant
    {
        bool IsSceneReady { get; }
    }
}
