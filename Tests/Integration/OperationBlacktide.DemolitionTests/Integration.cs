using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OperationBlacktide.Client.Network;
using OperationBlacktide.Server;
using OperationBlacktide.Server.Network;
using OperationBlacktide.Shared.Battle;
using OperationBlacktide.Shared.Modes.Demolition;
using OperationBlacktide.Shared.Protocol;

static class Integration
{
    public static async Task Run(string serverExecutable = null)
    {
        string root=Directory.GetCurrentDirectory();string output=Path.Combine(root,".build-check","demolition-integration-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(output);
        byte[] key=RandomNumberGenerator.GetBytes(32);string keyFile=Path.Combine(output,"key");File.WriteAllBytes(keyFile,key);
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
        using var cts=new CancellationTokenSource();var server=new GameServer(port,Path.Combine(output,"accounts.json"),key,7778,keyFile,
            Path.GetFullPath(serverExecutable ?? Path.Combine(root,"Builds/DemolitionServer/Blacktide.DemolitionServer.exe")));server.Start(cts);
        try
        {
            using var a=await Account.Login(port,"DemoOne");using var b=await Account.Login(port,"DemoTwo");
            await a.Request(LobbyAction.Queue,true);await b.Request(LobbyAction.Queue,true);
            var timer=Stopwatch.StartNew();MatchAssignment ma=null,mb=null;
            while(timer.Elapsed.TotalSeconds<120)
            {
                var sa=await a.Request(LobbyAction.Poll);var sb=await b.Request(LobbyAction.Poll);
                if(timer.Elapsed.TotalSeconds<29&&sa.Match!=null)throw new Exception("AI launched before 30 seconds");
                ma=sa.Match;mb=sb.Match;if(ma!=null&&mb!=null)break;await Task.Delay(250);
            }
            Assert(ma!=null&&mb!=null&&ma.MatchId==mb.MatchId,"shared match after 30 seconds");
            Assert(ma.Seats.Count(s=>s.IsBot)==8,"eight real server bots");
            Console.WriteLine("MATCH_ALLOCATED elapsed="+timer.Elapsed.TotalSeconds.ToString("F1"));
            using var ca=new DemolitionConnection();using var cb=new DemolitionConnection();
            DemolitionSnapshot saLast=null,sbLast=null;int count=0;string disconnected="";bool verticalShot=false;
            ca.Shot+=(shooter,hit,from,to)=>{if(shooter==ca.PlayerId&&to.Y>from.Y&&Math.Abs(to.X-from.X)<.02f&&Math.Abs(to.Z-from.Z)<.02f)verticalShot=true;};
            ca.Snapshot+=s=>{saLast=s;count++;};cb.Snapshot+=s=>sbLast=s;
            ca.Disconnected+=s=>disconnected=s;cb.Disconnected+=s=>disconnected=s;
            ca.Connect(ma);cb.Connect(mb);
            uint qa=0,qb=0;var clock=Stopwatch.StartNew();bool purchased=false;Dictionary<int,float> startX=null;int moneyBefore=0;
            while(clock.Elapsed.TotalSeconds<23)
            {
                ca.Poll();cb.Poll();
                if(ca.Ready&&saLast!=null)
                {
                    var me=saLast.Actors.Single(p=>p.Motion.Id==ca.PlayerId);
                    ca.Input(new BattleInput{Sequence=++qa,Yaw=me.Motion.Yaw,Buttons=InputButtons.Fire},false,new DemolitionPoint(me.Motion.X,me.Motion.Y+50,me.Motion.Z));
                    if(!purchased&&saLast.Phase==RoundPhase.Freeze){moneyBefore=me.Money;ca.Buy(BuyItem.Armor);purchased=true;}
                    if(saLast.Phase==RoundPhase.Live&&startX==null)startX=saLast.Actors.Where(p=>p.Motion.IsBot).ToDictionary(p=>p.Motion.Id,p=>p.Motion.X);
                }
                if(cb.Ready&&sbLast!=null){var me=sbLast.Actors.Single(p=>p.Motion.Id==cb.PlayerId);cb.Input(new BattleInput{Sequence=++qb,Yaw=me.Motion.Yaw},false);}
                await Task.Delay(17);
            }
            Assert(verticalShot,"authority uses 3D aim rather than flat character heading");
            Assert(disconnected.Length==0,"no transport errors: "+disconnected);Assert(count>100,"continuous snapshots");
            Assert(saLast.Actors.Length==10&&sbLast.Actors.Length==10,"ten avatars in both snapshots");
            Assert(purchased&&saLast.Actors.Single(p=>p.Motion.Id==ca.PlayerId).Money==moneyBefore-650,"authoritative buy deduction");
            Assert(startX!=null&&saLast.Actors.Where(p=>p.Motion.IsBot).Any(p=>Math.Abs(p.Motion.X-startX[p.Motion.Id])>.5f),"bots navigate on Dust II");
            int id=ca.PlayerId,life=saLast.Actors.Single(p=>p.Motion.Id==id).Motion.Life;
            ca.Dispose();await Task.Delay(500);
            ma=(await a.Request(LobbyAction.Reconnect)).Match;
            using var rejoin=new DemolitionConnection();DemolitionSnapshot rejoined=null;rejoin.Snapshot+=s=>rejoined=s;rejoin.Connect(ma);clock.Restart();
            while(rejoined==null&&clock.Elapsed.TotalSeconds<8){cb.Poll();rejoin.Poll();await Task.Delay(15);}
            Assert(rejoined!=null&&rejoin.PlayerId==id&&rejoined.Actors.Single(p=>p.Motion.Id==id).Motion.Life==life,"reconnect preserves identity/life");
            File.WriteAllText(Path.Combine(output,"result.txt"),"DEMOLITION_INTEGRATION_PASS");Console.WriteLine("DEMOLITION_INTEGRATION_PASS "+output);
        }
        finally{server.Stop();}
    }
    static void Assert(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("PASS "+message);}
    sealed class Account:IDisposable
    {
        readonly TcpClient socket;readonly StreamReader reader;readonly StreamWriter writer;
        Account(TcpClient socket){this.socket=socket;reader=new StreamReader(socket.GetStream(),Encoding.UTF8);writer=new StreamWriter(socket.GetStream(),new UTF8Encoding(false)){AutoFlush=true};}
        public static async Task<Account> Login(int port,string name)
        {
            var socket=new TcpClient();await socket.ConnectAsync(IPAddress.Loopback,port);var a=new Account(socket);
            await a.Send(MessageType.RegisterRequest,new RegisterRequest{Username=name,Password="DemoTest_918"});await a.Read();
            await a.Send(MessageType.LoginRequest,new LoginRequest{Username=name,Password="DemoTest_918"});var response=MessageCodec.DecodePayload<LoginResponse>(await a.Read());
            if(response.Code!=AuthResultCode.Ok)throw new Exception("login failed");return a;
        }
        Task Send<T>(MessageType type,T payload)=>writer.WriteLineAsync(MessageCodec.Encode(type,payload));
        async Task<NetworkMessage> Read(){string line=await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));return MessageCodec.DecodeEnvelope(line);}
        public async Task<DemolitionResponse> Request(LobbyAction action,bool ai=false)
        {await Send(MessageType.DemolitionRequest,new DemolitionRequest{Action=action,AllowAI=ai});var response=MessageCodec.DecodePayload<DemolitionResponse>(await Read());if(!response.Success)throw new Exception(response.Message);return response;}
        public void Dispose()=>socket.Dispose();
    }
}
