CS2 批量导出：5 CT + 5 T，全部基础武器和装备

快速入口
  characters_overview.blend：10 人角色总览，独立模型和骨架，按空格播放。
  characters_overview.png：角色预览图。
  catalog.csv：全部模型、骨骼数、动作数、Blender/FBX 路径。
  animations.csv：完整动画资源、对应动作库与骨架。
  Audio/sound_list.csv：武器相关音效清单。
  summary.json：最终数量和验证摘要。

角色（Characters）
CT：SAS、FBI、ST6、SWAT、Gendarmerie。
T：Phoenix、Leet、Balkan、Professional、Jungle Raider。
每个角色独立提供 .blend 和 .fbx，保留骨骼、蒙皮、UV、法线、原尺寸纹理及模型自带形态键。
每个角色附带 53 个常用动作资源（含 2 个静态姿势），默认选中跑步动作。
第一人称备选部件保存在 .blend 的隐藏集合中，第三人称 FBX 不重复导出这些部件。

武器与装备（Weapons）
共 51 项：全部基础枪械、CT/T 默认刀、六类手雷、C4、拆弹装备、医疗针、防弹衣、头盔及护甲套装。
各自提供 .blend 和 .fbx；有高清与旧版网格时默认显示高清版，旧版保留在隐藏集合中。
匹配的武器部件动作已烘焙到各自骨架；WM_ 为第三人称、VM_ 为第一人称。
武器文件中的动画控制枪机、弹匣、扳机等部件；角色/手臂动作在共享库中单独保留。
防具、拆弹器等没有匹配独立动作的资源保留模型和骨架；角色的拆弹动作在共享库中。
本批不包含饰品涂装、挂件、独立手套外观、StatTrak 模块或饰品刀造型。

配套部件（WeaponParts）
64 个独立部件，包括弹匣、子弹/弹壳、手雷保险销与握片、双枪枪套、燃烧瓶碎片。
这些部件另存为独立资源，便于制作换弹掉落、抛壳或破碎效果。

完整共享动画（Animations + RawAnimations）
2,141 个源动画/姿势资源，分成 147 个小型 Blender 动作库。
覆盖第三人称、第一人称、购买菜单、展示和入场等动作，包含单帧姿势与 additive 参考姿势合成版本。
Animations 中每个子目录包含 animations.blend、按骨架分出的 FBX，以及 report.json。
这些是纯骨架动作库；源片段中的角色和武器轨道分别保留。
RawAnimations 保留每个源片段的 glTF/BIN，可用来向选定角色或武器添加更多动作。
完整动作库按共享骨架存放；没有把 2,141 个资源重复烘焙进每个角色模型。

向模型追加一个共享动画
可用上一层 Scripts/apply_shared_animation.py；它按绑定姿态映射骨骼，另存到新文件。
PowerShell 示例（当前目录为 CrossFire2048 项目目录）：

& 'D:/SteamLibrary/steamapps/common/Blender/blender.exe' --background `
  'ArtSource/CS2Imports/Batch/Characters/tm_phoenix/tm_phoenix.blend' `
  --python 'ArtSource/CS2Imports/Scripts/apply_shared_animation.py' -- `
  --clip 'ArtSource/CS2Imports/Batch/RawAnimations/animation/anims/world/rifle/rifle_ak/reload_ak.gltf' `
  --output 'ArtSource/CS2Imports/Batch/Checks/my_phoenix_reload.blend'

输出路径须为新文件；多骨架场景可增加 --rig '骨架对象名' 指定目标。
已验证的示例在 Checks/tm_phoenix_reload_ak.blend。

音效（Audio）
1,237 个武器相关音效已整理为 WAV，保留源采样率与声道，未做响度归一化。
原 WAV 原样复制；MP3 解码为浮点 WAV，避免裁切解码峰值。
声音事件与文件映射见 Audio/sound_events.json；原始提取内容保存在 Audio/Source。
这些是独立音频，尚未自动绑定动画触发帧；没有烘焙游戏中的距离衰减、遮挡、混响和随机音高。

验证与使用边界
125 个模型成品均已回读 FBX，核对网格、骨骼和骨骼动作数量。
10 个角色都检查了实际网格随跑步动画变形；贴图已打包到 .blend。
共享动画库检查了源片段覆盖完整性，并抽查 35 个动画 FBX 的骨骼和动作数量。
音效全部通过解码检查，相关声音事件所引用的音频依赖已齐全。
这些文件尚未在 Unity 编辑器内逐一导入检查；骨骼转换可先按 Generic Rig 使用。
Source 2 的程序 IK、瞄准混合、装备抖动、物理效果和动画状态机没有转换。
材质为基础预览；保留的原尺寸纹理可供自定义 Shader 使用。
部分脸部形态键在 FBX 回读时生成额外动作数据块，清单中的动作数按骨骼动作统计。

RawModels / RawAnimations 是源提取缓存；成品清单以 catalog.csv 为准。
各模型目录的 report.json、动画库的 report.json 与 Logs 保存检查记录。
原游戏目录和 Unity Assets 目录未改动；原有 SAS、M4A1-S 和 Dust II 文件保留。
