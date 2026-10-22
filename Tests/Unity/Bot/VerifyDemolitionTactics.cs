using System;
using System.IO;
using System.Linq;
using OperationBlacktide.BattleServer;
using OperationBlacktide.EditorTools;
using OperationBlacktide.Shared.AI;
using OperationBlacktide.Shared.Battle;
using OperationBlacktide.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class VerifyDemolitionTactics
{
    [Serializable] sealed class Routes { public DemolitionRoute[] routes; public Vector3[] attackers, defenders; }
    public static void Run()
    {
        string[] args = Environment.GetCommandLineArgs();
        string output = args[Array.IndexOf(args, "--test-output") + 1];
        BuildBattleArena.Prepare(); EditorSceneManager.OpenScene(BuildBattleArena.ServerScene);
        var map = UnityEngine.Object.FindObjectOfType<DemolitionMap>(); map.Validate();
        CheckPlans(map);
        File.WriteAllText(Path.Combine(output, "routes.json"), JsonUtility.ToJson(new Routes { routes=map.OpeningRoutes, attackers=map.AttackSpawns, defenders=map.DefendSpawns }, true));
        Require(map.AttackSpawns.Average(p => p.x) > 15 && map.DefendSpawns.Average(p => p.x) < -55, "Original T/CT spawn regions");
        foreach (var pool in new[] { map.AttackSpawns, map.DefendSpawns })
            for (int i=0;i<pool.Length;i++) for (int j=i+1;j<pool.Length;j++) Require((pool[i]-pool[j]).magnitude > .75f, "Overlapping spawns");
        foreach (var route in map.OpeningRoutes)
        {
            var pool = (int)route.lane < 4 ? map.AttackSpawns : map.DefendSpawns;
            Vector3 start = pool.OrderBy(p => DemolitionBotNavigation.Distance(p, route.waypoints[0])).First();
            var motor = CharacterSimulation.CreatePawn(900);
            var state = new BattleState { Id=900, Health=100, X=start.x, Y=start.y, Z=start.z };
            CharacterSimulation.Restore(motor, state); Physics.SyncTransforms();
            var navigation = new DemolitionBotNavigation();
            Vector3 progress = start; int progressAt=0; bool arrived=false;
            for (int tick=0;tick<120*60;tick++)
            {
                Vector3 position = motor.transform.position;
                var goal = navigation.RouteGoal(route, position);
                if (navigation.Waypoint == route.waypoints.Length-1 && DemolitionBotNavigation.Near(position, goal, .8f)) { arrived=true; break; }
                var command = new BattleInput { Yaw=state.Yaw };
                navigation.Move(ref command, state, goal, route.lookAt, tick);
                CharacterSimulation.Step(motor, ref state, command); Physics.SyncTransforms();
                if ((position-progress).sqrMagnitude > .25f) { progress=position; progressAt=tick; }
                if (tick-progressAt > 240) throw new Exception("Route stuck: " + route.lane + " gate=" + navigation.Waypoint + " position=" + position + " goal=" + goal);
            }
            Require(arrived, "Route timeout: " + route.lane);
            UnityEngine.Object.DestroyImmediate(motor.gameObject);
            Debug.Log("TACTICAL_ROUTE_PASS " + route.lane);
        }
        File.WriteAllText(Path.Combine(output, "result.txt"), "DEMOLITION_TACTICS_PASS");
        Debug.Log("DEMOLITION_TACTICS_PASS");
    }
    public static void Plans()
    {
        EditorSceneManager.OpenScene(BuildBattleArena.ServerScene);
        CheckPlans(UnityEngine.Object.FindObjectOfType<DemolitionMap>());
    }
    public static void BuildServer()
    {
        Plans();
        BuildBattleArena.BuildPreparedScenes(new[] { BuildBattleArena.ServerScene }, "DemolitionServer/Blacktide.DemolitionServer.exe", false);
    }
    static void CheckPlans(DemolitionMap map)
    {
        var names = new System.Collections.Generic.HashSet<string>();
        int attacksA = 0;
        int seed = DemolitionTactics.Seed("tactics-alpha");
        for (int round=1;round<=24;round++)
        {
            var slots=DemolitionTactics.SpawnSlots(map.AttackSpawns.Length,round,seed);
            var ids=Enumerable.Range(1,5).ToArray();
            var plan=DemolitionTactics.Plan(true,round,seed,ids,(round-1)%5+1,
                (id,lane)=>DemolitionBotNavigation.Distance(map.AttackSpawns[slots[id-1]],map.Route(lane).waypoints[0]));
            names.Add(plan.Name); Debug.Log("TACTICAL_PLAN round="+round+" "+plan.Name);
            if (plan.Site == 0) attacksA++;
        }
        Require(names.Count>=3&&names.Any(n=>n.StartsWith("A "))&&names.Any(n=>n.StartsWith("B ")),"Real spawn layout collapses opening variety: "+string.Join(",",names));
        Require(attacksA >= 6 && attacksA <= 18, "Actual spawns over-favour one site: A rounds=" + attacksA);
        Debug.Log("TACTICAL_PLANS_PASS count="+names.Count);
    }
    static void Require(bool pass, string message) { if (!pass) throw new Exception(message); }
}
