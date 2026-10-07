# 黑潮行动 / OPERATION BLACKTIDE

俯视角轻量版 CS/CF 爆破联机射击项目。

## 协作开发：克隆和打开项目

### 环境与首次启动

- 安装 Git 和 [Git LFS](https://git-lfs.com/)。本项目的模型、贴图、音频和字体通过 LFS 保存，打开 Unity 前必须下载完整资源。
- 用 Unity Hub 安装 **Unity `2022.3.47f1c1`**，以 `ProjectSettings/ProjectVersion.txt` 为准。这是带 `c1` 后缀的编辑器版本，协作者应使用相同版本。
- 需要本机注册、登录时，安装 **.NET 8 SDK** 并启动服务端；只打开工程、查看场景不需要服务端。

```powershell
# 首次使用 Git LFS 时安装本机过滤器和钩子。
git lfs install
# 克隆工程；之后的命令均在项目根目录运行。
git clone https://github.com/guzeming/CrossFire2048.git
cd CrossFire2048
# 下载模型、贴图和音频实体，并检查资源完整性。
git lfs pull
git lfs fsck
```

在 Unity Hub 中添加克隆后的项目根目录（包含 `Assets`、`Packages`、`ProjectSettings`），然后使用上述编辑器打开。第一次打开会重新生成 `Library`，并联网恢复包依赖；当前锁定文件使用 `https://packages.unity.cn`。不要把自己的 `Library` 复制给协作者，也不要删除 `packages-lock.json`。

打开 `Assets/Scenes/SampleScene.unity`，点击 Play 进入登录页。注册、登录前，在项目根目录运行：

```powershell
dotnet run --project GServer/OperationBlacktide.Server/OperationBlacktide.Server.csproj -- --port 7777
```

Windows 也可运行 `GTools/run-server.bat`。默认连接 `127.0.0.1:7777`；每位协作者可启动自己的本机服务端。登录后进入 `LobbyScene`，选择训练场进入 `DustII`；直接打开 `Assets/Scenes/DustII.unity` 可用默认角色本地检查训练场。

### 已有协作者更新项目

先保存本地改动并关闭 Unity，再更新 `main`：

```powershell
git switch main
# 仅接受快进更新；存在本地未同步提交时，先处理分支差异。
git pull --ff-only origin main
git lfs pull
git lfs fsck
```

更新完成后再用 Unity Hub 打开工程。仓库目录名仍为 `CrossFire2048`，产品名和服务端工程已改为 `OperationBlacktide`；请使用 `GServer/OperationBlacktide.Server/OperationBlacktide.Server.csproj`，不要再引用旧服务端目录。

### 哪些文件需要提交

| 路径 | 提交规则 |
| --- | --- |
| `Assets/` | 完整提交资源、脚本及所有对应的 `.meta`，包括目录的 `.meta`；二进制美术资源使用 LFS |
| `Packages/` | 提交 `manifest.json` 和 `packages-lock.json`，以及以后新增的内嵌包源码 |
| `ProjectSettings/` | 完整提交，保证编辑器版本、渲染设置、输入设置和构建场景一致 |
| `GServer/` | 提交服务端源码和手写 `.csproj`，排除 `bin/`、`obj/` 和本机账号数据 |
| `GTools/` | 提交构建、导入和验证脚本；其中 `*Validation/` 是验证源码，需要保留 |
| `ArtSource/` | 默认只提交制作脚本、说明和生成提示词；原始模型、贴图、音效和导出缓存另行共享，见 [ArtSource/README.md](ArtSource/README.md) |
| `README.md`、`spec/`、`ReadMeRes/`、`.gitignore`、`.gitattributes` | 提交协作说明、文档及仓库规则 |
| `Library/`、`Temp/`、`Logs/`、`UserSettings/`、`.build-check/`、`.idea/`、`.vs/` | 本机缓存、验证输出或个人配置，不提交 |
| 根目录自动生成的 `.sln`、`.csproj`，以及 `bin/`、`obj/`、构建产物、日志 | 不提交，由 Unity/构建工具重新生成 |
| `.git/` | Git 自行管理；通过 clone/push 同步历史，无需手动上传这个目录 |

移动或重命名 Unity 资源时，要连同原来的 `.meta` 一起操作，避免场景、材质和预制体引用丢失。项目已启用 `Visible Meta Files` 和 `Force Text`。不要给 `.meta`、场景或预制体设置 LFS。

### 提交前检查

先保存 Unity 工程，再在项目根目录执行：

```powershell
git lfs install --local
git status --short
git add --dry-run .
# 确认清单后，将新增文件、修改和删除一起暂存。
git add -A
git diff --cached --stat
git lfs status
git lfs fsck
git commit -m "更新项目资源与协作配置"
git push origin main
```

`.gitattributes` 会让匹配规则的资源在 `git add` 时自动转成 LFS 指针；`git push` 的 LFS 钩子先上传资源实体，再推送提交。若上传失败，修复原因后重新执行 `git push`，不要跳过 LFS 钩子。团队后续功能开发建议使用分支和合并请求。

当前项目中的已导入资源约 2.85 GiB，首次分享需要上传对应 LFS 对象，协作者首次克隆也需要下载它们。先确认远程仓库的 LFS 存储和下载额度足够。若出现 LFS 下载错误，应先修复下载，再打开 Unity；只拿到代码或 LFS 指针文件不能完整恢复模型和贴图。GitHub 普通 Git 单文件上限为 100 MiB，详见 [GitHub 大文件说明](https://docs.github.com/en/repositories/working-with-files/managing-large-files/about-large-files-on-github)。

原始美术制作工具不是打开项目的前置条件。重建导入资源时才需要额外的原始素材、Blender、Python 和导出工具；相关脚本中的本机工具路径需按各自环境调整。Unity 验证脚本可通过 `-UnityPath` 指定编辑器位置，输出统一留在被忽略的 `.build-check/`。

## 项目概况

品牌资源位于 `Assets/UI/Art/Branding/`：主 LOGO `BlacktideLogo.png`（登录页）、横版 `BlacktideHorizontal.png`（大厅顶部）、徽章 `BlacktideEmblem.png`（默认应用图标）。原设计板与生成提示词保存在 `ArtSource/UI/Blacktide/`。大厅背景中的旧标识已同步替换。Unity 产品名为 `Operation Blacktide`，包标识为 `com.operationblacktide.game`；代码、编辑器菜单和服务端工程统一使用 `OperationBlacktide`。工作区根目录保持现有路径。

品牌回归验证入口：`GTools/verify-branding.ps1`，在独立副本中检查资源引用、登录/大厅界面、旧装备存档迁移，并输出 16:9 和 16:10 截图。

当前工程已包含账户注册登录、独立 Socket 服务端、登录/大厅 UI，以及 DustII 本地训练场。训练场已有角色运动、装备展示、武器射击/换弹、投掷物和相关表现。后续继续推进联机房间、匹配、权威同步、客户端预测、快照同步和回滚命中判定。

## 核心方向

### 客户端
- 引擎：Unity `2022.3.47f1c1` + URP `14.0.11`。
- 语言：C#。
- 重点：登录注册界面、服务器连接、输入采集、表现层预测、快照校正。
- 原则：先保持目录简单，不提前拆过多模块和框架。

### 服务端
- 语言：C#。
- 运行方式：独立命令行控制台程序。
- 网络：Socket 通信，后续区分登录/大厅可靠消息和战斗内高频同步消息。
- 权威模型：服务端保存账户、会话、房间、玩家状态和命中判定结果。
- 调试方式：服务端控制台支持输入命令，方便在不打开 Unity 的情况下观察和控制服务器。

### 网络同步目标
- 服务器权威。
- 客户端预测。
- 服务器快照同步。
- 客户端校正。
- 基于时间戳的回滚命中判定。

## 精简目录规划

```text
项目根目录/
  Assets/
    Scripts/
      GClient/
        Runtime/
          App/              AppConfig、GameSceneFlow 场景流程
          Common/           GameEvents 全局事件
          Features/
            Account/        AuthClient、LoginPanel、LobbyPanel
          Network/          TcpGameClient、消息编解码
          UI/               UIRoot、UIManager、面板基类与规范
        Editor/
      GShare/
        Runtime/
          Protocol/
          Models/
          Netcode/

  GServer/
    OperationBlacktide.Server/
      Program.cs
      ServerConsole.cs
      GameServer.cs
      Accounts/
      Network/
      DebugCommands/
      Storage/

  GTools/
    build-server.bat
    run-server.bat

  README.md
```

### 目录原则
- `Assets/Scripts/GClient` 放 Unity 客户端代码。
- `Assets/Scripts/GShare` 放客户端和服务端共用的协议、消息结构和基础模型源码。
- `GServer` 放独立 C# Socket 服务端，可以直接从命令行启动。
- `GServer` 通过 `.csproj` 直接引用 `Assets/Scripts/GShare/Runtime/**/*.cs`，保证客户端和服务端使用同一份共享源码。
- `GTools` 放构建、启动、调试脚本。
- UI 使用自研轻量框架（`UIRoot` + `UIManager` + `UIPanel`），详见 `Assets/Scripts/GClient/Runtime/UI/ui-spec.md`。
- 暂不引入 Addressables、多 asmdef 拆分和大型资源规范。

## 第一阶段：账户登录注册

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

计划支持的调试命令：

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
public enum UIPanelId { None, Login, Lobby, Toast }

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

大厅是 3D 场景：中央展示所选角色和主武器，包含平台、地面、环境结构和灯光。拖动中央角色区域可旋转模型。顶部可切换大厅、装备、生涯页签；右下角选择爆破模式、团队竞技或训练场，再点击进入游戏。训练场已接入 DustII 本地练习；爆破和团队竞技的房间、匹配及联机对战业务尚未接入，进入按钮会提示当前模式暂未开放。

| 资源 | 位置 |
|------|------|
| 大厅场景 | `Assets/Scenes/LobbyScene.unity` |
| CT 模型、材质、贴图与预制体 | `Assets/Art/Characters/CT_SAS/` |
| CT 展示预制体 | `Assets/Art/Characters/CT_SAS/Prefabs/CT_SAS_Lobby.prefab` |
| 大厅环境材质和后处理 | `Assets/Art/Lobby/` |
| 大厅 UI 预制体 | `Assets/Resources/UI/Panels/LobbyPanel.prefab` |
| 页签、模式和按钮逻辑 | `Assets/Scripts/GClient/Runtime/Features/Account/LobbyPanel.cs` |
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
4. 大厅按 Esc 保持在大厅；退出登录 → 断开连接、清除会话并回到 SampleScene
5. 再次登录可重新进入大厅；大厅断线或被服务端踢出后返回登录场景
6. 服务端 `accounts` / `sessions` 核对状态

### 记住登录信息

Windows 编辑器和 Windows 客户端会在登录成功后记住当前服务器的最后一个账号和密码，下次打开登录页或退出登录后自动回填，仍需点击登录。登录失败、仅注册或请求发出后修改输入框，都不会覆盖已保存的成功登录信息；显式传入 `LoginOpenArgs` 时优先使用指定值。

`Features/Account/LoginCredentialStore.cs` 使用 Windows 凭据管理器的 Generic credential，按服务器地址和端口区分，记录名以 `OperationBlacktide/Login/v1/` 开头。记录仅为当前 Windows 用户在本机保存，密码不会写入 PlayerPrefs、场景或项目配置。可在 Windows 凭据管理器中删除对应记录；其他平台尚未接入安全存储，不会退回明文密码保存。

独立验证：先运行 `GTools/build-server.bat`，再运行 `python GTools/verify-login-memory.py`（可用 `--unity` 指定 Unity.exe）。验证使用临时账号和端口，覆盖成功/失败登录、退出回填、重复点击、换账号、密码掩码，以及重启 Unity 进程后的回填，并清理测试凭据。

### 云服务器部署方向
- 购买一台云服务器，例如阿里云 ECS 或腾讯云 CVM。
- 在云服务器上运行 `GServer` 服务端。
- 云服务器安全组和系统防火墙放行 TCP `7777`。
- 域名解析到云服务器公网 IP。
- 客户端 `AppConfig` 使用域名连接服务器。

## 当前实现状态

### 已验证链路
- `GServer` 服务端可以启动并监听 TCP `7777`。
- Unity 客户端可以通过 `TcpGameClient` 连接服务端。
- Unity 客户端可以发送注册请求并收到注册结果。
- Unity 客户端可以发送登录请求并收到登录结果。
- 服务端命令行可以通过 `status`、`accounts`、`clients`、`sessions` 查看运行状态。

### 客户端网络模块
- 代码位置：`Assets/Scripts/GClient/Runtime/Network`
- 当前用途：登录注册等低频可靠消息。
- 当前传输：TCP，一行一个 JSON 消息。
- 详细说明：`Assets/Scripts/GClient/Runtime/Network/network-spec.md`

### 客户端 UI 模块
- 代码位置：`Assets/Scripts/GClient/Runtime/UI`
- 已实现：`UIRoot`、`UIManager`（栈管理）、`UIPanel`、`UIPanelId`/`PanelIds`、`UIPath`、`ToastPanel`、`UIModalBlocker`、`GameUIEntry`、Esc 返回、DontDestroyOnLoad；登录与大厅预制体分别由 SampleScene 和 LobbyScene 的入口打开。
- 详细说明：`Assets/Scripts/GClient/Runtime/UI/ui-spec.md`

### 账号模块
- 代码位置：`Assets/Scripts/GClient/Runtime/Features/Account`
- 当前能力：注册、登录、保存本地会话、`GameEvents` 广播、`LoginPanel` / `LobbyPanel` 脚本、Inspector 调试（`LoginController`）。
- 正式 UI：打开 SampleScene 后 Play 自动显示 Login；注册/登录结果显示状态文字和 Toast，登录成功加载独立 LobbyScene，退出登录或断线返回 SampleScene。
- 调试方式：Play Mode 下用 `LoginPanel` 表单，或 Inspector 中 `LoginController` 的 Register / Login 按钮。

## 后续阶段

### 大厅角色与装备
- 大厅左侧选择 CT / T 与角色（各 5 名）；装备页按手枪、步枪、微冲、重型、装备分类，支持模型预览、确认装备、双阵营装备和设置大厅展示武器。
- 采用经典 CS:GO 替换槽位：P2000 / USP-S、Five-SeveN / CZ75-Auto、Tec-9 / CZ75-Auto、Desert Eagle / R8、M4A4 / M4A1-S、MP7 / MP5-SD。其余武器保持各阵营的固定槽位；C4 为 T 方任务装备。
- 10 个角色共用标准化骨架和 26 组 idle / 检视动作；双持贝瑞塔分别挂在左右手，防弹衣、头盔和拆弹器使用独立模型预览。可拖动中央模型旋转。
- 配置由 `LobbyLoadoutStore` 按账号 ID 存入本机 PlayerPrefs（`OperationBlacktide.Loadout.v1.{userId}`），CT / T 分别保存；首次读取会兼容旧存档键和 Windows 上旧产品 Test 的存档位置；仅预览不修改装备。无效或旧版本配置会回退到有效默认值。
- 当前实现大厅配置和展示；房间、购买、战斗仍待接入。业务层可通过 `Snapshot(LobbyTeam)` 获取独立配置副本，未来由服务器校验装备，不应直接信任客户端存档。
- Unity 目录：`Assets/Art/Loadout`、`Assets/Resources/Loadout/LobbyCatalog.asset`、`Assets/Scripts/GClient/Runtime/Features/Lobby`。Blender 导出脚本为 `ArtSource/CS2Imports/Scripts/export_lobby_loadout.py`；导出完成后可运行菜单 `OperationBlacktide > Lobby > Build Character and Equipment Selection` 重建资源、预制体和界面。
- 回归入口：`GTools/verify-lobby-loadout.ps1`，在独立副本中启动真实大厅、遍历角色/装备、检查动画与存档并输出截图。需要本机相同版本 Unity 和图形设备。

### P0 - 工程骨架
- [ ] 清理 Unity 模板资源。
- [x] 建立 `Assets/Scripts/GClient` 客户端目录。
- [x] 建立 `Assets/Scripts/GShare` 共享源码目录。
- [x] 创建 `GServer` 控制台服务端。
- [x] 创建 `GTools` 工具脚本目录。
- [x] 更新 README 和启动说明。

### P1 - 账户登录注册
- [x] 服务端账号注册。
- [x] 服务端账号登录。
- [x] 本地账号文件存储。
- [x] 客户端 UI 框架（栈、Toast、Modal、PanelId、DontDestroyOnLoad）。
- [x] LoginPanel / LobbyPanel 脚本与 GameEvents 接入。
- [x] Unity 登录注册界面预制体与场景配置。
- [x] Unity 客户端与服务端请求响应闭环。
- [x] 服务端命令行调试命令。

### P2 - 大厅与单房间
- [x] 登录后进入独立 3D 大厅（CT SAS 持枪模型、场景灯光、顶部页签、右下角模式选择，保留账号会话和 TCP 连接）。
- [x] CT / T 角色选择、经典武器替换槽位、按账号本地保存、idle / 检视展示。
- [ ] 创建房间。
- [ ] 加入房间。
- [ ] 房间玩家列表同步。

### P3 - 战斗同步雏形
- [ ] 客户端输入上传。
- [ ] 服务端 Tick 模拟。
- [ ] 服务端快照下发。
- [ ] 客户端预测和校正。
- [ ] 他人角色插值显示。

### P4 - 射击与回滚命中
- [x] 本地训练场左键开火与射速限制（服务端校验待接入）。
- [ ] 服务端保存历史状态。
- [ ] 按时间戳回滚命中判定。
- [ ] 伤害、死亡、重生流程。

训练场相机平滑跟随角色的位置和朝向；W/S 沿角色朝向前进、后退，A/D 左右横移。
按住鼠标中键拖动可调整镜头相对角色的水平偏角和俯仰，松开保留；滚轮调整距离。
遮挡角色的建筑整体半透明，镜头保持缩放距离。鼠标移动调整瞄准，镜头跟转不会自行改变角色的瞄准方向。
运行 `GTools/verify-training.ps1 -UseExistingAssets` 可直接验证已保存场景中的角色运动及返回大厅流程；
测试通过 UI 射线点击确认和继续按钮，并检查遮罩阻止点击穿透、保留账号会话和重复进入训练场。

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
命中仍为即时射线，视觉参数可在 `TrainingWeaponEffects` 的 `Shot visibility` 中调整，瞄准激光参数独立。

开火入口为 `TrainingWeaponController.ProcessTrigger` / `FireBullet`，射速状态机为 `TrainingFireControl.Tick`。
`ShotFired` 事件提供命中、位置和距离衰减后的基础伤害，尚未应用生命值、护甲、穿透、CS2 后坐力/散布或网络回滚。
`TrainingVitals` 提供本地生命值、护甲、伤害接口与训练重生；当前场景尚无敌人或伤害来源，射线伤害尚未接入目标。
武器参数来源于本机 CS2 `scripts/weapons.vdata_c` 的导出数据，原文保存在
`ArtSource/CS2Imports/CombatFX/Metadata/Weapons/scripts/weapons.vdata`，精简配置在 `Assets/Art/Combat/weapon_data.json`。
AK / M4A1-S 间隔 0.10 秒，M4A4 为 0.09 秒；距离按已有导出比例 0.0254 转换为米。
特效复用 CombatFX 导出的贴图与 AK 弹壳网格，三把步枪暂共用该弹壳外观；粒子行为由 Unity 重建。
这些是 Unity 实现和导出参数，不是 CS2 原生开火函数或完整的 CS2 弹道复刻。

资源已保存在 `Assets/Resources/Training/TrainingWeapons.asset`；菜单
`OperationBlacktide > Training > Build Weapon Effects` 可根据导入素材重新生成材质、枪口位置和资源配置。
运行 `GTools/verify-firing.ps1` 可在独立项目副本中检查不同帧率射速、UI 阻断、10 个角色的三种枪口位置、
地面/高处瞄准、枪口与准星之间的遮挡、空处射程、枪声/特效和场景清理，并生成 `training-firing.png`。

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
`GTools/verify-training-menu.ps1` 验证四类循环、三投掷物回绕、每件手持模型、弹匣保留、菜单选择/返回和多分辨率布局。
`GTools/verify-hud.ps1` 在独立副本中验证弹药、换弹、血量、菜单输入和重复入场，并生成 16:9、4:3、超宽屏与各状态截图。

开枪和换弹使用 AK-47 / M4A4 / M4A1-S 各自导出的第三人称动作，共 6 个片段。
`TrainingWeaponPresentation` 在移动动画上叠加上半身动作：每次实际 `AttackStarted` 触发一次攻击动作，
换弹动作按 `TrainingAmmoState.ReloadProgress` 采样，与 HUD 进度和补满弹匣使用同一个时间源。
下半身保持行走、疾跑和跳跃；菜单、失焦与暂停时动作/换弹声音暂停，重生和退出场景时清理。
换弹分别播放拔弹匣、插弹匣、拉枪栓/枪机复位音效，9 个 WAV 从现有 CombatAudio 导出素材原样复制。
原第三人称片段没有导出声音事件时间，触发点按动作阶段配置在 `BuildTrainingWeaponActions.Bind`，不是 CS2 原始事件时间。
动画源为 `ArtSource/CS2Imports/Batch/RawAnimations`，通过 `Scripts/export_training_weapon_actions.py` 烘焙到共享角色骨架；
菜单 `OperationBlacktide > Training > Build Weapon Actions` 重建片段导入设置、上半身遮罩、动画层和武器资源引用。
`Scripts/prepare_training_primaries.py` 从本地导出素材补齐 17 把主武器参数与 113 个音效引用，
`Scripts/export_training_primaries.py` 用 Blender 烘焙 52 个片段到 `PrimaryWeaponActions.fbx`（含共用参考姿势）。
`Scripts/export_training_primary_grips.py` 从第三人称持枪姿势生成 17 个战斗挂点，避免大厅的展示挂点导致左手悬空；不改大厅预览。
`GTools/verify-weapon-actions.ps1` 验证 10 个角色的动作绑定、35 把枪的目录覆盖及实际开火/换弹、两种匕首攻击、分段音效、
换弹禁射、暂停/恢复、重复换弹、禁用/启用、重生清理以及移动时的下半身姿态，并生成开火/换弹截图。

训练场脚步由 `TrainingFootstepAudio` 根据实际地面位移触发，疾跑加快节奏并提高音量，
侧移和后退同样生效；静止、顶墙、腾空、弹窗、失焦和暂停时不产生新脚步，落地单独播放一次。
复生会重置步伐与落地状态，离开场景销毁独立脚步声源，不影响武器声源。
当前使用 `CombatAudio/WAV/sounds/player` 中已导出的 17 条水泥地脚步和 4 条落地音效，
原样复制到 `Assets/Resources/Training/Audio`；连续脚步不会重复同一样本，暂未按地表切换音色。
运行 `GTools/verify-footsteps.ps1` 在独立 Unity 副本中验证移动节奏、碰撞、跳跃/落地、输入阻断和场景清理。

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
`GTools/verify-throwables.ps1` 在独立 Unity 副本中验证遮挡、伤害、弹道、输入与生命周期并生成实景截图。
本阶段是本地训练玩法；烟雾采用粒子和遮挡区域，尚非 CS2 体积流体模拟，也未接入服务器权威同步。

投掷动作使用 CS 的 7 个第三人称片段：普通投掷物与燃烧瓶各自的取出、持握、拔销／点燃，以及共用的挥臂投掷。
`export_training_throwable_actions.py` 将动作烘焙到共享骨架，`Build Throwable Actions` 提取上半身旋转曲线；
`TrainingThrowablePresentation` 在移动姿态之后采样这些曲线，保留下半身移动、跳跃和不同角色的骨骼长度。
按住时停在准备动作末帧，快速点按也会先播完准备；弹体、手持模型隐藏和投掷声音在配置的出手帧同步触发。
出手点取动画中的手部装备中心，并保留近墙碰撞检查；预览弹道也使用该出手姿势。出手时点为本项目按片段设定，非原 CS 音效事件导出。
投掷验证同时检查 10 个角色的动画绑定、5 种投掷模型的阶段切换、手部出手位置、下半身保留、长帧、取消和切枪清理，并输出动作截图。

### 角色视野

参考 `2026-10-05-23-51-16.mp4` 中的朝向视野与遮挡表现，训练场增加角色视野：默认前方 110°、28 米，
身边 2.4 米保留周边感知，狙击开镜时随开镜过渡延伸至 55 米；这些是本项目可调参数，不是视频原作的精确数值。
实体墙同时阻挡前方视野与周边感知，矮掩体上露出的目标仍可见，烟雾和闪光接入同一套目标可见性判断。
视野外环境压暗、保留地图轮廓；不可见的训练靶、目标高亮、血条和伤害数字隐藏。碰撞和伤害结算继续保留。
建筑因挡住相机而半透明时，仍阻挡角色视线；相机遮挡处理不会提供穿墙视野。

`TrainingVision` 在训练相机上运行，可调整角度、距离、周边感知半径、暗度和软边。
环境明暗使用眼部高度的 720 条水平射线与场景深度重建，每 0.05 秒更新遮挡；目标显示使用实时三维视线，检查头、胸、腿。
这是一套本地训练视野表现，环境遮罩不是任意高度的完整三维可见体积，也尚未用于服务器信息裁剪。
URP 的三个画质档均安装 `TrainingVisionRendererFeature`，只作用于绑定角色的训练相机，HUD 与大厅不受影响。
菜单 `OperationBlacktide > Training > Build Vision` 可重建渲染接入；`GTools/verify-vision.ps1` 验证规则、画面、目标提示及 Dust II 实景。

### P5 - 爆破模式
- [ ] 回合状态机。
- [ ] 阵营与出生点。
- [ ] 安装炸弹。
- [ ] 拆除炸弹。
- [ ] 胜负结算和比分同步。
