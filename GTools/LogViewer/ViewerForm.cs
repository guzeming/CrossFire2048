using System.Diagnostics;
using System.Text.Json;

namespace OperationBlacktide.LogViewer;

public sealed class ViewerForm : Form
{
    private LogTailer tailer;
    private readonly List<LogEntry> entries = new(), visible = new();
    private readonly TextBox player = new() { Width = 150, PlaceholderText = "玩家名 / User ID" };
    private readonly TextBox action = new() { Width = 145, PlaceholderText = "操作，例如 fire" };
    private readonly TextBox search = new() { Width = 190, PlaceholderText = "搜索日志内容" };
    private readonly ComboBox source = new() { Width = 105, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox level = new() { Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox pause = new() { Text = "暂停显示", AutoSize = true };
    private readonly CheckBox scroll = new() { Text = "自动滚动", AutoSize = true, Checked = true };
    private readonly Label summary = new() { AutoSize = true, Padding = new Padding(0, 12, 0, 0) };
    private readonly Label location = new() { Dock = DockStyle.Bottom, Height = 32, Padding = new Padding(16, 5, 0, 0) };
    private readonly TextBox details = new() { Dock = DockStyle.Bottom, Height = 85, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly DataGridView grid = new() { Dock = DockStyle.Fill, VirtualMode = true, ReadOnly = true, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.FromArgb(20, 27, 35), BorderStyle = BorderStyle.None };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    private long total;
    public ViewerForm(string directory)
    {
        tailer = new LogTailer(directory);
        Text = "黑潮行动 · 服务器实时日志"; Size = new Size(1360, 820); MinimumSize = new Size(1050, 640);
        Font = new Font("Microsoft YaHei UI", 10); BackColor = Color.FromArgb(20, 27, 35); ForeColor = Color.FromArgb(222, 232, 238);
        foreach (Control input in new Control[] { player, action, search, source, level, details })
        { input.BackColor = Color.FromArgb(37, 47, 59); input.ForeColor = ForeColor; }
        source.ForeColor = level.ForeColor = Color.FromArgb(20, 27, 35);
        var top = new Panel { Dock = DockStyle.Top, Height = 132, Padding = new Padding(16, 12, 16, 8) };
        var title = new Label { Text = "SERVER LIVE LOG", Dock = DockStyle.Top, Height = 35, Font = new Font(Font.FontFamily, 17, FontStyle.Bold), ForeColor = Color.FromArgb(255, 173, 66) };
        var filters = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, WrapContents = false };
        source.Items.AddRange(new object[] { "全部服务", "battle", "account" }); source.SelectedIndex = 0;
        level.Items.AddRange(new object[] { "全部级别", "INFO", "WARN", "ERROR" }); level.SelectedIndex = 0;
        foreach (ComboBox combo in new[] { source, level })
        {
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.DrawItem += (_, e) =>
            {
                using var background = new SolidBrush(Color.FromArgb(37, 47, 59)); e.Graphics.FillRectangle(background, e.Bounds);
                string text = e.Index >= 0 ? combo.Items[e.Index]?.ToString() ?? "" : combo.Text;
                TextRenderer.DrawText(e.Graphics, text, Font, e.Bounds, ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            };
        }
        player.Width = 130; action.Width = 110; search.Width = 160; source.Width = 100; level.Width = 80;
        void FilterField(string text, Control input)
        { filters.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(2, 5, 2, 0) }); filters.Controls.Add(input); }
        FilterField("服务", source); FilterField("级别", level); FilterField("玩家", player); FilterField("操作", action); FilterField("搜索", search);
        filters.Controls.AddRange(new Control[] { pause, scroll });
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        Button Button(string text, Action click) { var b = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat }; b.Click += (_, _) => click(); return b; }
        actions.Controls.Add(Button("选择日志目录", ChooseDirectory));
        actions.Controls.Add(Button("打开目录", () => { if (Directory.Exists(tailer.DirectoryPath)) Process.Start(new ProcessStartInfo(tailer.DirectoryPath) { UseShellExecute = true }); }));
        actions.Controls.Add(Button("导出筛选结果", Export)); actions.Controls.Add(summary);
        top.Controls.Add(actions); top.Controls.Add(filters); top.Controls.Add(title);
        string[] columns = { "时间", "级别", "服务", "房间", "玩家", "Tick", "输入序号", "操作", "详情" };
        for (int i = 0; i < columns.Length; i++) grid.Columns.Add(new DataGridViewTextBoxColumn { Name = columns[i], HeaderText = columns[i], FillWeight = i == 8 ? 260 : i == 0 || i == 7 ? 110 : 75, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.EnableHeadersVisualStyles = false; grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(37, 47, 59), ForeColor = ForeColor, Padding = new Padding(3, 8, 3, 8) };
        grid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = BackColor, ForeColor = ForeColor, SelectionBackColor = Color.FromArgb(44, 70, 89), SelectionForeColor = Color.White };
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(25, 33, 42); grid.GridColor = Color.FromArgb(37, 47, 59); grid.RowTemplate.Height = 29;
        grid.CellValueNeeded += (_, e) =>
        {
            if (e.RowIndex >= visible.Count) return; var v = visible[e.RowIndex];
            e.Value = e.ColumnIndex switch { 0 => v.Time, 1 => v.Level, 2 => v.Source, 3 => v.Room, 4 => v.Player, 5 => v.Tick, 6 => v.Sequence, 7 => v.Action, _ => v.Message };
        };
        grid.SelectionChanged += (_, _) => { if (grid.CurrentCell is { } cell && cell.RowIndex < visible.Count) details.Text = JsonSerializer.Serialize(visible[cell.RowIndex], new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }); };
        grid.CellFormatting += (_, e) => { if (e.RowIndex < visible.Count && visible[e.RowIndex].Level != "INFO") e.CellStyle!.ForeColor = Color.FromArgb(255, 177, 87); };
        Controls.Add(grid); Controls.Add(details); Controls.Add(location); Controls.Add(top);
        foreach (var box in new[] { player, action, search }) box.TextChanged += (_, _) => Filter();
        source.SelectedIndexChanged += (_, _) => Filter(); level.SelectedIndexChanged += (_, _) => Filter(); pause.CheckedChanged += (_, _) => Filter();
        timer.Tick += (_, _) => RefreshLogs(); timer.Start(); RefreshLogs();
        FormClosed += (_, _) => { timer.Stop(); timer.Dispose(); tailer.Dispose(); };
        Shown += (_, _) => Filter();
    }
    public void RefreshLogs()
    {
        var incoming = tailer.Poll(); entries.AddRange(incoming); total += incoming.Count;
        if (entries.Count > 10000) entries.RemoveRange(0, entries.Count - 10000);
        if (!pause.Checked && incoming.Count > 0) Filter();
        location.Text = tailer.DirectoryPath + "   |   " + tailer.Status;
        summary.Text = $"已读 {total:N0} 条 · 保留 {entries.Count:N0} · 显示 {visible.Count:N0}" + (pause.Checked ? " · 显示已暂停，仍在采集" : " · 实时");
    }
    private void Filter()
    {
        if (pause.Checked) return;
        bool Has(string text, string query) => text.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);
        visible.Clear(); visible.AddRange(entries.Where(e => (source.SelectedIndex == 0 || e.Source == source.Text) &&
            (level.SelectedIndex == 0 || e.Level == level.Text) && (Has(e.Player, player.Text) || Has(e.UserId, player.Text)) &&
            Has(e.Action, action.Text) && Has(e.Message + " " + e.Player + " " + e.Action, search.Text)));
        grid.RowCount = visible.Count; grid.Invalidate();
        if (scroll.Checked && visible.Count > 0 && grid.IsHandleCreated)
            grid.FirstDisplayedScrollingRowIndex = Math.Clamp(visible.Count - Math.Max(1, grid.DisplayedRowCount(false)), 0, visible.Count - 1);
    }
    private void ChooseDirectory()
    {
        using var dialog = new FolderBrowserDialog { InitialDirectory = tailer.DirectoryPath, Description = "选择服务器 JSONL 日志目录" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        tailer.Dispose(); tailer = new LogTailer(dialog.SelectedPath); entries.Clear(); visible.Clear(); total = 0; grid.RowCount = 0; RefreshLogs();
    }
    private void Export()
    {
        using var dialog = new SaveFileDialog { Filter = "JSON lines|*.jsonl", FileName = "server-events.jsonl" };
        if (dialog.ShowDialog(this) == DialogResult.OK) File.WriteAllLines(dialog.FileName, visible.Select(e => JsonSerializer.Serialize(e, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })));
    }
    public void RenderPreview(string path)
    {
        RefreshLogs(); using var bitmap = new Bitmap(Width, Height); DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height)); bitmap.Save(path);
    }
}
