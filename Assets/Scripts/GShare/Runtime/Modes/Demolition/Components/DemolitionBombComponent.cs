using System;
using System.Collections.Generic;
using System.Linq;
using OperationBlacktide.Shared.Characters;
using OperationBlacktide.Shared.Combat;
using OperationBlacktide.Shared.Components;
using OperationBlacktide.Shared.GameLogic;

namespace OperationBlacktide.Shared.Modes.Demolition
{
    public sealed class DemolitionBombComponent : GameComponent
    {
        readonly DemolitionRules rules;
        readonly IDemolitionMap map;
        DemolitionTeamComponent teams;
        public BombPhase Phase { get; private set; }
        public int Carrier { get; private set; }
        public int Site { get; private set; } = -1;
        public int Interactor { get; private set; }
        public float Progress { get; private set; }
        public DemolitionPoint Position { get; private set; }
        public double ExplodesAt { get; private set; }
        double interactionStarted;
        int interactionSite = -1;
        public override IEnumerable<Type> Dependencies => new[] { typeof(DemolitionTeamComponent), typeof(DemolitionEconomyComponent) };
        public DemolitionBombComponent(DemolitionRules rules, IDemolitionMap map) { this.rules = rules; this.map = map; }
        public override void Initialize() { teams = Host.Get<DemolitionTeamComponent>(); }
        public void Reset(int round)
        {
            var attackers = teams.Players.Where(p => teams.IsAttacker(p) && p.Alive).ToArray();
            Carrier = attackers.Length > 0 ? attackers[(round-1) % attackers.Length].Seat.Id : 0;
            Phase = BombPhase.Carried; Site = -1; ExplodesAt = 0; ClearInteraction();
        }
        void ClearInteraction() { Interactor = 0; Progress = 0; interactionStarted = 0; interactionSite = -1; }
        public void Advance(double now, double roundEnd)
        {
            if (Phase == BombPhase.Carried)
            {
                var p = teams.Find(Carrier);
                if (p != null) Position = p.Position;
                if (p == null || !p.Alive) { Phase = BombPhase.Dropped; Carrier = 0; ClearInteraction(); }
            }
            if (Phase == BombPhase.Dropped)
            {
                var picker = teams.Players.FirstOrDefault(p => p.Alive && teams.IsAttacker(p) && p.Position.DistanceSquared(Position) < 2.25f && map.CanReach(p.Position, Position));
                if (picker != null) { Carrier = picker.Seat.Id; Phase = BombPhase.Carried; }
            }
            bool planted = Phase == BombPhase.Planted;
            var actor = planted ? teams.Find(Interactor) : teams.Find(Carrier);
            if (planted && !ValidDefuser(actor)) actor = teams.Players.FirstOrDefault(ValidDefuser);
            int site = -1;
            bool valid = actor != null && actor.Alive && actor.Connected && actor.Interact &&
                (planted ? ValidDefuser(actor) : Phase == BombPhase.Carried && map.InSite(actor.Position, out site));
            if (!valid) ClearInteraction();
            else
            {
                if (Interactor != actor.Seat.Id || (!planted && site != interactionSite))
                { Interactor = actor.Seat.Id; interactionStarted = now; interactionSite = site; }
                double duration = planted ? actor.Kit ? rules.KitSeconds : rules.DefuseSeconds : rules.PlantSeconds;
                Progress = (float)Math.Min(1, (now - interactionStarted) / duration);
                double due = interactionStarted + duration;
                if (now + .000001 >= due && due <= (planted ? ExplodesAt : roundEnd) + .000001)
                {
                    if (planted) Phase = BombPhase.Defused;
                    else { Position = actor.Position; Phase = BombPhase.Planted; Carrier = 0; Site = site; ExplodesAt = due + rules.BombSeconds; Host.Get<DemolitionEconomyComponent>().Credit(actor, 300); }
                    ClearInteraction();
                }
            }
            if (Phase == BombPhase.Planted && now + .000001 >= ExplodesAt) { Phase = BombPhase.Exploded; ClearInteraction(); }
        }
        bool ValidDefuser(DemolitionPlayer p) => p != null && p.Alive && p.Connected && p.Interact && !teams.IsAttacker(p) &&
            p.Position.DistanceSquared(Position) <= 9 && map.CanReach(p.Position, Position);
    }
}
