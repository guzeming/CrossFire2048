# 爆破性能跑测

入口为 `GTools/measure-demolition-performance.ps1`，也可在游戏启动面板的“构建与验证”中选择“爆破性能跑测”。

```powershell
# 构建同一份源码的独立客户端和服务端，顺序执行四组诊断。
./GTools/measure-demolition-performance.ps1

# 源码未变时复用测试构建，重复三次基线。
./GTools/measure-demolition-performance.ps1 -ReuseBuild -Variants baseline -Repeats 3

# 每阶段 30 秒，1920×1080；保存额外 5 秒的 Unity Profiler 原始记录。
./GTools/measure-demolition-performance.ps1 -ReuseBuild -Variants baseline -SecondsPerPhase 30 -Width 1920 -Height 1080 -ProfilerTrace

# 第一人称本来就关闭视野蒙版和建筑淡化。
./GTools/measure-demolition-performance.ps1 -ReuseBuild -View firstperson -Variants baseline,balanced,no-glass
```

## 工作负载

- 真实 Dust II 地图、爆破服务器、一个脚本控制的客户端和九个 AI；保留生产代码的 UDP、预测校正、角色、HUD、射击表现与原始回合/伤害规则。直接签发临时入房票据，跳过登录和大厅。
- 固定种子 `performance-baseline-v1`；每组启动新的服务器和客户端，使用随机本地端口和独立临时密钥。
- 预热 12 秒，再依次执行静止观察、跟随队友实际轨迹并旋转视角、持续移动与射击三个阶段。每阶段默认采样 20 秒；截图和切换后的 3 秒不计入数据。
- 真实窗口、D3D11、Development Mono；默认 1600×900，关闭 VSync 和帧率上限。保持测试窗口未最小化，尽量关闭其他占用 GPU 的程序。进程失去焦点会在 CSV 中标记，但不会暂停游戏。
- 客户端可能被击杀，之后按真实逻辑观战。逐帧记录存活、回合、快照 tick、位置和累计开火事件；报告包含存活比例、移动距离、旋转角度和交战覆盖检查。未覆盖十人、实际移动、转视角或开火不会报告成功。
- AI 对局受执行时序影响，并非确定性录像。不同组应结合各阶段的存活比例和事件数量解释，不能仅凭整局平均 FPS 宣称某项优化的精确收益。

## 对照组

| 组 | 诊断改动 |
| --- | --- |
| baseline | 当前 High Fidelity 画质及正常玩法表现 |
| no-vision | 停用客户端 CharacterVision，定位视野蒙版与相应查询的成本 |
| no-cutaway | 停用 CameraCutaway；同时恢复原本的相机遮挡剔除设置，画面遮挡行为会变化 |
| balanced | 切换现有 Balanced 画质，多项渲染设置一起改变 |
| no-glass | 停用 UI 玻璃背景采样渲染功能 |

这些是定位用途的删减对照，不是可以直接发布的优化结果；尤其不能将 `no-vision` 当作正式玩法方案。

## 输出和读数

结果在 `.build-check/performance-<时间>-<随机标识>/`，不写回生产场景、资源或用户设置。构建缓存为 `.build-check/performance-players`；源码元数据指纹变化会拒绝 `-ReuseBuild`。

- `comparison.csv`：每组/每阶段的 FPS、平均值、P95、P99。
- `report.html`：可直接打开的表格报告和场景截图；失败的组会单独标记。
- `client-frames.csv`：逐帧 wall time、CPU/GPU frame timing、主/渲染线程、GC 分配字节、Draw Calls、三角形和定制代码区间。
- `client-summary.json`：硬件、图形 API、实际分辨率、计数器可用性及各阶段分布。FPS 按帧数/总耗时计算；百分位使用排序后的 nearest rank。
- `server-frames.csv`、`server-summary.json`：同机服务器的帧间隔与 `Blacktide.Server.Tick`。服务器限制在 60 Hz，帧间隔包含等待，应使用 Tick 区间判断模拟成本。
- `coverage.json`、三张 PNG：运行场景证据；`client.log` / `server.log` / `build.log`：故障定位。
- 可选 `client-profiler.raw`：在 Unity Profiler 中加载。录制在统计阶段之后单独进行，避免原始跟踪的开销影响基线。
- `client-available-markers.txt` / `server-available-markers.txt`：引擎实际提供的计数器名称、类别和单位；便于跨 Unity 版本排查缺失指标。

不可用的计数器、无效/重复的 GPU timing 样本记为 `-1`，不作为零耗时纳入均值。FrameTiming 可能滞后数帧，只用于阶段统计，不与同一 CSV 行的代码区间强行关联。主/渲染线程计时包含等待；定制区间是包含子调用的耗时，不能相加当成 CPU 总时间。每帧采集写入预分配内存，结束后一次性导出，避免逐帧磁盘 IO。

客户端和服务器运行在同一台机器上，结果包含资源争用；该流程衡量本机实际体验，不替代目标配置机器上的最终验收。默认不连接 Deep Profiling。

## 计时点

`Blacktide.Vision.Mask`、`Blacktide.Camera.Obstructions`、`Blacktide.Client.Snapshot`、`Blacktide.Client.Predict`、`Blacktide.Effects.Update`、`Blacktide.Decals.Update`、`Blacktide.Server.Tick`。

驱动源码仅由脚本复制到隔离工程，编译符号 `BLACKTIDE_PERFORMANCE` 控制测试代码，不随普通客户端和战斗服发布。运行器只清理自己启动的进程，遇到编译错误、断线、超时、覆盖不足会以失败退出。
