using System;
using System.Collections.Generic;
using System.Linq;
using OperationBlacktide.Shared.Characters;
using OperationBlacktide.Shared.Combat;
using OperationBlacktide.Shared.Components;
using OperationBlacktide.Shared.GameLogic;

namespace OperationBlacktide.Shared.Modes.Demolition
{
    public sealed class DemolitionEconomyComponent : GameComponent
    {
        readonly DemolitionRules rules;
        readonly int[] losses = new int[2];
        DemolitionTeamComponent teams;
        public override IEnumerable<Type> Dependencies => new[] { typeof(DemolitionTeamComponent) };
        public DemolitionEconomyComponent(DemolitionRules rules) { this.rules = rules; }
        public override void Initialize() { teams = Host.Get<DemolitionTeamComponent>(); }
        public void Credit(DemolitionPlayer p, int amount) { p.Money = Math.Min(rules.MaxMoney, p.Money + Math.Max(0, amount)); }
        public void RoundStart(bool reset)
        {
            if (reset) Array.Clear(losses, 0, losses.Length);
            foreach (var p in teams.Players)
            {
                bool lostEquipment = reset || !p.Alive;
                if (reset) p.Money = rules.StartingMoney;
                if (lostEquipment)
                {
                    foreach (var w in p.Character.Equipment.Weapons.ToArray()) p.Character.Equipment.Remove(w.InstanceId)?.Dispose();
                    var pistol = Weapon(teams.IsAttacker(p) ? "weapon_pist_glock18" : "weapon_pist_usp_silencer");
                    p.Character.Equipment.AddWeapon(pistol); p.Character.Equipment.Equip(pistol.InstanceId); p.Kit = false;
                }
                p.Character.Vitals.Restore(lostEquipment ? 0 : p.Character.Vitals.Armor);
                p.Character.Equipment.Reset(); p.Life++; p.Interact = false;
                if (p.Abandoned) p.Character.Vitals.ApplyDamage(1000, true);
            }
        }
        public void Award(int winningTeam, bool planted)
        {
            losses[winningTeam] = 0; int loser = 1 - winningTeam; losses[loser]++;
            foreach (var p in teams.Players)
                Credit(p, p.Seat.Team == winningTeam ? 3250 : Math.Min(3400, 1400 + 500 * (losses[loser] - 1)) + (planted && teams.IsAttacker(p) ? 800 : 0));
        }
        public bool Buy(int id, BuyItem item)
        {
            var round = Host.Get<DemolitionRoundComponent>(); var p = teams.Find(id);
            if (round.Phase != RoundPhase.Freeze || p == null || !p.Alive || !p.Connected) return false;
            string weapon = ""; int price;
            switch (item)
            {
                case BuyItem.AK47: if (!teams.IsAttacker(p)) return false; price = 2700; weapon = "weapon_rif_ak47"; break;
                case BuyItem.M4A1: if (teams.IsAttacker(p)) return false; price = 2900; weapon = "weapon_rif_m4a1_silencer"; break;
                case BuyItem.Deagle: price = 700; weapon = "weapon_pist_deagle"; break;
                case BuyItem.Armor: if (p.Character.Vitals.Armor == 100) return false; price = 650; break;
                case BuyItem.DefuseKit: if (teams.IsAttacker(p) || p.Kit) return false; price = 400; break;
                default: return false;
            }
            if (p.Money < price || (weapon.Length > 0 && p.Character.Equipment.Weapons.Any(w => w.Info.id == weapon))) return false;
            p.Money -= price;
            if (item == BuyItem.Armor) p.Character.Vitals.SetArmor(100);
            else if (item == BuyItem.DefuseKit) p.Kit = true;
            else
            {
                bool rifle = weapon.Contains("_rif_");
                foreach (var old in p.Character.Equipment.Weapons.Where(w => w.Info.id.Contains("_rif_") == rifle).ToArray())
                    p.Character.Equipment.Remove(old.InstanceId)?.Dispose();
                var next = Weapon(weapon); p.Character.Equipment.AddWeapon(next); p.Character.Equipment.Equip(next.InstanceId);
            }
            return true;
        }
        public void Equip(int id, int slot)
        {
            var p = teams.Find(id); if (p == null || !p.Alive || (slot != 1 && slot != 2)) return;
            var selected = p.Character.Equipment.Weapons.FirstOrDefault(w => w.Info.id.Contains("_rif_") == (slot == 1));
            if (selected != null) p.Character.Equipment.Equip(selected.InstanceId);
        }
        static Weapon Weapon(string id)
        {
            bool rifle = id.Contains("_rif_"), deagle = id.Contains("deagle"), glock = id.Contains("glock");
            return new Weapon(id, new WeaponInfo { id = id, modelId = id, cycleTime = rifle ? .1f : .3f,
                automatic = true, damage = deagle ? 50 : rifle ? 30 : 22, range = 80, rangeModifier = 1,
                magazineSize = rifle && id.Contains("ak47") ? 30 : rifle || glock ? 20 : deagle ? 7 : 12,
                reserveAmmo = rifle ? 90 : 60, reloadDuration = 2.4f });
        }
    }
}
