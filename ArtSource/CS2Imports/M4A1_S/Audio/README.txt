M4A1-S 动作与开火音效

交付：WAV 目录共 34 个独立音频文件，均已重新解码验证。
音频采样率保留为 44100 Hz，声道保留原单声道/双声道。
未重采样、未统一响度、未添加混响。
28 个原 WAV 直接复制；6 个源 MP3 解码为 32 位浮点 WAV。
浮点格式保留 MP3 解码时可能出现的超过 +/-1 的瞬时峰值，不裁切、不自动降音量。
Source 目录保留提取出的原 WAV、MP3 和声音描述文件。

常用文件：
消音开火：WAV/Fire/Suppressed/m4a1_silencer_01.wav
非消音开火：WAV/Fire/Unsuppressed/m4a1_01.wav 至 m4a1_04.wav
远距离开火：WAV/Fire/Distant
弹匣移出：WAV/Handling/Reload/m4a1_clipout.wav
弹匣插入：WAV/Handling/Reload/m4a1_clipin.wav
拍击弹匣：WAV/Handling/Reload/m4a1_cliphit.wav
装填：WAV/Handling/Reload/m4a1_addammo_01.wav
M4A1-S 枪机后拉/复位：WAV/Handling/Reload/m4a1_silencer_boltback.wav、m4a1_silencer_boltforward.wav
拔枪/装备：WAV/Handling/Draw/m4a1_draw.wav
持枪动作声：WAV/Handling/Movement
消音器安装、拆卸、旋拧：WAV/Handling/Silencer

目录统计：
Fire/Suppressed：1 个；Fire/Unsuppressed：4 个；Fire/Distant：4 个；Fire/Alternate：4 个。
Handling/Reload：8 个；Handling/Draw：1 个；Handling/Movement：3 个；Handling/Silencer：9 个。
Alternate 是同目录中的额外开火变体，本次提取的事件表未引用这些文件。
部分原音效由 M4A1 与 M4A4 共用，例如 m4a1_distant_01 在事件表中属于 Weapon_M4A4.SingleDistant。
请以 sound_events.json 中的事件映射为准，不要仅依赖文件名判断用途。

与动画的关系：
现有 Blender 文件中的角色/武器动作没有自动绑定这些声音。
已检查提取的第三人称动画事件轨：包含 IK 标识或粒子事件，没有声音事件触发时间。
animation_sound_map.json 提供按事件名称整理的候选对应关系，没有猜测或填写触发帧。
在 Unity Animation Event、Timeline 或武器逻辑中绑定 WAV 即可继续整合。
开火用 Weapon_M4A1.Silenced 或 Weapon_M4A1.Single，按消音器状态选择；远距离声音单独混音。
拔枪、换弹时按弹匣、枪机的实际运动选择对应音效，不需要把所有候选文件同时播放。
游戏中的距离衰减、音高随机、音量、遮挡与混响属于运行时音效系统，不包含在单个 WAV 中。
原事件的音量、音高等参数保存在 sound_events.json，尚未应用到导出音频。

清单：
sound_list.csv：中文用途、相对路径、时长、声道、位深和游戏事件名。
sound_manifest.json：每个音频的源资源、格式、校验值、解码检查。
sound_events.json：33 个相关游戏事件及其音频文件映射，包括共用资源。
animation_sound_map.json：动画与候选音效事件的对应关系。
validation.json：34 个文件可解码、非空、非静音、样本有限值，所有事件音频依赖均已导出。
Metadata：原音效事件定义及第三人称动画元数据。

所有路径以本目录为根。文件已在 Blender 音频解码器中验证，尚未实际导入 Unity 编辑器。
FBX 本身不含这批音频，需将 WAV 一起导入 Unity。
