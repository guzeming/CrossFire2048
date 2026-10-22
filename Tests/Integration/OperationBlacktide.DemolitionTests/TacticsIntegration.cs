using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using OperationBlacktide.Client.Network;
using OperationBlacktide.Shared.Battle;
using OperationBlacktide.Shared.Modes.Demolition;

static class TacticsIntegration
{
    public static async Task Run(string executable)
    {
        string output=Path.GetFullPath(".build-check/tactics-network-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(output);
        foreach (string match in new[]{"tactics-alpha","tactics-echo"})
        {
            string folder=Path.Combine(output,match);Directory.CreateDirectory(folder);
            byte[] key=RandomNumberGenerator.GetBytes(32);string keyFile=Path.Combine(folder,"key");File.WriteAllBytes(keyFile,key);
            using var socket=new UdpClient(0);int port=((IPEndPoint)socket.Client.LocalEndPoint).Port;socket.Close();
            var seats=Enumerable.Range(1,10).Select(i=>new MatchSeat{Id=i,Team=i>=2&&i<=6?0:1,UserId="test-"+i,Name="Tester "+i,IsBot=i!=1}).ToArray();
            var manifest=new DemolitionManifest{MatchId=match,Port=port,KeyFile=keyFile,ReadyFile=Path.Combine(folder,"ready"),ResultFile=Path.Combine(folder,"result"),Seats=seats};
            string path=Path.Combine(folder,"manifest.json"), log=Path.Combine(folder,"server.log");
            File.WriteAllText(path,JsonSerializer.Serialize(manifest,new JsonSerializerOptions{IncludeFields=true}));
            var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true};
            foreach(var arg in new[]{"-batchmode","-nographics","--demolition-manifest",path,"-logFile",log})start.ArgumentList.Add(arg);
            using var process=Process.Start(start);
            try
            {
                var deadline=Stopwatch.StartNew();
                while(!File.Exists(manifest.ReadyFile)&&deadline.Elapsed.TotalSeconds<40&&!process.HasExited)await Task.Delay(100);
                Require(File.Exists(manifest.ReadyFile),"Server did not start: "+log);
                using var client=new DemolitionConnection();DemolitionSnapshot latest=null;BattleState[] frozen=null;
                var progress=new Dictionary<int,float>();int samples=0,shots=0;double liveAt=-1;string failure="";
                using var trace=new StreamWriter(Path.Combine(folder,"positions.csv"));trace.WriteLine("elapsed,round,phase,id,team,x,y,z,hp");
                client.Disconnected+=s=>failure=s;client.Shot+=(a,b,c,d)=>shots++;
                client.Snapshot+=s=>{
                    latest=s; samples++;
                    if(frozen==null&&s.Phase==RoundPhase.Freeze)frozen=s.Actors.Select(a=>a.Motion).ToArray();
                    if(frozen!=null&&s.Round==1)foreach(var actor in s.Actors.Where(a=>a.Motion.IsBot)){
                        var p=actor.Motion;var initial=frozen.Single(a=>a.Id==p.Id);float distance=MathF.Sqrt((p.X-initial.X)*(p.X-initial.X)+(p.Z-initial.Z)*(p.Z-initial.Z));
                        progress[p.Id]=Math.Max(progress.GetValueOrDefault(p.Id),distance);
                    }
                    if(samples%5==0)foreach(var actor in s.Actors){var p=actor.Motion;trace.WriteLine(FormattableString.Invariant($"{deadline.Elapsed.TotalSeconds:F2},{s.Round},{s.Phase},{p.Id},{p.Team},{p.X:F2},{p.Y:F2},{p.Z:F2},{p.Health}"));}
                };
                client.Connect(new MatchAssignment{MatchId=match,Host="127.0.0.1",Port=port,Ticket=DemolitionTicket.Issue(key,match,"test-1",DateTimeOffset.UtcNow.ToUnixTimeSeconds())});
                deadline.Restart();
                while(deadline.Elapsed.TotalSeconds<65&&!process.HasExited)
                {
                    client.Poll();
                    if(latest!=null&&(latest.Phase==RoundPhase.Live||latest.Phase==RoundPhase.Planted)&&liveAt<0)liveAt=deadline.Elapsed.TotalSeconds;
                    if(liveAt>=0&&deadline.Elapsed.TotalSeconds-liveAt>30)break;
                    await Task.Delay(10);
                }
                Require(failure==""&&frozen!=null&&samples>300,"Continuous tactical room snapshots: "+failure);
                Require(frozen.Where(p=>p.Team==BattleTeam.T).All(p=>p.X>15)&&frozen.Where(p=>p.Team==BattleTeam.CT).All(p=>p.X< -55),"Real T/CT spawn regions");
                Require(progress.Count==9&&progress.Values.All(d=>d>3),"Every pistol-round bot leaves its spawn: "+string.Join(",",progress.Select(x=>x.Key+"="+x.Value.ToString("F1"))));
                Require(progress.Values.Count(d=>d>20)>=4,"Team progresses through routes instead of clustering at spawn");
                Require(shots>0,"Teams meet and fight after taking lanes");
                var alive=latest.Actors.Where(a=>a.Motion.IsBot&&a.Motion.Health>0).Select(a=>a.Motion).ToArray();
                for(int i=0;i<alive.Length;i++)for(int j=i+1;j<alive.Length;j++){
                    var a=alive[i];var b=alive[j];if(a.Team!=b.Team||Math.Abs(a.Y-b.Y)>1.5f)continue;
                    Require((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z)>.25f,"Bots overlap during combat: "+a.Id+" / "+b.Id);
                }
                using var logStream=new FileStream(log,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
                using var logReader=new StreamReader(logStream);
                string[] openings=logReader.ReadToEnd().Split('\n').Where(l=>l.StartsWith("BOT_OPENING round=1 ")).ToArray();
                Require(openings.Length==2&&openings.Any(l=>l.Contains("2A / 2B / mid")),"Server assigns both team openings");
                foreach(string line in openings)Console.WriteLine(line);
                Console.WriteLine("TACTICS_NETWORK_MATCH_PASS "+match+" shots="+shots+" progress="+string.Join(",",progress.Select(x=>x.Key+"="+x.Value.ToString("F1"))));
            }
            finally{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}}
        }
        Console.WriteLine("DEMOLITION_TACTICS_NETWORK_PASS "+output);
    }
    static void Require(bool success,string message){if(!success)throw new Exception(message);}
}
