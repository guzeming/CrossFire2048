# 完全离线的大厅语音助手

Windows x64 第一版：Unity 麦克风 → sherpa-onnx 中文流式识别 → llama.cpp / Qwen2.5-1.5B-Instruct Q4_K_M → Windows 中文离线语音。

## 使用

大厅底部中央显示助手入口。按 **T** 或点击入口开始录音；说话时逐步显示字幕，停顿约 0.8 秒后由识别器判断结束。再次按 T 可以手动结束。助手思考或说话时按 T 会打断旧回复，并开始新一轮录音。每轮最多录音 30 秒；无输入会停止，不会生成虚假对话。

玩家声纹为青色，助手声纹为金色，来自实际音频的分段 RMS。思考时显示状态文字，不冒充正在说话。面板保留当前一轮的双方字幕，较长回复显示末尾并缩小字体。打开弹窗、窗口失焦、退出大厅会停止录音和播放；退出大厅同时释放推理进程和 GPU 资源。

模型在大厅打开时预热；加载完成后再接受语音。助手目前只聊天，不执行游戏操作。最近四轮上下文只存在进程内存中，关闭大厅清除。程序不保存用户录音、字幕或聊天记录，也没有云端 API 通路；模型服务仅绑定 127.0.0.1，使用每次启动随机的本机令牌。

## 首次安装 / 更新

需要 .NET 8 SDK（仅构建时）、Windows x64、已安装中文桌面语音。默认 Vulkan 后端可使用 NVIDIA/AMD/Intel 显卡；本机 RTX 4080 Laptop 已验证。

```powershell
powershell -ExecutionPolicy Bypass -File GTools/LocalAssistant/setup.ps1
```

首次安装需要联网下载约 1.2 GB 的模型与推理引擎，完成后对话完全离线。下载内容与自包含 .NET 运行库存放在项目根目录 `LocalAssistant/`，已排除 Git。重编译助手代码可使用 `setup.ps1 -SkipModels`，不重复下载。

Unity 中的界面 prefab 是 `Assets/Resources/UI/LobbyAssistant.prefab`，可直接在 Inspector 编辑。重建入口为 `OperationBlacktide/Lobby/Build Offline Voice Assistant`。大厅通过 `LobbyPanel.OnOpen` 挂载一次，不改动现有场景和选角布局。

打包分发时，将 `LocalAssistant/` 中的 `host/`、`llama/`、中文识别模型目录与 `dialogue.gguf` 放到游戏 exe 同级的 `LocalAssistant/`；不需要复制 `downloads/`。也可通过环境变量 `BLACKTIDE_ASSISTANT_HOME` 指定安装目录。用户机器需要安装中文离线语音，否则界面会明确提示。

## 验证

- `GTools/verify-lobby-assistant.ps1`：独立 Unity 工程编译、生成 prefab、检查字幕/状态/旧请求隔离/关闭清理，生成 16:9、16:10、超宽屏布局截图。布局截图使用已有大厅截图作为背景，不等同于完整大厅端到端录屏。
- `python Tests/Tools/verify_local_assistant.py`：用 Windows 生成的中文测试音频验证真实流式识别、本地模型推理、语音合成、静音拒绝、打断后新对话及进程退出。需要先安装模型；测试不打开麦克风。测试音频与报告仅写入 `.build-check/lobby-assistant/`。

“最后一个字到第一段回复语音”的延迟需要在目标机器和实际麦克风环境中测量；自动断句、模型输出长度、显卡负载与 Windows 语音启动均会影响结果，不承诺每次小于一秒。第一版系统语音的自然度有限，中文识别也可能出现同音字错误。

## 来源与许可证

- [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) 1.13.8，Apache-2.0；[中文小型流式识别模型](https://k2-fsa.github.io/sherpa/onnx/pretrained_models/online-ctc/zipformer-ctc-models.html)。
- [llama.cpp](https://github.com/ggml-org/llama.cpp) b11811，MIT；安装脚本固定版本并校验发布文件 SHA-256。
- [Qwen2.5-1.5B-Instruct-GGUF](https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF)，Apache-2.0；安装脚本校验官方模型文件 SHA-256。
- 中文语音使用用户已安装的 Windows 语音组件，不复制系统语音文件。

分发时应一并提供相关组件与模型许可证。本目录保留项目实现代码，下载目录保留上游包原有许可证。
