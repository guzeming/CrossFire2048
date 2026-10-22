# GClient UI Spec

本文档记录 Unity 客户端简易 UI 框架设计，方便后续会议和登录界面开发时统一认知。

## 设计目标

- 提供最小可用的 UI 分层与面板生命周期。
- 不引入复杂 UI 框架、动画库或资源管理系统。
- 为登录界面、大厅界面等后续 UI 提供统一打开/关闭入口。

## 黑金玻璃视觉规范

- 全局颜色由 `BlackGoldTheme` 提供：亮金 `#FFCB5E`、暖白金 `#FFF5C8`、烟黑 `#0A0C0F`、正文 `#EEEBE1`、次级文字 `#A3A9AD`。红色警告、绿色确认和阵营蓝保留语义。
- 以用户提供的大厅参考图为布局依据：场景与角色占据中央，左上导航依次为“大厅、选角、装备、生涯”，账号靠右上，地图/模式与主操作集中在右下。选角是独立 Tab，阵营和角色列表随页面切换；大厅不再提供左下角选角入口。只展示已实现的入口，不添加装饰文案或虚构功能。
- 使用直角、1px 分隔线与短角标，不使用圆角、厚边框和外投影。主操作参考用户提供的黑金鼠标：烟黑透镜外壳、内部琥珀金光、明暗变化的金属细边与暖白金文字；避免整块米金色平铺。悬停时斜向反射移动、内部金光和边缘高光增强，按下压暗，禁用去饱和。普通控件保持半透明，选中使用亮金边线，输入框使用底线。
- 大厅顶部不铺完整导航卡片，装备区只在两侧增加局部暗色渐隐，保持角色区清晰。暂停菜单采用左侧列表与向场景消隐的底色；选枪和确认对话框使用直角薄面板。
- `UIGlassEffect` 复用原 Graphic、Selectable 和引用，通过顶点数据向共享材质传递状态，不创建逐按钮材质。悬停/焦点过渡 140ms，按下 80ms，使用非缩放时间；选中显示金色底线，禁用降低表面亮度。
- `UIGlassRendererFeature` 在 URP 后处理之后复制半分辨率场景，所有控件共用。玻璃材质在控件区域进行七点模糊和轻微折射，支持 UGUI stencil、RectMask2D 与 CanvasGroup。未提供场景纹理时保留烟黑渐变底板。
- `UIPanel.OpenInternal` 为打开时生成的控件应用主题；动态控件工厂创建时调用 `BlackGoldTheme.Style`，选中状态调用 `BlackGoldTheme.Select`。进度条、准星、图片、输入遮罩不使用玻璃材质。
- Unity 菜单 `OperationBlacktide/UI/Apply Black Gold Design` 将主题写入已有面板 prefab 并安装三个画质级别的玻璃渲染功能。此操作保留 prefab GUID 与业务引用，可重复执行。
- 布局验证截图与结果位于 `.build-check/black-gold-ui`；验证入口为 `GTools/verify-black-gold-ui.ps1`，覆盖主按钮常态/悬停/按下/禁用、登录、装备导航、动态列表、训练选枪、Esc/弹窗与三类屏幕比例。
- 界面只保留功能标题、操作名称、装备/对局数据和必要状态反馈。不要添加装饰英文眉题、口号、重复副标题、常驻操作教程或无操作价值的“就绪/预览中”文案；键盘操作保持可用，但不在 HUD 底部常驻铺排。
- 训练菜单主页为左侧 520×408，选枪页为居中 680×760。HUD 弹药区只在换弹、充能、空弹匣或低弹药时显示简短状态，正常情况下不显示换弹教学。

