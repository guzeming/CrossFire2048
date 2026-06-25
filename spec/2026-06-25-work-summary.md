# 2026-06-25 工作总结

## 背景

项目 `CrossFire2048` 已从原来的复杂结构重置为一个空 Unity 工程，当前目标调整为从零搭建一个“俯视角轻量版 CS/CF 爆破联机射击”项目。

保留的核心技术方向：

- Unity 客户端。
- C# Socket 服务端。
- 服务器权威。
- 客户端预测。
- 快照同步。
- 回滚命中判定。
- 服务端命令行调试窗口。

第一阶段优先目标调整为：先完成账户注册、账户登录、会话管理和服务端命令行调试。

## 已完成工作

### 1. 检查空工程状态

确认当前 Unity 工程是一个非常干净的 URP 空工程：

- Unity 版本：`2022.3.47f1c1`。
- URP 版本：`14.0.11`。
- 当前 `Assets` 中只有模板场景、URP 设置、模板说明资源和一个占位脚本。
- `.gitignore` 已忽略 Unity 常见缓存目录，如 `Library`、`Temp`、`Obj`、`Logs`、`UserSettings`。

结论：这个工程适合作为重新开始的空项目。

### 2. 更新 README 开发路线

已重写 `README.md`，将项目方向从旧的复杂结构改为更精简的路线：

- 明确项目定位为俯视角轻量版 CS/CF 爆破联机射击。
- 删除旧的复杂目录规划和百分比进度。
- 新增精简目录规划：`Assets/_Project`、`Server`、`Shared`。
- 将第一阶段目标改为账户登录注册。
- 增加服务端命令行调试窗口设计。
- 后续阶段调整为：账户系统、大厅房间、战斗同步、射击回滚、爆破模式。

该 README 变更已提交并推送到 `main`。

提交信息：`更新项目开发路线图`

### 3. 调整 .gitignore

Unity 默认 `.gitignore` 会忽略所有 `*.csproj`，这适合 Unity 自动生成的工程文件，但会误伤我们自己写的服务端和共享库项目。

已调整规则：

- 继续忽略 Unity 自动生成的 `*.csproj` 和 `*.sln`。
- 放行 `Server/**/*.csproj`。
- 放行 `Shared/**/*.csproj`。
- 增加 `.NET` 构建输出忽略：`**/bin/`、`**/obj/`。

目的：允许提交真正的服务端项目和共享协议项目，同时避免提交构建缓存。

### 4. 创建 Shared 共享协议项目

已创建目录：

```text
Shared/
  CrossFire2048.Shared/
```

项目类型：C# 类库。

目标框架已调整为：

```text
netstandard2.1
```

原因：后续 Unity 客户端更容易复用协议类。

已创建的共享协议文件：

```text
Shared/CrossFire2048.Shared/Protocol/MessageType.cs
Shared/CrossFire2048.Shared/Protocol/NetworkMessage.cs
Shared/CrossFire2048.Shared/Protocol/AccountMessages.cs
Shared/CrossFire2048.Shared/Protocol/AccountRules.cs
```

当前协议内容包括：

- `MessageType`
- `NetworkMessage`
- `RegisterRequest`
- `RegisterResponse`
- `LoginRequest`
- `LoginResponse`
- `ErrorResponse`
- `Heartbeat`
- `AuthResultCode`
- `AccountRules`

设计思路：登录注册属于低频可靠业务，先用简单的消息信封承载 JSON 负载。之后战斗内同步再单独设计高频协议。

### 5. 创建 Server 控制台服务端项目

已创建目录：

```text
Server/
  CrossFire2048.Server/
```

项目类型：`.NET 8` 控制台程序。

服务端已引用共享协议项目：

```text
Shared/CrossFire2048.Shared/CrossFire2048.Shared.csproj
```

已创建的主要服务端文件：

```text
Server/CrossFire2048.Server/Program.cs
Server/CrossFire2048.Server/ServerConsole.cs
Server/CrossFire2048.Server/ServerLog.cs
Server/CrossFire2048.Server/GameServer.cs
Server/CrossFire2048.Server/Accounts/AccountService.cs
Server/CrossFire2048.Server/Storage/AccountRecord.cs
Server/CrossFire2048.Server/Storage/AccountStore.cs
Server/CrossFire2048.Server/Network/MessageCodec.cs
Server/CrossFire2048.Server/Network/ClientConnection.cs
Server/CrossFire2048.Server/DebugCommands/IDebugCommand.cs
Server/CrossFire2048.Server/DebugCommands/DebugCommandRegistry.cs
Server/CrossFire2048.Server/DebugCommands/ServerCommands.cs
```

