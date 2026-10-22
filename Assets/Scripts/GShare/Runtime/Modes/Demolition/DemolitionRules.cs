using System;
using System.Collections.Generic;
using System.Linq;
using OperationBlacktide.Shared.Characters;
using OperationBlacktide.Shared.Combat;
using OperationBlacktide.Shared.Components;
using OperationBlacktide.Shared.GameLogic;

namespace OperationBlacktide.Shared.Modes.Demolition
{
    [Serializable] public sealed class DemolitionRules
    {
        public float FreezeSeconds = 15, RoundSeconds = 115, BombSeconds = 40, PlantSeconds = 3, DefuseSeconds = 10, KitSeconds = 5, ResultSeconds = 5;
        public int HalfRounds = 12, StartingMoney = 800, MaxMoney = 16000;
        public void Validate()
        {
            foreach (float n in new[] { FreezeSeconds, RoundSeconds, BombSeconds, PlantSeconds, DefuseSeconds, KitSeconds, ResultSeconds })
                if (float.IsNaN(n) || float.IsInfinity(n) || n <= 0) throw new ArgumentException("Invalid demolition timer.");
            if (HalfRounds != 12 || StartingMoney < 0 || MaxMoney < StartingMoney) throw new ArgumentException("Invalid standard rules.");
        }
    }
}
