using System;
using System.Collections.Generic;
using System.Linq;
using OperationBlacktide.Shared.Characters;
using OperationBlacktide.Shared.Combat;
using OperationBlacktide.Shared.Components;
using OperationBlacktide.Shared.GameLogic;

namespace OperationBlacktide.Shared.Modes.Demolition
{
    public sealed class DemolitionGame : IDisposable
    {
        public readonly GameLogic.GameLogic Logic;
        public DemolitionTeamComponent Teams => Logic.Get<DemolitionTeamComponent>();
        public DemolitionEconomyComponent Economy => Logic.Get<DemolitionEconomyComponent>();
        public DemolitionBombComponent Bomb => Logic.Get<DemolitionBombComponent>();
        public DemolitionRoundComponent Round => Logic.Get<DemolitionRoundComponent>();
        public DemolitionGame(MatchSeat[] seats, IDemolitionMap map, DemolitionRules rules = null)
        {
            rules = rules ?? new DemolitionRules(); rules.Validate();
            Logic = new GameLogic.GameLogic(new GameLogicInfo { id = "demolition", components = new[] { "world", "clock" } }, true,
                new DemolitionTeamComponent(seats), new DemolitionEconomyComponent(rules), new DemolitionBombComponent(rules, map), new DemolitionRoundComponent(rules));
        }
        public void Dispose() => Logic.Dispose();
    }
}