### 6. 服务端当前能力

当前服务端骨架已包含：

- TCP 监听。
- 客户端连接管理。
- 注册请求处理。
- 登录请求处理。
- 本地账号 JSON 文件存储。
- 密码盐值与 PBKDF2 哈希存储。
- 登录会话 Token 生成。
- 重复登录时踢掉旧连接。
- 心跳消息基础处理。
- 简单日志输出。

当前账号数据默认保存到：

```text
Server/CrossFire2048.Server/bin/.../data/accounts.json
```

后续可以通过启动参数修改账号文件路径。

### 7. 服务端命令行调试窗口

已设计并实现第一版命令行调试框架。

计划启动方式：

```powershell
dotnet run --project Server/CrossFire2048.Server -- --port 7777
```

当前命令包括：

```text
help        显示命令列表
accounts    查看已注册账号数量
sessions    查看当前登录会话
clients     查看当前连接
kick         踢出指定用户
save         手动保存账号数据
stop         关闭服务器
```

命令系统由 `DebugCommandRegistry` 管理，后续可以很容易继续添加如 `rooms`、`tick`、`snapshot`、`netstat` 等调试命令。

## 还没有完成的工作

以下内容还没有继续做：

- 未创建 `Assets/_Project` Unity 客户端目录。
- 未制作 Unity 登录注册界面。
- 未编写 Unity 客户端连接服务端的脚本。
- 未运行服务端构建验证。
- 未创建启动服务端的 `.bat` 文件。
- 未提交今天后续新增的服务端和共享库代码。

## 当前建议的下一步

建议下一步先做两件事：

1. 构建并运行服务端，确认当前 C# 代码能编译、能启动命令行窗口。
2. 新增一个启动脚本，例如：

```text
run-server.bat
```

用于一键执行：

```powershell
dotnet run --project Server/CrossFire2048.Server -- --port 7777
```

等服务端确认能跑，再进入 Unity 客户端部分，创建登录注册 UI 和 TCP 客户端。

## 今天遇到的问题

今天最大的问题不是权限不足，而是执行过程不连续，导致多次看起来像卡住。后续应该避免一次性解释过多，改成小步推进：

1. 每次只做一个明确步骤。
2. 做完马上汇报结果。
3. 需要新建文件时直接写入，不长时间停在解释阶段。
4. 运行耗时命令前先说明目的，命令完成后立即说明结果。

## 后续结构调整：GClient / GServer / GShare / GTools

根据新的目录讨论，项目结构从 `Assets/_Project`、`Server`、`Shared` 调整为更明确的分区命名：

```text
Assets/GClient   Unity 客户端代码与客户端资源
GServer          C# Socket 服务端
GShare           客户端和服务端共享协议/模型
GTools           构建、启动、调试脚本
```

本次调整已完成：

- `Server` 已迁移为 `GServer`。
- `Shared` 已迁移为 `GShare`。
- `build-server.bat` 和 `run-server.bat` 已迁移到 `GTools`。
- `Assets/GClient/Runtime/App` 已创建，用于客户端应用级入口和生命周期。
- `Assets/GClient/Runtime/Features/Account` 已创建，用于正式账户登录注册功能。
- `Assets/GClient/Runtime/Network` 已创建，用于客户端网络通信。
- `Assets/GClient/Runtime/Common` 已创建，用于客户端通用工具。
- `Assets/GClient/Editor` 已创建，用于 Unity 编辑器扩展代码。

后续客户端登录注册脚本应优先放在：

```text
Assets/GClient/Runtime/Features/Account
Assets/GClient/Runtime/Network
```

## 最终脚本目录调整：Assets/Scripts/GClient 与 Assets/Scripts/GShare

根据最新目录约定，所有 Unity 可见的 C# 脚本统一放到 `Assets/Scripts` 下：

```text
Assets/Scripts/GClient   Unity 客户端专属脚本
Assets/Scripts/GShare    客户端与服务端共享源码
GServer                  独立 C# 服务端项目
GTools                   构建与启动脚本
```

这次调整后的关键点：

- `Assets/GClient` 已迁移到 `Assets/Scripts/GClient`。
- 原根目录 `GShare` 已迁移到 `Assets/Scripts/GShare/Runtime`。
- 服务端不再通过独立 `GShare` 类库引用共享代码。
- `GServer/CrossFire2048.Server.csproj` 改为直接编译 `Assets/Scripts/GShare/Runtime/**/*.cs`。
- 这样 Unity 客户端和 C# 服务端会使用同一份共享协议/模型源码，避免两边各维护一套。
