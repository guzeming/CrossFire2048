# 开发实现与验证细节

[返回图文首页](../README.md) · [截图来源](../ReadMeRes/screenshots/README.md)

本文保留原 README 的实现细节、资源构建菜单与现有检查命令。旧的 Unity 玩法、界面、联机客户端回归及性能跑测已移除，后续另建专门验证管线；日常开发按需要做编译和静态检查。快速开始、架构流程图和功能截图见首页。文中所有代码路径均以仓库根目录为基准，命令也在根目录运行。`ArtSource/` 只存在于美术维护者本机，不是克隆或运行现有项目的依赖。

## 游戏启动面板

Windows 下直接双击项目根目录的 **`游戏启动面板.bat`**。首次打开会自动构建桌面面板，需要 .NET 8 SDK；后续仅在面板源码变化时重新构建。

首次启动也会在根目录生成带黑潮徽章图标的 **`游戏启动面板` 快捷方式**（`.lnk`），之后可直接双击它打开。快捷方式按本机路径生成，不提交到仓库；工程移动后运行一次 `.bat` 即可更新。面板窗口和程序文件使用同一图标。

右上角“使用说明”在面板内打开阅读窗口，包含启动步骤、实时日志、构建检查与常见问题，无需打开编辑器。

- **游戏与服务**：启动游戏、多开客户端、开服并进入游戏、启动/停止/重启服务器、打开实时服务器日志或 Unity 工程。客户端缺失时会先自动构建。多客户端联机请使用不同账号。
- **构建与检查**：集中运行客户端、战斗服、账号服、日志工具的构建，以及目录与程序集检查、日志工具自检；支持搜索。
- **路径与目录**：自动查找工程所需 Unity 版本，也可手动选择 `Unity.exe`；快速打开项目、构建产物、日志、工具和构建工作目录。

客户端窗口支持拖动边框调整大小。大厅右上角“设置”可切换全屏、选择分辨率并保存，下次启动自动恢复；大厅按 Esc 打开“设置 / 返回登录界面 / 退出游戏”菜单。设置修改后未保存就返回或关闭游戏时，会提示继续编辑、不保存或保存并离开。

面板每两秒刷新客户端数量和服务器状态，运行输出直接显示在底部，并保存到 `Logs/Launcher/`。构建和检查串行执行。关闭面板不会关闭已启动的游戏或服务器；正在执行工具时关闭窗口会先最小化，完成后可正常退出。Unity 路径设置保存在 `UserSettings/launcher.json`，不提交到仓库。

运行任务时显示当前任务、阶段与已用时间，日志上方的进度条和百分比随实际阶段推进；Unity 编译任务及目录/程序集检查中的可计数工作项按完成数量更新，多步骤任务合并为整体进度。百分比表示阶段完成度，不表示耗时占比；没有新进展时保持当前值，整个任务成功后才显示绿色 100%，失败或取消后保留当时的百分比、结果与耗时。期间锁定工具选择，避免选中的工具与执行中的任务混淆。构建/检查可使用“取消任务”停止本次工具及子进程。Unity 构建和程序集检查使用独立工程，首次运行可能花费数分钟导入资源；统一通过 `GTools/wait-unity-task.ps1` 显示同步、导入、编译、检查等阶段，保留完整日志路径并实时转发通过/失败标记与错误。文件复制失败最多重试一次；长时间没有新日志时会明确显示等待状态，直到完成、超时或取消。

若显示“端口已占用”，请先关闭旧的账号/战斗服务，再通过面板开服。面板仅停止 `data/local-server-processes.json` 中身份匹配的托管进程，不会按端口强制终止未知服务。当前本地服务端口仍为 TCP 7777、UDP 7778。

面板源码位于 `GTools/Launcher/`，独立于 Unity 游戏程序集。开发检查可运行构建产物 `Blacktide.Launcher.exe --self-test --root <项目目录>`；`--integration-test` 使用临时数据和随机端口测试真实服务启停，`--render-preview <PNG 路径>` 验证界面并输出两页预览。

进度日志使用 `[进度] <0-100 的整数> <阶段说明>`；Unity 编辑器脚本通过 `LAUNCHER_PROGRESS <百分比> <说明>` 输出，由共享日志读取器映射到当前任务区间。`Tests/Tools/verify-launcher-progress.ps1` 使用独立子进程验证日志转发、区间映射、末行无换行和失败退出，无需启动 Unity。

## 项目概况

品牌资源位于 `Assets/UI/Art/Branding/`：主 LOGO `BlacktideLogo.png`（登录页）、横版 `BlacktideHorizontal.png`（大厅顶部）、徽章 `BlacktideEmblem.png`（默认应用图标）。原设计板与生成提示词保存在 `ArtSource/UI/Blacktide/`。大厅背景中的旧标识已同步替换。Unity 产品名为 `Operation Blacktide`，包标识为 `com.operationblacktide.game`；代码、编辑器菜单和服务端工程统一使用 `OperationBlacktide`。工作区根目录保持现有路径。

当前工程已包含账户注册登录、登录/大厅 UI、DustII 本地训练场、服务端权威联机测试场及首版爆破模式。客户端、共享逻辑、公共模拟和战斗服已拆分程序集，配套启动面板、配置工具、实时日志和验证入口；大厅另有可选的离线语音助手。联机测试场和爆破模式的已实现能力及当前边界见下文与 `spec/`，后续继续完善完整玩法、联网安全和性能验证。

## 项目目录与依赖边界

```text
Assets/Scripts/
  GShare/Runtime/              纯 C#；客户端、服务端和工具共用
    Components/               组件宿主与生命周期
    Characters/ Combat/       角色能力、WeaponInfo / Weapon
    AI/Components/            感知、导航、决策
    GameLogic/Components/     每局玩法基座、时钟、重生、补给
    Modes/Demolition/         爆破规则、协议和玩法组件
    Protocol/ Battle/         通信协议和战斗同步数据
  GSimulation/Runtime/
    Movement/ Maps/ Config/   Unity 运动、地图与配置适配
  GClient/Runtime/
    App/ Common/              启动、场景流程、全局事件
    Content/                  公共装备目录、角色/武器模型与动画资源绑定
    Gameplay/                 可复用的 Avatar、战斗、相机、表现组件
    Features/                 Account、Lobby、Training、Battle、Demolition
    Network/ UI/              客户端网络与 UI 基础设施
    Development/              地图查看等开发辅助行为
  GBattleServer/Runtime/
    Hosting/ AI/              权威战斗宿主与 Unity AI 适配
    Modes/Demolition/         爆破战斗服宿主
  Editor/
    Build/ Import/ Inspectors/ 客户端与战斗服共用的 Unity 编辑器工具
GServer/OperationBlacktide.Server/  独立 .NET 账号、大厅与匹配服务
GTools/
  Launcher/ GameConfigEditor/ LogViewer/  独立开发工具
  Common/ DustII/                       工程同步工具、地图导入工具
  build-*.ps1、verify-architecture.ps1  构建和程序集检查入口
Tests/
  Core/                       无引擎共享逻辑验证
  Unity/Architecture/         Unity 程序集与资源引用检查
  Integration/                战斗网络、账号与爆破集成验证
  Tools/                      开发工具和工程同步脚本验证
GConfig/                      跨引擎 JSON 配置源
```

