using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using SherpaOnnx;

namespace Blacktide.LocalAssistant;

internal sealed class Command
{
    public string type { get; set; } = "";
    public int turn { get; set; }
    public string pcm { get; set; } = "";
    public string text { get; set; } = "";
    public int sampleRate { get; set; } = 16000;
}

internal sealed class Assistant : IDisposable
{
    private readonly string root;
    private readonly object outputLock = new(), historyLock = new();
    private readonly List<object> history = new();
    private readonly HttpClient http = new(new HttpClientHandler { UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly SemaphoreSlim speechGate = new(1, 1);
    private OnlineRecognizer? recognizer;
    private OnlineStream? stream;
    private Process? modelProcess;
    private ProcessJob? modelJob;
    private CancellationTokenSource? responseCancellation;
    private Task responseTask = Task.CompletedTask;
    private int turn;
    private string partial = "", voice = "";
    private bool heardSpeech;
    private double inputSeconds, trailingQuiet;
    private const string SystemPrompt = "你是《黑潮行动》的大厅AI助手，名字叫小潮。用自然、简短的简体中文对话，每次一到两句，尽量不超过60个汉字。" +
        "可以闲聊，也可以介绍大厅里已有的选角、装备、训练场和爆破模式入口。你当前只能进行语音对话，不能替玩家点击界面、操作角色或查看实时对局；不要声称已完成这些操作。" +
        "不要编造游戏规则和未提供的信息。不输出Markdown、表情、舞台动作或思考过程。";

    public Assistant(string root) { this.root = root; }

    public void Send(string type, string text = "", int? id = null, string pcm = "", int sampleRate = 24000)
    {
        lock (outputLock)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { type, turn = id ?? turn, text, pcm, sampleRate }));
            Console.Out.Flush();
        }
    }

    public async Task Initialize()
    {
        Send("loading", "正在加载本地语音模型…", 0);
        string modelDir = Path.Combine(root, "sherpa-onnx-streaming-zipformer-small-ctc-zh-int8-2025-04-01");
        var config = new OnlineRecognizerConfig();
        config.FeatConfig.SampleRate = 16000;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.Zipformer2Ctc.Model = Path.Combine(modelDir, "model.int8.onnx");
        config.ModelConfig.Tokens = Path.Combine(modelDir, "tokens.txt");
        config.ModelConfig.NumThreads = 2;
        config.ModelConfig.Provider = "cpu";
        config.DecodingMethod = "greedy_search";
        config.EnableEndpoint = 1;
        config.Rule1MinTrailingSilence = 8;
        config.Rule2MinTrailingSilence = .8f;
        config.Rule3MinUtteranceLength = 30;
        if (!File.Exists(config.ModelConfig.Zipformer2Ctc.Model)) throw new FileNotFoundException("中文识别模型未安装，请运行 GTools/LocalAssistant/setup.ps1。");
        recognizer = new OnlineRecognizer(config);
        using (var warmup = recognizer.CreateStream())
        {
            warmup.AcceptWaveform(16000, new float[16000]);
            while (recognizer.IsReady(warmup)) recognizer.Decode(warmup);
        }
        using (var synth = new SpeechSynthesizer())
        {
            var installed = synth.GetInstalledVoices().Where(v => v.Enabled && v.VoiceInfo.Culture.Name.StartsWith("zh")).ToArray();
            voice = installed.FirstOrDefault(v => v.VoiceInfo.Name.Contains("Huihui"))?.VoiceInfo.Name ?? installed.FirstOrDefault()?.VoiceInfo.Name ?? "";
            if (voice.Length == 0) throw new InvalidOperationException("Windows 尚未安装中文离线语音，请在系统语言设置中安装中文语音。");
        }
        Send("loading", "正在加载本地对话模型…", 0);
        await StartModel();
        Send("ready", "按 T 开始对话", 0);
    }

    private async Task StartModel()
    {
        string executable = Directory.GetFiles(Path.Combine(root, "llama"), "llama-server.exe", SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new FileNotFoundException("本地对话引擎未安装，请运行 setup.ps1。");
        string model = Path.Combine(root, "dialogue.gguf");
        if (!File.Exists(model)) throw new FileNotFoundException("本地对话模型未安装，请运行 setup.ps1。");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        string secret = Guid.NewGuid().ToString("N");
        http.BaseAddress = new Uri($"http://127.0.0.1:{port}/");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
        foreach (string arg in new[] { "-m", model, "--host", "127.0.0.1", "--port", port.ToString(), "--api-key", secret, "-c", "2048", "-ngl", "99", "--parallel", "1", "--threads", "4", "--no-webui" }) start.ArgumentList.Add(arg);
        modelJob = new ProcessJob();
        modelProcess = Process.Start(start) ?? throw new InvalidOperationException("无法启动本地对话引擎。");
        modelJob.Attach(modelProcess);
        // Drain both pipes: diagnostics must never enter the JSON stdout protocol.
        modelProcess.OutputDataReceived += (_, _) => { };
        modelProcess.ErrorDataReceived += (_, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); };
        modelProcess.BeginOutputReadLine(); modelProcess.BeginErrorReadLine();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        while (!deadline.IsCancellationRequested)
        {
            if (modelProcess.HasExited) throw new InvalidOperationException("本地对话引擎启动失败，请检查显卡驱动或重新安装运行库。");
            try
            {
                using var result = await http.GetAsync("health", deadline.Token);
                if (result.IsSuccessStatusCode)
                {
                    // Compile GPU kernels and cache the system prompt before accepting microphone input.
                    using var warmup = new StringContent(JsonSerializer.Serialize(new
                    {
                        messages = new[] { new { role = "system", content = SystemPrompt }, new { role = "user", content = "你好" } },
                        max_tokens = 1, stream = false
                    }), Encoding.UTF8, "application/json");
                    using var warmed = await http.PostAsync("v1/chat/completions", warmup, deadline.Token);
                    warmed.EnsureSuccessStatusCode();
                    return;
                }
            }
            catch (HttpRequestException) { }
            await Task.Delay(200, deadline.Token);
        }
        throw new TimeoutException("本地模型加载超时。");
    }

    public void Handle(Command command)
    {
        if (command.type == "begin" || command.type == "text")
        {
            Cancel(); turn = command.turn; partial = ""; heardSpeech = false; inputSeconds = trailingQuiet = 0;
            if (command.type == "text") { StartReply(command.text.Trim()); return; }
            stream = recognizer!.CreateStream();
            Send("listening"); return;
        }
        if (command.type == "cancel") { Cancel(); turn = command.turn; return; }
        if (command.turn != turn || stream == null) return;
        if (command.type == "end") { EndInput(); return; }
        if (command.type != "audio") return;
        if (command.sampleRate < 8000 || command.sampleRate > 96000) throw new InvalidDataException("不支持的麦克风采样率。");
        byte[] bytes = Convert.FromBase64String(command.pcm);
        if (bytes.Length > 192000 || bytes.Length % 2 != 0) throw new InvalidDataException("无效的音频数据。");
        float[] samples = new float[bytes.Length / 2];
        double sum = 0;
        for (int i = 0; i < samples.Length; i++) { samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f; sum += samples[i] * samples[i]; }
        bool voiced = samples.Length > 0 && Math.Sqrt(sum / samples.Length) > .004;
        heardSpeech |= voiced;
        double duration = samples.Length / (double)command.sampleRate;
        inputSeconds += duration; trailingQuiet = voiced ? 0 : trailingQuiet + duration;
        stream.AcceptWaveform(command.sampleRate, samples);
        DecodePartial();
        // CTC blank runs can occur inside speech; require acoustic quiet too.
        if ((recognizer!.IsEndpoint(stream) && trailingQuiet >= .8) || inputSeconds >= 30) EndInput();
    }

    private void DecodePartial()
    {
        while (recognizer!.IsReady(stream!)) recognizer.Decode(stream!);
        // Byte-BPE partials can end halfway through a Unicode character.
        string text = recognizer.GetResult(stream!).Text.Replace("\uFFFD", "").Trim();
        if (text != partial) { partial = text; Send("partial", text); }
    }

    private void EndInput()
    {
        if (stream == null) return;
        stream.AcceptWaveform(16000, new float[8000]);
        stream.InputFinished(); DecodePartial();
        stream.Dispose(); stream = null;
        if (!heardSpeech || string.IsNullOrWhiteSpace(partial)) { Send("empty", "没听清，请按 T 再说一次"); return; }
        StartReply(partial);
    }

    private void StartReply(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) { Send("empty", "没听清，请按 T 再说一次"); return; }
        Send("final", text);
        responseCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = responseCancellation.Token; int responseTurn = turn;
        responseTask = Reply(text, responseTurn, token);
    }

    private async Task Reply(string text, int id, CancellationToken token)
    {
        var sentences = Channel.CreateBounded<string>(8);
        Task speech = SpeakSentences(sentences.Reader, id, token);
        var all = new StringBuilder(); var sentence = new StringBuilder();
        try
        {
            List<object> messages;
            lock (historyLock) messages = new List<object>(history);
            messages.Insert(0, new { role = "system", content = SystemPrompt });
            messages.Add(new { role = "user", content = text });
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { messages, stream = true, max_tokens = 160, temperature = .65, top_p = .85 }), Encoding.UTF8, "application/json")
            };
            using var result = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            result.EnsureSuccessStatusCode();
            using var reader = new StreamReader(await result.Content.ReadAsStreamAsync(token));
            while (await reader.ReadLineAsync(token) is { } line)
            {
                if (!line.StartsWith("data: ") || line == "data: [DONE]") continue;
                using var json = JsonDocument.Parse(line[6..]);
                var choices = json.RootElement.GetProperty("choices");
                if (choices.GetArrayLength() == 0 || !choices[0].GetProperty("delta").TryGetProperty("content", out var value)) continue;
                string delta = value.GetString() ?? "";
                all.Append(delta); sentence.Append(delta);
                Send("reply", all.ToString(), id);
                // Stream each short sentence into local speech synthesis while the model continues.
                int boundary;
                while ((boundary = SentenceBoundary(sentence)) >= 0)
                {
                    string segment = sentence.ToString(0, boundary + 1); sentence.Remove(0, boundary + 1);
                    await sentences.Writer.WriteAsync(segment, token);
                }
            }
            if (sentence.Length > 0) await sentences.Writer.WriteAsync(sentence.ToString(), token);
            sentences.Writer.TryComplete();
            await speech;
            token.ThrowIfCancellationRequested();
            if (all.Length == 0) throw new InvalidOperationException("本地模型没有返回内容，请重试。");
            lock (historyLock)
            {
                if (id == turn)
                {
                    history.Add(new { role = "user", content = text });
                    history.Add(new { role = "assistant", content = all.ToString() });
                    if (history.Count > 8) history.RemoveRange(0, history.Count - 8);
                }
            }
            Send("done", all.ToString(), id);
        }
        catch (OperationCanceledException)
        {
            if (id == turn) Send("error", "回复已超时，请按 T 重试", id);
        }
        catch (Exception error) { Console.Error.WriteLine(error); Send("error", "本地语音回复失败，请按 T 重试", id); }
        finally
        {
            sentences.Writer.TryComplete();
            try { await speech; } catch (OperationCanceledException) { } catch (Exception error) { Console.Error.WriteLine(error); }
        }
    }

    private static int SentenceBoundary(StringBuilder text)
    {
        for (int i = 0; i < text.Length; i++)
            if ("。！？!?\n；;".Contains(text[i]) || (i >= 12 && "，,".Contains(text[i])) || i >= 55) return i;
        return -1;
    }

    private async Task SpeakSentences(ChannelReader<string> sentences, int id, CancellationToken token)
    {
        await foreach (string text in sentences.ReadAllAsync(token))
        {
            await speechGate.WaitAsync(token);
            try
            {
                byte[] audio = await Task.Run(() => Synthesize(text, token), token);
                token.ThrowIfCancellationRequested();
                Send("audio", text, id, Convert.ToBase64String(audio));
            }
            finally { speechGate.Release(); }
        }
    }

    private byte[] Synthesize(string text, CancellationToken token)
    {
        using var synth = new SpeechSynthesizer();
        using var buffer = new MemoryStream();
        synth.SelectVoice(voice); synth.Rate = 1;
        synth.SetOutputToAudioStream(buffer, new SpeechAudioFormatInfo(24000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
        using var completed = new ManualResetEventSlim();
        Exception? failure = null;
        synth.SpeakCompleted += (_, e) => { failure = e.Error; completed.Set(); };
        synth.SpeakAsync(text);
        using var cancel = token.Register(synth.SpeakAsyncCancelAll);
        completed.Wait();
        token.ThrowIfCancellationRequested();
        if (failure != null) throw failure;
        return buffer.ToArray();
    }

    private void Cancel()
    {
        // Advance the identity before cancellation, so a superseded request cannot report a timeout.
        turn = -1;
        responseCancellation?.Cancel(); responseCancellation?.Dispose(); responseCancellation = null;
        stream?.Dispose(); stream = null;
    }

    public void Dispose()
    {
        Cancel();
        try { responseTask.Wait(2000); } catch { }
        recognizer?.Dispose(); http.Dispose();
        modelJob?.Dispose();
        try { if (modelProcess is { HasExited: false }) modelProcess.Kill(true); } catch { }
        modelProcess?.Dispose();
    }
}

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.InputEncoding = new UTF8Encoding(false); Console.OutputEncoding = new UTF8Encoding(false);
        string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
        using var assistant = new Assistant(root);
        try
        {
            // Start reading immediately: EOF from a closed game also stops initialization.
            var commands = Channel.CreateBounded<string>(100);
            _ = Task.Run(async () =>
            {
                while (await Console.In.ReadLineAsync() is { } line) await commands.Writer.WriteAsync(line);
                commands.Writer.TryComplete();
            });
            await assistant.Initialize();
            await foreach (string line in commands.Reader.ReadAllAsync())
            {
                try
                {
                    var command = JsonSerializer.Deserialize<Command>(line);
                    if (command == null) continue;
                    if (command.type == "shutdown") break;
                    assistant.Handle(command);
                }
                catch (Exception error) { Console.Error.WriteLine(error); assistant.Send("error", "语音处理失败，请按 T 重试"); }
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); assistant.Send("fatal", error.Message, 0); return 1; }
    }
}
