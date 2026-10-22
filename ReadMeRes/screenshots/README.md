# README 截图来源

[返回项目首页](../../README.md)

这些图片从本地输出原样复制，未重绘或补画游戏内容。文件由 Git LFS 管理；完整来源、文件时间与 SHA-256 见 [sources.json](sources.json)。文件时间为本机时区（Asia/Shanghai）的已有输出时间，不表示本次重新执行了游戏验证。不同日期的画面可能存在界面差异。

旧的运行时回归及性能脚本已在工作区清理，下表链接固定到历史提交，仅供追溯截图生成方式，不作为当前执行入口。助手布局图和部署测试图不能作为在线会话或真实匹配成功的证明。

| 图片 | 场景与限制 | 历史来源 |
| --- | --- | --- |
| [lobby.png](lobby.png) | 大厅实际场景，测试会话 | [GTools/verify-black-gold-ui.ps1](https://github.com/guzeming/CrossFire2048/tree/802ff75d3d6ca5da04dc149ee0bceeef1f359e21/GTools/verify-black-gold-ui.ps1) |
| [login.png](login.png) | 登录实际界面，表单已清空 | [GTools/verify-black-gold-ui.ps1](https://github.com/guzeming/CrossFire2048/tree/802ff75d3d6ca5da04dc149ee0bceeef1f359e21/GTools/verify-black-gold-ui.ps1) |
| [agents.png](agents.png) | 大厅选角实际界面，测试会话 | [GTools/verify-black-gold-ui.ps1](https://github.com/guzeming/CrossFire2048/tree/802ff75d3d6ca5da04dc149ee0bceeef1f359e21/GTools/verify-black-gold-ui.ps1) |
| [loadout.png](loadout.png) | 大厅装备实际界面，测试会话 | [GTools/verify-black-gold-ui.ps1](https://github.com/guzeming/CrossFire2048/tree/802ff75d3d6ca5da04dc149ee0bceeef1f359e21/GTools/verify-black-gold-ui.ps1) |
| [training.png](training.png) | Dust II 训练场与 HUD | [GTools/verify-black-gold-ui.ps1](https://github.com/guzeming/CrossFire2048/tree/802ff75d3d6ca5da04dc149ee0bceeef1f359e21/GTools/verify-black-gold-ui.ps1) |
| [first-person.png](first-person.png) | 训练场第一人称相机输出，不含 HUD | [GTools/verify-first-person.ps1](https://github.com/guzeming/CrossFire2048/tree/802ff75d3d6ca5da04dc149ee0bceeef1f359e21/GTools/verify-first-person.ps1) |
| [weapons.png](weapons.png) | 训练场切枪菜单 | [GTools/verify-black-gold-ui.ps1](https://github.com/guzeming/CrossFire2048/tree/802ff75d3d6ca5da04dc149ee0bceeef1f359e21/GTools/verify-black-gold-ui.ps1) |
| [smoke.png](smoke.png) | Dust II 投掷物测试相机输出 | [GTools/verify-throwables.ps1](https://github.com/guzeming/CrossFire2048/tree/802ff75d3d6ca5da04dc149ee0bceeef1f359e21/GTools/verify-throwables.ps1) |
| [deployment.png](deployment.png) | 实际部署面板，测试数据；未连接匹配服务 | [GTools/verify-black-gold-ui.ps1](https://github.com/guzeming/CrossFire2048/tree/802ff75d3d6ca5da04dc149ee0bceeef1f359e21/GTools/verify-black-gold-ui.ps1) |
| [demolition.png](demolition.png) | 开发版客户端跑测：一名脚本玩家与九名 AI，保留性能叠层 | [GTools/measure-demolition-performance.ps1](https://github.com/guzeming/CrossFire2048/tree/802ff75d3d6ca5da04dc149ee0bceeef1f359e21/GTools/measure-demolition-performance.ps1) |
| [settings.png](settings.png) | 显示设置面板验证，保留性能叠层 | [GTools/verify-display-settings.ps1](https://github.com/guzeming/CrossFire2048/tree/802ff75d3d6ca5da04dc149ee0bceeef1f359e21/GTools/verify-display-settings.ps1) |
| [assistant.png](assistant.png) | 助手 UI 布局验证：复用大厅截图背景，不是完整大厅端到端截图 | [GTools/verify-lobby-assistant.ps1](https://github.com/guzeming/CrossFire2048/tree/802ff75d3d6ca5da04dc149ee0bceeef1f359e21/GTools/verify-lobby-assistant.ps1) |
| [launcher.png](launcher.png) | 启动面板自身渲染输出，2026-10-22 刷新 | [GTools/Launcher 的 --render-preview](https://github.com/guzeming/CrossFire2048/tree/802ff75d3d6ca5da04dc149ee0bceeef1f359e21/GTools/Launcher) |
| [config-editor.jpg](config-editor.jpg) | 本地配置工作台页面截图 | [GTools/GameConfigEditor](https://github.com/guzeming/CrossFire2048/tree/802ff75d3d6ca5da04dc149ee0bceeef1f359e21/GTools/GameConfigEditor) |
| [server-log.png](server-log.png) | 实时日志工具截图；内容来自本地集成测试账号 | [GTools/LogViewer](https://github.com/guzeming/CrossFire2048/tree/802ff75d3d6ca5da04dc149ee0bceeef1f359e21/GTools/LogViewer) |

`.build-check/` 为忽略的本地目录，克隆不会恢复原始验证输出；README 使用此处已提交的图片副本。以后更新截图时，应使用当时的项目版本和明确开展的截图流程，记录来源并只提交选中的文档图片。不要上传整个验证目录或恢复已移除的旧脚本。