### 放置和依赖规则

- `GShare` 的 `Blacktide.Shared` 禁止引擎引用；独立 .NET 工程编译同一份共享源码，不复制实现。
- `GSimulation` 只依赖共享层。`GClient` 和 `GBattleServer` 分别依赖共享层与模拟层，互不引用。
- `GClient/Content` 保存 Unity 资源绑定；`EquipmentCatalog` 供大厅、训练和联机战斗共同使用。`WeaponInfo` 是纯配置、`Weapon` 是逻辑实例、`WeaponModel` 是表现对象，资源绑定不代替这三者。
- `Gameplay` 和 `Content` 不依赖 `Features`；具体功能组合公共能力。账户功能放 `Account`，大厅面板和换装操作放 `Lobby`。
- 独立演进的组件使用同名文件；紧密相关的小型枚举、DTO 可以放在同一文件。玩法规则放 `Modes/<玩法>`，不混入通用组件。
- `Blacktide.Editor` 只在编辑器中编译。编辑器同时加载客户端和战斗服；普通客户端构建排除战斗服，服务端构建通过 `BuildPlayerOptions.extraScriptingDefines` 添加 `BLACKTIDE_SERVER` 并排除客户端。专用服务器构建也支持 `UNITY_SERVER`。
- 战斗服由 `BattleServer.unity` 中的 `BattleServerEntry` 启动，根据命令行选择普通战斗或爆破宿主；服务端构建临时移除客户端渲染管线引用，构建结束后恢复编辑器设置。
- `Assets/Resources/Gameplay` 保存公共武器、投掷物、动画与视野资源；`Training` 只保留训练玩法专用资源。资源移动必须保留 `.meta` 并更新字符串加载路径。
- `GTools` 只存放开发工具和命令入口；验证源码放 `Tests`。手写 `.csproj` 必须提交，`bin/obj` 和 Unity 根目录生成的工程文件忽略。
- Unity 程序集检查和构建通过 `GTools/Common/UnityProject.ps1` 镜像同步到 `.build-check` 内的独立工程，自动清理旧脚本、旧资源和上一套测试驱动，保留 `Library` 缓存；输出与本机数据不提交。
- UI 基础规范见 `Assets/Scripts/GClient/Runtime/UI/ui-spec.md`。新增目录应对应明确职责，不建立无边界的通用杂物目录。

目录调整后可执行 `GTools/verify-architecture.ps1` 检查程序集归属、装备资源、场景与预制体脚本引用。添加 `-BuildPlayers` 会在 `.build-check` 下实际构建客户端和战斗服，验证产物没有混入另一端的程序集。`Tests/Tools/VerifyUnityProject.ps1` 验证工程同步的路径限制、旧文件清理、GUID 和缓存保留。

### 组件化架构：共享逻辑、玩法与表现

对象负责身份、组件装配和生命周期，组件负责具体能力。共享层使用普通 C#，不引用 Unity、Unreal 或客户端程序集；客户端、服务端和工具编译同一份源码。现有源码入口仍在 `Assets/Scripts/GShare/Runtime`，目录位置不表示对 Unity 的依赖，`Blacktide.Shared.asmdef` 使用 `noEngineReferences` 约束。

| 边界 | 实现与职责 |
| --- | --- |
| `GShare/Runtime/Components` | `ComponentHost` / `GameComponent`：单一宿主、依赖检查、拓扑初始化与 Tick、逆序释放；开始后冻结组件组成 |
| `GShare/Runtime/Characters` | `Character` 组合移动规则、生命护甲和装备组件；不持有模型、相机或 UI |
| `GShare/Runtime/Combat` | `WeaponInfo` 配置，`Weapon` 实例，`AmmoState` 弹药/换弹组件，`FireControl` 射击节奏组件 |
| `GShare/Runtime/AI` | `AIController` 组合感知、导航请求和决策；`IBotEnvironment` 提供遮挡与路径查询，AI 输出与玩家相同的战斗指令 |
| `GShare/Runtime/GameLogic` | 每局独立的 `GameLogic`，装配 `CombatWorld`、时钟、重生和补给组件；扩展玩法组件在 Start 前装配 |
| `GClient/Runtime/Gameplay/Avatars` | Unity 表现适配：`Avatar` 组合模型、骨骼挂点、装备挂接和动画组件；`LobbyAvatar` 与 `BattleAvatar` 独立管理场景表现 |
| `GSimulation` | Unity 碰撞、资源和运动适配，客户端预测与 Unity 战斗服共用；这一层仍需在其他引擎中重写 |
| `GBattleServer` | 联机输入校验、物理命中与回滚、快照和权威执行；复用共享 Character、Weapon、GameLogic 和 AI |
| `GConfig` / `GTools/GameConfigEditor` | 引擎独立的 JSON 配置源文件与本地配置面板，验证规则复用共享 C# |

#### 对象所有权

`GameLogic` 持有本局的 `CombatWorld`，世界持有 `Character`；角色的 `EquipmentComponent` 持有 `Weapon` 实例。切换装备保留原实例的弹药，移除装备可将实例所有权转交给拾取物或另一份装备集合。

本地训练由模式控制器关联 Character 与 BattleAvatar；联机客户端由 `BattleArenaClient` 按实体 ID 管理 BattleState 快照、预测与 BattleAvatar。共享 Character 不持有 BattleAvatar，因此服务端可以只创建逻辑对象。大厅展示模块持有 LobbyAvatar，装备预览持有独立 WeaponModel；角色手持的 WeaponModel 由 Avatar 的装备挂接组件持有。场景退出时，模型组件释放自己创建的模型和动画覆盖控制器。

`WeaponInfo` 保存类型 ID、数值与资源 ID，`Weapon` 保存一把武器的运行状态，`WeaponModel` 管理模型和挂点。同类型的多个武器实例不共享弹药。原 `WeaponDefinition` 作为 Unity 资源绑定兼容层保留，基础字段继承 WeaponInfo，以保留现有资源序列化；进入玩法时由公共 JSON 提供数值。

`LobbyLoadoutPreview` 保留原脚本 GUID，作为已有场景/UI 的薄适配器调用 LobbyAvatar。训练玩家、训练靶与联机角色直接使用 BattleAvatar。底层 Avatar 不保存等级、任务、商城、账号或生命数据。

#### 玩法与权威边界

训练场与联机测试场的战斗服各自创建独立 GameLogic，玩法组成来自 `GConfig/Modes/*.json`。自动重生和补给能力由组件提供；训练场开启无限备弹、无限投掷物和电击枪充能，联机测试场使用有限供给。移除 respawn 组件会关闭自动重生，移除 supply 组件则使用有限供给。规则组件可声明依赖；缺少依赖、重复组件或循环依赖会在启动时失败。

`GameLogic`、`Character`、`Weapon`、`Avatar` 和 `WeaponModel` 构造时可传入额外的 `GameComponent`，统一在 Start 前装配。新增能力通过组件声明依赖并加入宿主，不需要为每种组合派生一套对象类。

