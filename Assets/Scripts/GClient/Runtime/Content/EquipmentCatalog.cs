using System;
using System.Linq;
using UnityEngine;

namespace OperationBlacktide.Client.Content
{
    public enum EquipmentTeam { CT = 1, T = 2 }
    // Keep serialized category values stable; the tab layout can use a different visual order.
    public enum EquipmentCategory { Pistols = 0, Rifles = 1, SMGs = 2, Heavy = 3, Gear = 4, Snipers = 5 }

    [Serializable]
    public sealed class AgentModelDefinition
    {
        public string id, displayName;
        public EquipmentTeam team;
        public GameObject prefab;
        public Sprite portrait;
    }

    [Serializable]
    public sealed class WeaponModelDefinition
    {
        public string id, displayName, slot, profile;
        public EquipmentCategory category;
        public int teams;
        public bool held;
        public GameObject prefab;
        public GameObject offhandPrefab;
        public Sprite thumbnail;
        public bool Supports(EquipmentTeam team) => (teams & (int)team) != 0;
    }

    [Serializable]
    public sealed class EquipmentPose
    {
        public string id;
        public AnimationClip idle, inspect;
    }

    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "OperationBlacktide.Client.Features.Lobby", "Assembly-CSharp", "LobbyLoadoutCatalog")]
    [CreateAssetMenu(menuName = "OperationBlacktide/Content/Equipment Catalog")]
    public sealed class EquipmentCatalog : ScriptableObject
    {
        public AgentModelDefinition[] agents = Array.Empty<AgentModelDefinition>();
        public WeaponModelDefinition[] weapons = Array.Empty<WeaponModelDefinition>();
        public EquipmentPose[] poses = Array.Empty<EquipmentPose>();
        public RuntimeAnimatorController controller;
        public AgentModelDefinition Agent(string id) => agents.FirstOrDefault(a => a.id == id);
        public WeaponModelDefinition Weapon(string id) => weapons.FirstOrDefault(w => w.id == id);
        public EquipmentPose Pose(string id) => poses.FirstOrDefault(p => p.id == id);
        public WeaponModelDefinition[] Options(EquipmentTeam team, string slot) => weapons.Where(w => w.Supports(team) && w.slot == slot).ToArray();
    }
}
