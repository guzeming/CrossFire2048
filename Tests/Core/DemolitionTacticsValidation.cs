using OperationBlacktide.Shared.AI;
using OperationBlacktide.Shared.Battle;
using OperationBlacktide.Shared.Characters;

static class DemolitionTacticsValidation
{
    public static void Run(Action<bool,string> check)
    {
        var ids = Enumerable.Range(1,5).ToArray();
        var plans = Enumerable.Range(1,8).Select(round => DemolitionTactics.Plan(true,round,123,ids,3,(id,lane) => 20)).ToArray();
        check(plans.Select(p=>p.Name).Distinct().Count()==4,"Viable openings never vary across rounds");
        foreach(var plan in plans)
        {
            check(plan.Orders.Select(o=>o.PlayerId).Distinct().Count()==5,"Duplicate opening assignment");
            check(plan.Orders.Single(o=>o.PlayerId==3).Role==OpeningRole.BombCarrier,"Carrier role lost");
            var carrier=plan.Orders.Single(o=>o.PlayerId==3);
            check(plan.Orders.Count(o=>o.Lane==carrier.Lane)>=3 && carrier.Delay>0,"Bomb carrier is isolated or leads the entry");
            check(plan.Orders.Count(o=>o.Lane==OpeningLane.MidB)<=2,"Whole team sent through middle");
        }
        OpeningLane[] lanes={OpeningLane.GuardLongA,OpeningLane.GuardShortA,OpeningLane.GuardBPlatform,OpeningLane.GuardBDoors,OpeningLane.GuardMid};
        var defense=DemolitionTactics.Plan(false,1,123,ids,0,(id,lane)=>lane==lanes[id-1]?1:100);
        check(defense.Orders.All(o=>o.Lane==lanes[o.PlayerId-1]),"Defense ignores spawn travel advantage");
        var swapped=DemolitionTactics.Plan(false,2,123,ids,0,(id,lane)=>lane==lanes[5-id]?1:100);
        check(swapped.Orders.All(o=>o.Lane==lanes[5-o.PlayerId]),"New spawns retain old opening roles");
        var a=DemolitionTactics.SpawnSlots(15,1,123);var b=DemolitionTactics.SpawnSlots(15,2,123);
        check(a.Distinct().Count()==5&&!a.SequenceEqual(b)&&a.SequenceEqual(DemolitionTactics.SpawnSlots(15,1,123)),"Round spawn selection is not stable/varied/unique");
        using var ai=new AIController(1,new[]{new WorldVector(10,0,0)},new World());
        var self=new BattleState{Id=1,Health=100,Ammo=12,Reserve=24,Team=BattleTeam.CT}; ai.Decision.Reset(default,0);
        check(!ai.Decision.Think(self,Array.Empty<BattleState>(),0,false,12).Has(InputButtons.Reload),"Full pistol locks AI in reload");
        var enemy=new BattleState{Id=2,Health=100,Z=20,Team=BattleTeam.T};
        var hold=ai.Decision.Think(self,new[]{enemy},6,false,12);
        check(hold.X==0&&hold.Z==0&&ai.Decision.Mode==BotMode.Engage,"Tactical guard chases enemy away from assignment");
        Console.WriteLine("DEMOLITION_TACTICS_CORE_PASS");
    }
    sealed class World:IBotEnvironment
    { public bool IsOccluded(WorldVector a,WorldVector b,float distance)=>false;public WorldVector[] FindPath(WorldVector a,WorldVector b)=>new[]{a,b}; }
}
