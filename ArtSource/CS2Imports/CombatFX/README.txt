子弹与投掷物特效资源

打开 START_HERE.html，从 16 个子弹/弹壳模型和 11 个常用特效候选入口开始。
全部 1123 个粒子配置包含大量子效果、不同表面/武器、远近/质量及旧版备选，
无需在 Unity 中创建同等数量的独立 Prefab，也不会因为存放在 ArtSource 而自动进入游戏。

Models：FBX + Blender + glTF，保持现有角色/武器导出的单位与贴图。
Decompiled：VPCF、PNG、MKS、材质和快照等解码资源。
Metadata/sprite_sequences.json：按原始顺序保留序列帧、LOOP、RGB/Alpha 分组和时长数值。
Metadata/MaterialData：编译材质的原始参数 KV3；优先据此重建 Unity 材质。
Raw：从安装包提取且经过 CRC 验证的原始文件。

粒子配置不能直接作为 Unity ParticleSystem 使用。本次没有创建 Unity 特效 Prefab。
体积烟雾模拟、子弹打散烟雾、闪光致盲、燃烧范围和伤害属于后续重建的渲染/游戏逻辑。
贴图里包含颜色、法线、运动矢量和分离 Alpha，不能全部当作普通颜色贴图。
材质解码器对游戏着色器版本 72 有兼容限制；已另存原始材质参数与编译文件。
9 个旧版模型材质在主资源包和额外 65 个本机资源包中均未找到，详见缺失清单。
models/weapons/w_bullet.vmdl_c 是空资源，未伪造它的网格；提供其他 16 个实际弹药模型。

统计及验证结果：summary.json。
来源工具与格式说明：https://s2v.app/ValveResourceFormat/guides/format-support.html
