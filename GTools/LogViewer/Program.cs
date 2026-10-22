namespace OperationBlacktide.LogViewer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--self-test")) { SelfTest.Run(); return; }
        ApplicationConfiguration.Initialize();
        string Option(string key, string fallback) { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
        using var form = new ViewerForm(Option("--logs", Path.GetFullPath("Logs/Server")));
        string preview = Option("--render-preview", "");
        if (preview.Length > 0)
        {
            form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-30000, -30000);
            form.Shown += (_, _) => { form.Refresh(); form.RenderPreview(preview); form.Close(); };
        }
        Application.Run(form);
    }
}
