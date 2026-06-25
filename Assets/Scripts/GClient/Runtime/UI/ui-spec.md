# GClient UI Spec

本文档记录 Unity 客户端简易 UI 框架设计，方便后续会议和登录界面开发时统一认知。

## 设计目标

- 提供最小可用的 UI 分层与面板生命周期。
- 不引入复杂 UI 框架、动画库或资源管理系统。
- 为登录界面、大厅界面等后续 UI 提供统一打开/关闭入口。

## 目录位置

```text
Assets/Scripts/GClient/Runtime/UI/
  PanelId.cs
  PanelIds.cs
  UILayer.cs
  UIPanel.cs
  UIPanelOpenArgs.cs
  ToastOpenArgs.cs
  UIPanelEntry.cs
  UIManager.cs
  UIRoot.cs
  ToastPanel.cs
  UIModalBlocker.cs
  GameUIEntry.cs
  ui-spec.md

Assets/Scripts/GClient/Runtime/Features/Account/
  LoginPanel.cs
  LoginOpenArgs.cs
  LobbyPanel.cs
```

## 核心概念

### PanelId / PanelIds

全项目面板 ID 用 **枚举** 维护，字符串 key 与 UIManager 注册表保持一致：

```csharp
public enum PanelId
{
    None = 0,
    Login = 1,
    Lobby = 2,
    Toast = 3,
}

PanelIds.Key(PanelId.Login);   // "Login"
PanelIds.All;                  // 所有已定义 ID
PanelIds.IsOverlayOnly(...);   // Toast 等不参与栈
```

Inspector 中 `UIPanelEntry.Panel Id` 仍填字符串（如 `Login`），需与枚举名一致。

代码中优先使用枚举 overload：

```csharp
UIManager.Instance.Push(PanelId.Login);
UIManager.Instance.PopTo(PanelId.Login);
```

### OpenArgs 传参

打开面板时通过 `object args` 传入，各面板定义自己的 `UIPanelOpenArgs` 子类：

```csharp
UIManager.Instance.Push(PanelId.Login, new LoginOpenArgs
{
    DefaultUsername = "test",
});
```

已有类型：

| 面板 | Args 类 |
|------|---------|
| Login | `LoginOpenArgs` |
| Toast | `ToastOpenArgs` 或直接 `string` |

### GameEvents

全局轻量事件总线，位置：

```text
Assets/Scripts/GClient/Runtime/Common/
  GameEventId.cs
  GameEvents.cs
```

用于跨模块通信，例如：

- 登录成功
- 网络断开
- 账户状态变化

面板内订阅请使用 `AddGameEvent`，面板关闭时会自动取消订阅。

```csharp
AddGameEvent(GameEventId.LoginCompleted, OnLoginCompleted);
AddGameEvent<string>(GameEventId.AccountStatusChanged, ShowStatus);
```

### UIPanel 生命周期 API

`PanelLifetime` 由 `UIPanel` 内部持有，**不对外暴露**。

业务面板只使用以下函数：

```csharp
AddButton(button, callback);
AddEvent(subscribe, unsubscribe, handler);
AddGameEvent(eventId, handler);
AddTimer(seconds, callback);
AddIntervalTimer(seconds, callback);
AddAsync(taskFunc);
```

面板关闭时，上述注册内容会自动释放。

### UILayer

UI 显示层级：

```text
Background = 0   背景层
Normal     = 100 常规界面（登录、大厅）
Popup      = 200 弹窗
Overlay    = 300 顶层提示（Toast、Loading）
```

### UIPanel

所有 UI 面板的基类。

生命周期：

```text
Push(panelId, args)
  -> Instantiate（首次）
  -> OnOpen(args)

Pop / Close
  -> OnClose()
  -> SetActive(false)
```

子类只需继承 `UIPanel`，重写 `OnOpen` / `OnClose`。

#### Modal 输入拦截

`UIPanel` 提供 `IsModal` 属性：

- `Popup` 层面板**默认视为 Modal**（同层显示全屏遮罩，拦截下层点击）。
- 其它层可在 Inspector 勾选 `Is Modal` 启用遮罩。
- `UIManager` 自动创建 `UIModalBlocker`（半透明黑色 Image + `raycastTarget`），置于栈顶 Modal 面板正下方。

```csharp
// Popup 层任意 Modal 面板打开时，下层 UI 无法被点击
UIManager.Instance.Push("ConfirmDialog"); // Layer = Popup, IsModal 默认 true
```

遮罩颜色可在 `UIModalBlocker.Create` 默认 `(0,0,0,0.55)`，后续可扩展为可配置。

### ToastPanel

Overlay 层轻提示，**不参与栈管理**。通过专用 API 调用：

```csharp
UIManager.Instance.ShowToast("注册成功");
UIManager.Instance.ShowToast("网络错误", duration: 3f);
```

Toast 预制体需在 UIManager 注册，`Panel Id` 填 `Toast`，`Layer` 选 `Overlay`。

### UIManager

职责：

- 注册面板预制体（通过 `UIPanelEntry`）。
- **按 UILayer 使用栈管理面板**。
- 缓存已实例化的面板，避免重复创建。
- Overlay 专用 API：`ShowToast`。

