# GClient UI Spec

本文档记录 Unity 客户端简易 UI 框架设计，方便后续会议和登录界面开发时统一认知。

## 设计目标

- 提供最小可用的 UI 分层与面板生命周期。
- 不引入复杂 UI 框架、动画库或资源管理系统。
- 为登录界面、大厅界面等后续 UI 提供统一打开/关闭入口。

## 目录位置

```text
Assets/Scripts/GClient/Runtime/UI/
  UIPanelId.cs
  PanelIds.cs
  UILayer.cs
  UIPanel.cs
  UIPanelOpenArgs.cs
  ToastOpenArgs.cs
  UIPath.cs
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

### UIPanelId / PanelIds

全项目面板 ID 用 **枚举** 维护，字符串 key 与 UIManager 注册表保持一致：

```csharp
public enum UIPanelId
{
    None = 0,
    Login = 1,
    Lobby = 2,
    Toast = 3,
}

PanelIds.Key(UIPanelId.Login);   // "Login"
PanelIds.All;                  // 所有已定义 ID
PanelIds.IsOverlayOnly(...);   // Toast 等不参与栈
```

Inspector 中面板预制体自身的 `Panel Id` 仍填字符串（如 `Login`），需与 `UIPanelId` 枚举名一致。

### UIPath（界面名 → Prefab 路径字典）

**脚本文件** `UIPath.cs` 维护界面名与 Resources 路径的映射：

```csharp
{ "Login", "UI/Panels/LoginPanel" },
{ "Lobby", "UI/Panels/LobbyPanel" },
```

Prefab 必须放在 `Assets/Resources/` 下，运行时 `Resources.Load` 加载。

**Add UI 工具**（菜单 `OperationBlacktide → UI → Add UI`）：

- 填写界面名字 + Resources 路径，点击 Add 写入 `UIPath.cs`
- 或拖入 Resources 下的 Prefab 自动填路径

代码中只需：

```csharp
UIManager.Instance.Push(UIPanelId.Login);
UIManager.Instance.PopTo(UIPanelId.Login);
```

### OpenArgs 传参

打开面板时通过 `object args` 传入，各面板定义自己的 `UIPanelOpenArgs` 子类：

```csharp
UIManager.Instance.Push(UIPanelId.Login, new LoginOpenArgs
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

Toast 路径需在 **UIPath** 中注册（通过 Add UI 添加）。

### UIManager

职责：

- 从 **UIPath** 按界面名 `Resources.Load` Prefab 并实例化。
- **按 UILayer 使用栈管理面板**。
- 缓存已实例化的面板，避免重复创建。
- Overlay 专用 API：`ShowToast`。

#### 栈管理规则

每个 `UILayer` 各自维护一个栈，例如：

```text
Normal 栈：当前场景主面板 -> 当前场景子面板
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
切到大厅场景   清理旧栈，再 Push(Lobby)，栈：[Lobby]
Esc           栈：[Lobby]           仅一个主面板，不返回登录
```

常用 API：

```csharp
UIManager.Instance.Push(UIPanelId.Login);
UIManager.Instance.Push(UIPanelId.Lobby);
UIManager.Instance.ShowToast("提示文字");
UIManager.Instance.Back();                      // Normal 层返回
UIManager.Instance.HandleBackInput();           // Esc 统一处理
UIManager.Instance.Pop(UILayer.Popup);          // 关闭 Popup 栈顶
UIManager.Instance.PopTo(UIPanelId.Login);
UIManager.Instance.Close(UIPanelId.Lobby);
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
- `GameUIEntry` 挂在场景自己的 `SceneUI` 上，不能挂在持久化的 UIRoot 上；每次场景启动清理 Popup/Normal 栈，再 Push 该场景的起始面板。
- `App/GameSceneFlow` 将 Game 对象（含 AuthClient、TcpGameClient）跨场景保留，并销毁新场景中的重复实例。

场景中只需挂一个带 `UIRoot + UIManager` 的对象即可（通常放在首个启动场景）。

### GameUIEntry

场景 UI 入口：清理 Popup/Normal 栈后自动 `Push` 起始面板。SampleScene 使用 Login，LobbyScene 使用 Lobby。未登录时不会打开 Lobby，由 GameSceneFlow 返回登录场景。

### GameSceneFlow（应用层）

位置：`Assets/Scripts/GClient/Runtime/App/GameSceneFlow.cs`。

- 订阅 `AuthClient.LoginCompleted`，仅登录成功且存在有效会话时加载 LobbyScene。
- 使用 `SceneManager.LoadSceneAsync` 切换场景；账号、TCP 连接和 UIRoot 持续保留。
- `EnterTrainingGround()` 在有效会话下加载 DustII；该场景的 `TrainingSceneController` 清理 Popup/Normal 层后打开 Training 面板。
- `ReturnToLobby()` 保留账号和 TCP 连接回到 LobbyScene；会话失效时进入 SampleScene。
- 退出登录会清除会话、断开 TCP 并加载 SampleScene；大厅或训练场断线同样返回登录，加载期间断线也会在加载完成后返回登录。
- 在编辑器直接 Play LobbyScene 时，未登录则返回 SampleScene。
- SampleScene、LobbyScene 和 DustII 三个场景须在 Build Settings 中启用；场景路径常量集中定义在此脚本中。

## 场景搭建步骤

1. 新建空物体，例如 `UIRoot`。
2. 挂上 `UIRoot`、`UIManager`；另建 `SceneUI` 挂 `GameUIEntry` 并选择起始面板。
3. 同场景的 Game 对象挂 `GameSceneFlow`、`AuthClient`、`TcpGameClient`，绑定 AppConfig。
4. 自己做 Login / Lobby / Toast 等 Prefab，放到 `Assets/Resources/` 下。
5. 用 **Add UI** 把界面名和路径写入 `UIPath.cs`。

**UIManager 无需任何 Inspector 配置。**

| UIPanelId | 脚本 | Layer |
|-----------|------|-------|
| Login | `LoginPanel` | Normal |
| Lobby | `LobbyPanel` | Normal |
| Toast | `ToastPanel` | Overlay |

6. `LoginPanel` / `LobbyPanel` 优先使用 `GameSceneFlow.Auth`，保持跨场景一致的账号对象。
7. SampleScene 和 LobbyScene 都加入 Build Settings，SampleScene 排在首位。

## LoginPanel / LobbyPanel

`LoginPanel`：

- 绑定账号/密码输入框、登录/注册按钮、状态文本。
- 通过 `AddGameEvent` 监听账户状态与登录/注册结果。
- 登录成功：`ShowToast`；由应用层 `GameSceneFlow` 切换大厅场景，该场景的 `GameUIEntry` 打开 Lobby。
- 注册成功：`ShowToast` 提示。

`LobbyPanel`：

- 透明 UGUI 叠加层，中央不放全屏背景图；CT 模型、地面、灯光和相机属于 LobbyScene。
- 顶部大厅/装备/生涯页签控制面板内的子视图；按钮通过 `AddButton` 注册，关闭时统一释放。
- 右下角选择模式和进入游戏；模式下拉是面板内部控件，选择后关闭，支持点击外部或 Esc 收起。
- 点击模式下拉中的训练场直接进入 DustII；已选训练场时点击进入游戏也使用同一入口。初始化或重新打开大厅时只恢复模式选择，不自动跳转。
- 房间、匹配和对战暂未接入，爆破模式和团队竞技仍提示暂未开放。
- 显示账号并提供退出登录按钮。
- 调用 `GameSceneFlow.Logout()`，清除会话、断开连接并返回登录场景。

`Features/Lobby/LobbyCharacterPreview` 挂在大厅场景对象上，仅处理中央区域拖动旋转模型，跳过 UI 点击；它不创建 Canvas 或管理 UI 生命周期。

训练场 UI：

- `TrainingPanel` 位于 Normal 层，显示 CS 风格 HUD：左下生命值/护甲，右下主武器剪影、弹匣与备弹，顶部训练计时，以及鼠标准星、换弹进度和低状态提示。
- HUD 绑定 `TrainingSceneController` 的当前角色、`TrainingVitals` 和 `TrainingWeaponController.Ammo`；事件通过 `AddEvent` 随面板关闭释放，重新进入训练场时绑定新的角色。
- 使用 `SafeArea` 容器和 CanvasScaler 适配分辨率。只有右上训练菜单按钮接收射线，血条、图标、准星不阻挡瞄准与开火。
- `BuildTrainingHud.Build` 生成 HUD prefab、模型剪影和训练菜单；`BuildTrainingMenu.Build` 可单独重建训练菜单，`BuildTrainingScene` 重建训练场时复用此入口。
- `TrainingMenuPanel` 位于 Popup 层，使用居中通高的半透明深色面板、橙色强调线与纵向按钮，提供继续训练、切换武器、返回大厅。武器页按四类展示模型缩略图，点击即可装备，保持菜单打开便于选择。
- 数字键（含小键盘）1 主武器（包含狙击）、2 副武器、3 近战、4 投掷；首次进入类别恢复上次选择，同类重复按键循环到下一件。训练场提供目录内可手持的四类装备，不写回大厅配装。无装备类别不切换。
- 切换时更新手持模型、HUD 图标、名称和类别，取消开镜和换弹，每把枪保留独立弹匣。菜单打开时禁止游戏快捷键、移动和开火，菜单内的装备按钮仍然可用。
- 装备目录内全部 35 把枪（含电击枪）接入开火、弹药与换弹；步枪、微冲、霰弹枪和机枪共用主武器切换入口。所有枪械（包含 4 把狙击）均显示跟随枪管的 30 米瞄准红线，遇到障碍物截断，狙击开镜时继续显示。
- 无训练攻击配置的其他装备仍支持展示，隐藏枪械弹药数字，禁用上一把武器的攻击/激光/动作；构建器会拒绝任何缺少射击配置的枪械。
- `ReturnToLobbyPanel` 位于 Popup 层，提供“继续训练”和“返回大厅”，复用 UIModalBlocker 拦截点击。
- `UIRoot.HandleBackInput()` 先关闭现有弹窗/处理返回历史；未消耗时触发 `UnhandledBackRequested`，训练场据此打开训练菜单。再次按 Esc 关闭菜单；从返回大厅确认框按 Esc 回到菜单，“继续训练”关闭整个 Popup 栈。
- `TrainingSceneController` 随场景订阅/退订返回事件；弹窗或场景加载期间禁用角色与相机输入，不更改全局 Time.timeScale。离开训练场恢复原鼠标状态。
- UI 预制体及 DustII 的训练组件由 `OperationBlacktide/Training/Build Dust II Training` 菜单生成。

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
