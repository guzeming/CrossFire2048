沙二 Unity 导入流程

项目资源：Assets/Art/Maps/DustII
独立场景：Assets/Scenes/DustII.unity
地图预制体：Assets/Art/Maps/DustII/Prefabs/DustII.prefab

登录后在大厅模式菜单点击“训练场”，直接进入 DustII。
场景会生成大厅所选阵营的角色和主步枪，使用俯视跟随相机。
WASD 移动，Shift 疾跑，空格跳跃，鼠标控制朝向，滚轮缩放。
角色使用 CharacterController，支持地面/墙体碰撞、重力和跌落后重生。
TrainingCharacterAnimator 使用实际碰撞后的速度驱动八方向步行/跑步和跳跃动画。
动作方向相对于角色朝向计算，支持倒退和横移；停止、顶墙或打开弹窗时回到待机。
待机使用 idle_rifle 战斗瞄准姿势；移动和跳跃保留完整躯干/手臂补偿，
让枪口保持朝向人物正前方。角色位移由控制器统一负责。
按 Esc 或点击“返回大厅”打开确认框；继续训练或再次按 Esc 关闭弹窗，
确认后保留登录会话返回大厅。弹窗打开时禁用移动、转向和缩放输入。
编辑器直接运行 DustII 也会生成默认角色；此时无登录会话，退出时进入登录页。

资源保留原始地图尺寸（米）、3599 个源 Mesh、UV、法线和原分辨率 PNG。
地图分为 9 个 FBX 文件；14 个原引擎遮光辅助网格未导入可见地图。
地图预制体禁用其中 45 个烟尘、蒸汽和光束特效面片，实际显示 3554 个 Mesh。
这些面片使用 materials/effects/smoke/ 下的 Source 2 专用特效材质；普通 URP/Lit
无法还原其遮罩、深度羽化和透明效果，会变成棕色实体薄片。禁用对象同时移除其
碰撞、阴影和射线遮挡；地面、建筑、车辆、门窗及其碰撞保持不变。
材质转换为 URP/Lit，ORM 转换为 Unity 金属度/光滑度/AO 通道。
Source 2 的层混合、专用着色器、植被动画和原始烘焙光照没有转换。
不透明实体网格附加了非凸 MeshCollider；贴花、透明效果和植被不参与该
基础碰撞。未还原原游戏的玩家裁剪体、触发器、导航或玩法逻辑。

生成脚本位于 GTools/DustII 和 ArtSource/CS2Imports/Scripts。
重建顺序：
1. 用 Blender 后台运行 ArtSource/CS2Imports/Scripts/export_dustii_unity.py。
2. 用带 Pillow 的 Python 运行 GTools/DustII/prepare_import.py。
3. Unity 2022.3.47f1c1 后台打开 .build-check/dustii-import，执行 BuildDustII.Run。
4. 查看 Validation/import_report.json、DustII_Unity_Overview.png 和 import.log。
5. 验证成功后运行 GTools/DustII/publish_import.ps1，将资源连同 .meta 复制到项目。
6. 重建地图后执行 Unity 菜单 OperationBlacktide/Training/Build Dust II Training，
   恢复训练场出生点、角色预制体、相机控制器和训练 UI。

BuildDustII 在生成预制体及碰撞前自动执行 DustIIEffectCleanup.Apply，避免重新导入
后又出现特效薄片。仅修复旧地图时，在暂存项目执行 DustIIEffectCleanup.Run，查看
Validation/effect_cleanup_report.json 和 effects_*_before/after.png，验证后仅复制
Prefabs/DustII.prefab（保留 .meta），不覆盖当前训练场场景或其他资源。

导入工作使用独立暂存项目。已有大厅、登录场景和 Build Settings 不由此流程修改。
训练场验证：GTools/verify-training.ps1，在独立副本验证真实大厅入口、角色移动、
疾跑、墙体碰撞、跳跃与重力、鼠标朝向、确认弹窗、返回大厅及再次进入。

移动动画资源：Assets/Art/Training/RifleLocomotion.fbx（21 个运动片段及 1 个战斗待机姿势，复用本地已导出的 CS2 动作）。
动画控制器：Assets/Resources/Training/TrainingLocomotion.controller。
重建动画：Blender 后台执行 ArtSource/CS2Imports/Scripts/export_training_locomotion.py，
传入 --output Assets/Art/Training，再执行 Unity 菜单
OperationBlacktide/Training/Build Character Animations。
验证同时检查全部 22 个片段在 10 个角色上的骨骼绑定、实际播放、落地回待机和方向切换。
枪口回归覆盖 M4A4、M4A1-S、AK-47 的实际枪身方向，检查转身、行走/跑步混合及跳跃。
