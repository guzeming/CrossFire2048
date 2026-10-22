using System;
using System.Collections.Generic;
using System.Linq;
using OperationBlacktide.Shared.Characters;
using OperationBlacktide.Shared.Combat;
using OperationBlacktide.Shared.Components;
using OperationBlacktide.Shared.GameLogic;

namespace OperationBlacktide.Shared.Modes.Demolition
{
    public sealed class DemolitionTeamComponent : GameComponent
    {
        public readonly DemolitionPlayer[] Players;
        public bool Swapped { get; internal set; }
        public override IEnumerable<Type> Dependencies => new[] { typeof(CombatWorld) };
        public DemolitionTeamComponent(MatchSeat[] seats)
        {
            if (seats.Length != 10 || seats.Select(s => s.Id).Distinct().Count() != 10 || seats.Select(s => s.UserId).Distinct().Count() != 10 ||
                seats.Count(s => s.Team == 0) != 5 || seats.Count(s => s.Team == 1) != 5 || seats.All(s => s.IsBot)) throw new ArgumentException("Expected ten unique seats, five per team, with at least one human.");
            Players = seats.Select(s => new DemolitionPlayer { Seat = s, Connected = s.IsBot, Character = new Character(s.Id, s.Team) }).ToArray();
        }
        public override void Initialize() { foreach (var p in Players) Host.Get<CombatWorld>().Add(p.Character); }
        public DemolitionPlayer Find(int id) => Players.FirstOrDefault(p => p.Seat.Id == id);
        public bool IsAttacker(DemolitionPlayer p) => p.Seat.Team == (Swapped ? 1 : 0);
        public int AttackTeam => Swapped ? 1 : 0;
        public void Connection(int id, bool connected)
        {
            var p = Find(id); if (p == null || p.Seat.IsBot || p.Abandoned) return;
            p.Connected = connected; p.Interact = false;
            p.OfflineSince = connected ? -1 : Host.Get<MatchClockComponent>().ElapsedSeconds;
        }
    }
}
