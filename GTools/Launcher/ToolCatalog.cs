namespace OperationBlacktide.Launcher;

public sealed record ToolItem(string Name, string Description, string Kind, string File, string Argument = "", bool UsesUnity = false);

public static class ToolCatalog
{
    public static IReadOnlyList<ToolItem> All { get; } = new[]
    {
        new ToolItem("目录与程序集", "检查模块边界、装备资源和场景脚本引用。", "构建", "verify-architecture.ps1", UsesUnity: true),
        new ToolItem("构建游戏客户端", "生成可独立运行的 Windows 客户端，输出到 Builds/Client。", "构建", "build-battle.ps1", "Client", true),
        new ToolItem("构建无窗口战斗服", "生成本机开发使用的 Unity 战斗服务器。", "构建", "build-battle.ps1", "Server", true),
        new ToolItem("构建爆破战斗服", "爆破匹配和开房会为每局自动启动独立服务器。", "构建", "build-battle.ps1", "Demolition", true),
        new ToolItem("构建 Dedicated Server", "需要当前 Unity 安装对应的 Dedicated Server 构建模块。", "构建", "build-battle.ps1", "Dedicated", true),
        new ToolItem("构建账号服务器", "编译 .NET 账号与入房服务。", "构建", "GServer/OperationBlacktide.Server", "dotnet"),
        new ToolItem("构建日志查看器", "编译服务器实时日志桌面工具。", "构建", "GTools/LogViewer", "dotnet"),
        new ToolItem("权威战斗集成验证", "随机端口、临时账号；验证双客户端、异常输入、延迟与丢包。", "联机", "Tests/Integration/OperationBlacktide.BattleTests", "integration"),
        new ToolItem("Unity 联机客户端验证", "检查预测校正、射击、换弹和实际渲染。", "联机", "verify-battle-client.ps1", UsesUnity: true),
        new ToolItem("爆破性能跑测", "独立客户端与 9 个 AI 实战；输出逐帧耗时及视野、遮挡、画质对照。", "玩法", "measure-demolition-performance.ps1", UsesUnity: true),
        new ToolItem("日志读取自检", "检查半行、UTF-8 分片、文件滚动与坏行。", "联机", "viewer", "self-test"),
        new ToolItem("通用战斗结构与回归", "检查模块依赖、预制体与训练模式；包含 3C、投掷物和视野。", "玩法", "verify-gameplay-structure.ps1", UsesUnity: true),
        new ToolItem("训练场", "训练场场景及基础功能验证。", "玩法", "verify-training.ps1", UsesUnity: true),
        new ToolItem("俯视角 3C", "角色运动、相机与瞄准验证。", "玩法", "verify-top-down.ps1", UsesUnity: true),
        new ToolItem("相机遮挡", "相机与建筑遮挡处理验证。", "玩法", "verify-camera.ps1", UsesUnity: true),
        new ToolItem("射击与特效", "武器开火、命中和特效验证。", "玩法", "verify-firing.ps1", UsesUnity: true),
        new ToolItem("武器动作", "持枪、换弹与武器动画验证。", "玩法", "verify-weapon-actions.ps1", UsesUnity: true),
        new ToolItem("投掷物", "投掷、库存和投掷动作验证。", "玩法", "verify-throwables.ps1", UsesUnity: true),
        new ToolItem("视野系统", "角色可见性与视野渲染验证。", "玩法", "verify-vision.ps1", UsesUnity: true),
        new ToolItem("脚步音效", "脚步触发及地表音效验证。", "玩法", "verify-footsteps.ps1", UsesUnity: true),
        new ToolItem("命中音效", "命中反馈与音效验证。", "玩法", "verify-impact-audio.ps1", UsesUnity: true),
        new ToolItem("战斗 HUD", "准星、弹药和生命显示验证。", "界面", "verify-hud.ps1", UsesUnity: true),
        new ToolItem("训练菜单", "训练场菜单及返回流程验证。", "界面", "verify-training-menu.ps1", UsesUnity: true),
        new ToolItem("大厅装备", "角色外观与装备选择验证。", "界面", "verify-lobby-loadout.ps1", UsesUnity: true),
        new ToolItem("品牌与登录界面", "品牌素材和登录界面验证。", "界面", "verify-branding.ps1", UsesUnity: true)
    };
}