服务端执行权威规则，客户端读取权威状态并预测必要的运动。共享代码不代表信任客户端计算结果：位置、命中、伤害、奖励和资产变更仍由服务端确认。本地训练场在本机运行权威 GameLogic。每个世界维护自己的角色和事件订阅，退出房间必须释放整个宿主。

键鼠输入位于 `LocalPlayerInput` / `LocalWeaponInput`；角色运动和武器执行接口可由测试或其他控制器调用。AI 的纯规则已抽到 GShare，Unity 战斗服中的 BattleBotBrain 只适配 NavMesh 与 Physics。时间通过 Tick 参数传入；普通 C# 的数值类型跨越引擎边界，不将 Transform、Animator、RaycastHit 传入共享核心。

玩家等级、背包、任务、活动、商城属于跨局业务模块，后续通过结算结果与 GameLogic 衔接；当前重构不把尚未实现的业务伪装成空组件。新玩法组合现有组件并补充专属目标/计分/回合组件，不在通用角色、武器里硬编码玩法分支。

#### 配置工作台

在仓库根目录执行：

```powershell
dotnet run --project GTools/GameConfigEditor/GameConfigEditor.csproj
```

打开 `http://127.0.0.1:8094`，编辑武器参数、联机武器参数和训练/联机玩法组件及补给策略。工具不启动 Unity；保存时校验参数与重复 ID，并检测外部文件修改，保存公共源文件后导出到 `Assets/Resources/Config`。也可从 Unity 菜单 `OperationBlacktide > Configuration > Import Portable Config` 导入。运行中的配置不会热替换，请停止并重新进入玩法。已导入武器的 ID 保持稳定，新增武器还需要维护模型与动画资源映射。

- `GConfig/Weapons.json` 是训练武器数值的编辑入口；`Assets/Art/Combat/weapon_data.json` 保留为美术导入原始数据。
- `GConfig/BattleWeapon.json` 保留联机测试场原有的 30 发、90 备弹、25 伤害和 2.4 秒换弹配置，与训练武器平衡配置分开。
- `Assets/Resources/Config` 是 Unity 导入副本，需要随构建提交；不要直接编辑生成副本。
- 资源 ID 与 Unity 资源绑定分开。Unreal 接入时需先选定并验证 C# 运行时集成方案，再复用共享 C#，新增资源、物理、导航和表现适配；当前尚未实现或验证 Unreal 适配器。
- Avatar/WeaponModel 的现有模型组件仍为 Unity 实现。引擎独立配置工具并不意味着材质、动画、物理行为已经实现跨引擎等价。

#### 验证入口

```powershell
dotnet run --project Tests/Core/CoreValidation.csproj
dotnet build GTools/GameConfigEditor/GameConfigEditor.csproj
node GTools/GameConfigEditor/verify.cjs
```

CoreValidation 不依赖 Unity，覆盖组件生命周期、配置校验、武器状态隔离与转移、重生时序、房间隔离、客户端权限和 AI 决策。联机变更按需要重建战斗服；明确要求执行集成测试时使用与源码匹配的产物。

### 通用战斗、3C 与模式边界

角色、控制、相机和战斗组件位于 `Assets/Scripts/GClient/Runtime/Gameplay/`，供各玩法模式复用：

| 目录 | 职责 |
| --- | --- |
| `Gameplay/Characters` | `CharacterMotor` 角色运动与瞄准、角色动画、上半身瞄准和脚步声 |
| `Gameplay/Cameras` | `TopDownCameraController` 跟随、开镜、旋转，以及建筑遮挡处理 |
| `Gameplay/Combat` | 武器、近战、投掷物、弹孔与特效、装备选择、`Damageable` 受伤对象和 `CharacterVitals` |
| `Gameplay/Vision` | 角色视野、目标可见性和 URP 视野渲染 |
| `Gameplay/UI` | 通用 `CombatHudPanel`、准星、命中反馈和 HUD 图形；通过 `ICombatHudContext` 获取模式状态 |
| `GShare/Runtime/Combat` | 不依赖 Unity 的 `AmmoState` 弹药/换弹状态与 `FireControl` 射速状态，客户端与 .NET 服务端均可引用 |
| `Features/Training` | 训练场组装、菜单、返回大厅、HUD 入口与训练靶生成/重生 |

依赖方向是模式调用通用组件。射击、近战、投掷伤害和视野统一面向 `Damageable`，不依赖 `TrainingTarget`；玩家生命组件和训练靶分别实现它。
`CharacterMotor` 提供重生与跌出地图事件，训练场负责恢复生命、重置装备和自动复生。无限备弹、无限投掷物与电击枪自动充能由训练场显式开启，普通组件默认使用有限补给；有限投掷物由模式通过 `ThrowableController.SetSupply` 发放。
`WeaponSelection` 仅管理传入的装备集合；训练场传入全目录，其他模式应传入实际持有的装备。

脚本迁移保留原 `.meta` GUID，Unity 类型使用 `MovedFrom` 记录旧名，训练 HUD 原预制体继续通过 `TrainingPanel` 使用通用基类。
公共资源现位于 `Resources/Gameplay`，`Resources/Training` 仅保留训练专用配置；资源构建菜单保留现有入口。角色外观与装备目录仍复用现有 Loadout 资源。
本地训练继续使用这些客户端组件。大厅中的“联机测试场”使用独立的服务端权威链路：`GSimulation` 的公共角色模拟与碰撞、`GBattleServer` 的房间/武器/伤害裁决、`Features/Battle` 的客户端预测与表现。联机客户端不上传位置、命中目标或伤害值。

### 联机测试场与实时日志

联机测试场已接入基础服务端人机：使用 AI Navigation 1.1.7 和 C# 状态机，实现巡逻寻路、视距/视角/墙体感知、三秒目标记忆、搜索、有限转身与反应延迟、点射、换弹、队友挡枪时侧移和三秒重生。导航基于同一份服务端碰撞体在构建时烘焙，运行时不要求地图网格可读。当前巡逻范围为测试出生区域，尚未包含爆破战术、声音/烟雾感知或投掷物。

启动器默认 `BotCount=7`，首名真人进入后填入七个人机，按席位组成 4 CT / 4 T；真人继续进入时替换人机，退出时补位，最后一名真人离开后移除全部人机。该测试模式由服务器分配阵营，使用相同的单步枪规则并关闭友军伤害；HUD 显示阵营、真人和人机人数。用 `GTools/start-local-server.bat -BotCount 0` 可关闭人机，或指定 0–7 的数量。战斗服独立运行时通过 `--bots 7` 开启，缺省为 0，保持原有纯真人验证入口。

本次战斗协议版本为 3，需同时更新客户端和战斗服。已有服务先停止，再运行 `GTools/start-local-server.bat -Rebuild -ArenaOnly`；独立客户端还需运行 `GTools/build-battle.ps1 -Target Client -ArenaOnly`。`-ArenaOnly` 仅生成联机测试场，独立于正在开发的爆破点位验证；完整模式构建仍使用原入口。

