# CrossFire2048

俯视角轻量版 CS/CF 爆破联机射击项目。

项目当前从空 Unity 工程重新开始。优先目标不是先搭复杂框架，而是先跑通账户登录注册、客户端连接服务端、服务端命令行调试，再逐步进入权威同步、客户端预测、快照同步和回滚命中判定。

## 核心方向

### 客户端
- 引擎：Unity `2022.3 LTS` + URP。
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
CrossFire2048/
  Assets/
    Scripts/
      GClient/
        Runtime/
          App/              AppConfig 等应用配置
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
    CrossFire2048.Server/
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
- 登录流程：登录成功后保存当前会话信息，并进入后续场景占位。
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
dotnet run --project GServer/CrossFire2048.Server -- --port 7777
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
Host: server.crossfire2048.com
Port: 7777
```

这样服务器换 IP 时只需要修改 DNS 解析，不需要重新打包客户端。

Unity 中可通过菜单创建默认配置资源：

```text
CrossFire2048/Create Default App Config
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
| `GameUIEntry` | 启动时自动 `Push(Login)`，跨场景时避免重复打开 |

### 面板 ID（PanelId 枚举）

全项目面板 ID 用枚举维护，字符串 key 与 Inspector 注册表一致：

```csharp
public enum PanelId { None, Login, Lobby, Toast }

UIManager.Instance.Push(PanelId.Login);
UIManager.Instance.ShowToast("注册成功");
UIManager.Instance.PopTo(PanelId.Login);
```

`PanelIds.Key(PanelId.Login)` → `"Login"`，`PanelIds.All` 列出全部 ID。

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
| `LoginPanel` | 登录/注册表单，监听 `GameEvents`，成功则 Toast + Push Lobby |
| `LobbyPanel` | 大厅占位，欢迎语 + 退出登录 → PopTo Login |
| `LoginController` | Inspector 调试按钮（可与正式 UI 并存） |

### GameEvents

位置：`Assets/Scripts/GClient/Runtime/Common/`

`AuthClient` 在注册/登录/连接状态变化时发布事件，UI 通过 `AddGameEvent` 订阅：

```text
AccountStatusChanged、RegisterCompleted、LoginCompleted
NetworkConnected、NetworkDisconnected
```

## Unity 场景搭建（登录 UI）

当前代码已就绪，需在 Unity 编辑器中完成预制体与场景配置：

### 1. 启动服务端

```bat
GTools\run-server.bat
```

### 2. 场景对象

**GameBootstrap**（建议 DontDestroyOnLoad）：

- `TcpGameClient`（绑定 `AppConfig.asset`）
- `AuthClient`

**UIRoot**：

- `UIRoot`（默认 DontDestroyOnLoad）
- `UIManager`（注册面板预制体）
- `GameUIEntry`（启动 Push Login）

### 3. 制作并注册预制体

在 `UIManager` → Panel Entries 注册：

| Panel Id | 脚本 | Layer |
|----------|------|-------|
| `Login` | `LoginPanel` | Normal |
| `Lobby` | `LobbyPanel` | Normal |
| `Toast` | `ToastPanel` | Overlay |

Panel Id 必须与枚举名一致。`LoginPanel` / `LobbyPanel` 需绑定 `AuthClient`、InputField、Button、Text。

### 4. Play 验证

1. 自动打开登录面板  
2. 注册 → Toast + 状态提示  
3. 登录成功 → Toast + 进入 Lobby  
4. Esc 从 Lobby 返回 Login  
5. 服务端 `accounts` / `sessions` 核对状态  

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
- 已实现：`UIRoot`、`UIManager`（栈管理）、`UIPanel`、`PanelId`/`PanelIds`、`ToastPanel`、`UIModalBlocker`、`GameUIEntry`、Esc 返回、DontDestroyOnLoad。
- 详细说明：`Assets/Scripts/GClient/Runtime/UI/ui-spec.md`

### 账号模块
- 代码位置：`Assets/Scripts/GClient/Runtime/Features/Account`
- 当前能力：注册、登录、保存本地会话、`GameEvents` 广播、`LoginPanel` / `LobbyPanel` 脚本、Inspector 调试（`LoginController`）。
- 正式 UI：需在 Unity 中制作 Login/Lobby/Toast 预制体并注册到 `UIManager`（见上文「Unity 场景搭建」）。
- 调试方式：Play Mode 下用 `LoginPanel` 表单，或 Inspector 中 `LoginController` 的 Register / Login 按钮。

## 后续阶段

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
- [ ] Unity 登录注册界面预制体与场景配置（Editor 手工步骤）。
- [x] Unity 客户端与服务端请求响应闭环。
- [x] 服务端命令行调试命令。

### P2 - 大厅与单房间
- [x] 登录后进入大厅占位界面（LobbyPanel 脚本，待预制体接入）。
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
- [ ] 开火输入和射速校验。
- [ ] 服务端保存历史状态。
- [ ] 按时间戳回滚命中判定。
- [ ] 伤害、死亡、重生流程。

### P5 - 爆破模式
- [ ] 回合状态机。
- [ ] 阵营与出生点。
- [ ] 安装炸弹。
- [ ] 拆除炸弹。
- [ ] 胜负结算和比分同步。
