# 爆破模式实现

## 入口与部署

大厅选择爆破模式，点击进入游戏，打开匹配 / 开房面板。组队用六位队伍号邀请；房间支持公开列表、六位房间号、密码、私密、选边、准备、转让房主、踢出未开局成员。标准房间需十名真人；自定义房间也可显式允许 AI，由房主在全员准备后发起五秒开局倒计时。

普通匹配支持 1–5 人完整组队。十名真人凑齐可提前开局；未开启 AI 补齐的玩家有十五秒确认，已开启者自动确认。有人拒绝或超时，已全部确认的队伍保留等待优先级重新排队。确认阶段锁定队伍名单，且不会延长已开启 AI 队伍的三十秒期限。

开启「允许 AI 补齐」后，从服务端接受排队的单调时间起等待三十秒，再固定真人名单，用 AI 补足两边各五人。只选择也允许 AI 的其他队伍；关闭开关的队伍继续等真人。撤销或队伍变更取消原计时。超时发起开局，战斗进程启动和地图加载另计。

先构建 `.NET` 账号服与爆破服，独立客户端还需构建 Client：

```powershell
dotnet build GServer/OperationBlacktide.Server -c Release
./GTools/build-battle.ps1 -Target Demolition
./GTools/build-battle.ps1 -Target Client
```

账号服在需要时启动 `Builds/DemolitionServer/Blacktide.DemolitionServer.exe`，不需要手工为每局开进程。可通过 `--demolition-server <绝对路径>` 覆盖路径。已运行的旧账号服需要重启才能载入新业务。账号服 / 战斗服沿用现有本机 loopback 部署；当前没有公网服务发现或云端服务器分配。

原客户端仍在运行、无法覆盖文件时，可使用 `-Target Client -OutputDirectory Builds/DemolitionPreview`，生成独立的 `Builds/DemolitionPreview/Client/Blacktide.exe`。它仍使用相同的账号服地址。

开发环境每个账号服最多并发四局，每局一个 Unity 进程，独立世界、端口、密钥验证、名单和 GameLogic。容量不足、启动失败和加载超时不伪造开局成功。运行清单、服务日志与结算保存在 `data/demolition/<matchId>`；签名密钥不发送给客户端。账号服退出会关闭其拥有的战斗进程。

## 结构

- `GShare/Runtime/Modes/Demolition`：消息、签名票据、二进制协议和纯 C# 玩法组件；独立能力放在 `Components` 下。
- `GameLogic` 装配 world、clock、DemolitionTeamComponent、DemolitionEconomyComponent、DemolitionBombComponent、DemolitionRoundComponent；不装配自动重生组件。
- `GServer/.../Demolition`：组队、匹配、房间权限、准备倒计时和进程分配。
- `GBattleServer/DemolitionServerHost`：验证名单、接收指令，适配物理、导航、出生和网络快照。
- `GClient/.../Demolition`：大厅面板、网关、客户端运动预测、BattleAvatar、HUD、购买和队友观战。
- `GSimulation/DemolitionMap`：Unity 地图 / 交互可达性适配。出生点来自原始 Dust II 标准出生实体，A/B 点从地面标记生成；烘焙时验证落地、通路和胶囊不穿模。出生池、路线入口与守点朝向配置在 `Assets/Art/Maps/DustII/DemolitionLayout.json`。

`GConfig/DemolitionRules.json` 是回合数值来源，配置导入器导出到 `Assets/Resources/Config`；规则验证不依赖 Unity。角色生命和武器实例复用共享 Character / Weapon 组件，表现复用 BattleAvatar。

## 已实现规则

5v5、先胜 13 回合、12 回合后交换攻防、12:12 平局。15 秒购买准备，115 秒进攻时间；炸弹安装 3 秒，40 秒爆炸；拆除 10 秒，有拆弹器 5 秒。按住 E 交互，松开、中断或离开有效区域重置进度。炸弹携带者死亡掉落，活着的进攻方接近拾取；下包后进攻方全灭继续拆弹 / 爆炸倒计时。

