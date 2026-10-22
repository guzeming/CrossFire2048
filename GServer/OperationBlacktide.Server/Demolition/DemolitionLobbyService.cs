using OperationBlacktide.Shared.Modes.Demolition;
using System.Security.Cryptography;

namespace OperationBlacktide.Server.Demolition;

public enum AllocationState { Starting, Ready, Completed, Failed }
public interface IDemolitionAllocator : IDisposable
{
    int Start(string id, MatchSeat[] seats);
    AllocationState Status(string id);
    string Result(string id);
    DemolitionResult Outcome(string id);
    void Release(string id);
}

/// <summary>Account-server authority. All commands and the monotonic timer share one lock.</summary>
public sealed class DemolitionLobbyService : IDisposable
{
    sealed class Member
    {
        public string Id = "", Name = "", Party = "", Room = "", Match = "", Pending = "", Notice = "";
        public bool Online = true, Ready;
        public int Team;
        public double OfflineAt;
    }
    sealed class Party
    {
        public string Code = "", Leader = "";
        public List<string> Users = new();
        public bool Queued, AllowAI;
        public double Since;
    }
    sealed class Room
    {
        public string Code = "", Name = "", Host = "", Match = "";
        public bool Private, AllowAI;
        public byte[] Salt = Array.Empty<byte>(), Password = Array.Empty<byte>();
        public List<string> Users = new();
        public double StartAt = -1;
    }
    sealed class Match
    {
        public string Id = "", Room = "";
        public int Port;
        public double Since;
        public MatchSeat[] Seats = Array.Empty<MatchSeat>();
        public AllocationState State;
    }
    sealed class Confirmation
    {
        public string Id = "";
        public double Deadline;
        public List<(Party party,int team)> Parties = new();
        public HashSet<string> Accepted = new();
    }
    readonly object gate = new();
    readonly Dictionary<string, Member> users = new();
    readonly Dictionary<string, Party> parties = new();
    readonly Dictionary<string, Room> rooms = new();
    readonly Dictionary<string, Match> matches = new();
    readonly Dictionary<string, Confirmation> confirmations = new();
    readonly IDemolitionAllocator allocator;
    readonly byte[] key;
    readonly DemolitionRatings ratings;
    public DemolitionLobbyService(IDemolitionAllocator allocator, byte[] key, DemolitionRatings? ratings = null)
    { this.allocator = allocator; this.key = key; this.ratings = ratings ?? new DemolitionRatings(); }

