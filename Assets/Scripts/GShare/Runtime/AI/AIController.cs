using System;
using OperationBlacktide.Shared.Battle;
using OperationBlacktide.Shared.Characters;
using OperationBlacktide.Shared.Components;

namespace OperationBlacktide.Shared.AI
{
    public sealed class AIController : ComponentHost
    {
        public BotDecisionComponent Decision { get; }
        public AIController(int id, WorldVector[] patrol, IBotEnvironment environment)
        {
            Add(new BotPerceptionComponent(environment)); Add(new BotNavigationComponent(environment));
            Decision = Add(new BotDecisionComponent(id, patrol)); Start();
        }
    }
}