每回合一条命。存活保留武器和剩余护甲；死亡下回合恢复默认手枪。换边重置资金、装备和连败补偿。初始 800，上限 16000；胜方 3250，败方 1400 起逐次加 500 到 3400，击杀 300，安装个人奖励 300，安装后败方进攻队额外 800。购买由服务器验证阶段、资金、阵营和已持有状态。

首版商店包含 AK-47、M4A1-S、沙漠之鹰、防弹衣、拆弹器，默认手枪为 Glock / USP。武器数值是本项目首版平衡参数。1/2 切换已持有的主/副武器，B 打开购买，Tab 战绩，死亡后 V 切换己方存活角色。友军可挡枪，不扣友军生命。

AI 在服务器输出与真人相同的移动、瞄准、攻击、换弹指令，并按资金购买、前往 A/B 点、拾取炸弹、安装与拆除。基础战斗感知复用共享 AIController；当前没有声音/烟雾感知、高阶协同战术、动态段位难度或投掷物战术。

开局使用原地图的 15 个 T 出生候选、5 个 CT 出生候选，每回合按对局种子重排五个席位，半场按实际攻防换出生区域。共享 `DemolitionTactics` 根据出生点到路线入口的 NavMesh 路程分配职责：T 可走 A 大夹 A 小、A 小主攻、B 洞夹中路或 B 洞快攻；CT 固定 2A / 2B / 1 中路，具体谁去哪个位置由出生优势决定。同等可行的计划按回合变化，持包者随主攻组，入口与支援错开起步。

`DemolitionBotNavigation` 按路线关键点逐段前进，局部重新寻路不会直接跳到包点而绕回中门。防守者到位后看住入口，战斗感知不再把角色拖去追敌；短暂交火结束后继续原路线。队友靠近时减速/侧移。掉包只派一名存活人机取回；下包后 T 分散守点，CT 指派一人拆包、其余支援，死亡后重新选人。手枪/步枪换弹按当前弹匣容量判断，机器人射击同时考虑目标高差。

开局后固定十个席位，不从队列补入陌生玩家。原玩家可返回大厅重连，保留原角色、生命和装备；死亡玩家等待下回合。连续断线三分钟判为弃赛，开放剩余真人一致投降。加载等待 120 秒超时则异常结束。对局结束返回原好友队伍或自定义房间，房间重新准备。

## C4 模型、动画与声音

爆破客户端使用已导入的 CS C4 模型，携带时挂在背部，安装时切为手持，掉落和安装后显示地面实体。安装使用 CS `planting` 全身动作，拆除使用 `defuse_crouch_enter` / `defuse_crouch_loop`，根据服务器交互进度播放；拆弹器的 5 秒和普通的 10 秒共用同一动作循环。交互期间隐藏枪械并暂停持枪动作与瞄准补偿，取消、死亡、换装备和回合结束后恢复。真人、其他客户端角色和 AI 共用表现。

CS 原声包括取出 C4、启动、按键、安装完成、开始拆除、拆除完成、倒计时蜂鸣和爆炸；世界音效按距离衰减，剩余 10 秒切换紧迫蜂鸣。音效由快照状态变化触发，重复快照不重复播放完成音效，重连时不补播已经发生的安装声。

资源位于 `Assets/Art/Demolition`，运行时引用位于 `Assets/Resources/Gameplay/Demolition/Presentation.asset`。重建源动画使用 `ArtSource/CS2Imports/Scripts/export_demolition_actions.py`（Blender），音频使用同目录 `prepare_demolition_audio.py`，然后执行 Unity 菜单 `OperationBlacktide/Demolition/Build C4 Presentation`。已有资源可直接使用，无需原始 CS 安装。

`GTools/verify-demolition-presentation.ps1` 在独立 Unity 工程检查两个阵营骨架、动作、模型落地、交互取消、拆弹器时长、换装备恢复、完成音效去重和回合重置，并生成携带／安装／落地／拆除截图。