    public DemolitionResponse Handle(string userId, string name, DemolitionRequest request, double now)
    {
        lock (gate)
        {
            if (!users.TryGetValue(userId, out var user)) users.Add(userId, user = new Member { Id = userId, Name = name });
            user.Online = true;
            string error = "";
            try { Execute(user, request, now); }
            catch (InvalidOperationException e) { error = e.Message; }
            var result = View(user, now);
            result.RequestId = request.RequestId; result.Success = error.Length == 0;
            result.Message = error.Length > 0 ? error : user.Notice; user.Notice = "";
            return result;
        }
    }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static string Short(string value, int max) => (value ?? "").Trim().Substring(0, Math.Min((value ?? "").Trim().Length, max));
    string Code() { string value; do value = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(); while (rooms.ContainsKey(value) || parties.ContainsKey(value)); return value; }
    Party OwnParty(Member u)
    {
        if (u.Party.Length > 0) return parties[u.Party];
        var p = new Party { Code = Code(), Leader = u.Id }; p.Users.Add(u.Id); parties.Add(p.Code, p); u.Party = p.Code; return p;
    }
    void Cancel(Party p)
    {
        p.Queued = false;
        foreach (string id in p.Users) users[id].Notice = "匹配已取消";
    }
    void Execute(Member u, DemolitionRequest r, double now)
    {
        Require(r != null && Enum.IsDefined(typeof(LobbyAction), r.Action), "无效操作");
        if (r.Action == LobbyAction.Poll || r.Action == LobbyAction.Reconnect) return;
        if (r.Action == LobbyAction.AcceptMatch || r.Action == LobbyAction.DeclineMatch)
        {
            Require(u.Pending.Length>0 && confirmations.ContainsKey(u.Pending), "当前没有待确认对局");
            var confirm=confirmations[u.Pending];
            if(r.Action==LobbyAction.DeclineMatch)CancelConfirmation(confirm,u.Id);
            else { Require(now<confirm.Deadline,"确认已超时");confirm.Accepted.Add(u.Id); }
            return;
        }
        Require(u.Pending.Length==0,"请先确认或取消找到的对局");
        Require(u.Match.Length == 0, "已分配对局，请进入或重连原对局");
        switch (r.Action)
        {
            case LobbyAction.CreateParty: Require(u.Room.Length == 0, "请先离开房间"); OwnParty(u); break;
            case LobbyAction.JoinParty:
                Require(u.Room.Length == 0 && parties.TryGetValue(r.Code ?? "", out _), "队伍不存在或你仍在房间内");
                var target = parties[r.Code];
                Require(target.Users.Count < 5 && target.Users.All(id => users[id].Match.Length == 0 && users[id].Pending.Length == 0), "队伍已满、正在确认或已开局");
                Require(target.Code != u.Party, "已经在该队伍");
                LeaveParty(u); Cancel(target); target.Users.Add(u.Id); u.Party = target.Code; break;
            case LobbyAction.LeaveParty: LeaveParty(u); break;
            case LobbyAction.TransferParty:
                var transfer = OwnParty(u); Require(transfer.Leader == u.Id && transfer.Users.Contains(r.Target), "只有队长可以转让给队员");
                Cancel(transfer); transfer.Leader = r.Target; break;
            case LobbyAction.Queue:
                Require(u.Room.Length == 0, "请先离开自定义房间");
                var party = OwnParty(u); Require(party.Leader == u.Id, "只有队长可以开始匹配");
                Require(!party.Queued && party.Users.All(id => users[id].Online && users[id].Match.Length == 0), "队员离线或正在匹配/对局");
                party.AllowAI = r.AllowAI; party.Since = now; party.Queued = true;
                foreach (string id in party.Users) users[id].Notice = r.AllowAI ? "已开启 AI 补齐；30 秒后自动开局" : "正在匹配真人";
                break;
            case LobbyAction.CancelQueue: if (u.Party.Length > 0) Cancel(parties[u.Party]); break;
            case LobbyAction.CreateRoom:
                Require(u.Room.Length == 0 && (u.Party.Length == 0 || parties[u.Party].Users.Count == 1), "请先离开当前房间或多人队伍");
                LeaveParty(u);
                var room = new Room { Code = Code(), Host = u.Id }; Configure(room, r); room.Users.Add(u.Id); rooms.Add(room.Code, room);
                u.Room = room.Code; u.Team = 0; u.Ready = false; break;
            case LobbyAction.JoinRoom:
                Require(u.Room.Length == 0 && (u.Party.Length == 0 || parties[u.Party].Users.Count == 1), "请先离开当前房间或多人队伍");
                Require(rooms.TryGetValue(r.Code ?? "", out var join), "房间不存在");
                Require(join.Match.Length == 0 && join.Users.Count < 10, "房间已满或已开局");
                Require(join.Password.Length == 0 || CryptographicOperations.FixedTimeEquals(join.Password, Hash(r.Password, join.Salt)), "房间密码错误");
                LeaveParty(u); join.StartAt = -1; join.Users.Add(u.Id); u.Room = join.Code;
                u.Team = join.Users.Count(id => users[id].Team == 0) < 5 ? 0 : 1; u.Ready = false; break;
            case LobbyAction.LeaveRoom: LeaveRoom(u); break;
            case LobbyAction.ConfigureRoom:
                var config = HostRoom(u); Configure(config, r); ResetReady(config); break;
            case LobbyAction.SelectTeam:
                var teamRoom = UserRoom(u); Require(r.Team == 0 || r.Team == 1, "无效队伍");
                Member changed = string.IsNullOrEmpty(r.Target) ? u : users.GetValueOrDefault(r.Target);
                Require(changed != null && changed.Room == teamRoom.Code && (changed == u || teamRoom.Host == u.Id), "没有调整权限");
                Require(teamRoom.Users.Count(id => users[id].Team == r.Team && id != changed.Id) < 5, "该队已满");
                changed.Team = r.Team; ResetReady(teamRoom); break;
            case LobbyAction.Ready:
                var readyRoom = UserRoom(u); u.Ready = r.Ready; if (!u.Ready) readyRoom.StartAt = -1; break;
            case LobbyAction.StartRoom:
                var start = HostRoom(u);
                Require(start.Users.All(id => users[id].Ready && users[id].Online), "需要所有成员在线并准备");
                Require(start.AllowAI || start.Users.Count == 10, "标准爆破需要 10 人，或开启 AI 补齐");
                Require(start.StartAt < 0, "正在倒计时"); start.StartAt = now + 5; break;
            case LobbyAction.Kick:
                var kick = HostRoom(u); Require(r.Target != u.Id && kick.Users.Contains(r.Target), "请选择其他房间成员");
                users[r.Target].Notice = "已被移出房间"; LeaveRoom(users[r.Target]); break;
            case LobbyAction.TransferHost:
                var host = HostRoom(u); Require(host.Users.Contains(r.Target), "请选择房间成员"); host.Host = r.Target; break;
            default: throw new InvalidOperationException("暂不支持的操作");
        }
    }
    static byte[] Hash(string password, byte[] salt) => Rfc2898DeriveBytes.Pbkdf2(password ?? "", salt, 10000, HashAlgorithmName.SHA256, 32);
    void Configure(Room room, DemolitionRequest r)
    {
        Require((r.Password ?? "").Length <= 32, "密码最多 32 字符");
        room.Name = Short(r.Name, 24); if (room.Name.Length == 0) room.Name = "5v5 爆破房间";
        room.Private = r.IsPrivate; room.AllowAI = r.AllowAI;
        if (room.Salt.Length == 0 || r.ChangePassword)
        {
            room.Salt = RandomNumberGenerator.GetBytes(16);
            room.Password = string.IsNullOrEmpty(r.Password) ? Array.Empty<byte>() : Hash(r.Password, room.Salt);
        }
    }
    Room UserRoom(Member u) { Require(u.Room.Length > 0 && rooms.ContainsKey(u.Room), "当前不在房间内"); return rooms[u.Room]; }
    Room HostRoom(Member u) { var room = UserRoom(u); Require(room.Host == u.Id, "只有房主可以执行此操作"); return room; }
    void ResetReady(Room r) { r.StartAt = -1; foreach (string id in r.Users) users[id].Ready = false; }
    void LeaveParty(Member u)
    {
        if (u.Party.Length == 0) return;
        var p = parties[u.Party]; Cancel(p); p.Users.Remove(u.Id); u.Party = "";
        if (p.Users.Count == 0) parties.Remove(p.Code); else if (p.Leader == u.Id) p.Leader = p.Users[0];
    }
    void LeaveRoom(Member u)
    {
        if (u.Room.Length == 0) return;
        var r = rooms[u.Room]; r.Users.Remove(u.Id); u.Room = ""; u.Ready = false;
        if (r.Users.Count == 0) rooms.Remove(r.Code);
        else { if (r.Host == u.Id) r.Host = r.Users.FirstOrDefault(id => users[id].Online) ?? r.Users[0]; ResetReady(r); }
    }
    public void Disconnect(string id, double now)
    {
        lock (gate)
        {
            if (!users.TryGetValue(id, out var u)) return;
            u.Online = false; u.OfflineAt = now; u.Ready = false;
            if(u.Pending.Length>0 && confirmations.TryGetValue(u.Pending,out var confirm))CancelConfirmation(confirm,id);
            if (u.Party.Length > 0) Cancel(parties[u.Party]);
            if (u.Room.Length > 0)
            {
                var r = rooms[u.Room]; r.StartAt = -1;
                if (r.Host == id) r.Host = r.Users.FirstOrDefault(x => users[x].Online) ?? id;
            }
        }
    }

