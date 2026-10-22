using System.Text.Json;
using OperationBlacktide.Shared.Modes.Demolition;

namespace OperationBlacktide.Server.Demolition;

/// <summary>Hidden casual Elo. AI, custom rooms and incomplete games never change it.</summary>
public sealed class DemolitionRatings
{
    public sealed class Data
    {
        public Dictionary<string,int> Players { get; set; } = new();
        public List<string> AppliedMatches { get; set; } = new();
    }
    readonly string? path;
    Data data = new();
    public DemolitionRatings(string? path = null)
    {
        this.path=path;
        if(path!=null&&File.Exists(path))data=JsonSerializer.Deserialize<Data>(File.ReadAllText(path))??new();
    }
    public int Get(string user) => data.Players.TryGetValue(user,out int rating)?Math.Clamp(rating,100,3000):1000;
    public void Record(string id,MatchSeat[] seats,DemolitionResult result)
    {
        if(!result.Completed||result.HasAI||seats.Any(s=>s.IsBot)||data.AppliedMatches.Contains(id))return;
        double a=seats.Where(s=>s.Team==0).Average(s=>Get(s.UserId)),b=seats.Where(s=>s.Team==1).Average(s=>Get(s.UserId));
        double expected=1/(1+Math.Pow(10,(b-a)/400)),actual=result.ScoreA==result.ScoreB?.5:result.ScoreA>result.ScoreB?1:0;
        int delta=(int)Math.Round(32*(actual-expected));
        foreach(var seat in seats)data.Players[seat.UserId]=Math.Clamp(Get(seat.UserId)+(seat.Team==0?delta:-delta),100,3000);
        data.AppliedMatches.Add(id);if(data.AppliedMatches.Count>2000)data.AppliedMatches.RemoveAt(0);
        if(path==null)return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path+".tmp",JsonSerializer.Serialize(data));File.Move(path+".tmp",path,true);
    }
}
