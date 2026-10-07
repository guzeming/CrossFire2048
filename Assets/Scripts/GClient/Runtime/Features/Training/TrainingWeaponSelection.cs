using System;
using System.Collections.Generic;
using OperationBlacktide.Client.Features.Lobby;

namespace OperationBlacktide.Client.Features.Training
{
    /// <summary>Training inventory: entering a category restores its last choice; repeating it cycles.</summary>
    public sealed class TrainingWeaponSelection
    {
        private readonly List<LobbyWeapon>[] categories = { new List<LobbyWeapon>(), new List<LobbyWeapon>(), new List<LobbyWeapon>(), new List<LobbyWeapon>() };
        private readonly int[] remembered = new int[4];
        public string CurrentId { get; private set; }
        public int CurrentSlot { get; private set; }

        public TrainingWeaponSelection(IEnumerable<LobbyWeapon> weapons, string initialId)
        {
            foreach (var item in weapons)
            {
                int slot = SlotOf(item);
                if (slot > 0 && item.prefab != null) categories[slot - 1].Add(item);
            }
            Select(initialId);
        }

        public IReadOnlyList<LobbyWeapon> Options(int slot) => slot >= 1 && slot <= 4 ? categories[slot - 1] : Array.Empty<LobbyWeapon>();

        public string Next(int slot)
        {
            var items = Options(slot);
            if (items.Count == 0) return null;
            int index = remembered[slot - 1];
            if (CurrentSlot == slot) index = (index + 1) % items.Count;
            return items[index].id;
        }

        public bool Select(string id)
        {
            for (int slot = 1; slot <= 4; slot++)
            {
                int index = categories[slot - 1].FindIndex(w => w.id == id);
                if (index < 0) continue;
                CurrentId = id; CurrentSlot = slot; remembered[slot - 1] = index;
                return true;
            }
            return false;
        }

        public static int SlotOf(LobbyWeapon weapon)
        {
            if (weapon == null || !weapon.held) return 0;
            if (weapon.slot == "gear.knife") return 3;
            switch (weapon.slot)
            {
                case "gear.flash": case "gear.he": case "gear.smoke": case "gear.decoy": case "gear.fire": return 4;
            }
            if (weapon.category == LoadoutCategory.Pistols || weapon.slot == "gear.taser") return 2;
            return weapon.category == LoadoutCategory.Rifles || weapon.category == LoadoutCategory.Snipers
                || weapon.category == LoadoutCategory.SMGs || weapon.category == LoadoutCategory.Heavy ? 1 : 0;
        }

        public static string SlotName(int slot)
        {
            switch (slot)
            {
                case 1: return "主武器";
                case 2: return "副武器";
                case 3: return "近战武器";
                case 4: return "投掷武器";
                default: return "武器";
            }
        }
    }
}
