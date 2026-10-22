using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace OperationBlacktide.Launcher;

public partial class MainWindow : Window
{
    private readonly ProjectPaths paths;
    private readonly Commands commands;
    private readonly string preview;
    private readonly DispatcherTimer timer;
    private readonly ConcurrentQueue<string> incoming = new();
    private readonly Queue<string> lines = new();
    private readonly StreamWriter transcript;
    private readonly object logLock = new();
    private bool busy, refreshing, closing;
    private int refreshCounter;
    private string unityPath;
    private ServiceStatus? services;
    private HelpWindow? helpWindow;
    private CancellationTokenSource? jobCancellation;
    private readonly Stopwatch jobClock = new();
    private string jobName = "";
    private readonly TaskProgressTracker jobProgress = new();
    private bool jobCanCancel;
    private long lastOutputTicks;
    public bool VerificationFailed { get; private set; }
    private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(103, 217, 167));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(152, 170, 184));
    private static readonly Brush Orange = new SolidColorBrush(Color.FromRgb(255, 175, 84));
    private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(239, 128, 128));

    public MainWindow(ProjectPaths paths, string preview = "")
    {
        this.paths = paths; this.preview = preview; commands = new Commands(paths); unityPath = paths.LoadUnityPath();
        string logFolder = paths.At("Logs/Launcher"); Directory.CreateDirectory(logFolder);
        transcript = new StreamWriter(Path.Combine(logFolder, "launcher-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Environment.ProcessId + ".log"), false, new UTF8Encoding(false)) { AutoFlush = true };
        InitializeComponent();
        if (preview.Length == 0)
        {
            var area = SystemParameters.WorkArea;
            MinWidth = Math.Min(MinWidth, area.Width); MinHeight = Math.Min(MinHeight, area.Height);
            Width = Math.Max(MinWidth, Math.Min(Width, area.Width - 24));
            Height = Math.Max(MinHeight, Math.Min(Height, area.Height - 24));
        }
        ProjectLocation.Text = paths.Root; UnityPathBox.Text = unityPath; UnityVersionLabel.Text = paths.UnityVersion;
        ToolsList.ItemsSource = ToolCatalog.All; ToolsList.SelectedIndex = 0;
        Log("启动面板已就绪。可启动游戏、管理本地服务，或运行项目工具。");
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += async (_, _) => { FlushOutput(); UpdateJobProgress(); if (++refreshCounter % 8 == 0) await RefreshStatus(); };
        timer.Start();
        Loaded += OnLoaded;
        Closing += (_, e) =>
        {
            if (busy) { e.Cancel = true; WindowState = WindowState.Minimized; Log("任务仍在执行，面板已最小化。完成后可正常关闭。"); return; }
            closing = true; timer.Stop(); FlushOutput(); lock (logLock) transcript.Dispose();
        };
        if (preview.Length > 0) { WindowStartupLocation = WindowStartupLocation.Manual; Left = -30000; Top = -30000; ShowInTaskbar = false; }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await RefreshStatus(); FlushOutput();
        if (preview.Length == 0) return;
        try
        {
            await Dispatcher.Yield(DispatcherPriority.Render); Capture(preview);
            Pages.SelectedIndex = 1; ToolSearch.Text = "投掷";
            if (ToolsList.Items.Count != 2) throw new Exception("Tool search did not find both throwable-related tools.");
            ToolSearch.Text = ""; ToolsList.SelectedIndex = 0;
            if (ToolsList.Items.Count != ToolCatalog.All.Count) throw new Exception("Tool list did not reset after search.");
            await Dispatcher.Yield(DispatcherPriority.Render);
            Capture(Path.Combine(Path.GetDirectoryName(preview)!, Path.GetFileNameWithoutExtension(preview) + "-tools.png"));
            Console.WriteLine("LAUNCHER_UI_VERIFY_PASS " + ToolCatalog.All.Count + " tools");
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); VerificationFailed = true; }
        Close();
    }

    private void Capture(string file)
    {
        UpdateLayout();
        var surface = (FrameworkElement)Content;
        var bitmap = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        using var stream = File.Create(file); encoder.Save(stream);
    }

    private void Log(string message)
    {
        if (closing) return;
        jobProgress.Consume(message);
        Interlocked.Exchange(ref lastOutputTicks, DateTime.UtcNow.Ticks);
        string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message;
        incoming.Enqueue(line);
        lock (logLock) { if (!closing) transcript.WriteLine(line); }
    }
    private void FlushOutput()
    {
        bool changed = false;
        while (incoming.TryDequeue(out var line)) { lines.Enqueue(line); changed = true; if (lines.Count > 1200) lines.Dequeue(); }
        if (!changed) return;
        Output.Text = string.Join(Environment.NewLine, lines);
        if (AutoScroll.IsChecked == true) Output.ScrollToEnd();
    }
    private async Task RefreshStatus()
    {
        if (refreshing || closing) return;
        refreshing = true;
        try
        {
            services = await Task.Run(() => StatusReader.Read(paths));
            if (closing) return;
            ClientStatus.Text = File.Exists(paths.Client) ? services.Clients > 0 ? $"运行中 · {services.Clients} 个" : "已构建" : "尚未构建";
            ClientStatus.Foreground = File.Exists(paths.Client) ? Green : Muted;
            ClientDetail.Text = File.Exists(paths.Client) ? "Windows · 可多开" : "首次启动将自动构建";
            AccountStatus.Text = services.AccountLabel; AccountStatus.Foreground = services.TcpListening ? services.AccountManaged ? Green : Orange : Muted;
            BattleStatus.Text = services.BattleLabel; BattleStatus.Foreground = services.UdpListening ? services.BattleManaged ? Green : Orange : Muted;
            UnityStatus.Text = File.Exists(unityPath) ? "可用" : "需要设置路径"; UnityStatus.Foreground = File.Exists(unityPath) ? Green : Orange;
            EnvironmentNote.Text = services.HasForeignPort
                ? "检测到端口已被其他启动方式占用。请先关闭旧服务，再用本面板启动；面板只停止有管理记录的进程。"
                : "联机入口：大厅 → 联机测试场。关闭面板后，已启动的游戏和服务器会继续运行。";
            UpdateButtons();
        }
        catch (Exception ex) { Footer.Text = "状态暂不可用：" + ex.Message; }
        finally { refreshing = false; }
    }
    private void UpdateButtons()
    {
        PlayButton.IsEnabled = PlayTogetherButton.IsEnabled = SecondClientButton.IsEnabled = RunToolButton.IsEnabled = !busy;
        StartServerButton.IsEnabled = !busy && services?.Ready != true;
        StopServerButton.IsEnabled = RestartServerButton.IsEnabled = !busy && services?.Managed == true;
        ToolsList.IsEnabled = ToolSearch.IsEnabled = !busy;
        CancelTaskButton.Visibility = busy && jobCanCancel ? Visibility.Visible : Visibility.Collapsed;
        CancelTaskButton.IsEnabled = busy && jobCancellation?.IsCancellationRequested == false;
    }
    private void UpdateJobProgress()
    {
        if (!busy) return;
        bool cancelling = jobCancellation?.IsCancellationRequested == true;
        TaskStatus.Text = $"{(cancelling ? "正在取消" : "正在执行")} · {jobName} · {JobElapsed()}";
        int quiet = (int)TimeSpan.FromTicks(Math.Max(0, DateTime.UtcNow.Ticks - Interlocked.Read(ref lastOutputTicks))).TotalSeconds;
        var progress = jobProgress.Read();
        string stage = progress.Stage;
        TaskProgress.Value = progress.Percent;
        TaskProgressPercent.Text = $"阶段进度 {progress.Percent}%";
        if (cancelling) TaskProgress.Foreground = Muted;
        else TaskProgress.ClearValue(Control.ForegroundProperty);
        TaskProgressPercent.Foreground = cancelling ? Muted : Green;
        TaskProgressDetail.Text = cancelling ? "正在停止当前工具及其子进程，请稍候…"
            : stage.Length > 0 ? "当前阶段：" + stage : "正在执行，请稍候…";
        if (!cancelling && quiet >= 15) TaskProgressDetail.Text += $" · {quiet} 秒未收到新输出";
        Footer.Text = quiet >= 15 ? $"任务尚未结束 · {quiet} 秒未收到新输出 · 可查看日志或取消任务" : stage.Length > 0 ? "当前阶段：" + stage : "正在启动工具…";
        Footer.ToolTip = Footer.Text;
    }
    private string JobElapsed()
    {
        var elapsed = jobClock.Elapsed;
        return $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
    }
    private void FinishJobProgress(string status, Brush color, bool succeeded, string detail)
    {
        jobClock.Stop();
        jobProgress.Finish(succeeded);
        TaskStatus.Text = $"{status} · {jobName} · {JobElapsed()}";
        TaskStatus.Foreground = color;
        if (succeeded) TaskProgress.ClearValue(Control.ForegroundProperty);
        else TaskProgress.Foreground = color;
        TaskProgress.IsIndeterminate = false;
        TaskProgress.Value = jobProgress.Read().Percent;
        TaskProgressPercent.Text = $"阶段进度 {TaskProgress.Value:0}%";
        TaskProgressPercent.Foreground = color;
        TaskProgressDetail.Text = detail + " · 用时 " + JobElapsed();
    }
    private async void CancelTask(object sender, RoutedEventArgs e)
    {
        var cancellation = jobCancellation;
        if (!busy || !jobCanCancel || cancellation == null || cancellation.IsCancellationRequested) return;
        CancelTaskButton.IsEnabled = false;
        Log("正在取消当前工具及其子进程，请稍候…");
        try { await cancellation.CancelAsync(); }
        catch (ObjectDisposedException) { }
    }
    private async Task Job(string name, Func<Task> task, bool canCancel = false)
    {
        if (busy) { Log("当前任务仍在执行，请完成后再运行：" + name); return; }
        using var cancellation = new CancellationTokenSource(); jobCancellation = cancellation;
        jobName = name; jobProgress.Reset(); jobCanCancel = canCancel; jobClock.Restart();
        busy = true; TaskStatus.Foreground = Orange;
        TaskProgressPanel.Visibility = Visibility.Visible; TaskProgress.Value = 0; TaskProgress.IsIndeterminate = false;
        TaskProgressPercent.Foreground = Green;
        UpdateButtons(); Log("开始：" + name); UpdateJobProgress();
        try
        {
            await task(); cancellation.Token.ThrowIfCancellationRequested();
            FinishJobProgress("完成", Green, true, "任务已完成");
            Log("完成：" + name + $"（用时 {jobClock.Elapsed.TotalSeconds:F1} 秒）");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { FinishJobProgress("已取消", Muted, false, "任务已取消"); Log("已取消：" + name); }
        catch (Exception ex) { FinishJobProgress("未完成", Red, false, "任务失败，请查看下方日志"); Log("失败：" + ex.Message); }
        finally { busy = false; jobClock.Stop(); jobCancellation = null; Footer.Text = "本机开发服务 · 127.0.0.1"; Footer.ToolTip = null; UpdateButtons(); FlushOutput(); await RefreshStatus(); }
    }
    private async Task Execute(Command command, int progressStart = 0, int progressEnd = 100)
    {
        jobProgress.BeginStep(progressStart, progressEnd);
        int exit = await ProcessRunner.RunAsync(command, Log, jobCancellation?.Token ?? default);
        if (exit != 0) throw new InvalidOperationException("工具退出码 " + exit + "。请查看上方输出；完整记录已保存到 Logs/Launcher。");
        jobProgress.CompleteStep();
    }
    private string RequireUnity()
    {
        if (!File.Exists(unityPath)) { Pages.SelectedIndex = 2; throw new FileNotFoundException("未找到 Unity 编辑器，请在“路径与目录”中选择 Unity.exe。"); }
        return unityPath;
    }
    private async Task EnsureClient(int progressStart = 0, int progressEnd = 100)
    {
        if (File.Exists(paths.Client)) return;
        Log("客户端尚未构建，先构建 Windows 客户端。首次可能需要几分钟。");
        await Execute(commands.Script("build-battle.ps1", RequireUnity(), "Client"), progressStart, progressEnd);
        if (!File.Exists(paths.Client)) throw new FileNotFoundException("构建完成后仍未找到游戏客户端。");
    }
    private async Task EnsureServers(int progressStart = 0, int progressEnd = 100)
    {
        var state = await Task.Run(() => StatusReader.Read(paths));
        if (state.Ready) { Log("账号服与战斗服均已运行。"); return; }
        if (state.HasForeignPort) throw new InvalidOperationException("7777 或 7778 端口已被其他服务占用，请先关闭旧服务。未改动占用端口的进程。");
        if (state.Managed)
        {
            Log("服务状态不完整，先正常停止已有托管服务。");
            int stopped = progressStart + (progressEnd - progressStart) / 5;
            await Execute(commands.Script("stop-local-server.ps1"), progressStart, stopped);
            progressStart = stopped;
        }
        if (!File.Exists(paths.BattleServer)) RequireUnity();
        await Execute(commands.Script("start-local-server.ps1", unityPath, noViewer: true), progressStart, progressEnd);
    }
    private void LaunchClient()
    {
        using var process = ProcessRunner.Open(paths.Client, paths.Root);
        Log("游戏客户端已启动，进程 " + process.Id + "。联机测试请使用不同账号。");
    }
    private async void Play(object sender, RoutedEventArgs e) => await Job("启动游戏", async () => { await EnsureClient(); LaunchClient(); });
    private async void PlayTogether(object sender, RoutedEventArgs e) => await Job("开服并启动游戏", async () => { await EnsureClient(0, 50); await EnsureServers(50, 100); LaunchClient(); });
    private async void StartServers(object sender, RoutedEventArgs e) => await Job("启动服务器", () => EnsureServers());
    private async void StopServers(object sender, RoutedEventArgs e) => await Job("停止服务器", () => Execute(commands.Script("stop-local-server.ps1")));
    private async void RestartServers(object sender, RoutedEventArgs e) => await Job("重启服务器", async () =>
    { await Execute(commands.Script("stop-local-server.ps1"), 0, 20); await EnsureServers(20, 100); });

    private async void OpenLogs(object sender, RoutedEventArgs e)
    {
        try
        {
            if (StatusReader.FocusExisting(paths.Viewer)) { Log("已切换到服务器日志窗口。"); return; }
            if (!File.Exists(paths.Viewer)) await Job("构建日志工具", () => Execute(commands.Build("GTools/LogViewer")));
            if (!File.Exists(paths.Viewer)) return;
            Directory.CreateDirectory(paths.At("Logs/Server"));
            using var process = ProcessRunner.Open(paths.Viewer, paths.Root, "--logs", paths.At("Logs/Server"));
            Log("已打开实时日志工具：Logs/Server。");
        }
        catch (Exception ex) { Log("日志工具打开失败：" + ex.Message); }
    }
    private void OpenUnity(object sender, RoutedEventArgs e)
    {
        try
        {
            string unity = RequireUnity(), name = new DirectoryInfo(paths.Root).Name;
            foreach (var process in Process.GetProcessesByName("Unity"))
            {
                using (process) if (process.MainWindowHandle != IntPtr.Zero && process.MainWindowTitle.Contains(" - " + name + " - ", StringComparison.OrdinalIgnoreCase))
                { WindowActivation.Focus(process.MainWindowHandle); Log("已切换到 Unity 工程窗口。"); return; }
            }
            using var opened = ProcessRunner.Open(unity, paths.Root, "-projectPath", paths.Root);
            Log("正在打开 Unity 工程。");
        }
        catch (Exception ex) { Log("Unity 打开失败：" + ex.Message); }
    }
    private async void RunTool(object sender, RoutedEventArgs e)
    {
        if (ToolsList.SelectedItem is not ToolItem tool) return;
        await Job(tool.Name, async () =>
        {
            string? unity = tool.UsesUnity ? RequireUnity() : null;
            if (tool.Kind == "构建")
            {
                var state = await Task.Run(() => StatusReader.Read(paths));
                if (tool.Argument == "Client" && state.Clients > 0) throw new InvalidOperationException("请先关闭正在运行的游戏客户端，再构建客户端。");
                if ((tool.Argument is "Server" or "Dedicated" || tool.File == "GServer/OperationBlacktide.Server") && state.Managed)
                    throw new InvalidOperationException("请先停止本地服务器，再构建服务端。");
            }
            var steps = new List<Command>();
            switch (tool.Argument)
            {
                case "dotnet": steps.Add(commands.Build(tool.File)); break;
                case "integration":
                    if (!File.Exists(paths.BattleServer)) steps.Add(commands.Script("build-battle.ps1", RequireUnity(), "Server"));
                    steps.Add(commands.Build("GServer/OperationBlacktide.Server"));
                    steps.Add(commands.Dotnet("run", "--project", paths.At(tool.File), "-c", "Release", "--", paths.Root)); break;
                case "self-test":
                    if (!File.Exists(paths.Viewer)) steps.Add(commands.Build("GTools/LogViewer"));
                    steps.Add(new Command(paths.Viewer, paths.Root, new[] { "--self-test" })); break;
                default:
                    if (tool.File == "verify-battle-client.ps1")
                    {
                        if (!File.Exists(paths.BattleServer)) steps.Add(commands.Script("build-battle.ps1", RequireUnity(), "Server"));
                        if (!File.Exists(paths.At("GServer/OperationBlacktide.Server/bin/Release/net8.0/OperationBlacktide.Server.dll")))
                            steps.Add(commands.Build("GServer/OperationBlacktide.Server"));
                    }
                    steps.Add(commands.Script(tool.File, unity, tool.Argument.Length > 0 ? tool.Argument : null)); break;
            }
            for (int i = 0; i < steps.Count; i++) await Execute(steps[i], i * 100 / steps.Count, (i + 1) * 100 / steps.Count);
        }, canCancel: true);
    }
    private void FilterTools(object sender, TextChangedEventArgs e)
    {
        if (ToolsList == null) return;
        string query = ToolSearch.Text.Trim();
        ToolsList.ItemsSource = ToolCatalog.All.Where(t => (t.Name + t.Description + t.Kind).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (ToolsList.Items.Count > 0) ToolsList.SelectedIndex = 0;
    }
    private void SelectTool(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedToolName == null) return;
        var tool = ToolsList.SelectedItem as ToolItem;
        SelectedToolName.Text = tool?.Name ?? "没有匹配的工具"; SelectedToolDetail.Text = tool?.Description ?? "尝试搜索其他关键词。";
    }
    private async void BrowseUnity(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Unity 编辑器|Unity.exe", Title = "选择项目使用的 Unity 编辑器" };
        if (File.Exists(unityPath)) dialog.InitialDirectory = Path.GetDirectoryName(unityPath);
        if (dialog.ShowDialog(this) != true) return;
        try { unityPath = dialog.FileName; paths.SaveUnityPath(unityPath); UnityPathBox.Text = unityPath; Log("Unity 路径已保存。"); await RefreshStatus(); }
        catch (Exception ex) { Log("设置保存失败：" + ex.Message); }
    }
    private void OpenPath(string relative, bool directory = true)
    {
        try
        {
            string path = paths.At(relative);
            if (directory) Directory.CreateDirectory(path);
            if (!directory && !File.Exists(path)) throw new FileNotFoundException("文件不存在：" + path);
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { Log("打开失败：" + ex.Message); }
    }
    private void OpenProject(object sender, RoutedEventArgs e) => OpenPath(".");
    private void OpenReadme(object sender, RoutedEventArgs e)
    {
        if (helpWindow != null)
        {
            if (helpWindow.WindowState == WindowState.Minimized) helpWindow.WindowState = WindowState.Normal;
            helpWindow.Activate();
            return;
        }
        helpWindow = new HelpWindow(this);
        helpWindow.Closed += (_, _) => helpWindow = null;
        helpWindow.Show();
    }
    private void OpenServerPlan(object sender, RoutedEventArgs e) => OpenPath("spec/server-authority-plan.md", false);
    private void OpenServerLogFolder(object sender, RoutedEventArgs e) => OpenPath("Logs/Server");
    private void OpenAllLogs(object sender, RoutedEventArgs e) => OpenPath("Logs");
    private void OpenBuildFolder(object sender, RoutedEventArgs e) => OpenPath("Builds");
    private void OpenToolsFolder(object sender, RoutedEventArgs e) => OpenPath("GTools");
    private void OpenVerificationFolder(object sender, RoutedEventArgs e) => OpenPath(".build-check");
    private void OpenLauncherLogs(object sender, RoutedEventArgs e) => OpenPath("Logs/Launcher");
    private void ClearOutput(object sender, RoutedEventArgs e) { lines.Clear(); Output.Clear(); }
}