视觉参考：[Deus Ex UI 设计师作品](https://www.benoitperreault.com/deusex)的黑金层次、[The Division 2 菜单截图](https://www.windowscentral.com/how-get-free-rainbow-six-siege-ela-division-2-skin-bundle)的场景上悬浮布局。仅参考信息层级与材质语言，不复制商用 UI 素材。

## UI 操作音效

- 使用本机 Counter-Strike 安装目录中的 CS2 UI 资源，按功能拆成 20 段短音效。大厅、选角、装备、生涯四个主 Tab 分别使用独立声音；普通点击、子 Tab、设置、开始、确认、返回、下拉展开/选项、开关开/关、装备选择/装备应用、角色选择、输入框、滑条、购买分别对应 `UISoundCue`。
- `UISoundPlayer` 常驻跨场景，预加载 `Resources/UI/Audio/catalog.json` 和 PCM WAV，使用四个 2D 声源，忽略游戏暂停且不随空间距离衰减。普通操作同类最短间隔 35ms，滑条 75ms；音量、资源长度和来源见 `Assets/Art/Combat/ui_audio_sources.json`。
- `UIPanel.OpenInternal` 在业务回调前绑定已有控件；`BlackGoldTheme.Style` 同时覆盖动态控件和已应用主题的控件；列表和 Dropdown 模板克隆会自行绑定。绑定可重复调用且不会叠加监听。Button 使用真正的 `onClick` 事件，关闭自身面板时仍能播完声音；Toggle、Dropdown、InputField 使用点击/提交事件，滑条只在用户拖动或键盘调整时播放。
- 初始化、代码同步数据、禁用控件、鼠标右键、按下后取消点击均不播放声音。无交互意义的背景遮罩不绑定；可点击的 Dismiss 使用返回声。Esc 关闭与打开菜单也有对应反馈。
- 新控件可以通过 `UISoundFeedback.Attach(control, UISoundCue.Confirm)` 或 Inspector 中的 Cue 显式覆盖自动分类；不再单独创建逐按钮 AudioSource。注册音效应早于按钮的业务回调。
- `ArtSource/CS2Imports/Scripts/prepare_ui_audio.py` 在 Blender Python 中提取选定原始资源、去除首尾静音、限制峰值和尾音，生成 WAV、清单和顺序试听。`GTools/verify-ui-audio.ps1` 在独立工程验证全部 UI prefab 控件、动态克隆、实际 UGUI 点击/提交、静默同步、关闭/切场景/暂停和声源池。

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
- `HandleBackInput`：Esc 先交给 Popup 栈顶的 `UIPanel.HandleBackInput()`，未消耗时 Pop Popup，再 Back Normal（Normal 栈深度 > 1 时）。设置面板通过此入口处理下拉框和未保存提示。

示例：

```text
Push(Login)   栈：[Login]           显示 Login
切到大厅场景   清理旧栈，再 Push(Lobby)，栈：[Lobby]
Esc           Normal：[Lobby]      Popup：[LobbyMenu]，打开大厅菜单
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

场景加载界面：

- 登录进入大厅、返回大厅、进入训练场/对局和返回登录共用 `SceneLoadingPanel`，注册为 `Loading`，位于 Overlay 层且不参与返回栈。面板常驻复用，独立 Canvas 排序 32000，覆盖场景 HUD；全屏拦截点击，加载期间禁用导航/提交和 Esc，关闭后恢复导航。
- 使用现有机库/沙 2 背景、下缘烟黑渐隐、亮金进度条和百分比。只显示目的地及实际加载状态；不增加教程、口号或装饰性副标题。菜单 `OperationBlacktide/UI/Build Scene Loading` 可重建 prefab。
- 开始异步加载前先绘制一帧遮罩；0–90% 直接跟随 `AsyncOperation.progress`，90% 激活场景，等待 `Start` 后进入 95% 的对局准备阶段。实现 `ISceneLoadParticipant` 的战斗客户端在收到首个有效状态或显示连接失败界面后释放遮罩；全部就绪才显示 100%。进度不随计时器虚增；网络等待受客户端连接超时和流程 20 秒兜底约束。
- 加载中账号断线时，遮罩持续覆盖并转入登录加载，避免闪出无会话的场景。不存在的场景在锁定输入前报错；异常结束释放遮罩。训练场直接在编辑器运行时返回登录也使用同一流程。
- `GTools/verify-scene-loading.ps1` 在独立精简工程运行真实 `LoadSceneAsync` 与测试场景，验证大厅/训练/对局/登录、重复点击、Esc/射线拦截、暂停、等待就绪、断线重定向和失败清理。结果与 16:9、16:10、超宽屏截图在 `.build-check/scene-loading-validation`，不改动主编辑器场景。

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

大厅菜单与显示设置：

- 大厅右上角“设置”直接打开 `SettingsPanel`；大厅按 Esc 打开 `LobbyMenuPanel`，包含设置、返回登录界面、退出游戏三个操作，再按 Esc 关闭菜单。模式下拉打开时，Esc 先收起下拉；打开菜单时保留当前大厅页签。
- 两个面板使用 Popup 层和统一遮罩。菜单中的设置打开同一个设置面板，返回后恢复菜单；大厅设置入口返回后直接回大厅。
- 设置页使用不透明的全屏底色，顶部标题、左侧分类、右侧独立分类内容区，底部固定返回和保存；当前只有“显示”分类。分类按钮与内容页通过数组绑定，后续扩展分类共用保存和未保存提示流程。各区域锚定屏幕边缘，适配 16:9、16:10、4:3 与超宽屏。
- 设置提供全屏开关、分辨率下拉、默认开启的“显示性能统计数据”开关和底部保存按钮；列表合并去重显示器支持的尺寸、常用尺寸及当前窗口尺寸。窗口模式允许拖动边框缩放，全屏使用无边框模式。
- 性能统计沿用草稿、保存、未保存退出提示流程，旧版本配置默认开启。`PerformanceStatsOverlay` 随 UIRoot 常驻，在登录、大厅、游戏场景右上角显示绿色文本，不拦截鼠标，每 0.25 秒更新一次，关闭后停止采样。显示 FPS、帧耗时、本进程 CPU 占整机处理能力的比例、CPU/GPU 帧耗时、进程驻留内存、托管 GC 内存、图形驱动内存分配、显卡型号和显存容量。编辑器中的进程数据对应编辑器。图形分配不代表整机显存占用，Unity 仅在编辑器/开发构建提供；平台不支持的数据明确显示“不可用”。开启 PlayerSettings 的 Frame Timing Stats 以支持正式构建的 GPU 帧耗时。
- 修改只写入草稿，点击保存才应用并写入本机 PlayerPrefs；下次启动恢复。无修改时保存按钮禁用，改回原值后取消未保存状态。启动器不再强制覆盖窗口模式和分辨率。
- 未保存时点击返回、按 Esc 或关闭客户端窗口，会出现“继续编辑 / 不保存 / 保存并离开”。提示作为设置面板内的子界面保留草稿，禁止操作底层控件；提示中按 Esc 等同于继续编辑。断线触发的场景清理仍可强制关闭面板。
- `BuildDisplaySettings.Build` 生成菜单、全屏设置页、性能统计三个 prefab，并绑定大厅入口；黑金布局构建器重建大厅时也保留设置按钮。验证入口 `GTools/verify-display-settings.ps1`，追加 `-BuildClient` 可构建客户端，结果和四种屏幕比例截图保存在 `.build-check/display-settings`。

`Features/Lobby/LobbyCharacterPreview` 挂在大厅场景对象上，仅处理中央区域拖动旋转模型，跳过 UI 点击；它不创建 Canvas 或管理 UI 生命周期。

训练场 UI：

- `TrainingPanel` 位于 Normal 层，显示 CS 风格 HUD：左下生命值/护甲，右下主武器剪影、弹匣与备弹，顶部训练计时，以及鼠标准星、换弹进度和低状态提示。
- HUD 绑定 `TrainingSceneController` 的当前角色、`CharacterVitals` 和 `WeaponController.Ammo`；事件通过 `AddEvent` 随面板关闭释放，重新进入训练场时绑定新的角色。
- 使用 `SafeArea` 容器和 CanvasScaler 适配分辨率。只有右上训练菜单按钮接收射线，血条、图标、准星不阻挡瞄准与开火。
- `BuildTrainingHud.Build` 生成 HUD prefab、模型剪影和训练菜单；`BuildTrainingMenu.Build` 可单独重建训练菜单，`BuildTrainingScene` 重建训练场时复用此入口。
- `TrainingMenuPanel` 位于 Popup 层，主页使用左侧列表，提供继续训练、切换武器、返回大厅。武器页居中按四类展示模型缩略图，点击即可装备，保持菜单打开便于选择。
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
