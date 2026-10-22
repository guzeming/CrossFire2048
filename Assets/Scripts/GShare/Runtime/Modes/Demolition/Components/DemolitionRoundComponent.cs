using System;
using System.Collections.Generic;
using System.Linq;
using OperationBlacktide.Shared.Characters;
using OperationBlacktide.Shared.Combat;
using OperationBlacktide.Shared.Components;
using OperationBlacktide.Shared.GameLogic;

namespace OperationBlacktide.Shared.Modes.Demolition
{
    public sealed class DemolitionRoundComponent : GameComponent
    {
        readonly DemolitionRules rules;
        DemolitionTeamComponent teams;
        DemolitionEconomyComponent economy;
        DemolitionBombComponent bomb;
        MatchClockComponent clock;
        public RoundPhase Phase { get; private set; } = RoundPhase.Loading;
        public int Number { get; private set; }
        public int ScoreA { get; private set; }
        public int ScoreB { get; private set; }
        public string Reason { get; private set; } = "等待玩家加载";
        public double Deadline { get; private set; } = 120;
        public float Remaining => (float)Math.Max(0, (Phase == RoundPhase.Planted ? bomb.ExplodesAt : Deadline) - clock.ElapsedSeconds);
        public event Action RoundStarted;
        public override IEnumerable<Type> Dependencies => new[] { typeof(MatchClockComponent), typeof(DemolitionBombComponent) };
        public DemolitionRoundComponent(DemolitionRules rules) { this.rules = rules; }
        public override void Initialize()
        { teams = Host.Get<DemolitionTeamComponent>(); economy = Host.Get<DemolitionEconomyComponent>(); bomb = Host.Get<DemolitionBombComponent>(); clock = Host.Get<MatchClockComponent>(); }
        public override void Tick(float dt)
        {
            double now = clock.ElapsedSeconds;
            foreach (var p in teams.Players.Where(p => !p.Seat.IsBot && !p.Connected && p.OfflineSince >= 0 && now-p.OfflineSince >= 180 && !p.Abandoned))
            { p.Abandoned = true; p.Character.Vitals.ApplyDamage(1000, true); }
            switch (Phase)
            {
                case RoundPhase.Loading:
                    if (teams.Players.All(p => p.Connected)) Begin();
                    else if (now >= Deadline) { Phase = RoundPhase.Aborted; Reason = "玩家加载超时"; }
                    break;
                case RoundPhase.Freeze:
                    if (now + .000001 >= Deadline) { Phase = RoundPhase.Live; Deadline = now + rules.RoundSeconds; Reason = "回合进行中"; }
                    break;
                case RoundPhase.Result:
                    if (now + .000001 >= Deadline)
                    {
                        if (ScoreA >= 13 || ScoreB >= 13 || Number >= 24) { Phase = RoundPhase.Finished; Reason = ScoreA == ScoreB ? "平局" : (ScoreA > ScoreB ? "A 队获胜" : "B 队获胜"); }
                        else Begin();
                    }
                    break;
                case RoundPhase.Live:
                case RoundPhase.Planted:
                    bomb.Advance(now, Deadline);
                    if (bomb.Phase == BombPhase.Planted) Phase = RoundPhase.Planted;
                    if (bomb.Phase == BombPhase.Defused) End(1-teams.AttackTeam, "炸弹已拆除");
                    else if (bomb.Phase == BombPhase.Exploded) End(teams.AttackTeam, "炸弹已爆炸");
                    else if (!teams.Players.Any(p => !teams.IsAttacker(p) && p.Alive)) End(teams.AttackTeam, "防守方被消灭");
                    else if (Phase == RoundPhase.Live && !teams.Players.Any(p => teams.IsAttacker(p) && p.Alive)) End(1-teams.AttackTeam, "进攻方被消灭");
                    else if (Phase == RoundPhase.Live && now + .000001 >= Deadline) End(1-teams.AttackTeam, "进攻时间耗尽");
                    break;
            }
        }
        void Begin()
        {
            Number++; teams.Swapped = Number > 12; economy.RoundStart(Number == 1 || Number == 13); bomb.Reset(Number);
            Phase = RoundPhase.Freeze; Deadline = clock.ElapsedSeconds + rules.FreezeSeconds; Reason = "购买准备"; RoundStarted?.Invoke();
        }
        void End(int team, string reason)
        {
            if (Phase != RoundPhase.Live && Phase != RoundPhase.Planted) return;
            if (team == 0) ScoreA++; else ScoreB++;
            economy.Award(team, bomb.Phase == BombPhase.Planted || bomb.Phase == BombPhase.Defused || bomb.Phase == BombPhase.Exploded);
            Phase = RoundPhase.Result; Reason = reason; Deadline = clock.ElapsedSeconds + rules.ResultSeconds;
            foreach (var p in teams.Players) { p.Interact = false; p.Character.Equipment.Current?.Ammo.CancelReload(); }
        }
        readonly HashSet<int> surrenderVotes = new HashSet<int>();
        public bool Surrender(int id)
        {
            if (Phase == RoundPhase.Loading || Phase == RoundPhase.Finished || Phase == RoundPhase.Aborted) return false;
            var p = teams.Find(id); if (p == null || p.Seat.IsBot || !p.Connected || !teams.Players.Any(x => x.Seat.Team == p.Seat.Team && x.Abandoned)) return false;
            surrenderVotes.Add(id);
            if (teams.Players.Where(x => x.Seat.Team == p.Seat.Team && !x.Seat.IsBot && x.Connected).All(x => surrenderVotes.Contains(x.Seat.Id)))
            { if (p.Seat.Team == 0) ScoreB = 13; else ScoreA = 13; Phase = RoundPhase.Finished; Reason = "对方投降"; }
            return true;
        }
    }
}
