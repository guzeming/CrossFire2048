CT SAS 大厅展示资产

来源：../ctm_sas_m4a1_s.blend（现有 CT_SAS 和 M4A1-S 持枪工程）。
生成脚本：../../Scripts/export_lobby_ct.py，由 Blender 后台执行。
原始 Blender 工程、完整动作库和独立武器文件保持原样。

CT_SAS_Lobby.fbx：第三人称身体、手套、拆弹工具包、M4A1-S，保留骨架和蒙皮。
大厅使用原始 idle_rifle 单帧持枪姿势，导出为两帧定姿片段；它不是呼吸动画。
Textures：此次展示所需的原尺寸颜色、法线和 ORM 纹理。
materials.json：源材质名称与纹理的对应关系。

Unity 成品：Assets/Art/Characters/CT_SAS/。
Prefabs/CT_SAS_Lobby.prefab：角色展示预制体；CT_SAS_Lobby.controller 播放持枪姿势。
URP Lit 使用颜色、法线和转换后的 UnityMask；原 ORM 通道为 R=AO、G=粗糙度、B=金属度，
UnityMask 为 R=金属度、G=AO、A=1-粗糙度。此为 URP 材质转换，并非 Source 2 原着色器。
