using System.Collections.Generic;
using OperationBlacktide.Shared.Battle;
using System;
using OperationBlacktide.Shared.Characters;
using OperationBlacktide.Shared.Components;

namespace OperationBlacktide.Shared.AI
{
    public enum BotMode { Patrol, Engage, Search, Reload, Dead }

    /// <summary>Server-only senses and decisions. Outputs the same commands as a human; never moves a Transform.</summary>
    public sealed class BotDecisionComponent : GameComponent
    {
        public const float SightRange = 28, FieldOfView = 110, MemorySeconds = 3;
        private readonly WorldVector[] patrol;
        public override IEnumerable<Type> Dependencies => new[] { typeof(BotPerceptionComponent), typeof(BotNavigationComponent) };
        private readonly int seed;
        private WorldVector[] corners = System.Array.Empty<WorldVector>();
        private WorldVector destination, progressPosition;
        private int corner, patrolIndex, nextSense, nextPath, lastSeen, reactAt, lastProgress;
        private bool visible, hasDestination;
        public BotMode Mode { get; private set; }
        public int TargetId { get; private set; }
        public WorldVector LastKnownPosition { get; private set; }

        public BotDecisionComponent(int id, WorldVector[] patrolPoints)
        {
            if (patrolPoints == null || patrolPoints.Length == 0) throw new ArgumentException("Patrol requires at least one point.");
            seed = id; patrol = (WorldVector[])patrolPoints.Clone();
            patrolIndex = id % patrol.Length;
        }

        public void Reset(WorldVector position, int tick)
        {
            TargetId = 0; visible = false; Mode = BotMode.Patrol;
            nextSense = tick; nextPath = tick; lastProgress = tick; progressPosition = position;
            corners = System.Array.Empty<WorldVector>(); hasDestination = false;
            LastKnownPosition = position;
        }

        public BattleInput Think(BattleState self, IReadOnlyList<BattleState> actors, int tick,
            bool allowPursuit = true, int magazineCapacity = BattleProtocol.Capacity)
        {
            var command = new BattleInput { Sequence = self.Ack + 1, Yaw = self.Yaw };
            if (self.Health <= 0) { Mode = BotMode.Dead; TargetId = 0; visible = false; return command; }
            WorldVector position = WorldVector.Position(self);
            if (tick >= nextSense) { Sense(self, actors, tick); nextSense = tick + 6; }
            bool hasMemory = TargetId != 0 && tick - lastSeen <= MemorySeconds * BattleProtocol.TickRate;
            if (!hasMemory) { TargetId = 0; visible = false; }

            if (self.ReloadTicks > 0 || (self.Ammo == 0 && self.Reserve > 0))
            {
                Mode = BotMode.Reload;
                if (self.ReloadTicks == 0) command.Buttons |= InputButtons.Reload;
                if (hasMemory) command.Yaw = Turn(self.Yaw, LastKnownPosition - position);
                return command;
            }
            if (visible && hasMemory)
            {
                Mode = BotMode.Engage;
                WorldVector offset = LastKnownPosition - position;
                float desiredYaw = GameMath.Atan2(offset.x, offset.z) * GameMath.Rad2Deg;
                float error = GameMath.Sin(tick * .045f + seed * 2.3f) * 3.5f;
                command.Yaw = GameMath.MoveTowardsAngle(self.Yaw, desiredYaw + error, 240 * BattleProtocol.Step);
                command.Yaw = GameMath.DeltaAngle(0, command.Yaw);
                // Reaction delay, bounded turning, aim error and short bursts make difficulty explicit.
                bool teammateBlocks = TeammateBlocksShot(self, actors, LastKnownPosition);
                if (!teammateBlocks && tick >= reactAt && GameMath.Abs(GameMath.DeltaAngle(command.Yaw, desiredYaw)) < 4 &&
                    (tick + seed * 11) % 60 < 18 && self.Ammo > 0 && CanSee(self, LastKnownPosition))
                    command.Buttons |= InputButtons.Fire;
                if (teammateBlocks) Sidestep(ref command, position);
                else if (allowPursuit && offset.sqrMagnitude > 8 * 8) MoveToward(ref command, self, LastKnownPosition, tick);
                return command;
            }
            if (hasMemory && allowPursuit)
            {
                Mode = BotMode.Search;
                // Only the last observed position is used after losing sight.
                if (PlanarDistance(position, LastKnownPosition) > .65f)
                {
                    command.Yaw = Turn(self.Yaw, LastKnownPosition - position);
                    MoveToward(ref command, self, LastKnownPosition, tick);
                }
                else command.Yaw = GameMath.DeltaAngle(0, self.Yaw + 65 * BattleProtocol.Step);
                return command;
            }

            Mode = BotMode.Patrol;
            if (self.Ammo < Math.Max(1, magazineCapacity / 2) && self.Reserve > 0)
            { command.Buttons = InputButtons.Reload; Mode = BotMode.Reload; return command; }
            if (PlanarDistance(position, patrol[patrolIndex]) < .7f || tick - lastProgress > 120)
            {
                patrolIndex = (patrolIndex + 1) % patrol.Length;
                nextPath = tick; lastProgress = tick; progressPosition = position;
            }
            MoveToward(ref command, self, patrol[patrolIndex], tick);
            if (command.X * command.X + command.Z * command.Z > .01f)
            {
                WorldVector world = WorldVector.Rotate(command.Yaw, new WorldVector(command.X, 0, command.Z));
                command.Yaw = Turn(self.Yaw, world);
                SetMovement(ref command, world);
            }
            return command;
        }