`dotnet run --project Tests/Integration/OperationBlacktide.BattleTests -c Release -- . --bots` 验证构建后的无窗口人机对战，省略 `--bots` 保持原有双客户端回归；支持 `--server <exe>` 和 `--account <dll>` 指定隔离产物。

1. 关闭之前启动的旧账号服，双击 `GTools/start-local-server.bat`。它启动账号服 TCP 7777、战斗服 UDP 7778，并打开日志查看器。首次没有战斗服产物时会自动构建；改动服务端代码后用 `start-local-server.bat -Rebuild`。
2. 在 Unity 或客户端登录，大厅选择“联机测试场”再进入 **沙 2（Dust II）**。使用两个不同账号测试；两名玩家出生在附近的同一条通道，左上角显示地图名和房间人数。WASD 移动、鼠标瞄准/左键射击、R 换弹、Shift 冲刺、Space 跳跃、Esc 返回。
3. 日志工具可按服务、级别、玩家名/User ID、操作和内容筛选。暂停只暂停显示；仍会采集。支持自动滚动、查看详细 JSON 和导出筛选结果。
4. 双击 `GTools/stop-local-server.bat` 停止这套启动器管理的服务器；日志保留。单独打开工具用 `GTools/run-log-viewer.bat`。

日志位于 `Logs/Server/*.jsonl`，账号和战斗进程各写各的文件，每 10 MiB 滚动。战斗记录入房/离开、移动状态变化、跳跃请求、开火、换弹、命中、受伤、死亡、重生和非法输入；包含玩家、房间、实体、tick、输入序号。异步日志队列与查看器保留条数有上限；密码、会话令牌和入房票据不写入日志。历史日志不自动删除，可归档后清理。

该版本是 P0–P2 的首个可运行纵向闭环：单房间最多 8 人、沙 2 地图、单步枪、有限弹药、60 Hz 权威模拟、20 Hz 快照、预测/校正、100 ms 远端插值和最多 200 ms 射线回滚。初始测试人数是 2 人，8 人仅为配置上限，尚未完成容量压测。账号服只向已登录连接签发 30 秒有效的一次性入房票据；签名密钥位于 `data/battle.key`，不要拷入 Assets 或客户端包。

联机客户端和战斗服场景由同一份 `DustII.prefab` 生成；构建时比对碰撞体并校验全部出生点。战斗服保留地图碰撞，移除地图渲染组件，移动和命中仍由服务端裁决。启动及入房日志记录 `map=dust2-v1`；地图/协议更新后需同时重建客户端和战斗服，再重启服务器。

目前两种服务都只监听回环地址，供同机开发测试。尚未迁移全部武器、近战、投掷物、完整模式或视野信息裁剪，也未实现公网加密通道及账号封禁/踢出到战斗服的实时撤销。联机测试场会同步房间内所有玩家的位置，因此还不能宣称防透视。本地训练不产生联机结算。

当前环境使用 Mono Windows 无窗口战斗服（`-batchmode -nographics`）；没有安装 Dedicated Server 模块，未宣称已完成该目标的资源裁剪。安装模块后可用 `GTools/build-battle.ps1 -Target Dedicated`。普通客户端构建用 `-Target Client`；构建在 `.build-check` 副本中运行，产物放在 `Builds/`。无窗口版本仍包含 Resources 构建资源，后续需做体积与资源分离。

验证命令：

```powershell
dotnet build GServer/OperationBlacktide.Server -c Release
dotnet run --project Tests/Integration/OperationBlacktide.BattleTests -c Release -- .
GTools/LogViewer/bin/Release/net8.0-windows/OperationBlacktide.LogViewer.exe --self-test
```

集成验证使用随机端口、临时账号/密钥和自建进程，不占用当前开发服务；诊断产物保存在 `.build-check/battle-*`。

后续权威化的边界、服务端路线、协议约束和阶段验收见 [服务端权威战斗迁移方案](server-authority-plan.md)。该文档保留后续迁移规划；当前已实现的联机测试场与爆破能力以上述说明及爆破模式文档为准。

## 账户登录注册

目标：先让 Unity 客户端能连接本机服务端，完成注册、登录、失败提示和基础会话保存。

### 客户端功能
- 登录界面：账号、密码、登录按钮、注册按钮、状态提示。
- 注册流程：输入账号密码后发送注册请求。
- 登录流程：登录成功后保存当前会话信息，加载独立的 LobbyScene 大厅场景。
- 网络层：连接服务端、发送请求、接收响应、处理断线和超时。

### 服务端功能
- 启动 Socket 服务并监听端口。
- 处理注册请求：校验账号格式、检查重复、保存账号。
- 处理登录请求：校验账号密码、创建会话。
- 返回统一响应：成功、失败原因、用户 ID、会话 Token。
- 账号数据先使用本地文件保存，后续再替换为数据库。

### 共享协议
- `RegisterRequest`
- `RegisterResponse`
- `LoginRequest`
- `LoginResponse`
- `ErrorResponse`
- `Heartbeat`

## 服务端命令行调试窗口

服务端启动后保持一个可输入命令的控制台窗口。

启动示例：

```powershell
dotnet run --project GServer/OperationBlacktide.Server -- --port 7777
```

服务端调试命令（以运行时 `help` 为准）：

```text
help              显示命令列表
accounts          查看已注册账号数量
status            查看服务器运行状态
sessions          查看当前登录会话
clients           查看当前连接
kick <userId>     踢出指定用户
save              手动保存账号数据
stop              关闭服务器
```

## 客户端服务器地址配置

打包后的客户端不应要求玩家手动输入 IP。客户端通过 `AppConfig` 保存默认服务器地址：

```text
Assets/Scripts/GClient/Runtime/App/AppConfig.cs
```

当前本机开发默认值：

```text
Host: 127.0.0.1
Port: 7777
```

后续部署到云服务器后，推荐改为域名：

```text
Host: server.example.com
Port: 7777
```

这样服务器换 IP 时只需要修改 DNS 解析，不需要重新打包客户端。

Unity 中可通过菜单创建默认配置资源：

```text
OperationBlacktide/Create Default App Config
```

创建后把 `AppConfig.asset` 拖到 `TcpGameClient` 的 `App Config` 字段即可。若未指定 `AppConfig`，`TcpGameClient` 会继续使用组件自身的 `serverHost/serverPort` 字段。

## 客户端 UI 框架

代码位置：`Assets/Scripts/GClient/Runtime/UI/`，详细设计见 `ui-spec.md`。

### 核心组件

| 组件 | 职责 |
|------|------|
| `UIRoot` | 创建 Canvas、EventSystem、层级容器；默认 `DontDestroyOnLoad` 跨场景保留 |
| `UIManager` | 按 `UILayer` 栈管理面板 Push/Pop/Back；Toast、Modal 遮罩 |
| `UIPanel` | 面板基类，生命周期 `OnOpen` / `Refresh` / `OnClose`，自动释放按钮/事件/定时器 |
| `GameUIEntry` | 每个场景的 UI 入口，清理旧栈并打开该场景的起始面板 |
| `GameSceneFlow` | 应用层场景流程，保留账号和 TCP 对象，处理登录成功、退出和断线跳转 |

