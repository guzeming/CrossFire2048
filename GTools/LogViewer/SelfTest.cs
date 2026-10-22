using System.Text;

namespace OperationBlacktide.LogViewer;

internal static class SelfTest
{
    public static void Run()
    {
        string folder = Path.Combine(Path.GetTempPath(), "blacktide-log-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            using var tailer = new LogTailer(folder);
            string path = Path.Combine(folder, "battle.jsonl");
            byte[] line = Encoding.UTF8.GetBytes("{\"player\":\"玩家甲\",\"action\":\"fire\",\"message\":\"first\\nsecond\"}\n");
            File.WriteAllBytes(path, line.Take(13).ToArray());
            Require(tailer.Poll().Count == 0, "partial UTF8 must wait");
            using (var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) file.Write(line, 13, line.Length - 13);
            var result = tailer.Poll(); Require(result.Count == 1 && result[0].Player == "玩家甲" && result[0].Message.Contains('\n'), "UTF8 and JSON escape");
            Require(tailer.Poll().Count == 0, "no duplicate lines");
            File.WriteAllText(path, "{\"action\":\"stop\"}\n"); Require(tailer.Poll().Single().Action == "stop", "truncation");
            File.WriteAllText(Path.Combine(folder, "account.jsonl"), "invalid\n{\"action\":\"login\"}\n");
            Require(tailer.Poll().Single().Action == "login" && tailer.InvalidLines == 1, "rotation + malformed line");
            Console.WriteLine("LOG_VIEWER_SELF_TEST_PASS");
        }
        finally { Directory.Delete(folder, true); }
    }
    private static void Require(bool ok, string description) { if (!ok) throw new Exception(description); }
}
