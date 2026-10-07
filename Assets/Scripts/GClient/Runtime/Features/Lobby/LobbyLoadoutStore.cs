using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OperationBlacktide.Client.Features.Lobby
{
    [Serializable] public sealed class EquippedSlot { public string slot, weaponId; }
    [Serializable] public sealed class TeamLoadout
    {
        public string agentId, displayWeaponId;
        public List<EquippedSlot> slots = new List<EquippedSlot>();
    }
    [Serializable] public sealed class LobbyLoadoutData
    {
        public int version = 1;
        public LobbyTeam activeTeam = LobbyTeam.CT;
        public TeamLoadout ct = new TeamLoadout(), t = new TeamLoadout();
    }

    /// <summary>Classic buy-menu replacements, independent for CT/T and each local account.</summary>
    public sealed class LobbyLoadoutStore
    {
        private readonly LobbyLoadoutCatalog catalog;
        private readonly string key;
        public LobbyLoadoutData Data { get; private set; }
        public event Action Changed;
        public LobbyTeam Team => Data.activeTeam;
        public TeamLoadout Current => ForTeam(Team);
        public string StorageKey => key;

        public LobbyLoadoutStore(LobbyLoadoutCatalog catalog, string userId)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            if (string.IsNullOrEmpty(userId)) throw new ArgumentException("A loadout requires an account ID.", nameof(userId));
            key = "OperationBlacktide.Loadout.v1." + userId;
            string saved = ReadSavedLoadout(userId);
            try { Data = JsonUtility.FromJson<LobbyLoadoutData>(saved); }
            catch (ArgumentException) { Data = null; }
            bool migrate = !PlayerPrefs.HasKey(key) && Data != null && Data.version == 1;
            if (Data == null || Data.version != 1) Data = new LobbyLoadoutData();
            if (Data.activeTeam != LobbyTeam.CT && Data.activeTeam != LobbyTeam.T) Data.activeTeam = LobbyTeam.CT;
            Data.ct = Normalize(Data.ct, LobbyTeam.CT);
            Data.t = Normalize(Data.t, LobbyTeam.T);
            if (migrate) Save();
        }

        private string ReadSavedLoadout(string userId)
        {
            if (PlayerPrefs.HasKey(key)) return PlayerPrefs.GetString(key);
            // Retain this legacy name only for migration; do not delete the original save.
            string legacyKey = "CrossFire2048.Loadout.v1." + userId;
            if (PlayerPrefs.HasKey(legacyKey)) return PlayerPrefs.GetString(legacyKey);
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            // Changing productName also changes Unity's Windows PlayerPrefs registry path.
#if UNITY_EDITOR_WIN
            const string legacyPath = @"Software\Unity\UnityEditor\DefaultCompany\Test";
#else
            const string legacyPath = @"Software\DefaultCompany\Test";
#endif
            return ReadLegacyWindowsValue(legacyPath, legacyKey);
#else
            return "";
#endif
        }

#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
        // Unity's .NET Standard profile does not ship Microsoft.Win32.Registry.
        // Read the old Unity value through Windows directly; never modify the old hive.
        private static string ReadLegacyWindowsValue(string path, string keyName)
        {
            if (RegOpenKeyEx(new IntPtr(unchecked((int)0x80000001)), path, 0, 0x20019, out var registry) != 0) return "";
            try
            {
                var name = new System.Text.StringBuilder(16384);
                for (uint index = 0; ; index++)
                {
                    uint length = (uint)name.Capacity;
                    name.Clear();
                    if (RegEnumValue(registry, index, name, ref length, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) != 0) break;
                    string valueName = name.ToString();
                    if (valueName != keyName && !valueName.StartsWith(keyName + "_h", StringComparison.Ordinal)) continue;
                    uint size = 0;
                    if (RegQueryValueEx(registry, valueName, IntPtr.Zero, out uint type, null, ref size) != 0 || size == 0 || size > 1048576) continue;
                    var bytes = new byte[size];
                    if (RegQueryValueEx(registry, valueName, IntPtr.Zero, out type, bytes, ref size) != 0) continue;
                    if (type == 1) return System.Text.Encoding.Unicode.GetString(bytes, 0, (int)size).TrimEnd('\0');
                    if (type == 3) return System.Text.Encoding.UTF8.GetString(bytes, 0, (int)size).TrimEnd('\0');
                }
            }
            finally { RegCloseKey(registry); }
            return "";
        }

        [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int RegOpenKeyEx(IntPtr key, string subKey, uint options, int access, out IntPtr result);
        [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int RegEnumValue(IntPtr key, uint index, System.Text.StringBuilder name, ref uint length, IntPtr reserved, IntPtr type, IntPtr data, IntPtr dataLength);
        [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int RegQueryValueEx(IntPtr key, string name, IntPtr reserved, out uint type, byte[] data, ref uint size);
        [System.Runtime.InteropServices.DllImport("advapi32.dll")]
        private static extern int RegCloseKey(IntPtr key);
#endif

        public TeamLoadout ForTeam(LobbyTeam team) => team == LobbyTeam.T ? Data.t : Data.ct;
        public LobbyWeapon Equipped(LobbyTeam team, string slot) => catalog.Weapon(ForTeam(team).slots.FirstOrDefault(s => s.slot == slot)?.weaponId);
        public bool IsEquipped(LobbyWeapon weapon) => weapon != null && weapon.Supports(Team) && Equipped(Team, weapon.slot)?.id == weapon.id;

        public void SelectTeam(LobbyTeam team)
        {
            if (team != LobbyTeam.CT && team != LobbyTeam.T) return;
            Data.activeTeam = team;
            Save();
        }

        public bool EquipAgent(string id)
        {
            var agent = catalog.Agent(id);
            if (agent == null || agent.team != Team) return false;
            Current.agentId = id;
            Save();
            return true;
        }

        public bool EquipWeapon(string id)
        {
            var weapon = catalog.Weapon(id);
            if (weapon == null || !weapon.Supports(Team)) return false;
            var slot = Current.slots.FirstOrDefault(s => s.slot == weapon.slot);
            if (slot == null) return false;
            bool replaceDisplay = Current.displayWeaponId == slot.weaponId;
            slot.weaponId = id;
            if (replaceDisplay && weapon.held) Current.displayWeaponId = id;
            Save();
            return true;
        }

        public bool SetDisplayWeapon(string id)
        {
            var weapon = catalog.Weapon(id);
            if (weapon == null || !weapon.held || !IsEquipped(weapon)) return false;
            Current.displayWeaponId = id;
            Save();
            return true;
        }

        public bool EquipForBothTeams(string id)
        {
            var weapon = catalog.Weapon(id);
            if (weapon == null || weapon.teams != 3) return false;
            foreach (var team in new[] { LobbyTeam.CT, LobbyTeam.T })
            {
                var data = ForTeam(team);
                var entry = data.slots.FirstOrDefault(s => s.slot == weapon.slot);
                if (entry == null) return false;
                if (data.displayWeaponId == entry.weaponId && weapon.held) data.displayWeaponId = id;
                entry.weaponId = id;
            }
            Save();
            return true;
        }

        // A copy can be passed to a future room/buy service without exposing mutable UI state.
        public TeamLoadout Snapshot(LobbyTeam team) => JsonUtility.FromJson<TeamLoadout>(JsonUtility.ToJson(ForTeam(team)));

        private TeamLoadout Normalize(TeamLoadout data, LobbyTeam team)
        {
            data = data ?? new TeamLoadout();
            var agent = catalog.Agent(data.agentId);
            if (agent == null || agent.team != team) data.agentId = team == LobbyTeam.CT ? "ctm_sas" : "tm_phoenix";
            var saved = data.slots ?? new List<EquippedSlot>();
            data.slots = new List<EquippedSlot>();
            foreach (var options in catalog.weapons.Where(w => w.Supports(team)).GroupBy(w => w.slot))
            {
                var old = saved.FirstOrDefault(s => s != null && s.slot == options.Key);
                string preferred = options.Key == "rifle.main" ? (team == LobbyTeam.CT ? "weapon_rif_m4a1_silencer" : "weapon_rif_ak47") :
                    options.Key == "pistol.start" ? (team == LobbyTeam.CT ? "weapon_pist_usp_silencer" : "weapon_pist_glock18") :
                    options.Key == "pistol.fast" ? (team == LobbyTeam.CT ? "weapon_pist_fiveseven" : "weapon_pist_tec9") :
                    options.Key == "pistol.heavy" ? "weapon_pist_deagle" : options.Key == "smg.silenced" ? "weapon_smg_mp7" : "";
                var chosen = options.FirstOrDefault(w => w.id == old?.weaponId) ?? options.FirstOrDefault(w => w.id == preferred) ?? options.First();
                data.slots.Add(new EquippedSlot { slot = options.Key, weaponId = chosen.id });
            }
            var displayed = catalog.Weapon(data.displayWeaponId);
            if (displayed == null || !displayed.held || !data.slots.Any(s => s.weaponId == displayed.id))
                data.displayWeaponId = data.slots.First(s => s.slot == "rifle.main").weaponId;
            return data;
        }

        private void Save()
        {
            PlayerPrefs.SetString(key, JsonUtility.ToJson(Data));
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