### 面板 ID（UIPanelId 枚举）

全项目面板 ID 用枚举维护，字符串 key 与 `UIPath` 路径表一致：

```csharp
// 面板 ID 以 UI/UIPanelId.cs 中的完整枚举为准。

UIManager.Instance.Push(UIPanelId.Login);
UIManager.Instance.ShowToast("注册成功");
UIManager.Instance.PopTo(UIPanelId.Login);
```

`PanelIds.Key(UIPanelId.Login)` → `"Login"`，`PanelIds.All` 列出全部 ID。

### UI 层级

```text
Background  背景
Normal      登录、大厅等主界面
Popup       弹窗（默认 Modal，带全屏遮罩拦截下层输入）
Overlay     Toast 等顶层提示（不参与栈）
```

### 主要能力

- **栈管理**：`Push` / `Pop` / `Back` / `PopTo` / `Close`，每层独立栈。
- **OpenArgs**：`UIPanelOpenArgs` 基类，`LoginOpenArgs` 传默认账号密码。
- **Toast**：`UIManager.ShowToast(message, duration)`，Overlay 层，不参与栈。
- **Modal 遮罩**：Popup 层面板默认 Modal；`UIModalBlocker` 半透明全屏拦截点击。
- **Esc 返回**：`UIRoot` 监听 Escape → 先 Pop Popup，再 Back Normal。
- **跨场景**：`UIRoot` + `EventSystem` 默认 `DontDestroyOnLoad`；新场景重复 UIRoot 自动销毁。

### 账户 UI 脚本

| 脚本 | 说明 |
|------|------|
| `LoginPanel` | 登录/注册表单，监听 `GameEvents` 并显示结果；场景跳转交给 `GameSceneFlow` |
| `LobbyPanel` | 3D 大厅的透明 UI：顶部页签、账号、模式选择、进入游戏和退出登录 |
| `LoginController` | Inspector 调试按钮（可与正式 UI 并存） |

### GameEvents

位置：`Assets/Scripts/GClient/Runtime/Common/`

`AuthClient` 在注册/登录/连接状态变化时发布事件，UI 通过 `AddGameEvent` 订阅：

```text
AccountStatusChanged、RegisterCompleted、LoginCompleted
NetworkConnected、NetworkDisconnected
```

## Unity 场景搭建（登录与大厅）

`Assets/Scenes/SampleScene.unity` 已接入登录 UI。打开场景后点击 Play 即可看到登录页，无需先启动服务端；注册、登录操作需要服务端在线。

登录成功后由 `GameSceneFlow` 异步加载 `Assets/Scenes/LobbyScene.unity`，再打开 `LobbyPanel`。两个场景已加入 Build Settings，登录场景排在第一位。未登录时直接 Play 大厅场景，会自动返回登录场景。

大厅是 3D 场景：中央展示所选角色和主武器，包含平台、地面、环境结构和灯光。拖动中央角色区域可旋转模型。顶部可切换大厅、装备、生涯页签；右下角选择模式，再点击进入游戏。爆破模式已接入 5v5 匹配、自定义房间和可选的三十秒 AI 补齐；联机测试入口连接现有战斗测试场，训练场进入 DustII 本地练习。爆破部署与首版边界见 [spec/demolition-mode.md](demolition-mode.md)。

| 资源 | 位置 |
|------|------|
| 大厅场景 | `Assets/Scenes/LobbyScene.unity` |
| CT 模型、材质、贴图与预制体 | `Assets/Art/Characters/CT_SAS/` |
| CT 展示预制体 | `Assets/Art/Characters/CT_SAS/Prefabs/CT_SAS_Lobby.prefab` |
| 大厅环境材质和后处理 | `Assets/Art/Lobby/` |
| 大厅 UI 预制体 | `Assets/Resources/UI/Panels/LobbyPanel.prefab` |
| 页签、模式和按钮逻辑 | `Assets/Scripts/GClient/Runtime/Features/Lobby/LobbyPanel.cs` |
| 场景角色旋转 | `Assets/Scripts/GClient/Runtime/Features/Lobby/LobbyCharacterPreview.cs` |

角色使用 `ArtSource/CS2Imports/CT_SAS/ctm_sas_m4a1_s.blend` 导出的持枪姿势；完整源模型与动画未改动。大厅资产导出脚本和材质转换说明见 `ArtSource/CS2Imports/CT_SAS/Lobby/README.txt`。

现有预制体位于 `Assets/Resources/UI/Panels/`：`LoginPanel`、`LobbyPanel`、`ToastPanel`。可直接在 Unity 中编辑布局，输入框和按钮已绑定。中文字体位于 `Assets/UI/Fonts/`，许可证随字体保存。

登录页使用橙黑战术背景，位于 `Assets/UI/Art/TacticalMenuBackground.png`，生成提示词保存在 `ArtSource/UI/TacticalMenuBackground.prompt.txt`。大厅 UI 为透明叠加层，直接显示场景相机拍摄的 CT 模型，不使用登录页全屏背景。品牌 LOGO 使用透明 PNG Sprite；输入框和按钮为独立 UGUI 控件；切角外观由 `UIAngledGraphic` 绘制，修改预制体即可调整布局。

大厅的箱子、立柱、线缆、工作灯与地面标记已合入 `Assets/Art/Lobby/Hangar/Textures/HangarBackground_Baked.png`；场景不再放置对应实体道具。CT 仍为实时 3D 模型，`LobbyEnvironment/CharacterShadowGround` 接收角色阴影并与背景使用相同图片。背景制作说明和提示词位于 `ArtSource/Lobby/Hangar/`；重建大厅环境也会使用此方案。

以下说明现有场景的组成，供后续新增场景参考：

### 1. 启动服务端

```bat
GTools\run-server.bat
```

### 2. 场景对象

**Game**（应用服务对象，由 `GameSceneFlow` 跨场景保留）：

- `TcpGameClient`（绑定 `AppConfig.asset`）
- `AuthClient`
- `LoginController`（Inspector 调试）
- `GameSceneFlow`（登录成功进入大厅，退出/断线返回登录；自动销毁重复 Game）

**UIRoot**：

- `UIRoot`（默认 DontDestroyOnLoad）
- `UIManager`（通过 `UIPath` 加载预制体）

**SceneUI**（属于各自场景，不跨场景保留）：

- `GameUIEntry`：SampleScene 的 `Start Panel = Login`；LobbyScene 的 `Start Panel = Lobby`。
- 场景启动时清理 Popup 和 Normal 栈，再打开该场景主面板，因此大厅按 Esc 不会返回旧的登录页。

两个场景都包含上述对象，以支持编辑器直接 Play；通过正常场景切换进入时，重复的 Game 和 UIRoot 会自动销毁，账号、TCP 连接及 UI 管理器保持原实例。

### 3. 制作并注册预制体

`UIPath.cs` 中已登记以下预制体路径，`UIManager` 无需在 Inspector 中配置注册表：

