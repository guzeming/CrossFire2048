using OperationBlacktide.Client.Content;
using System;
using System.Collections.Generic;

namespace OperationBlacktide.Client.Gameplay.Combat
{
    /// <summary>Inventory over mode-supplied equipment: entering a category restores its last choice; repeating it cycles.</summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "OperationBlacktide.Client.Features.Training", "Assembly-CSharp", "TrainingWeaponSelection")]
    public sealed class WeaponSelection
    {
        private readonly List<WeaponModelDefinition>[] categories = { new List<WeaponModelDefinition>(), new List<WeaponModelDefinition>(), new List<WeaponModelDefinition>(), new List<WeaponModelDefinition>() };
        private readonly int[] remembered = new int[4];
        public string CurrentId { get; private set; }
        public int CurrentSlot { get; private set; }

        public WeaponSelection(IEnumerable<WeaponModelDefinition> weapons, string initialId)
        {
            foreach (var item in weapons)
            {
                int slot = SlotOf(item);
                if (slot > 0 && item.prefab != null) categories[slot - 1].Add(item);
            }
            Select(initialId);
        }

        public IReadOnlyList<WeaponModelDefinition> Options(int slot) => slot >= 1 && slot <= 4 ? categories[slot - 1] : Array.Empty<WeaponModelDefinition>();

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

        // The wheel visits carried categories, restoring the last selected weapon in each.
        // Number keys continue to cycle the training catalogue within a category.
        public string Cycle(int direction)
        {
            if (direction == 0) return null;
            int step = direction > 0 ? 1 : -1;
            for (int offset = 1; offset <= categories.Length; offset++)
            {
                int index = ((CurrentSlot - 1 + step * offset) % categories.Length + categories.Length) % categories.Length;
                if (categories[index].Count > 0) return categories[index][remembered[index]].id;
            }
            return null;
        }

        public static int SlotOf(WeaponModelDefinition weapon)
        {
            if (weapon == null || !weapon.held) return 0;
            if (weapon.slot == "gear.knife") return 3;
            switch (weapon.slot)
            {
                case "gear.flash": case "gear.he": case "gear.smoke": case "gear.decoy": case "gear.fire": return 4;
            }
            if (weapon.category == EquipmentCategory.Pistols || weapon.slot == "gear.taser") return 2;
            return weapon.category == EquipmentCategory.Rifles || weapon.category == EquipmentCategory.Snipers
                || weapon.category == EquipmentCategory.SMGs || weapon.category == EquipmentCategory.Heavy ? 1 : 0;
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