        private void Sense(BattleState self, IReadOnlyList<BattleState> actors, int tick)
        {
            visible = false;
            float best = float.PositiveInfinity;
            BattleState selected = default;
            foreach (var other in actors)
            {
                if (other.Id == self.Id || other.Health <= 0 || other.Team == self.Team) continue;
                WorldVector point = WorldVector.Position(other);
                if (!CanSee(self, point)) continue;
                float score = (point - WorldVector.Position(self)).sqrMagnitude * (other.Id == TargetId ? .7f : 1);
                if (score >= best) continue;
                best = score; selected = other; visible = true;
            }
            if (!visible) return;
            if (TargetId != selected.Id) reactAt = tick + 24 + seed % 7;
            TargetId = selected.Id; LastKnownPosition = WorldVector.Position(selected); lastSeen = tick;
        }

        private bool CanSee(BattleState observer, WorldVector feet) => Host.Get<BotPerceptionComponent>().CanSee(observer, feet);

        private static bool TeammateBlocksShot(BattleState self, IReadOnlyList<BattleState> actors, WorldVector target)
        {
            WorldVector origin = WorldVector.Position(self);
            WorldVector line = WorldVector.ProjectOnPlane(target - origin, WorldVector.up);
            float length = line.magnitude; line = line.normalized;
            foreach (var ally in actors)
            {
                if (ally.Id == self.Id || ally.Team != self.Team || ally.Health <= 0) continue;
                WorldVector offset = WorldVector.Position(ally) - origin;
                float along = WorldVector.Dot(offset, line);
                if (along > 0 && along < length && WorldVector.ProjectOnPlane(offset - line * along, WorldVector.up).sqrMagnitude < .36f)
                    return true;
            }
            return false;
        }

        private void Sidestep(ref BattleInput command, WorldVector position)
        {
            WorldVector side = WorldVector.Rotate(command.Yaw, new WorldVector(1, 0, 0)) * (seed % 2 == 0 ? 1 : -1);
            for (int attempt = 0; attempt < 2; attempt++, side = side * -1)
            {
                WorldVector goal = position + side * .8f;
                var route = Host.Get<BotNavigationComponent>().FindPath(position, goal);
                // A short, straight, reachable segment keeps sidestepping inside the walkable region.
                if (route.Length == 2 && (route[1] - goal).sqrMagnitude < .16f)
                { SetMovement(ref command, side * .5f); return; }
            }
        }

        private void MoveToward(ref BattleInput command, BattleState self, WorldVector goal, int tick)
        {
            WorldVector position = WorldVector.Position(self);
            if ((position - progressPosition).sqrMagnitude > .16f)
            { progressPosition = position; lastProgress = tick; }
            if (tick >= nextPath && (!hasDestination || (goal - destination).sqrMagnitude > 1 ||
                corner >= corners.Length || tick - lastProgress > 60))
            {
                nextPath = tick + 30; hasDestination = true; destination = goal;
                corners = System.Array.Empty<WorldVector>(); corner = 0;
                corners = Host.Get<BotNavigationComponent>().FindPath(position, goal);
                if (corners.Length > 0) corner = 1;
            }
            while (corner < corners.Length && PlanarDistance(position, corners[corner]) < .3f) corner++;
            if (corner < corners.Length) SetMovement(ref command, WorldVector.ProjectOnPlane(corners[corner] - position, WorldVector.up).normalized);
        }

        private static void SetMovement(ref BattleInput command, WorldVector world)
        {
            WorldVector local = WorldVector.Rotate(-command.Yaw, world);
            command.X = local.x; command.Z = local.z;
        }
        private static float Turn(float yaw, WorldVector direction) => GameMath.DeltaAngle(0,
            GameMath.MoveTowardsAngle(yaw, GameMath.Atan2(direction.x, direction.z) * GameMath.Rad2Deg, 240 * BattleProtocol.Step));
        private static float PlanarDistance(WorldVector a, WorldVector b) => WorldVector.ProjectOnPlane(a - b, WorldVector.up).magnitude;
    }
}
