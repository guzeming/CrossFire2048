using System;
using System.Collections.Generic;
using System.Linq;
using OperationBlacktide.Shared.Characters;
using OperationBlacktide.Shared.Components;

namespace OperationBlacktide.Shared.GameLogic
{
    public sealed class MatchClockComponent : GameComponent
    {
        public double ElapsedSeconds { get; private set; }
        public override void Tick(float deltaTime) { ElapsedSeconds += deltaTime; }
    }
}
