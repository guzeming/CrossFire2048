CT SAS 动画资源

Blender：../ctm_sas_animated.blend
FBX：ctm_sas_animated.fbx

共 53 个第三人称动作资源，30 FPS：
- 8 方向行走、8 方向跑步、8 方向蹲行。
- 站立和蹲伏待机姿势各 1 个。这两个源资源只有单帧，不是循环动画。
- 原地及 4 方向跳跃共 5 个。
- M4A4、M4A1-S 各 5 个：拔枪、蹲伏拔枪、换弹、蹲伏换弹、射击。
- 4 个拆弹动作，7 个死亡动作，1 个呼吸动作。
其中 51 个为动态片段，2 个为静态姿势。本批是常用步枪动作集，非游戏全部动画。

Blender 播放：
打开动画版后默认选中 CT_SAS_Rig，当前动作 run_n_rifle，播放范围 1–23。
鼠标移到 3D 视图按空格播放/暂停。
在底部 Action Editor（动作编辑器）的动作下拉列表切换片段。
较长片段需把播放结束帧改为片段结束帧；例如 reload_m4a4 为 1–93。
所有动作都已设为资产，可在 Asset Browser 中查看。

Unity：
FBX 包含第三人称身体、手套、拆弹工具包、94 骨骨架、蒙皮和 53 个动画 Take。
为保留完整骨骼数据，可先以 Generic Rig 导入并在 Animation 页检查片段。
Blender 回读 FBX 验证已通过；尚未在 Unity 编辑器内实际导入检查。
FBX 已嵌入基础纹理，独立 PNG 也在 CT_SAS 原导出目录中。
第一人称部件保留在 .blend 的隐藏集合中，不包含在此第三人称 FBX。

数据与限制：
源动作来自本地 CS2 的 animation/anims/world，详见 manifest.json。
已将通用动画骨架的 64 根同名骨映射到原 SAS 94 骨骨架并烘焙。
源与目标的根骨和武器骨绑定坐标差异已通过参考姿态变换补偿。
其余辅助骨保留绑定偏移并继承父骨运动。
保留源片段中的位移；当前跑步等循环动作本身为原地循环。
射击和换弹提供角色身体动作，此角色文件未附带枪械模型。
游戏运行时 IK、瞄准混合、布料/装备抖动和动画状态机没有迁移。

验证：
validation.json：重新打开 .blend，检查全部动作确实驱动网格；
重新导入 FBX，确认 53 个动作、94 根骨骼、3 个 Mesh，抽查跑步、蹲行、换弹、射击、死亡的实际变形。
animation_report.json：每个片段的源路径、帧数、时长与根骨位移。
Clips：保留源动作 glTF/BIN，便于进一步处理。
Logs：提取、导入、验证日志。

重建：
../../Scripts/export_character_animations.ps1 提取本批动作。
Blender 后台运行 ../../Scripts/build_character_animations.py -- --fbx 可重建动画版与 FBX。
重建会覆盖生成的动画版文件；手动编辑后请先另存。