#### 栈管理规则

每个 `UILayer` 各自维护一个栈，例如：

```text
Normal 栈：Login -> Lobby -> Room
Popup 栈：Confirm -> Alert
```

行为：

- `Push`：关闭当前层栈顶面板，新面板入栈并显示。
- `Pop` / `Back`：关闭当前层栈顶，恢复下一层面板。
- `PopTo`：关闭栈顶直到指定面板重新显示。
- `Open`：兼容旧接口，等同于 `Push`。
- `HandleBackInput`：Esc 返回，先 Pop Popup，再 Back Normal（Normal 栈深度 > 1 时）。

示例：

```text
Push(Login)   栈：[Login]           显示 Login
Push(Lobby)   栈：[Login, Lobby]    显示 Lobby，Login 被关闭
Back()        栈：[Login]           显示 Login
```

常用 API：

```csharp
UIManager.Instance.Push(PanelId.Login);
UIManager.Instance.Push(PanelId.Lobby);
UIManager.Instance.ShowToast("提示文字");
UIManager.Instance.Back();                      // Normal 层返回
UIManager.Instance.HandleBackInput();           // Esc 统一处理
UIManager.Instance.Pop(UILayer.Popup);          // 关闭 Popup 栈顶
UIManager.Instance.PopTo(PanelId.Login);
UIManager.Instance.Close(PanelId.Lobby);
UIManager.Instance.CloseAll(UILayer.Popup);
UIManager.Instance.GetStackCount(UILayer.Normal);
UIManager.Instance.TryGetTopPanelId(UILayer.Normal, out string topId);
```

### UIRoot

职责：

- 自动创建 `Canvas`、`EventSystem`。
- 创建各层级容器节点。
- 初始化 `UIManager`。
- 监听 `Escape` 键并调用 `HandleBackInput`（可在 Inspector 关闭 `Enable Back Key`）。
- **默认 `DontDestroyOnLoad`**：UI 根与 EventSystem 跨场景保留；新场景若再挂 UIRoot 会自动销毁重复实例。

Inspector 选项：

| 字段 | 说明 |
|------|------|
| `Enable Back Key` | Esc 触发返回 |
| `Dont Destroy On Load` | 跨场景持久化（默认开启） |

跨场景注意：

- 首个场景的 `UIRoot` 会保留，后续场景**不要再挂第二个 UIRoot**（或挂了也会被销毁）。
- `GameUIEntry` 会检测 Normal 栈是否已有面板，避免重复 Push Login。
- 换场景后 `AuthClient` 等业务对象需自行处理（可同样 DDOL 或场景单例）。

场景中只需挂一个带 `UIRoot + UIManager` 的对象即可（通常放在首个启动场景）。

### GameUIEntry

可选启动脚本：场景 Play 后自动 `Push` 起始面板（默认 Login）。

## 场景搭建步骤

1. 新建空物体，例如 `UIRoot`。
2. 挂上 `UIRoot`、`UIManager`、`GameUIEntry`（可选）。
3. 同场景放置 `AuthClient`（含 `TcpGameClient`）。
4. 制作面板预制体并注册到 `UIManager.Panel Entries`：

| Panel Id | 脚本 | Layer |
|----------|------|-------|
| Login | `LoginPanel` | Normal |
| Lobby | `LobbyPanel` | Normal |
| Toast | `ToastPanel` | Overlay |

5. `LoginPanel` / `LobbyPanel` 在 Inspector 绑定 `AuthClient`、按钮、输入框等。

## LoginPanel / LobbyPanel

`LoginPanel`：

- 绑定账号/密码输入框、登录/注册按钮、状态文本。
- 通过 `AddGameEvent` 监听账户状态与登录/注册结果。
- 登录成功：`ShowToast` + `Push(PanelId.Lobby)`。
- 注册成功：`ShowToast` 提示。

`LobbyPanel`：

- 显示欢迎语，提供退出登录按钮。
- 退出后 `PopTo(PanelId.Login)`。

示例：

```csharp
protected override void OnOpen(object args)
{
    if (args is LoginOpenArgs loginArgs) { /* 填充默认值 */ }

    AddButton(loginButton, OnLogin);
    AddButton(registerButton, OnRegister);
    AddGameEvent<string>(GameEventId.AccountStatusChanged, ShowStatus);
    AddGameEvent<LoginResponse>(GameEventId.LoginCompleted, OnLoginCompleted);
}
```

## 与登录模块的关系

当前登录业务在：

```text
Assets/Scripts/GClient/Runtime/Features/Account/
  AuthClient.cs
  LoginController.cs   // Inspector 调试，正式 UI 用 LoginPanel
```

## 已知限制

- 当前不支持面板栈动画。
- 当前不支持 Addressables 动态加载。
- 当前每个 `panelId` 只缓存一个实例。
- 栈返回时恢复上一层会重新走 `OnOpen`，尚未区分 `OnShow` / `OnHide`。
- Modal 遮罩按层管理，不支持跨层组合遮罩。

## 后续计划

- Loading 专用 Overlay 组件。
- `Replace(panelId)` 替换栈顶而不增加深度。
- Modal 遮罩颜色/透明度可配置。
