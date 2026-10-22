using System;
using OperationBlacktide.Shared.Battle;
using OperationBlacktide.Simulation;
using UnityEngine;
using UnityEngine.AI;

namespace OperationBlacktide.BattleServer
{
    /// <summary>Visits authored lane gates in order. Replanning cannot shortcut directly to the bombsite.</summary>
    public sealed class DemolitionBotNavigation
    {
        readonly NavMeshPath path = new NavMeshPath();
        Vector3[] corners = Array.Empty<Vector3>();
        Vector3 plannedGoal;
        int corner, pathAt;
        bool planned;
        public int Waypoint { get; private set; }
        public void Reset() { Waypoint = 0; planned = false; pathAt = 0; corners = Array.Empty<Vector3>(); }

        public Vector3 RouteGoal(DemolitionRoute route, Vector3 position)
        {
            while (Waypoint < route.waypoints.Length - 1 && Near(position, route.waypoints[Waypoint], .85f)) Waypoint++;
            return route.waypoints[Waypoint];
        }
        public static bool Near(Vector3 a, Vector3 b, float radius) => Mathf.Abs(a.y - b.y) < 1.2f &&
            Vector3.ProjectOnPlane(a - b, Vector3.up).sqrMagnitude <= radius * radius;

        public void Move(ref BattleInput command, BattleState self, Vector3 goal, Vector3 lookAt, int tick, float stopRadius = .65f)
        {
            command.X = command.Z = 0;
            Vector3 position = CharacterSimulation.Position(self);
            if (Near(position, goal, stopRadius)) { Face(ref command, self.Yaw, lookAt - position); return; }
            if (!planned || (goal - plannedGoal).sqrMagnitude > .25f || tick >= pathAt)
            {
                planned = true; plannedGoal = goal; pathAt = tick + 30; corner = 1;
                corners = Array.Empty<Vector3>();
                if (NavMesh.SamplePosition(position, out var start, 1, NavMesh.AllAreas) &&
                    NavMesh.SamplePosition(goal, out var end, 1, NavMesh.AllAreas) &&
                    NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
                    corners = path.corners;
            }
            while (corner < corners.Length && Near(position, corners[corner], .25f)) corner++;
            if (corner >= corners.Length) return;
            Vector3 direction = Vector3.ProjectOnPlane(corners[corner] - position, Vector3.up).normalized;
            Face(ref command, self.Yaw, direction);
            var local = Quaternion.Euler(0, -command.Yaw, 0) * direction; command.X = local.x; command.Z = local.z;
        }
        static void Face(ref BattleInput input, float yaw, Vector3 direction)
        {
            if (direction.sqrMagnitude < .01f) return;
            input.Yaw = Mathf.DeltaAngle(0, Mathf.MoveTowardsAngle(yaw, Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, 240 * BattleProtocol.Step));
        }
        public static float Distance(Vector3 from, Vector3 to)
        {
            var path = new NavMeshPath();
            if (!NavMesh.SamplePosition(from, out var a, 1, NavMesh.AllAreas) || !NavMesh.SamplePosition(to, out var b, 1, NavMesh.AllAreas) ||
                !NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) return float.PositiveInfinity;
            var points = path.corners; float distance = 0;
            for (int i = 1; i < points.Length; i++) distance += Vector3.Distance(points[i - 1], points[i]);
            return distance;
        }
    }
}
