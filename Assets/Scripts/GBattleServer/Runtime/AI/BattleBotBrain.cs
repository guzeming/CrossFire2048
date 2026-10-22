using System.Collections.Generic;
using System.Linq;
using OperationBlacktide.Shared.AI;
using OperationBlacktide.Shared.Battle;
using OperationBlacktide.Shared.Characters;
using OperationBlacktide.Simulation;
using UnityEngine;
using UnityEngine.AI;

namespace OperationBlacktide.BattleServer
{
    public enum BattleBotMode { Patrol, Engage, Search, Reload, Dead }
    /// <summary>Unity navigation/physics adapter. All decisions and perception rules live in GShare.</summary>
    public sealed class BattleBotBrain : System.IDisposable
    {
        private sealed class Environment : IBotEnvironment
        {
            private readonly NavMeshPath path = new NavMeshPath();
            public bool IsOccluded(WorldVector origin, WorldVector direction, float distance) =>
                Physics.Raycast(ToUnity(origin), ToUnity(direction), distance,
                    Physics.DefaultRaycastLayers & ~(1 << CharacterSimulation.PawnLayer), QueryTriggerInteraction.Ignore);
            public WorldVector[] FindPath(WorldVector start, WorldVector goal)
            {
                if (NavMesh.SamplePosition(ToUnity(start), out var from, 1, NavMesh.AllAreas) &&
                    NavMesh.SamplePosition(ToUnity(goal), out var to, 1, NavMesh.AllAreas) &&
                    NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
                    return path.corners.Select(ToCore).ToArray();
                return System.Array.Empty<WorldVector>();
            }
        }
        public const float SightRange = BotDecisionComponent.SightRange, FieldOfView = BotDecisionComponent.FieldOfView, MemorySeconds = BotDecisionComponent.MemorySeconds;
        private readonly AIController controller;
        public BattleBotMode Mode => (BattleBotMode)controller.Decision.Mode;
        public int TargetId => controller.Decision.TargetId;
        public Vector3 LastKnownPosition => ToUnity(controller.Decision.LastKnownPosition);
        public BattleBotBrain(int id, Vector3[] patrolPoints)
        { controller = new AIController(id, patrolPoints.Select(ToCore).ToArray(), new Environment()); }
        public void Reset(Vector3 position, int tick) => controller.Decision.Reset(ToCore(position), tick);
        public BattleInput Think(BattleState self, IReadOnlyList<BattleState> actors, int tick,
            bool allowPursuit = true, int magazineCapacity = BattleProtocol.Capacity) => controller.Decision.Think(self, actors, tick, allowPursuit, magazineCapacity);
        public static bool CanSee(BattleState observer, Vector3 feet) => new BotPerceptionComponent(new Environment()).CanSee(observer, ToCore(feet));
        public void Dispose() => controller.Dispose();
        private static WorldVector ToCore(Vector3 value) => new WorldVector(value.x, value.y, value.z);
        private static Vector3 ToUnity(WorldVector value) => new Vector3(value.x, value.y, value.z);
    }
}
