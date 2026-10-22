using System;
using OperationBlacktide.Shared.Battle;
using OperationBlacktide.Shared.Characters;
using OperationBlacktide.Shared.Components;

namespace OperationBlacktide.Shared.AI
{
    public sealed class BotNavigationComponent : GameComponent
    {
        private readonly IBotEnvironment environment;
        public BotNavigationComponent(IBotEnvironment environment) { this.environment = environment; }
        public WorldVector[] FindPath(WorldVector start, WorldVector goal) => environment.FindPath(start, goal) ?? Array.Empty<WorldVector>();
    }
}
