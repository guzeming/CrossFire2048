# GClient Network Spec

本文档记录 Unity 客户端网络模块当前设计，方便后续会议、复盘和继续开发时统一认知。

## 当前目标

当前网络模块只服务第一阶段账户链路：

- 连接 C# Socket 服务端。
- 发送注册请求。
- 发送登录请求。
- 接收注册/登录响应。
- 将网络结果交给账号模块处理。

战斗内同步、客户端预测、快照同步、回滚命中判定暂不放在当前 TCP 登录链路中实现，后续会单独扩展。

## 目录位置

```text
Assets/Scripts/GClient/Runtime/Network/
  NetworkMessageCodec.cs
  TcpGameClient.cs
  network-spec.md
```

相关共享协议位置：

```text
Assets/Scripts/GShare/Runtime/Protocol/
  AccountMessages.cs
  AccountRules.cs
  MessageType.cs
  NetworkMessage.cs
```

服务端网络实现位置：

```text
GServer/CrossFire2048.Server/Network/
  ClientConnection.cs
  MessageCodec.cs
```

## 当前传输方式

当前使用 TCP。

原因：

- 登录、注册、心跳属于低频可靠消息。
- TCP 便于先验证客户端和服务端的基本链路。
- 当前阶段更重视可调试性，而不是高频同步性能。

后续战斗同步会单独规划 UDP 或自定义可靠 UDP 层。

## 消息格式

当前约定：每条消息是一行 JSON，使用换行符分隔。

外层统一包成 `NetworkMessage`：

```csharp
public class NetworkMessage
{
    public MessageType Type;
    public string Json;
}
```

字段含义：

- `Type`：消息类型，例如 `RegisterRequest`、`LoginResponse`。
- `Json`：具体消息负载序列化后的 JSON 字符串。

示意流程：

```text
RegisterRequest
  -> JsonUtility.ToJson(payload)
  -> NetworkMessage(Type=RegisterRequest, Json=payloadJson)
  -> JsonUtility.ToJson(envelope)
  -> TCP WriteLine
```

服务端使用 `System.Text.Json`，并开启 `IncludeFields = true`，确保能正确序列化/反序列化共享协议中的 public fields。

## 当前组件职责

### NetworkMessageCodec

位置：

```text
Assets/Scripts/GClient/Runtime/Network/NetworkMessageCodec.cs
```

职责：

- 将具体消息负载编码为 `NetworkMessage` JSON。
- 将服务端返回的一行 JSON 解码为 `NetworkMessage`。
- 根据 `NetworkMessage.Type` 进一步解码具体负载。

它不负责网络连接，也不负责业务逻辑。

### TcpGameClient

位置：

```text
Assets/Scripts/GClient/Runtime/Network/TcpGameClient.cs
```

职责：

- 根据 `AppConfig` 或组件字段读取服务器地址。
- 建立 TCP 连接。
- 发送一行 JSON 消息。
- 后台线程读取服务端响应。
- 在 Unity 主线程触发事件。

当前公开事件：

```csharp
Connected
Disconnected
MessageReceived
ErrorReceived
```

当前连接状态：

```csharp
Disconnected
Connecting
Connected
```

## AppConfig

服务器地址通过 `AppConfig` 统一配置。

位置：

```text
Assets/Scripts/GClient/Runtime/App/AppConfig.cs
```

当前默认值：

```text
ServerHost = 127.0.0.1
ServerPort = 7777
```

后续部署到云服务器后，建议改为域名：

```text
ServerHost = server.crossfire2048.com
ServerPort = 7777
```

这样客户端打包后不需要玩家手动配置 IP。

## 账号模块调用关系

账号模块位置：

```text
Assets/Scripts/GClient/Runtime/Features/Account/
  AuthClient.cs
  AuthSession.cs
  LoginController.cs
```

调用关系：

```text
LoginController
  -> AuthClient.RegisterAsync / LoginAsync
    -> TcpGameClient.ConnectConfiguredAsync
    -> TcpGameClient.SendAsync
      -> NetworkMessageCodec.Encode
```

响应关系：

```text
TcpGameClient.MessageReceived
  -> AuthClient.OnMessageReceived
    -> Decode RegisterResponse / LoginResponse
    -> RegisterCompleted / LoginCompleted
    -> LoginController 输出日志或后续驱动 UI
```

## 当前验证结果

已完成本机验证：

- Unity 客户端可以连接本机服务端。
- 注册 `test_user` 成功。
- 重复注册返回已注册。
- 登录成功。
- 服务端 `accounts`、`clients`、`sessions`、`status` 输出符合预期。

## 已知限制

- 当前没有自动重连。
- 当前没有请求超时机制。
- 当前没有请求 ID，因此同一时间不应并发发送多个同类型登录/注册请求。
- 当前没有加密传输，密码仅在服务端存储时做盐值哈希。
- 当前 TCP 链路只适合登录、大厅等低频可靠消息，不适合作为战斗高频同步通道。

## 后续计划

短期：

- 给注册/登录请求增加超时提示。
- 增加连接状态显示。
- 正式 UI 接入 `LoginController`。
- 把 `AppConfig.asset` 纳入客户端启动流程。

中期：

- 增加大厅、房间相关消息。
- 增加会话 Token 校验。
- 服务端增加更完整的连接踢出和断线处理。

长期：

- 战斗同步单独设计 UDP 通道。
- 增加输入序列号、服务器 Tick、快照 ID。
- 增加客户端预测和服务器校正。
- 增加基于时间戳的回滚命中判定。