| Panel Id | 脚本 | Layer |
|----------|------|-------|
| `Login` | `LoginPanel` | Normal |
| `Lobby` | `LobbyPanel` | Normal |
| `Toast` | `ToastPanel` | Overlay |

Panel Id 必须与枚举名一致。预制体中的 InputField、Button、Text 已绑定；`LoginPanel` / `LobbyPanel` 在打开时优先使用 `GameSceneFlow` 保留的 `AuthClient`。

### 4. Play 验证

1. 自动打开登录面板
2. 注册 → Toast + 状态提示
3. 登录成功 → 场景切换为 LobbyScene，显示大厅；会话与 TCP 连接保留
4. 大厅按 Esc 打开大厅菜单；退出登录 → 断开连接、清除会话并回到 SampleScene
5. 再次登录可重新进入大厅；大厅断线或被服务端踢出后返回登录场景
6. 服务端 `accounts` / `sessions` 核对状态

### 记住登录信息

Windows 编辑器和 Windows 客户端会在登录成功后记住当前服务器的最后一个账号和密码，下次打开登录页或退出登录后自动回填，仍需点击登录。登录失败、仅注册或请求发出后修改输入框，都不会覆盖已保存的成功登录信息；显式传入 `LoginOpenArgs` 时优先使用指定值。

`Features/Account/LoginCredentialStore.cs` 使用 Windows 凭据管理器的 Generic credential，按服务器地址和端口区分，记录名以 `OperationBlacktide/Login/v1/` 开头。记录仅为当前 Windows 用户在本机保存，密码不会写入 PlayerPrefs、场景或项目配置。可在 Windows 凭据管理器中删除对应记录；其他平台尚未接入安全存储，不会退回明文密码保存。

### 云服务器部署方向
- 购买一台云服务器，例如阿里云 ECS 或腾讯云 CVM。
- 在云服务器上运行 `GServer` 服务端。
- 云服务器安全组和系统防火墙放行 TCP `7777`。
- 域名解析到云服务器公网 IP。
- 客户端 `AppConfig` 使用域名连接服务器。

## 大厅角色与装备
- 大厅左侧选择 CT / T 与角色（各 5 名）；装备页按手枪、步枪、微冲、重型、装备分类，支持模型预览、确认装备、双阵营装备和设置大厅展示武器。
- 采用经典 CS:GO 替换槽位：P2000 / USP-S、Five-SeveN / CZ75-Auto、Tec-9 / CZ75-Auto、Desert Eagle / R8、M4A4 / M4A1-S、MP7 / MP5-SD。其余武器保持各阵营的固定槽位；C4 为 T 方任务装备。
- 10 个角色共用标准化骨架和 26 组 idle / 检视动作；双持贝瑞塔分别挂在左右手，防弹衣、头盔和拆弹器使用独立模型预览。可拖动中央模型旋转。
- 配置由 `LobbyLoadoutStore` 按账号 ID 存入本机 PlayerPrefs（`OperationBlacktide.Loadout.v1.{userId}`），CT / T 分别保存；首次读取会兼容旧存档键和 Windows 上旧产品 Test 的存档位置；仅预览不修改装备。无效或旧版本配置会回退到有效默认值。
- 大厅装备存档通过 `Snapshot(EquipmentTeam)` 提供独立配置副本，训练与战斗表现共用 `EquipmentCatalog`。服务器仍需按玩法规则校验装备，不能直接信任客户端存档。
- Unity 目录：`Assets/Art/Loadout`、`Assets/Resources/Loadout/EquipmentCatalog.asset`、`Assets/Scripts/GClient/Runtime/Features/Lobby`。Blender 导出脚本为 `ArtSource/CS2Imports/Scripts/export_lobby_loadout.py`；导出完成后可运行菜单 `OperationBlacktide > Lobby > Build Character and Equipment Selection` 重建资源、预制体和界面。

## 本地训练实现

训练场相机平滑跟随角色的位置和水平朝向；W/S 沿角色朝向前进、后退，A/D 左右横移。
按住鼠标中键拖动可调整镜头相对角色的水平偏角和俯仰，松开保留；滚轮调整距离。
遮挡角色的建筑、高架地面和上层平台整体半透明，镜头保持缩放距离；脚下未遮挡角色的地面保持不透明。鼠标移动调整瞄准，镜头跟转不会自行改变角色的瞄准方向。
水平转向使用角色所在高度的参考平面，枪口瞄准与射击使用实际三维命中点；准星扫过墙面或高处物体不会改变镜头角度。
持枪按住右键（包括狙击开镜）时，镜头暂时保持水平角度，准星进入屏幕外侧 10% 后持续向对应方向平移，越靠边越快，最多每秒 14 米，不受角色跟随半径限制。
准星移回中央区域后视角停在当前观察位置；松开右键、退出开镜或输入中断后平滑回到角色视角，恢复水平朝向跟随。中键拖动期间暂停边缘平移。
按 **P** 在俯视与第一人称之间切换。第一人称锁定鼠标、准星居中，鼠标水平转动角色和视线，垂直只调整视线；WASD 仍相对角色水平朝向移动。
第一人称关闭俯视遮挡淡化、视野遮罩和激光，右键狙击开镜使用中心放大；菜单、失焦时释放鼠标，返回俯视时恢复原有中键角度和滚轮距离。
手模使用本地 CS 的 SAS 袖子和手套，持枪、拔枪、开火、换弹、刀与投掷物动作来自 `animation/anims/viewmodel`，手臂和枪械骨骼烘焙在同一时间轴；M4A1-S/USP-S 使用各自动画图引用的 `_default_rifle`/`_default_pistol`。
`ArtSource/CS2Imports/Scripts/export_first_person.py` 导出模型和动作（Blender 后台执行，参数分隔符 `--`，可加 `--only weapon_rif_ak47`）；Unity 菜单 `OperationBlacktide/Camera/Build First Person Assets` 生成 `Gameplay/FirstPerson` 资源目录。
第一人称专用相机使用第 31 层，仅绘制手模，避免贴墙时枪身被场景截断。

训练场按住鼠标左键连射，松开停止；弹匣耗尽后停火，按 R 换弹，训练场保留无限备弹。
AK-47 / M4A4 弹匣为 30 发，M4A1-S 为 20 发；换弹时不能射击，弹窗、失焦和暂停时换弹进度停止。
准星通过相机射线拾取场景表面的实际三维瞄准点；子弹从枪口朝该点发射，可向上或向下射击。
上半身在移动和武器动作采样后，通过四节脊柱分担瞄准俯仰与转向，双手与枪随肩膀一起调整；腿部保留移动/跳跃动画。
射击和激光读取调整后的枪口；换弹时使用持枪参考姿势驱动躯干，保留原有手部动作。
枪口到瞄准点之间有障碍物时，命中沿途最近的实体，不会越过掩体直接命中准星处。
准星未指向实体时，使用相机射线远端作为瞄准点；实际弹道仍受武器射程限制。
支持大厅的 AK-47、M4A4、M4A1-S，包含枪口火光、弹道、命中火花、抛壳和对应枪声。
UI 按钮、返回确认框、失焦与暂停会阻止开火，关闭弹窗后需要释放再按下左键。
枪管穿入掩体时先检测角色到枪口的遮挡；32 槽特效池随场景释放。

