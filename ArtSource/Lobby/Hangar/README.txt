机库大厅：完整静态背景 + 实时角色

当前使用
  场景：Assets/Scenes/LobbyScene.unity
  背景：Assets/Art/Lobby/Hangar/Textures/HangarBackground_Baked.png
  提示词：HangarBackground_Baked.prompt.txt
  使用内置 image_gen，参考用户设计稿和项目现有黑潮行动机库背景。
  箱子、立柱、线缆、工作灯、地面标记与反光全部包含在同一张图片中。
  图中没有主角色或 UI；角色仍使用项目导入的 CT 模型、装备、动画和拖动交互。

Unity 组织
  LobbyEnvironment/DistantHangar_ImagePlate：显示完整背景的 Quad。
  LobbyEnvironment/CharacterShadowGround：不投射阴影的平面，只接收实时角色阴影。
    与背景使用相同纹理、曝光和屏幕投影裁切；不再额外叠加地面高光。
  LobbyEnvironment/HangarLighting：偏暖主光、中性补光、橙色轮廓光和脚边暖反射光。
  LobbyEnvironment/AirborneDust：少量动态尘粒。
  CT_SAS_Character：独立实时角色。
  UI：继续使用现有 LobbyPanel.prefab / UIManager。
  不再实例化 BlenderForeground，也没有实体箱子、立柱或灯圈。

更换背景
  同时替换 HangarPlate.mat 和 ProjectedGround.mat 的 BaseMap，保持一致。
  此方案用于固定大厅镜头，支持等比裁切；背景内的反光与远处烟雾是静态画面。
  角色可以旋转、播放动画、更换装备，脚下阴影实时更新。

重建
  Unity 菜单 OperationBlacktide/Lobby/Rebuild Hangar Environment。
  编辑器脚本：Assets/Scripts/GClient/Editor/BuildHangarLobby.cs。
  批处理入口 ApplyBakedBackground 可保留现有镜头与角色，只切换环境并调整灯光。
  RebuildPropLayout 旧入口也使用新的图片方案，不会重新添加实体箱子。

保留的原始资产
  HangarBackground.png 为无近景箱子的旧机库背景。
  HangarKit.blend、build_hangar_kit.py、asset-report.json 及 Assets/Art/Lobby/Hangar
    下的 8 类 Blender 模型和预制体保留作为素材库，当前大厅不使用这些实例。
  DesignReference.png 为原始设计参考。
