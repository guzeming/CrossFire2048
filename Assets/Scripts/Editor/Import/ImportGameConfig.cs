using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using OperationBlacktide.Shared.GameLogic;
using OperationBlacktide.Shared.Combat;

namespace OperationBlacktide.Client.Editor
{
    public static class ImportGameConfig
    {
        [Serializable] private sealed class Weapons { public WeaponInfo[] weapons; }
        [MenuItem("OperationBlacktide/Configuration/Import Portable Config")]
        public static void Import()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop play mode before importing configuration.");
            string source = Path.GetFullPath("GConfig");
            var files = new[] { "Weapons.json", "BattleWeapon.json", "DemolitionRules.json", "Modes/training.json", "Modes/battle.json" };
            // Validate the complete set before changing generated assets.
            foreach (string file in files)
            {
                string json = File.ReadAllText(Path.Combine(source, file));
                if (file == "DemolitionRules.json") JsonUtility.FromJson<OperationBlacktide.Shared.Modes.Demolition.DemolitionRules>(json).Validate();
                else if (file.StartsWith("Modes/")) JsonUtility.FromJson<GameLogicInfo>(json).Validate();
                else if (file == "BattleWeapon.json") JsonUtility.FromJson<WeaponInfo>(json).Validate();
                else foreach (var weapon in JsonUtility.FromJson<Weapons>(json).weapons) weapon.Validate();
            }
            foreach (string file in files)
            {
                string target = Path.Combine("Assets/Resources/Config", file);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(Path.Combine(source, file), target, true);
            }
            AssetDatabase.Refresh();
            Debug.Log("GAME_CONFIG_IMPORT_PASS");
        }
    }
}
