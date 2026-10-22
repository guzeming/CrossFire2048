using System.Text.RegularExpressions;

namespace OperationBlacktide.Launcher;

// Percentages describe completed workflow stages, not elapsed or remaining time.
internal sealed class TaskProgressTracker
{
    private static readonly Regex Report = new(@"^\[进度\] (?<percent>\d{1,3}) (?<stage>.+)$", RegexOptions.CultureInvariant);
    private readonly object gate = new();
    private int percent, start, end = 100;
    private string stage = "";
    private bool active;

    public (int Percent, string Stage) Read() { lock (gate) return (percent, stage); }
    public void Reset()
    {
        lock (gate) { percent = start = 0; end = 100; stage = ""; active = true; }
    }
    public void BeginStep(int from, int to)
    {
        if (from < 0 || to > 100 || from >= to) throw new ArgumentOutOfRangeException(nameof(from));
        lock (gate) { start = from; end = to; Advance(0); stage = "正在启动工具…"; }
    }
    public void Consume(string message)
    {
        lock (gate)
        {
            if (!active) return;
            var match = Report.Match(message);
            if (match.Success && int.TryParse(match.Groups["percent"].Value, out int value) && value <= 100)
            {
                Advance(value);
                stage = match.Groups["stage"].Value;
            }
            else if (message.StartsWith("[阶段] ", StringComparison.Ordinal)) stage = message[5..];
        }
    }
    private void Advance(int local) => percent = Math.Max(percent, Math.Min(99, start + (end - start) * local / 100));
    public void CompleteStep() { lock (gate) { if (active) Advance(100); } }
    public void Finish(bool succeeded) { lock (gate) { if (succeeded) percent = 100; active = false; } }
}
