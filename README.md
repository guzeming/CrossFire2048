# CrossFire2048

5v5 俯视角多人联机射击游戏（爆破模式）项目。

## 项目技术路线

### 客户端（Unity）
- 引擎：Unity（URP）
- 架构：模块化分层（`Core / Network / Gameplay / UI / Data`）
- UI：预制体 + `UIManager` 管理分组（HUD / Popup / Overlay）
- 输入：Unity New Input System
- 资源：按目录规范管理，后续可接入 Addressables

### 服务端（C# Socket）
- 网络模型：UDP + 自定义可靠层（序列号、ACK 位图、重传）
- 同步策略：服务器权威 + 客户端预测 + 服务器校正
- 命中判定：基于时间戳的回滚校验
- 时钟同步：RTT 估算偏移并平滑校时

### 协议与同步
- 包头字段：魔数 / 版本 / 连接 ID / 序列号 / ACK / 时间戳 / 消息类型
- 消息分类：
  - 可靠有序：状态变更、装备变更、回合状态
  - 可靠无序：聊天、通知
  - 不可靠：输入、快照、特效事件
- 快照优化：量化、差分、按优先级调度

## 当前文件结构

> 以下为当前已搭建的主干结构（可持续扩展）

```text
Assets/
  Art/
    UI/
      Fonts/
      Sprites/
      Atlases/
  Prefabs/
    UI/
  Scenes/
    Bootstrap/
    Menu/
    Match/
  Scripts/
    Core/
      Bootstrap/
      Events/
      Utils/
      CrossFire.Core.asmdef
    Network/
      Transport/
      Protocol/
      Sync/
      Replay/
      CrossFire.Network.asmdef
    Gameplay/
      Modes/
      Combat/
      Player/
      Bomb/
      CrossFire.Gameplay.asmdef
    UI/
      Base/
      Managers/
      HUD/
      Popup/
      Overlay/
      CrossFire.UI.asmdef
    Data/
      Configs/
      Runtime/
      CrossFire.Data.asmdef
    Debug/
  Tests/
    EditMode/
    PlayMode/
README.md
```

## TODO 与完成进度

### 总览进度
- 当前总进度：`12%`（项目骨架阶段）

### P0 - 项目骨架（进行中，80%）
- [x] 搭建 Unity 主目录结构
- [x] 创建核心 `asmdef` 并建立基础依赖
- [x] 建立 UI/场景/测试占位目录
- [ ] 完成 `UIBase` / `UIManager` 最小可用骨架
- [ ] 创建共享协议层（`Shared/Protocol`）初版定义

### P1 - 单房间联机闭环（未开始，0%）
- [ ] 客户端输入上传（定频）
- [ ] 服务端模拟移动并下发快照
- [ ] 客户端预测 + 服务器校正
- [ ] 他人角色插值显示
- [ ] 基础心跳与断线检测

### P2 - 射击与命中（未开始，0%）
- [ ] 开火输入与开火频率校验
- [ ] 服务器命中判定
- [ ] 时间戳回滚雏形
- [ ] 伤害、死亡、重生流程打通

### P3 - 爆破模式（未开始，0%）
- [ ] 回合状态机（准备/进行/结算）
- [ ] 安装与拆包流程
- [ ] 阵营胜负与比分
- [ ] HUD 对接（回合计时、炸弹状态、比分）

### P4 - 稳定性与优化（未开始，0%）
- [ ] 丢包/乱序网络仿真测试
- [ ] 协议压缩与快照优化
- [ ] 带宽与重传监控面板
- [ ] 基础反作弊（速率/位移/射速校验）

## 维护约定
- 每完成一个任务，更新复选框状态与阶段百分比。
- 每个里程碑结束后，更新“总览进度”。
