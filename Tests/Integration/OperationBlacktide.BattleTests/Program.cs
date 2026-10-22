using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LiteNetLib;
using OperationBlacktide.Client.Network;
using OperationBlacktide.Shared.Battle;
using OperationBlacktide.Shared.Protocol;

static class Program
{
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    private static readonly List<Bot> Bots = new();
    private sealed class Bot : IDisposable
    {
        public BattleConnection Connection = new();
        public Dictionary<int, BattleState> States = new();
        public BattleInput Input;
        public uint Sequence;
        public int Snapshots, Shots;
        public BattleState Self => States.GetValueOrDefault(Connection.PlayerId);
        public Bot(BattleJoinResponse ticket)
        {
            Connection.Snapshot += (_, states) => { Snapshots++; States = states.ToDictionary(s => s.Id); };
            Connection.Shot += (_, _, _) => Shots++;
            Connection.Connect(ticket.Host, ticket.Port, ticket.Ticket); Bots.Add(this);
        }
        public void Send() { if (!Connection.Ready) return; var value = Input; value.Sequence = ++Sequence; Connection.Send(new[] { value }); }
        public void Dispose() { Connection.Dispose(); Bots.Remove(this); }
    }
    private sealed class Account : IDisposable
    {
        private readonly TcpClient tcp;
        private readonly StreamReader reader;
        private readonly StreamWriter writer;
        public Account(int port) { tcp = new TcpClient("127.0.0.1", port); reader = new StreamReader(tcp.GetStream()); writer = new StreamWriter(tcp.GetStream(), new UTF8Encoding(false)) { AutoFlush = true }; }
        public async Task<T> Request<T>(MessageType type, object value)
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(new NetworkMessage(type, JsonSerializer.Serialize(value, Json)), Json));
            string line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            var envelope = JsonSerializer.Deserialize<NetworkMessage>(line, Json);
            return JsonSerializer.Deserialize<T>(envelope.Json, Json);
        }
        public async Task<BattleJoinResponse> Login(string name)
        {
            var registered = await Request<RegisterResponse>(MessageType.RegisterRequest, new RegisterRequest { Username = name, Password = "TestOnly_9482" });
            Require(registered.Code == AuthResultCode.Ok, "register " + name);
            var logged = await Request<LoginResponse>(MessageType.LoginRequest, new LoginRequest { Username = name, Password = "TestOnly_9482" });
            Require(logged.Code == AuthResultCode.Ok, "login " + name);
            var ticket = await Request<BattleJoinResponse>(MessageType.BattleJoinRequest, new BattleJoinRequest());
            Require(ticket.Success, "authenticated ticket"); return ticket;
        }
        public void Dispose() { tcp.Dispose(); }
    }
    public static async Task<int> Main(string[] args)
    {
        string root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
        string Option(string name, string fallback) { int index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback; }
        bool testBots = args.Contains("--bots");
        string run = Path.Combine(root, ".build-check", "battle-integration-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(run);
        int accountPort = FreeTcpPort(), battlePort = FreeUdpPort();
        Process account = null, battle = null;
        try
        {
            account = Start("dotnet", root, Option("--account", Path.Combine(root, "GServer/OperationBlacktide.Server/bin/Release/net8.0/OperationBlacktide.Server.dll")),
                "--no-console", "--port", accountPort.ToString(), "--battle-port", battlePort.ToString(), "--accounts", Path.Combine(run, "accounts.json"), "--battle-key", Path.Combine(run, "battle.key"), "--logs", Path.Combine(run, "logs"));
            await WaitFor(() => File.Exists(Path.Combine(run, "battle.key")), 10);
            battle = Start(Option("--server", Path.Combine(root, "Builds/BattleServer/Blacktide.BattleServer.exe")), root, "-batchmode", "-nographics", "--battle-server", "--bots", testBots ? "7" : "0", "--port", battlePort.ToString(),
                "--battle-key", Path.Combine(run, "battle.key"), "--logs", Path.Combine(run, "logs"), "-logFile", Path.Combine(run, "unity.log"));
            await WaitFor(() => File.Exists(Path.Combine(run, "unity.log")) && ReadShared(Path.Combine(run, "unity.log")).Contains("BATTLE_SERVER_READY"), 45);
            using var unauth = new Account(accountPort);
            Require(!(await unauth.Request<BattleJoinResponse>(MessageType.BattleJoinRequest, new BattleJoinRequest())).Success, "anonymous ticket rejected");
            using var aa = new Account(accountPort); using var ab = new Account(accountPort);
            var ta = await aa.Login("Alice"); var tb = await ab.Login("Bob");
            using var a = new Bot(ta); using var b = new Bot(tb);
            Pump(1.3);
            if (testBots)
            {
                Require(a.Connection.Ready && b.Connection.Ready && a.States.Count == 8 && b.States.Count == 8, "headless bot room replicates eight actors");
                Require(a.States.Values.Count(s => s.IsBot) == 6, "two humans and six server bots");
                Require(a.States.Values.Count(s => s.Team == BattleTeam.CT) == 4 && a.States.Values.Count(s => s.Team == BattleTeam.T) == 4, "both teams replicated");
                int shots = a.Shots;
                Pump(8);
                Require(a.Shots > shots && a.States.Values.Any(s => s.IsBot && s.Life > 1), "built server bots shoot, kill and respawn");
                b.Dispose(); Pump(1);
                Require(a.States.Count == 8 && a.States.Values.Count(s => s.IsBot) == 7, "built server replenishes bot after disconnect");
                var botLogs = Directory.GetFiles(Path.Combine(run, "logs"), "*.jsonl").SelectMany(p => ReadShared(p).Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    .Select(s => JsonDocument.Parse(s).RootElement.Clone()).Where(e => e.GetProperty("userId").GetString().StartsWith("bot-")).ToArray();
                foreach (string action in new[] { "bot_join", "bot_state", "move", "fire", "hit", "death", "respawn" })
                    Require(botLogs.Any(e => e.GetProperty("action").GetString() == action), "built bot event: " + action);
                File.WriteAllText(Path.Combine(run, "result.txt"), "BATTLE_BOTS_HEADLESS_PASS");
                Console.WriteLine("BATTLE_BOTS_HEADLESS_PASS " + run); return 0;
            }
            Require(a.Connection.Ready && b.Connection.Ready && a.States.Count == 2 && b.States.Count == 2, "two clients joined and receive snapshots");
            Require(a.Self.Health == 100 && a.Self.Ammo == 30, "server initial state");
            var malformedTransport = new NetManager(new EventBasedNetListener()) { AutoRecycle = true, IPv6Enabled = false };
            try
            {
                malformedTransport.Start(); var handshake = new LiteNetLib.Utils.NetDataWriter(); handshake.Put((ushort)500); handshake.Put((byte)1);
                malformedTransport.Connect("127.0.0.1", battlePort, handshake); Pump(.2);
                Require(a.States.Count == 2 && !ReadShared(Path.Combine(run, "unity.log")).Contains("InvalidOperationException"), "truncated handshake safely rejected");
            }
            finally { malformedTransport.Stop(); }
            var rawPeer = (NetPeer)typeof(BattleConnection).GetField("peer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(a.Connection);
            rawPeer.Send(BattleProtocol.Write(BattlePacket.Shot, w => { w.Write(b.Connection.PlayerId); w.Write(9999); }), DeliveryMethod.ReliableOrdered);
            Pump(.2);
            Require(b.Self.Health == 100 && a.Self.Ammo == 30, "client-supplied shot/damage packet rejected");
            using (var replay = new Bot(ta)) { Pump(.4); Require(!replay.Connection.Ready, "replayed ticket rejected"); }
            var badTicket = new BattleJoinResponse { Port = battlePort, Ticket = "forged" };
            using (var forged = new Bot(badTicket)) { Pump(.4); Require(!forged.Connection.Ready, "forged ticket rejected"); }

            float startZ = a.Self.Z;
            a.Input = new BattleInput { Z = -1, Yaw = 0 }; Pump(1);
            a.Input = default; Pump(.2);
            float moved = startZ - a.Self.Z;
            Require(moved > 3.5 && moved < 5.1, "server movement speed: " + moved);
            float x = a.Self.X;
            var malformed = new BattleInput { Sequence = a.Sequence + 1, X = float.NaN };
            a.Connection.Send(new[] { malformed }); Pump(.2);
            Require(Math.Abs(a.Self.X - x) < .2 && float.IsFinite(a.Self.X), "NaN input rejected");

            float yaw = MathF.Atan2(b.Self.X - a.Self.X, b.Self.Z - a.Self.Z) * 180 / MathF.PI;
            a.Input = new BattleInput { Yaw = yaw, Buttons = InputButtons.Fire }; Pump(.55);
            a.Input = new BattleInput { Yaw = yaw }; Pump(.2);
            Require(b.Self.Health == 0 && a.Self.Ammo is >= 23 and <= 26, "server fire cadence / ammo / damage / death");
            Require(a.Shots > 0 && b.Shots > 0, "confirmed shot events fan out");
            int life = b.Self.Life, deadAmmo = b.Self.Ammo;
            b.Input = new BattleInput { Buttons = InputButtons.Fire }; Pump(.25); b.Input = default;
            Require(b.Self.Health == 0 && b.Self.Ammo == deadAmmo, "dead player cannot fire");
            a.Input = new BattleInput { Yaw = yaw, Buttons = InputButtons.Reload }; Pump(.1);
            a.Input = new BattleInput { Yaw = yaw, Buttons = InputButtons.Fire }; Pump(.3);
            Require(a.Self.ReloadTicks > 0 && a.Self.Ammo < 30, "reload is server-timed and blocks firing");
            a.Input = new BattleInput { Yaw = yaw }; Pump(2.6);
            Require(a.Self.Ammo == 30 && a.Self.Reserve < 90 && a.Self.ReloadTicks == 0, "finite reserve reload completion");
            Require(b.Self.Health == 100 && b.Self.Life > life, "server respawn");

            // Walk Bob around the Dust II building corner; its wall must stop Alice's shot.
            b.Input = new BattleInput { Z = 1 }; Pump(Math.Max(0, (8 - b.Self.Z) / 4.5)); b.Input = default; Pump(.2);
            b.Input = new BattleInput { X = 1 }; Pump(Math.Max(0, (-2 - b.Self.X) / 4.5)); b.Input = default; Pump(.3);
            Require(b.Self.X > -3 && b.Self.Z > 7, "opponent walks around Dust II corner");
            yaw = MathF.Atan2(b.Self.X - a.Self.X, b.Self.Z - a.Self.Z) * 180 / MathF.PI;
            a.Input = new BattleInput { Yaw = yaw, Buttons = InputButtons.Fire }; Pump(.7); a.Input = default; Pump(.2);
            Require(b.Self.Health == 100, "static wall blocks server raycast");
            a.Input = new BattleInput { X = 1 }; Pump(2); a.Input = default; Pump(.2);
            Require(a.Self.X > -9 && a.Self.X < -7.5f, "Dust II wall blocks authoritative movement");

            // A presentation exception must not be swallowed as a malformed network packet.
            Action<int, int, float[]> brokenPresentation = (_, _, _) => throw new ArgumentException("presentation-test");
            a.Connection.Shot += brokenPresentation;
            a.Input = new BattleInput { Yaw = 90, Buttons = InputButtons.Fire };
            bool observedPresentationError = false;
            try { Pump(.3); }
            catch (ArgumentException ex) when (ex.Message == "presentation-test") { observedPresentationError = true; }
            finally { a.Connection.Shot -= brokenPresentation; a.Input = default; }
            Pump(.3);
            Require(observedPresentationError && a.Connection.Ready, "presentation errors are visible and do not disconnect transport");

            // Flood a fresh client with 20x the input rate; authoritative movement remains <=7m/s.
            using var ac = new Account(accountPort); var tc = await ac.Login("FloodTest"); using var flood = new Bot(tc); Pump(.5);
            float before = flood.Self.Z;
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed.TotalSeconds < .5)
            {
                for (int i = 0; i < 20; i++) { var input = new BattleInput { Sequence = ++flood.Sequence, Z = -1, Buttons = InputButtons.Sprint }; flood.Connection.Send(new[] { input }); }
                foreach (Bot bot in Bots) bot.Connection.Poll(); Thread.Sleep(5);
            }
            Pump(.5);
            Require(before - flood.Self.Z < 7.5, "input flooding cannot accelerate time");
            using var networkAccount = new Account(accountPort);
            using var networkBot = new Bot(await networkAccount.Login("NetworkTest")); Pump(.5);
            foreach (int rtt in new[] { 50, 100, 150 })
            {
                networkBot.Connection.ConfigureSimulation(rtt, rtt == 50 ? 1 : 5);
                networkBot.Input = new BattleInput { Z = -1 }; float zBefore = networkBot.Self.Z;
                Pump(1); networkBot.Input = default; Pump(.5);
                float delta = zBefore - networkBot.Self.Z;
                Require(networkBot.Connection.Ready && delta > 2 && delta < 5.6, "RTT " + rtt + " ms / loss movement bounded: " + delta);
            }
            await Task.Delay(300);
            var logs = Directory.GetFiles(Path.Combine(run, "logs"), "*.jsonl").SelectMany(p => ReadShared(p).Split('\n', StringSplitOptions.RemoveEmptyEntries)).ToArray();
            var events = logs.Select(s => JsonDocument.Parse(s).RootElement.Clone()).ToArray();
            foreach (string action in new[] { "login", "ticket_issued", "join", "move", "stop", "fire", "hit", "damage", "death", "respawn", "reload_start", "reload_complete", "input_denied" })
                Require(events.Any(e => e.GetProperty("action").GetString() == action), "structured log: " + action);
            Require(logs.All(s => !s.Contains("TestOnly_9482") && !s.Contains(ta.Ticket)), "no password or ticket in logs");
            File.WriteAllText(Path.Combine(run, "result.txt"), "BATTLE_INTEGRATION_PASS\n" + string.Join('\n', events.Select(e => e.GetProperty("action").GetString()).Distinct()));
            Console.WriteLine("BATTLE_INTEGRATION_PASS " + run); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Console.Error.WriteLine("Diagnostics: " + run); return 1; }
        finally
        {
            foreach (Bot bot in Bots.ToArray()) bot.Dispose();
            foreach (Process process in new[] { battle, account }) if (process != null) { if (!process.HasExited) { process.Kill(true); process.WaitForExit(5000); } process.Dispose(); }
        }
    }
    private static void Pump(double seconds)
    {
        var timer = Stopwatch.StartNew(); double next = 0;
        while (timer.Elapsed.TotalSeconds < seconds)
        {
            foreach (Bot bot in Bots) bot.Connection.Poll();
            if (timer.Elapsed.TotalSeconds >= next) { foreach (Bot bot in Bots) bot.Send(); next += 1.0 / 60; }
            Thread.Sleep(1);
        }
    }
    private static Process Start(string executable, string directory, params string[] args)
    {
        var info = new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in args) info.ArgumentList.Add(arg);
        var process = Process.Start(info); process.BeginOutputReadLine(); process.BeginErrorReadLine(); return process;
    }
    private static async Task WaitFor(Func<bool> test, int seconds)
    { var timer = Stopwatch.StartNew(); while (!test()) { if (timer.Elapsed.TotalSeconds > seconds) throw new TimeoutException(); await Task.Delay(100); } }
    private static string ReadShared(string path) { using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); using var r = new StreamReader(f); return r.ReadToEnd(); }
    private static int FreeTcpPort() { var l = new TcpListener(System.Net.IPAddress.Loopback, 0); l.Start(); int p = ((System.Net.IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p; }
    private static int FreeUdpPort() { using var u = new UdpClient(0); return ((System.Net.IPEndPoint)u.Client.LocalEndPoint).Port; }
    private static void Require(bool ok, string text) { if (!ok) throw new Exception("FAIL: " + text); Console.WriteLine("PASS " + text); }
}