命中实体后留下沿表面法线对齐的弹孔，复用本机 CS2 的两种水泥弹孔颜色/透明度和 AO 贴图。
弹孔保留 45 秒，最后 3 秒淡出，最多 128 个循环复用；重复打同一点会刷新旧弹孔，
墙边放不下时缩小或跳过，命中物体移动时跟随，物体销毁/禁用和退出场景时清理。
当前统一使用水泥弹孔外观。新贴图原始导出保存在 `ArtSource/CS2Imports/CombatFX/BulletDecals`。
为方便俯视观察，曳光宽度由 0.024 米调至 0.12 米、长度由 1.5 米调至 3.5 米，
亮度 3.5 倍，视觉速度 150 米/秒，末端淡出 0.12 秒；枪口火光和命中火花同步增强。
命中仍为即时射线，视觉参数可在 `WeaponEffects` 的 `Shot visibility` 中调整，瞄准激光参数独立。

开火入口为 `WeaponController.ProcessTrigger` / `FireBullet`，射速状态机为 `FireControl.Tick`。
`ShotFired` 事件提供命中、位置和距离衰减后的基础伤害，`TargetHit` 提供实际扣除的伤害。尚未接入穿透、完整 CS2 后坐力/散布或网络回滚。
`CharacterVitals` 提供本地生命值、护甲和伤害接口；射击、近战、爆炸和燃烧通过 `Damageable` 结算伤害，训练场负责玩家和靶子的复生。

枪弹命中已接入按部位和材质选音：裸头、身体、护甲和头盔使用独立音效组；训练靶的头部和身体碰撞体通过
`HitRegion` 显式标记，`Shot.impactKind` 在扣血前记录，致死或打空护甲的这一枪仍保留正确声音。
本次仅增加声音反馈，不改变原有伤害倍率；普通 `Damageable` 默认无护甲，`CharacterVitals` 根据剩余护甲和
`wearsHelmet` 区分防弹衣与头盔，四肢音色按肉体处理。

环境支持混凝土、金属、金属网/桶、木材/木板、玻璃、瓷砖、泥土、沙地、草地、塑料、纸板、橡胶、布和水。
Dust II 的 713 个导入材质条目对应 275 个原始 VMAT，映射取自本机 CS2 的 `PhysicsSurfaceProperties`，
保存在 `Assets/Resources/Combat/ImpactAudio/surfaces.json`；按射线实际命中的三角形选择子材质，相机淡化后的材质也保留分类。
新增或只有碰撞体的道具可挂 `ImpactSurface` 手工选材质；未配置/未知材质使用默认命中声。
命中角色不再放置混凝土弹孔。环境音在命中点空间播放，头部和身体音作为本地射手反馈；同类连续命中随机换样本，
霰弹同一目标/材质去重，一发最多 4 声并为身体/头部预留名额，24 个声源循环复用。
菜单、暂停、失焦、角色禁用/重生和场景退出会停止或清理命中声。枪弹与脚步共用材质映射，近战仍使用原有声音逻辑。

92 条 WAV（21 组、约 6.8 MB）复用已经校验过的本机 CS2 `CombatAudio` 导出；不是旧版 CS:GO 专用资源包。
素材事件、原始路径和 SHA-256 见 `Assets/Art/Combat/impact_audio_sources.json`，
运行 `ArtSource/CS2Imports/Scripts/prepare_impact_audio.py --materials` 可从本地源重建音效与地图材质映射。

武器参数来源于本机 CS2 `scripts/weapons.vdata_c` 的导出数据，原文保存在
`ArtSource/CS2Imports/CombatFX/Metadata/Weapons/scripts/weapons.vdata`，精简配置在 `Assets/Art/Combat/weapon_data.json`。
AK / M4A1-S 间隔 0.10 秒，M4A4 为 0.09 秒；距离按已有导出比例 0.0254 转换为米。
特效复用 CombatFX 导出的贴图与 AK 弹壳网格，三把步枪暂共用该弹壳外观；粒子行为由 Unity 重建。
这些是 Unity 实现和导出参数，不是 CS2 原生开火函数或完整的 CS2 弹道复刻。

资源已保存在 `Assets/Resources/Gameplay/Weapons.asset`；菜单
`OperationBlacktide > Training > Build Weapon Effects` 可根据导入素材重新生成材质、枪口位置和资源配置。

训练 HUD 采用 CS 风格的边缘布局：左下生命值/血条与护甲，右侧当前主武器剪影，右下弹匣/备弹和换弹进度，
顶部训练计时与菜单，中间区域保留给战斗。准星跟随鼠标，菜单打开时显示普通光标；低血量、低弹药与空弹匣有颜色和文字提示。
武器名称、剪影和弹匣数据跟随大厅实际装备；护甲初始为 0，不显示虚构的资金、比分或未持有武器。
HUD 使用屏幕安全区与 CanvasScaler，非交互图形不阻挡瞄准；面板退出后释放订阅，再次进入时重新绑定角色。
Esc 或右上按钮打开居中的训练菜单，包含“继续训练、切换武器、返回大厅”；切换武器页提供四类装备与当前选择高亮。
数字键 1（主武器，含狙击）、2（副武器）、3（近战）、4（投掷）支持主键盘和小键盘。
切入其他类别时恢复该类上次选择，重复同一数字键循环下一件，到末尾回到第一件；不修改大厅配装。
切枪保留各枪弹匣，取消未完成换弹与开镜。菜单内可点击装备，游戏快捷键在菜单打开时屏蔽。
目录内 35 把枪械（含电击枪）均接入训练攻击配置；构建时检查装备栏，缺少任何枪械配置会直接报错。
本次补齐 AUG、FAMAS、Galil AR、SG 553、7 把微冲、4 把霰弹枪、M249 和 Negev，使用各自的持枪、开火、换弹动作和声音。
霰弹枪每发消耗一颗霰弹，按独立弹丸散射、命中和衰减计算；一次开火只触发一次枪声、枪焰、抛壳及动作。
训练参数由项目自行配置；目前霰弹枪换弹仍使用完整换弹进度补满弹仓，不支持逐发装填打断。
菜单 `OperationBlacktide > Training > Build HUD` 重建 HUD、剪影与训练菜单，不修改当前场景；`Build Training Menu` 可单独重建菜单。

