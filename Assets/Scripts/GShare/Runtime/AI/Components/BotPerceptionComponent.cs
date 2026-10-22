using System;
using OperationBlacktide.Shared.Battle;
using OperationBlacktide.Shared.Characters;
using OperationBlacktide.Shared.Components;

namespace OperationBlacktide.Shared.AI
{
    public sealed class BotPerceptionComponent : GameComponent
    {
        private readonly IBotEnvironment environment;
        public BotPerceptionComponent(IBotEnvironment environment) { this.environment = environment; }
        public bool CanSee(BattleState observer, WorldVector feet)
        {
            var origin = WorldVector.Position(observer); var offset = feet - origin;
            float distance = offset.magnitude;
            if (distance > BotDecisionComponent.SightRange) return false;
            var planar = WorldVector.ProjectOnPlane(offset, WorldVector.up);
            if (planar.magnitude > 2.4f && WorldVector.Dot(WorldVector.Rotate(observer.Yaw, WorldVector.forward), planar.normalized) <
                GameMath.Cos(BotDecisionComponent.FieldOfView * .5f * GameMath.Deg2Rad)) return false;
            return !environment.IsOccluded(origin + WorldVector.up * 1.25f, offset.normalized, distance);
        }
    }
}
