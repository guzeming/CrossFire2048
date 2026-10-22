using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace OperationBlacktide.Launcher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string Option(string name, string fallback = "") { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
        try
        {
            if (args.Contains("--self-test") || args.Contains("--integration-test") || args.Contains("--render-preview"))
            {
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
                Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });
            }
            string root = ProjectPaths.FindRoot(Option("--root", AppContext.BaseDirectory));
            if (args.Contains("--self-test")) { SelfTest.Run(root); return 0; }
            if (args.Contains("--integration-test")) { SelfTest.RunServices(root); return 0; }
            string preview = Option("--render-preview");
            string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..16];
            using var mutex = new Mutex(true, "Local\\BlacktideLauncher-" + id, out bool created);
            if (!created && preview.Length == 0)
            {
                foreach (var process in Process.GetProcessesByName("Blacktide.Launcher"))
                {
                    using (process)
                    {
                        if (process.Id != Environment.ProcessId && process.MainWindowHandle != IntPtr.Zero &&
                            process.MainModule?.FileName == Environment.ProcessPath) { WindowActivation.Focus(process.MainWindowHandle); break; }
                    }
                }
                return 0;
            }
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var window = new MainWindow(new ProjectPaths(root), preview);
            app.Run(window);
            return window.VerificationFailed ? 1 : 0;
        }
        catch (Exception ex)
        {
            if (args.Contains("--self-test") || args.Contains("--integration-test") || args.Contains("--render-preview")) Console.Error.WriteLine(ex);
            else MessageBox.Show(ex.Message, "启动面板未能打开", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }
}
