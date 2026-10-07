M4A1-S（消音型）

独立武器：m4a1_s.blend
Unity 用 FBX：m4a1_s.fbx
CT 持枪与换弹预览：../CT_SAS/ctm_sas_m4a1_s.blend
动作与开火音效：Audio/WAV，共 34 个文件；用途见 Audio/sound_list.csv。
音效目前为独立文件，尚未绑定到 Blender 或 Unity 动画触发点。

默认显示游戏内 body_hd 高清版：37010 顶点，45372 三角面。
7 根骨骼：weapon、weapon_offset、bolt、clip、silencer、trigger、ag1_hand_r。
保留原网格、UV、法线、蒙皮和原尺寸贴图。
高清材质的颜色、法线、ORM 纹理均为 4096×4096，原 PNG 保存在本目录。
ORM 为 AO/粗糙度/金属度通道打包纹理，后续自定义 Unity Shader 可使用这些独立 PNG。
Blender 已打包所有贴图。材质是基础预览，没有复现原游戏着色器。
body_legacy 旧版保存在 .blend 的 Legacy_Alternative 隐藏集合中。
FBX 只包含高清版网格和骨架，避免新旧版本叠在一起。

5 个动作，30 FPS：
M4A1S_draw_m4a1s：1–35
M4A1S_draw_crouch_m4a1s：1–35
M4A1S_reload_m4a1s：1–93
M4A1S_reload_crouch_m4a1s：1–93
M4A1S_shoot_m4a1s：1–13
独立武器文件默认无活动动作，方便检查完整模型。
在动作编辑器给 M4A1S_Rig 选择动作即可播放武器部件运动。

CT 持枪场景：
角色活动动作为 reload_m4a1s，武器为 M4A1S_reload_m4a1s。
播放范围 1–93，按空格播放同步换弹。
通过角色的 wpn 骨挂接武器，已补偿通用动画骨架与模型的绑定坐标差异。
切换其他拔枪/换弹/射击动作时，需同时给角色和武器选择同名配套动作。
使用行走/跑步动作时，可清空武器的活动动作，让武器部件保持默认姿势。
未重建游戏运行时 IK、瞄准混合和动画状态机。

检查记录：
m4a1_s_report.json：网格、骨骼、贴图尺寸和动作清单。
validation.json：重开 .blend 和回读 FBX，验证骨骼、动作及弹匣网格运动。
FBX 回读确认 1 个高清 Mesh、7 根骨骼和 5 个动画 Take；尚未在 Unity 内实际导入检查。
m4a1_s_preview.png：独立武器预览。
ct_m4a1_s_preview.png：CT 持枪预览。
ct_m4a1_s_reload_check.png：换弹中间帧检查。

源文件：weapons/models/m4a1_silencer/weapon_rif_m4a1_silencer.vmdl_c
源动作：之前提取的 CT_SAS/Animations/Clips/*m4a1s.gltf，包含同步的角色及武器动画骨架。
生成脚本：../Scripts/import_m4a1_s.py
验证脚本：../Scripts/verify_m4a1_s.py
重建脚本会覆盖这里的生成结果，手动编辑后请先另存。
