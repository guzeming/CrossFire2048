using System.IO;
using System.Text.Json;

namespace OperationBlacktide.Launcher;

public sealed class ProjectPaths
{
    public string Root { get; }
    public string Client => At("Builds/Client/Blacktide.exe");
    public string BattleServer => At("Builds/BattleServer/Blacktide.BattleServer.exe");
    public string Viewer => At("GTools/LogViewer/bin/Release/net8.0-windows/OperationBlacktide.LogViewer.exe");
    public string SettingsFile => At("UserSettings/launcher.json");
    public string UnityVersion { get; }
    public ProjectPaths(string root)
    {
        Root = FindRoot(root);
        UnityVersion = File.ReadLines(At("ProjectSettings/ProjectVersion.txt")).First(s => s.StartsWith("m_EditorVersion:")).Split(':', 2)[1].Trim();
    }
    public string At(string relative) => Path.GetFullPath(Path.Combine(Root, relative));
    public static string FindRoot(string start)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(start));
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ProjectSettings/ProjectVersion.txt")) &&
                Directory.Exists(Path.Combine(directory.FullName, "GTools"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("未找到 CrossFire2048 工程。请从项目根目录的“游戏启动面板.bat”打开。");
    }
    public string LoadUnityPath()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var settings = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(SettingsFile));
                if (settings != null && File.Exists(settings.UnityPath)) return settings.UnityPath;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity", "Hub", "Editor", UnityVersion, "Editor", "Unity.exe");
    }
    public void SaveUnityPath(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(new LauncherSettings(path), new JsonSerializerOptions { WriteIndented = true }));
    }
    private sealed record LauncherSettings(string UnityPath);
}