开枪和换弹使用 AK-47 / M4A4 / M4A1-S 各自导出的第三人称动作，共 6 个片段。
`WeaponPresentation` 在移动动画上叠加上半身动作：每次实际 `AttackStarted` 触发一次攻击动作，
换弹动作按 `AmmoState.ReloadProgress` 采样，与 HUD 进度和补满弹匣使用同一个时间源。
下半身保持行走、疾跑和跳跃；菜单、失焦与暂停时动作/换弹声音暂停，重生和退出场景时清理。
换弹分别播放拔弹匣、插弹匣、拉枪栓/枪机复位音效，9 个 WAV 从现有 CombatAudio 导出素材原样复制。
原第三人称片段没有导出声音事件时间，触发点按动作阶段配置在 `BuildTrainingWeaponActions.Bind`，不是 CS2 原始事件时间。
动画源为 `ArtSource/CS2Imports/Batch/RawAnimations`，通过 `Scripts/export_training_weapon_actions.py` 烘焙到共享角色骨架；
菜单 `OperationBlacktide > Training > Build Weapon Actions` 重建片段导入设置、上半身遮罩、动画层和武器资源引用。
`Scripts/prepare_training_primaries.py` 从本地导出素材补齐 17 把主武器参数与 113 个音效引用，
`Scripts/export_training_primaries.py` 用 Blender 烘焙 52 个片段到 `PrimaryWeaponActions.fbx`（含共用参考姿势）。
`Scripts/export_training_primary_grips.py` 从第三人称持枪姿势生成 17 个战斗挂点，避免大厅的展示挂点导致左手悬空；不改大厅预览。

训练场脚步由 `FootstepAudio` 根据实际地面位移触发，疾跑加快节奏并提高音量，
侧移和后退同样生效；静止、顶墙、腾空、弹窗、失焦和暂停时不产生新脚步，落地单独播放一次。
复生会重置步伐与落地状态，离开场景销毁独立脚步声源，不影响武器声源。
每次脚步和落地接触都探测角色脚下的真实支撑面：短射线保留三角形索引，复用 `ImpactSurfaceResolver` 的
原始地图材质映射，支持混凝土、木材/木板、金属、金属网、金属桶、玻璃、瓷砖、泥土、沙地、草地、塑料、纸板、橡胶、布和水。
连续行走跨过材质边界时，下一次脚步立即换音色；角色中心悬在箱沿时会补查胶囊底部四个方向，排除自身和普通触发器。
沙二运输木箱的 `Wood_Panel`、水果木箱的 `Wood` 已映射到木头脚步，箱顶布料按实际命中表面使用布类声音。

浅水可使用标记为 `ImpactSurface.Kind = Water` 的 BoxCollider 触发体覆盖水域，并保留下方实体地面。
只有脚下支撑面上方 4 厘米处位于水体内时才使用踩水/落水声音；站在水面上方的箱子或桥上仍使用其自身材质，
静止及腾空不会因为进入水域而产生脚步。实体碰撞面的 Water 标签也支持；未配置/未知地面回退到混凝土。

15 组材质共用 222 条本机 CS2 原始采样，包含各材质落地事件：保留已有 21 条混凝土/默认落地资源，新增 201 条 WAV，
单个素材只保存一次，资源配置见 `Assets/Resources/Combat/FootstepAudio/banks.json`。
每组脚步和落地分别避免连续重复样本，木材与木板共享随机记录；运行时不在每一步重新加载音频。
来源事件、原始资源和 SHA-256 见 `Assets/Art/Combat/footstep_audio_sources.json`；
`ArtSource/CS2Imports/Scripts/prepare_footstep_audio.py` 可从现有校验过的 CombatAudio 导出重建素材。

### 训练投掷物

训练投掷物已接入 4 号装备栏：手雷、闪光弹、烟雾弹、燃烧弹和燃烧瓶。重复按 4 循环切换；
按住左键查看弹道，松开投掷，右键取消。完成投掷收势和再次取出后自动补充；切枪、菜单、失焦会取消尚未出手的投掷。
手雷有碰撞反弹、引信、距离衰减和墙体遮挡伤害；燃烧持续 7 秒，每秒 32 点伤害，按地面与墙体探测铺开；
烟雾持续 18 秒，遮挡角色视野、目标提示与激光，进入烟雾有灰雾遮罩，并可扑灭火焰；
闪光按角色朝向、距离、墙体与烟雾计算白屏和耳鸣，近距离正面最长 4 秒。
范围伤害也作用于玩家，命中同一目标多个碰撞体不会重复结算；训练菜单和失焦时投掷物模拟与声音暂停。

模型复用已有 Loadout 资源；粒子由现有 CS 导出序列帧重建，19 条音效取自 CombatAudio，来源与哈希保存在
`Assets/Art/Throwables/sources.json`。`ArtSource/CS2Imports/Scripts/prepare_training_throwables.py` 可重新整理贴图图集和音效，
Unity 菜单 `OperationBlacktide > Training > Build Throwables` 重建资源配置和材质。
本阶段是本地训练玩法；烟雾采用粒子和遮挡区域，尚非 CS2 体积流体模拟，也未接入服务器权威同步。

投掷动作使用 CS 的 7 个第三人称片段：普通投掷物与燃烧瓶各自的取出、持握、拔销／点燃，以及共用的挥臂投掷。
`export_training_throwable_actions.py` 将动作烘焙到共享骨架，`Build Throwable Actions` 提取上半身旋转曲线；
`ThrowablePresentation` 在移动姿态之后采样这些曲线，保留下半身移动、跳跃和不同角色的骨骼长度。
按住时停在准备动作末帧，快速点按也会先播完准备；弹体、手持模型隐藏和投掷声音在配置的出手帧同步触发。
出手点取动画中的手部装备中心，并保留近墙碰撞检查；预览弹道也使用该出手姿势。出手时点为本项目按片段设定，非原 CS 音效事件导出。

### 角色视野

参考 `2026-10-05-23-51-16.mp4` 中的朝向视野与遮挡表现，训练场增加角色视野：默认前方 110°、28 米，
身边 2.4 米保留周边感知，狙击开镜时随开镜过渡延伸至 55 米；这些是本项目可调参数，不是视频原作的精确数值。
实体墙同时阻挡前方视野与周边感知，矮掩体上露出的目标仍可见，烟雾和闪光接入同一套目标可见性判断。
视野外环境压暗、保留地图轮廓；不可见的训练靶、目标高亮、血条和伤害数字隐藏。碰撞和伤害结算继续保留。
建筑因挡住相机而半透明时，仍阻挡角色视线；相机遮挡处理不会提供穿墙视野。

`CharacterVision` 在训练相机上运行，可调整角度、距离、周边感知半径、暗度和软边。
环境明暗使用眼部高度的 720 条水平射线与场景深度重建，每 0.05 秒更新遮挡；目标显示使用实时三维视线，检查头、胸、腿。
这是一套本地训练视野表现，环境遮罩不是任意高度的完整三维可见体积，也尚未用于服务器信息裁剪。
URP 的三个画质档均安装 `CharacterVisionRendererFeature`，只作用于绑定角色的训练相机，HUD 与大厅不受影响。
菜单 `OperationBlacktide > Training > Build Vision` 可重建渲染接入。

### 连射后坐力开发记录

以下为工作区正在集成的改动说明，相关代码不包含在本次文档提交中；主分支合入并验证后再将其列为已发布能力。

两种视角共用 `WeaponRecoil`：真实开火后逐发上抬，连射后段保留确定性的上下/左右弹道偏移，不会在上限处锁成一点。
第一人称通过视线后坐力影响下一发弹道，第三人称将同一偏移应用到枪口瞄准，移动/鼠标控制基准保持独立；停火后恢复，切枪、重生和视角切换清零。