    public void Tick(double now)
    {
        lock (gate)
        {
            foreach(var confirm in confirmations.Values.ToArray())
            {
                if(now>=confirm.Deadline) { CancelConfirmation(confirm, ""); continue; }
                if(!confirm.Parties.SelectMany(x=>x.party.Users).All(id=>confirm.Accepted.Contains(id)))
                {
                    // Another player's confirmation must never extend an opted-in party's 30-second deadline.
                    if(confirm.Parties.Any(x=>x.party.AllowAI && now-x.party.Since>=30))CancelConfirmation(confirm, "");
                    continue;
                }
                var humans=confirm.Parties.SelectMany(x=>x.party.Users.Select(id=>(users[id],x.team))).ToList();
                foreach(var x in humans)x.Item1.Pending="";
                confirmations.Remove(confirm.Id);Launch(humans,"",now);
            }
            foreach (var u in users.Values.Where(u => !u.Online && u.Match.Length == 0 && now - u.OfflineAt > 180).ToArray())
            { LeaveParty(u); LeaveRoom(u); users.Remove(u.Id); }
            foreach (var m in matches.Values.ToArray())
            {
                try { m.State = allocator.Status(m.Id); } catch { m.State = AllocationState.Failed; }
                if (m.State == AllocationState.Starting && now - m.Since > 120) m.State = AllocationState.Failed;
                if (m.State != AllocationState.Completed && m.State != AllocationState.Failed) continue;
                string notice = m.State == AllocationState.Completed ? allocator.Result(m.Id) : "对局服务启动失败或异常中止，本场不计胜负";
                if (m.State == AllocationState.Completed && m.Room.Length == 0)
                {
                    try { ratings.Record(m.Id, m.Seats, allocator.Outcome(m.Id)); }
                    catch (Exception e) { ServerLog.Warn("隐藏实力分保存失败：" + e.Message); }
                }
                foreach (var seat in m.Seats.Where(s => !s.IsBot))
                    if (users.TryGetValue(seat.UserId, out var u)) { u.Match = ""; u.Notice = notice; u.Ready = false; }
                if (m.Room.Length > 0 && rooms.TryGetValue(m.Room, out var r)) { r.Match = ""; ResetReady(r); }
                allocator.Release(m.Id); matches.Remove(m.Id);
            }
            foreach (var r in rooms.Values.Where(r => r.Match.Length == 0 && r.StartAt >= 0 && now >= r.StartAt).ToArray())
            {
                r.StartAt = -1;
                if (!r.Users.All(id => users[id].Ready && users[id].Online)) continue;
                Launch(r.Users.Select(id => (users[id], users[id].Team)).ToList(), r.Code, now);
            }
            // Bound work per tick. Every selected party remains intact; 6x6 DP caps the search space.
            for (int attempt = 0; attempt < 8; attempt++)
            {
                var queue = parties.Values.Where(p => p.Queued).OrderBy(p => p.Since).ToArray();
                if (queue.Length == 0) break;
                List<(Party party, int team)> selected = null;
                var expired = queue.FirstOrDefault(p => p.AllowAI && now - p.Since >= 30);
                if (expired != null) selected = Pack(expired, queue, true, now);
                foreach (var anchor in selected == null ? queue.Take(40) : Array.Empty<Party>())
                {
                    var candidate = Pack(anchor, queue, false, now);
                    if (candidate.Sum(x => x.party.Users.Count) == 10) { selected = candidate; break; }
                }
                if (selected == null) break;
                var seats = selected.SelectMany(x => x.party.Users.Select(id => (users[id], x.team))).ToList();
                foreach (var x in selected) x.party.Queued = false;
                if(seats.Count==10 && selected.Any(x=>!x.party.AllowAI))
                {
                    var confirm=new Confirmation { Id=Guid.NewGuid().ToString("N"),Deadline=now+15,Parties=selected };
                    foreach(var x in selected)foreach(string id in x.party.Users)
                    {users[id].Pending=confirm.Id;if(x.party.AllowAI)confirm.Accepted.Add(id);}
                    confirmations.Add(confirm.Id,confirm);
                }
                else Launch(seats, "", now);
            }
        }
    }
    void CancelConfirmation(Confirmation confirm,string declining)
    {
        foreach(var x in confirm.Parties)
        {
            bool resume=!x.party.Users.Contains(declining) && x.party.Users.All(id=>confirm.Accepted.Contains(id)&&users[id].Online);
            x.party.Queued=resume;
            foreach(string id in x.party.Users) {users[id].Pending="";users[id].Notice=resume?"有人未确认，已保留等待优先级继续匹配":"对局确认已取消";}
        }
        confirmations.Remove(confirm.Id);
    }
    double PartySkill(Party p) => p.Users.Average(id => ratings.Get(id));
    double Balance(List<(Party party, int team)> selected)
    {
        var a=selected.Where(x=>x.team==0).SelectMany(x=>x.party.Users).Select(ratings.Get).ToArray();
        var b=selected.Where(x=>x.team==1).SelectMany(x=>x.party.Users).Select(ratings.Get).ToArray();
        return a.Length==0||b.Length==0?double.MaxValue:Math.Abs(a.Average()-b.Average())+.25*Math.Abs(a.Max()-b.Max());
    }
    List<(Party party, int team)> Pack(Party anchor, Party[] queue, bool aiOnly, double now)
    {
        var dp = new Dictionary<(int a, int b), List<(Party, int)>> { [(anchor.Users.Count, 0)] = new() { (anchor, 0) } };
        double tolerance=Math.Min(3000,250+Math.Max(0,now-anchor.Since)*10);
        foreach (var p in queue.Where(p => p != anchor && (!aiOnly || p.AllowAI) && Math.Abs(PartySkill(p)-PartySkill(anchor))<=tolerance)
            .OrderByDescending(p=>p.Users.Count==anchor.Users.Count).ThenBy(p=>p.Since).Take(100))
        {
            foreach (var entry in dp.ToArray())
                for (int team = 0; team < 2; team++)
                {
                    var next = (a: entry.Key.a + (team == 0 ? p.Users.Count : 0), b: entry.Key.b + (team == 1 ? p.Users.Count : 0));
                    if (next.a > 5 || next.b > 5) continue;
                    var candidate = new List<(Party, int)>(entry.Value) { (p, team) };
                    if (!dp.TryGetValue(next,out var current) || Balance(candidate) < Balance(current)) dp[next] = candidate;
                }
            if (dp.ContainsKey((5, 5))) return dp[(5, 5)];
        }
        return dp.OrderByDescending(x => x.Key.a + x.Key.b).ThenBy(x => Math.Abs(x.Key.a - x.Key.b)).First().Value;
    }
    void Launch(List<(Member user, int team)> humans, string room, double now)
    {
        string id = Guid.NewGuid().ToString("N");
        int flip = room.Length == 0 ? RandomNumberGenerator.GetInt32(2) : 0;
        var seats = humans.Select((x, i) => new MatchSeat { Id = i + 1, UserId = x.user.Id, Name = x.user.Name, Team = x.team ^ flip }).ToList();
        for (int team = 0; team < 2; team++)
            while (seats.Count(s => s.Team == team) < 5)
            {
                int slot = seats.Count + 1;
                seats.Add(new MatchSeat { Id = slot, UserId = "bot-" + slot, Name = "AI " + slot, IsBot = true, Team = team });
            }
        try
        {
            var match = new Match { Id = id, Room = room, Seats = seats.ToArray(), Since = now, Port = allocator.Start(id, seats.ToArray()) };
            matches.Add(id, match);
            foreach (var x in humans) x.user.Match = id;
            if (room.Length > 0) rooms[room].Match = id;
        }
        catch (Exception)
        {
            allocator.Release(id);
            foreach (var x in humans) x.user.Notice = "无法启动战斗服，请检查服务端构建后重试";
            if (room.Length > 0) ResetReady(rooms[room]);
        }
    }
    LobbyMember MemberView(Member u) => new() { UserId = u.Id, Name = u.Name, Team = u.Team, Ready = u.Ready, Online = u.Online };
    RoomView RoomView(Room r, double now, bool detail) => new()
    {
        Code = r.Code, Name = r.Name, HostId = r.Host, IsPrivate = r.Private, HasPassword = r.Password.Length > 0, AllowAI = r.AllowAI,
        Started = r.Match.Length > 0, StartsIn = r.StartAt >= 0 ? (float)Math.Max(0, r.StartAt - now) : -1,
        Members = r.Users.Select(id => detail ? MemberView(users[id]) : new LobbyMember { Team = users[id].Team }).ToArray()
    };
    DemolitionResponse View(Member u, double now)
    {
        var v = new DemolitionResponse();
        if (u.Party.Length > 0)
        {
            var p = parties[u.Party]; v.PartyCode = p.Code; v.LeaderId = p.Leader; v.AllowAI = p.AllowAI;
            v.Party = p.Users.Select(id => MemberView(users[id])).ToArray();
            if (p.Queued) { v.Phase = LobbyPhase.Queued; v.WaitSeconds = (float)Math.Max(0, now - p.Since); }
        }
        if (u.Room.Length > 0) v.Room = RoomView(rooms[u.Room], now, true);
        if (u.Pending.Length>0 && confirmations.TryGetValue(u.Pending,out var confirm))
        {v.Phase=LobbyPhase.Confirming;v.ConfirmSeconds=(float)Math.Max(0,confirm.Deadline-now);v.Confirmed=confirm.Accepted.Contains(u.Id);}
        v.Rooms = rooms.Values.Where(r => !r.Private && r.Match.Length == 0).Take(40).Select(r => RoomView(r, now, false)).ToArray();
        if (u.Match.Length > 0 && matches.TryGetValue(u.Match, out var m))
        {
            v.Phase = m.State == AllocationState.Starting ? LobbyPhase.Allocating : LobbyPhase.Loading;
            if (m.State == AllocationState.Ready) v.Match = new MatchAssignment { MatchId = m.Id, Port = m.Port, Seats = m.Seats,
                HasAI = m.Seats.Any(s => s.IsBot), Ticket = DemolitionTicket.Issue(key, m.Id, u.Id, DateTimeOffset.UtcNow.ToUnixTimeSeconds()) };
        }
        return v;
    }
    public void Dispose() { lock (gate) allocator.Dispose(); }
}
