using OperationBlacktide.Server.Demolition;
using OperationBlacktide.Shared.Modes.Demolition;
using OperationBlacktide.Shared.Battle;
using System.Security.Cryptography;

static class Tests
{
    static int assertions;
    static void Check(bool condition, string label) { assertions++; if (!condition) throw new Exception(label); }
    static DemolitionRequest Request(LobbyAction action, bool ai = false, string code = "") => new() { Action = action, AllowAI = ai, Code = code };
    static DemolitionResponse Do(DemolitionLobbyService s, int id, LobbyAction action, double time = 0, bool ai = false, string code = "") => s.Handle("u"+id, "User "+id, Request(action, ai, code), time);
    public static void Main(string[] args)
    {
        if (args.Contains("--tactics"))
        {
            int server = Array.IndexOf(args, "--server");
            TacticsIntegration.Run(Path.GetFullPath(server >= 0 ? args[server + 1] : "Builds/DemolitionServer/Blacktide.DemolitionServer.exe")).GetAwaiter().GetResult(); return;
        }
        if (args.Contains("--integration"))
        {
            int server = Array.IndexOf(args, "--server");
            if (server >= 0 && server + 1 >= args.Length) throw new ArgumentException("--server requires an executable path.");
            Integration.Run(server >= 0 ? args[server + 1] : null).GetAwaiter().GetResult(); return;
        }
        var command = new BattleInput { Sequence = 3, Yaw = 42, Buttons = InputButtons.Fire };
        using (var wire = DemolitionProtocol.Read(DemolitionProtocol.Input(command, false, new DemolitionPoint(12, 7, -9)), out var packet))
        {
            var input = DemolitionProtocol.Input(wire);
            Check(packet == DemolitionPacket.Input && input.Command.Sequence == 3 && input.HasAim && input.AimPoint.Y == 7, "3D aim input roundtrip");
        }
        using (var wire = DemolitionProtocol.Read(DemolitionProtocol.Input(command, true), out _))
        { var input = DemolitionProtocol.Input(wire); Check(!input.HasAim && input.Interact, "legacy bot caller uses heading fallback"); }
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, 10001f })
        {
            bool rejected = false;
            try { using var wire = DemolitionProtocol.Read(DemolitionProtocol.Input(command, false, new DemolitionPoint(0, invalid, 0)), out _); DemolitionProtocol.Input(wire); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "invalid network aim rejected");
        }
        var key = RandomNumberGenerator.GetBytes(32);
        using (var a = new Allocator()) using (var s = new DemolitionLobbyService(a, key))
        {
            Do(s, 1, LobbyAction.Queue, ai:true); Do(s, 2, LobbyAction.Queue);
            s.Tick(29.999); Check(a.Matches.Count == 0, "AI must not start before 30s");
            s.Tick(30); Check(a.Matches.Count == 1, "AI starts at 30s");
            var seats = a.Matches.Values.Single(); Check(seats.Count(x => x.IsBot) == 9 && seats.All(x => x.UserId != "u2"), "opt-out must not enter AI game");
            Check(seats.Count(x => x.Team == 0) == 5 && seats.Count(x => x.Team == 1) == 5, "5v5 roster");
            Check(Do(s, 2, LobbyAction.Poll, 999).Phase == LobbyPhase.Queued, "pure-human queue stays waiting");
            Check(!Do(s, 1, LobbyAction.Queue).Success, "assigned player cannot double queue");
            a.Done = true; s.Tick(31); Check(Do(s, 1, LobbyAction.Poll).Phase == LobbyPhase.Idle, "completion returns to original party");
        }
        using (var a = new Allocator()) using (var s = new DemolitionLobbyService(a, key))
        {
            for (int i = 0; i < 10; i++) Do(s, i, LobbyAction.Queue, ai:i%2==0);
            s.Tick(.2);Check(a.Matches.Count==0&&Do(s,1,LobbyAction.Poll,.3).Phase==LobbyPhase.Confirming,"opt-out players confirm full human match");
            for(int i=1;i<10;i+=2)Do(s,i,LobbyAction.AcceptMatch,.4);
            s.Tick(.5); Check(a.Matches.Count == 1 && a.Matches.Values.Single().All(x => !x.IsBot), "10 humans start early after confirmation");
        }
        using (var a = new Allocator()) using (var s = new DemolitionLobbyService(a, key))
        {
            string code = Do(s, 1, LobbyAction.CreateParty).PartyCode;
            for (int i = 2; i <= 5; i++) Check(Do(s, i, LobbyAction.JoinParty, code:code).Success, "join party");
            Check(!Do(s, 6, LobbyAction.JoinParty, code:code).Success, "party capacity");
            Check(!Do(s, 2, LobbyAction.Queue).Success, "leader-only queue");
            Do(s, 1, LobbyAction.Queue, ai:true); s.Tick(30);
            Check(a.Matches.Values.Single().Where(x => !x.IsBot).Select(x => x.Team).Distinct().Count() == 1, "party never splits");
        }
        using (var a = new Allocator()) using (var s = new DemolitionLobbyService(a, key))
        {
            Do(s, 1, LobbyAction.Queue, ai:true); Do(s, 1, LobbyAction.CancelQueue, 29.9); s.Tick(30);
            Check(a.Matches.Count == 0, "cancellation wins timer race");
            Do(s, 1, LobbyAction.Queue, 50, true); s.Tick(79.999); Check(a.Matches.Count == 0, "requeue resets clock"); s.Tick(80); Check(a.Matches.Count == 1, "new deadline");
        }
        using (var a = new Allocator()) using (var s = new DemolitionLobbyService(a, key))
        {
            var room = s.Handle("u1", "Host", new DemolitionRequest { Action = LobbyAction.CreateRoom, IsPrivate = true, Password = "secret", AllowAI = true }, 0).Room;
            Check(Do(s, 2, LobbyAction.Poll).Rooms.Length == 0, "private room hidden");
            Check(!Do(s, 2, LobbyAction.JoinRoom, code:room.Code).Success, "password required");
            Check(s.Handle("u2", "Guest", new DemolitionRequest { Action = LobbyAction.JoinRoom, Code = room.Code, Password = "secret" }, 0).Success, "password join");
            Check(!Do(s, 2, LobbyAction.StartRoom).Success, "only host starts");
            foreach (string id in new[] { "u1", "u2" }) s.Handle(id, id, new DemolitionRequest { Action = LobbyAction.Ready, Ready = true }, 0);
            Check(Do(s, 1, LobbyAction.StartRoom).Success, "ready room countdown");
            Do(s, 2, LobbyAction.SelectTeam, 2); s.Tick(6); Check(a.Matches.Count == 0, "team change clears readiness/countdown");
            s.Disconnect("u1", 7); Check(Do(s, 2, LobbyAction.Poll, 7).Room.HostId == "u2", "host migration");
        }
        using (var a = new Allocator()) using (var s = new DemolitionLobbyService(a, key))
        {
            var r=s.Handle("u1","Host",new DemolitionRequest { Action=LobbyAction.CreateRoom, Password="secret", AllowAI=true },0).Room;
            s.Handle("u1","Host",new DemolitionRequest { Action=LobbyAction.ConfigureRoom, Name="Updated", AllowAI=true },0);
            Check(!Do(s,2,LobbyAction.JoinRoom,code:r.Code).Success,"changing AI does not clear password");
            s.Handle("u1","Host",new DemolitionRequest { Action=LobbyAction.Ready, Ready=true },0);
            Do(s,1,LobbyAction.StartRoom); s.Tick(4.99); Check(a.Matches.Count==0,"room countdown waits five seconds");
            s.Tick(5); Check(a.Matches.Values.Single().Count(x=>x.IsBot)==9,"custom room fills only at start");
            Check(!Do(s,1,LobbyAction.ConfigureRoom,6).Success,"room settings locked in match");
        }
        Confirmations(key);
        Ratings();
        Rules();
        string token = DemolitionTicket.Issue(key, "room", "user", 100);
        Check(DemolitionTicket.Validate(key, token, "room", 100, out string uid) && uid == "user", "signed admission");
        Check(!DemolitionTicket.Validate(key, token, "other", 100, out _), "cross-room ticket rejected");
        Check(!DemolitionTicket.Validate(key, token, "room", 281, out _), "expired ticket rejected");
        Check(!DemolitionTicket.Validate(RandomNumberGenerator.GetBytes(32), token, "room", 100, out _), "forged signature rejected");
        Console.WriteLine("DEMOLITION_RULES_PASS assertions="+assertions);
    }
    static MatchSeat[] Seats() => Enumerable.Range(1, 10).Select(i => new MatchSeat { Id = i, Team = i <= 5 ? 0 : 1, UserId = "u"+i, Name = "User "+i, IsBot = i != 1 }).ToArray();
    static void Confirmations(byte[] key)
    {
        using (var a = new Allocator()) using (var s = new DemolitionLobbyService(a, key))
        {
            for(int i=1;i<=10;i++)Do(s,i,LobbyAction.Queue);
            s.Tick(1);
            string code=Do(s,1,LobbyAction.Poll,2).PartyCode;
            Check(!Do(s,11,LobbyAction.JoinParty,2,code:code).Success,"pending roster cannot gain a new party member");
            for(int i=1;i<10;i++)Do(s,i,LobbyAction.AcceptMatch,2);
            s.Tick(16);
            var confirmed=Do(s,1,LobbyAction.Poll,16);
            Check(a.Matches.Count==0&&confirmed.Phase==LobbyPhase.Queued&&confirmed.WaitSeconds==16,"confirmed users retain queue age after timeout");
            Check(Do(s,10,LobbyAction.Poll,16).Phase==LobbyPhase.Idle,"unconfirmed user returns idle");
            Check(!Do(s,10,LobbyAction.AcceptMatch,16).Success,"late confirmation rejected");
        }
        using(var a=new Allocator())using(var s=new DemolitionLobbyService(a,key))
        {
            Do(s,1,LobbyAction.Queue,0,true);
            for(int i=2;i<=10;i++)Do(s,i,LobbyAction.Queue,29);
            s.Tick(29);
            Check(Do(s,1,LobbyAction.Poll,29).Phase==LobbyPhase.Confirming,"mixed roster found before AI deadline");
            s.Tick(30);
            Check(a.Matches.Count==1&&a.Matches.Values.Single().Count(x=>x.IsBot)==9,"confirmation cannot delay the AI deadline");
        }
    }
    static void Ratings()
    {
        string folder=Path.Combine(Path.GetTempPath(),"blacktide-ratings-"+Guid.NewGuid().ToString("N"));
        string path=Path.Combine(folder,"ratings.json");
        try
        {
            var ratings=new DemolitionRatings(path);var seats=Seats();foreach(var seat in seats)seat.IsBot=false;
            Check(ratings.Get("u1")==1000,"initial hidden rating");
            var result=new DemolitionResult{Completed=true,ScoreA=13,ScoreB=7};
            ratings.Record("game1",seats,result);
            Check(ratings.Get("u1")==1016&&ratings.Get("u6")==984,"zero sum Elo update");
            ratings.Record("game1",seats,result);Check(ratings.Get("u1")==1016,"duplicate result is idempotent");
            ratings=new DemolitionRatings(path);Check(ratings.Get("u6")==984,"rating survives restart");
            result.HasAI=true;ratings.Record("game2",seats,result);Check(ratings.Get("u1")==1016,"AI match does not change rating");
            result.HasAI=false;result.Completed=false;ratings.Record("game3",seats,result);Check(ratings.Get("u1")==1016,"aborted match does not change rating");
        }
        finally { if(File.Exists(path))File.Delete(path);if(Directory.Exists(folder))Directory.Delete(folder); }
    }
    static DemolitionRules Fast() => new() { FreezeSeconds = .05f, RoundSeconds = 10, PlantSeconds = .05f, BombSeconds = .3f, DefuseSeconds = .15f, KitSeconds = .075f, ResultSeconds = .05f };
    static void Advance(DemolitionGame g, double seconds) { for (int i = 0; i < Math.Ceiling(seconds/.005); i++) g.Logic.Tick(.005f); }
    static void Begin(DemolitionGame g) { g.Teams.Connection(1, true); Advance(g, .07); Check(g.Round.Phase == RoundPhase.Live, "loading -> freeze -> live"); }
    static void Plant(DemolitionGame g)
    {
        var carrier = g.Teams.Find(g.Bomb.Carrier); carrier.Position = new DemolitionPoint(10,0,0); carrier.Interact = true;
        Advance(g, .07); carrier.Interact = false; Check(g.Round.Phase == RoundPhase.Planted, "plant completes");
    }
    static void Rules()
    {
        using (var g = new DemolitionGame(Seats(), new Map(), Fast()))
        {
            Begin(g); Plant(g);
            foreach (var p in g.Teams.Players.Where(g.Teams.IsAttacker)) p.Character.Vitals.ApplyDamage(1000, true);
            Advance(g, .01); Check(g.Round.Phase == RoundPhase.Planted, "planted bomb survives all attackers dying");
            Advance(g, .35); Check(g.Round.ScoreA == 1, "explosion awards attacker score");
        }
        using (var g = new DemolitionGame(Seats(), new Map(), Fast()))
        {
            Begin(g); Plant(g); var ct = g.Teams.Find(6); ct.Position = new DemolitionPoint(10,0,0); ct.Interact = true;
            Advance(g, .18); Check(g.Round.ScoreB == 1 && g.Bomb.Phase == BombPhase.Defused, "defuse beats bomb");
            int score = g.Round.ScoreB; Advance(g, .01); Check(g.Round.ScoreB == score, "one result per round");
        }
        using (var g = new DemolitionGame(Seats(), new Map(), Fast()))
        {
            Begin(g); var t = g.Teams.Find(g.Bomb.Carrier); t.Position = new DemolitionPoint(10,0,0); t.Interact = true; Advance(g, .035);
            t.Interact = false; Advance(g, .005); t.Interact = true; Advance(g, .025); Check(g.Bomb.Phase != BombPhase.Planted, "released plant resets progress");
            t.Interact = false; t.Character.Vitals.ApplyDamage(1000, true); Advance(g, .01);
            Check(g.Bomb.Phase == BombPhase.Dropped && !t.Alive, "death drops bomb and does not respawn");
            var friend = g.Teams.Players.First(x => g.Teams.IsAttacker(x) && x.Alive); friend.Position = t.Position; Advance(g, .01);
            Check(g.Bomb.Carrier == friend.Seat.Id, "teammate retrieves bomb");
        }
        using (var g = new DemolitionGame(Seats(), new Map(), Fast()))
        {
            g.Teams.Connection(1, true); Advance(g, .005);
            Check(g.Economy.Buy(1, BuyItem.Armor), "buy during freeze"); Check(g.Teams.Find(1).Money == 150, "deduct once");
            Check(!g.Economy.Buy(1, BuyItem.Armor), "duplicate armor does not charge");
            Check(!g.Economy.Buy(1, BuyItem.DefuseKit), "attackers cannot buy kits");
            Advance(g, .07); Check(!g.Economy.Buy(1, BuyItem.Deagle), "no live purchase");
        }
        using (var g = new DemolitionGame(Seats(), new Map(), Fast()))
        {
            Begin(g);
            for (int i=0; i<12; i++)
            {
                foreach (var p in g.Teams.Players.Where(p => !g.Teams.IsAttacker(p))) p.Character.Vitals.ApplyDamage(1000,true);
                Advance(g,.12);
            }
            Check(g.Round.Number == 13 && g.Teams.Swapped && g.Round.ScoreA == 12, "halftime changes roles but preserves team scores");
            Check(g.Teams.Find(1).Money == 800, "halftime resets economy");
            foreach (var p in g.Teams.Players.Where(g.Teams.IsAttacker)) p.Character.Vitals.ApplyDamage(1000,true);
            Advance(g,.08); Check(g.Round.Phase == RoundPhase.Finished && g.Round.ScoreA == 13, "first to 13 ends match");
        }
        using (var g = new DemolitionGame(Seats(), new Map(), Fast()))
        { Advance(g,120.01); Check(g.Round.Phase == RoundPhase.Aborted, "load timeout cancels match"); }
        using (var g = new DemolitionGame(Seats(), new Map(), Fast()))
        {
            Begin(g);
            for(int round=1;round<=24;round++)
            {
                int loser=round%2;
                foreach(var p in g.Teams.Players.Where(p=>p.Seat.Team==loser))p.Character.Vitals.ApplyDamage(1000,true);
                Advance(g,.12);
            }
            Check(g.Round.Phase==RoundPhase.Finished&&g.Round.ScoreA==12&&g.Round.ScoreB==12,"24 rounds end in draw");
        }
        using(var g = new DemolitionGame(Seats(),new Map(),Fast()))
        {
            Begin(g);g.Teams.Connection(1,false);Advance(g,.2);Check(!g.Teams.Find(1).Abandoned,"disconnect preserves slot");
            g.Teams.Connection(1,true);Check(g.Teams.Find(1).Connected&&!g.Teams.Find(1).Abandoned,"reconnect restores same character");
        }
        var snapshot=new DemolitionSnapshot{Tick=9,Round=1,Actors=Seats().Select(s=>new DemolitionActorState{Motion=new BattleState{Id=s.Id,Health=100},Weapon="weapon_rif_m4a1_silencer"}).ToArray()};
        var bytes=DemolitionProtocol.Snapshot(snapshot);using var reader=DemolitionProtocol.Read(bytes,out var kind);
        var decoded=DemolitionProtocol.Snapshot(reader);Check(kind==DemolitionPacket.Snapshot&&decoded.Actors.Length==10&&decoded.Tick==9,"ten-seat wire roundtrip");
    }
    sealed class Map : IDemolitionMap
    {
        public bool InSite(DemolitionPoint p, out int site) { site = 0; return p.X >= 9; }
        public bool CanReach(DemolitionPoint a, DemolitionPoint b) => true;
    }
    sealed class Allocator : IDemolitionAllocator
    {
        public readonly Dictionary<string, MatchSeat[]> Matches = new(); public bool Done;
        public int Start(string id, MatchSeat[] seats) { Matches.Add(id,seats); return 7779; }
        public AllocationState Status(string id) => Done ? AllocationState.Completed : AllocationState.Ready;
        public string Result(string id) => "done";
        public DemolitionResult Outcome(string id) => new();
        public void Release(string id) { }
        public void Dispose() { }
    }
}