## 边界

当前为普通爆破首版，没有排位、赛季、加时、选图禁图或反作弊系统。组队匹配按等待时间、组队规模与隐藏实力分组合，不拆好友队伍；实力搜索窗口从 250 分起随等待放宽，比较双方平均分与最高分差异，优先相同组队规模。隐藏 Elo 初始 1000，K=32，仅完整真人普通匹配结算，AI 局、自定义房与异常局不改变分数；按账号持久化。尚未实现跨区域延迟筛选或好友关系数据库，邀请通过队伍号/房间号完成。AI 对局带独立标记，不提供排位积分。

武器经济只覆盖上述首批装备，不包含完整 CS 商店、武器掉落拾取、战术暂停、投掷物网络同步或爆炸范围伤害。当前网络快照同步完整位置，客户端只允许己方死亡观战；未实现服务器视野裁剪。公网部署、竞技公平性与容量扩展需单独完成。

## 验证

```powershell
dotnet run --project Tests/Integration/OperationBlacktide.DemolitionTests
dotnet run --project Tests/Integration/OperationBlacktide.DemolitionTests -- --integration
./GTools/verify-demolition-client.ps1
dotnet run --project Tests/Core/CoreValidation.csproj -- .
./GTools/verify-demolition-tactics.ps1
dotnet run --project Tests/Integration/OperationBlacktide.DemolitionTests -- --tactics
```

集成验证可添加 `--server <战斗服 exe 路径>` 使用独立构建产物，不必覆盖正在使用的 `Builds` 目录。

纯逻辑测试覆盖三十秒前后、真人提前开局、开关隔离、五人组队不拆、取消与重排、确认超时与名单锁定、隐藏实力分持久化与幂等结算、密码、权限、房主迁移、开局锁定、准备清除、购买校验、炸弹交互/掉落/拾取、全灭后炸弹继续、半场重置、13 分结束、12:12 和加载超时。

联通测试使用临时账号与随机端口，覆盖真实账号服排队、战斗进程分配、两客户端十人快照、八名 AI 导航、购买扣款和原角色重连。UI 验证从实际登录场景进入大厅，点击匹配与开房按钮，检查战斗 HUD 和返回重连，并保存截图到独立验证目录。

战术验证在独立 Unity 副本烘焙布局，并让真实 CharacterController 走完九条路线；核心测试覆盖出生分配、回合变化、持包协同、守位与手枪换弹。`--tactics` 用随机端口运行两场九个人机对局，检查双方出生区域、每名人机离开出生区、路线推进和交火，输出逐角色位置 CSV。服务日志 `BOT_OPENING` 记录每回合计划、角色分工和出生位置。修改布局或 AI 后需重新构建 Demolition 服务端；运行中的旧对局不会热更新。


## 战斗交互与匹配反馈（2026-10-08）

爆破客户端复用训练场的 `CharacterMotor` 瞄准、`TopDownCameraController`、`CombatOverlay`、`WeaponPresentation`、`WeaponEffects`、`UpperBodyAim`、`AimLaser` 和本地脚步音效。`NetworkWeaponPresentation` 只接收服务器快照与开火事件，不创建本地武器伤害或弹药逻辑。鼠标移动瞄准，右键进入观察瞄准，中键旋转、滚轮缩放；购买、菜单、死亡、失焦和断线会屏蔽战斗输入。手枪按次射击，换弹和跳跃使用按下事件，恢复控制后需重新按下攻击键。

爆破 UDP 协议版本为 2，输入包含经过有限数值与范围校验的三维瞄准点；服务器从角色位置计算实际弹道并裁决最近遮挡和伤害。客户端与专用服需同时更新。换弹剩余 tick 向上取整，完成前不显示为零。

匹配按钮立即进入请求状态并防止重复提交；收到服务端排队状态后显示 AI 补齐倒计时。请求独立按编号确认，旧轮询回复不会解除等待，八秒无响应给出错误提示。关闭尚在提交的匹配页面也会发送取消请求。
