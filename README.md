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
    _Project/
      Scenes/
        Boot.unity
        Login.unity
        Game.unity
      Scripts/
        Client/
        Netcode/
        Gameplay/
        UI/
        Debug/
      Prefabs/
      Art/
      Settings/

  Server/
    CrossFire2048.Server/
      Program.cs
      ServerConsole.cs
      GameServer.cs
      Accounts/
      Network/
      DebugCommands/
      Storage/

  Shared/
    CrossFire2048.Shared/
      Protocol/
      Models/
      Netcode/

  README.md
```

### 目录原则
- `Assets/_Project` 只放本项目自己的 Unity 资源和脚本。
- `Server` 放独立 C# Socket 服务端，可以直接从命令行启动。
- `Shared` 放客户端和服务端共用的协议、消息结构和基础模型。
- 暂不引入复杂 UI 框架、Addressables、多 asmdef 拆分和大型资源规范。

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
dotnet run --project Server/CrossFire2048.Server -- --port 7777
```

计划支持的调试命令：

```text
help              显示命令列表
accounts          查看已注册账号数量
sessions          查看当前登录会话
clients           查看当前连接
kick <userId>     踢出指定用户
save              手动保存账号数据
stop              关闭服务器
```

## 后续阶段

### P0 - 工程骨架
- [ ] 清理 Unity 模板资源。
- [ ] 建立 `Assets/_Project` 精简目录。
- [ ] 创建 `Server` 控制台服务端。
- [ ] 创建 `Shared` 协议层。
- [ ] 更新 README 和启动说明。

### P1 - 账户登录注册
- [ ] 服务端账号注册。
- [ ] 服务端账号登录。
- [ ] 本地账号文件存储。
- [ ] Unity 登录注册界面。
- [ ] Unity 客户端与服务端请求响应闭环。
- [ ] 服务端命令行调试命令。

### P2 - 大厅与单房间
- [ ] 登录后进入大厅占位界面。
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
