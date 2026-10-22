using System;
using System.Collections.Generic;
using System.Linq;
using OperationBlacktide.Shared.Characters;
using OperationBlacktide.Shared.Combat;
using OperationBlacktide.Shared.Components;
using OperationBlacktide.Shared.GameLogic;

namespace OperationBlacktide.Shared.Modes.Demolition
{
    public sealed class DemolitionPlayer
    {
        public MatchSeat Seat;
        public Character Character;
        public DemolitionPoint Position;
        public bool Connected, Interact, Kit, Abandoned;
        public double OfflineSince = -1;
        public int Money, Life, Kills, Deaths;
        public bool Alive => Character.Vitals.IsAlive;
    }
}
