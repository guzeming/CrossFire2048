using System;
using System.Collections.Generic;
using OperationBlacktide.Shared.Combat;
using OperationBlacktide.Shared.GameLogic;
using UnityEngine;

namespace OperationBlacktide.Simulation
{
    /// <summary>Unity resource adapter for portable JSON authored outside the engine.</summary>
    public static class GameConfig
    {
        [Serializable] private sealed class Weapons { public WeaponInfo[] weapons; }
        private static WeaponInfo[] weapons;
        private static string weaponSource;
        public static WeaponInfo LoadBattleWeapon()
        {
            var source = Resources.Load<TextAsset>("Config/BattleWeapon");
            if (source == null) throw new InvalidOperationException("Missing battle weapon configuration.");
            var info = JsonUtility.FromJson<WeaponInfo>(source.text); info.Validate(); return info;
        }
        public static GameLogicInfo LoadMode(string id)
        {
            var source = Resources.Load<TextAsset>("Config/Modes/" + id);
            if (source == null) throw new InvalidOperationException("Missing gameplay preset: " + id);
            var info = JsonUtility.FromJson<GameLogicInfo>(source.text); info.Validate(); return info;
        }
        public static WeaponInfo FindWeapon(string id)
        {
            var source = Resources.Load<TextAsset>("Config/Weapons");
            if (source == null) throw new InvalidOperationException("Missing portable weapon configuration.");
            // Editor imports can replace JSON without a domain reload; new instances use the new data.
            if (weapons == null || weaponSource != source.text)
            {
                var loaded = JsonUtility.FromJson<Weapons>(source.text)?.weapons;
                if (loaded == null || loaded.Length == 0) throw new InvalidOperationException("Empty weapon configuration.");
                var ids = new HashSet<string>();
                foreach (var weapon in loaded)
                {
                    if (weapon == null) throw new InvalidOperationException("Null weapon configuration.");
                    weapon.Validate();
                    if (!ids.Add(weapon.id)) throw new InvalidOperationException("Duplicate weapon: " + weapon.id);
                }
                weapons = loaded; weaponSource = source.text;
            }
            return Array.Find(weapons, w => w.id == id);
        }
    }
}
