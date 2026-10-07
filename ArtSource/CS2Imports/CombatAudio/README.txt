角色、武器与装备音效导出

入口：START_HERE.html（双击，用浏览器打开；全部数据内嵌，离线可用）。
按角色 / 武器 / 类别筛选后点击试听。每次只播放一条音频。

本次共 6,592 个 WAV：4,456 条所选 10 个角色的语音、
2,136 条武器/角色通用/装备/物理依赖声音。
原有 1,237 条武器 WAV 已校验并归入本目录；新增 5,355 条。

WAV/：可导入 Unity 的 WAV；保留原采样率和声道，不调整响度。
Source/sounds/：从 VPK 解出的原始 WAV/MP3 等音频及 .vsnd 参数。
Raw/：原始编译资源，6,623 个资源通过 VPK CRC 校验。
manifest.json：每条音频的路径、时长、SHA256、采样率、转换方式。
Metadata/characters.json：10 个角色与语音包的对应，以及原始配置证据。
Metadata/weapons.json：51 个武器/装备模型与音效事件的语义候选对应。
Metadata/sound_events.json：5,608 个原始事件及完整参数、曲线、分轨和依赖。
Metadata/ResponseRules/：原始语音响应规则的结构化导出。
Metadata/voice_responses.json：无线电/战术响应组到事件及声音文件的索引。
Metadata/surface_footsteps.json：原游戏地表材质到 CT/T 脚步事件的对应。
Metadata/missing_resources.json：缺失源文件与受影响事件。

已核实的限制：
1. 62 条事件引用的源音频不在本地安装包中（61 条法国宪兵，
   1 条 professional_epic）；额外检索 65 个地图/附加包仍未找到。缺项明确保留，不替换、不伪造。
2. WAV 是单个声音样本。原游戏的分层、随机选择、空间衰减、混响和触发条件需要在 Unity 接入；
   参数已保存，但未自动变成 AudioSource/AudioMixer 或动画事件。事件依赖列表包含条件性子事件，
   不代表所有文件应同时播放。
3. 未指定动画音效帧号；未把这批文件复制到 Unity Assets，也未修改场景。
4. 原有武器库的可选刀具变体也保留，试听页可按基础武器模型筛选。
5. 压缩源解码为 float32 WAV，保留可能超过 1.0 的峰值，不削波、不再次有损编码。
   试听用于选样；浏览器不能还原游戏的完整混音环境。

角色语音对应：
CT  ctm_fbi -> fbihrt (244 条)
CT  ctm_gendarmerie_varianta -> gendarmerie_male (454 条)
CT  ctm_sas -> sas (494 条)
CT  ctm_st6_variante -> seal (426 条)
CT  ctm_swat_variante -> swat_epic (627 条)
T  tm_balkan_variantf -> balkan (388 条)
T  tm_jungle_raider_varianta -> jungle_male (498 条)
T  tm_leet_varianta -> leet (381 条)
T  tm_phoenix -> phoenix (360 条)
T  tm_professional_varf -> professional_epic (584 条)
