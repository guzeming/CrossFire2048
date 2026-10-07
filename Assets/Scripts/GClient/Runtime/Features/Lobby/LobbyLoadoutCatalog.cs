using System;
using System.Linq;
using UnityEngine;

namespace OperationBlacktide.Client.Features.Lobby
{
    public enum LobbyTeam { CT = 1, T = 2 }
    // Keep serialized category values stable; the tab layout can use a different visual order.
    public enum LoadoutCategory { Pistols = 0, Rifles = 1, SMGs = 2, Heavy = 3, Gear = 4, Snipers = 5 }

    [Serializable]
    public sealed class LobbyAgent
    {
        public string id, displayName;
        public LobbyTeam team;
        public GameObject prefab;
        public Sprite portrait;
    }

    [Serializable]
    public sealed class LobbyWeapon
    {
        public string id, displayName, slot, profile;
        public LoadoutCategory category;
        public int teams;
        public bool held;
        public GameObject prefab;
        public GameObject offhandPrefab;
        public Sprite thumbnail;
        public bool Supports(LobbyTeam team) => (teams & (int)team) != 0;
    }

    [Serializable]
    public sealed class LobbyPose
    {
        public string id;
        public AnimationClip idle, inspect;
    }

    [CreateAssetMenu(menuName = "OperationBlacktide/Lobby Loadout Catalog")]
    public sealed class LobbyLoadoutCatalog : ScriptableObject
    {
        public LobbyAgent[] agents = Array.Empty<LobbyAgent>();
        public LobbyWeapon[] weapons = Array.Empty<LobbyWeapon>();
        public LobbyPose[] poses = Array.Empty<LobbyPose>();
        public RuntimeAnimatorController controller;
        public LobbyAgent Agent(string id) => agents.FirstOrDefault(a => a.id == id);
        public LobbyWeapon Weapon(string id) => weapons.FirstOrDefault(w => w.id == id);
        public LobbyPose Pose(string id) => poses.FirstOrDefault(p => p.id == id);
        public LobbyWeapon[] Options(LobbyTeam team, string slot) => weapons.Where(w => w.Supports(team) && w.slot == slot).ToArray();
    }
}
