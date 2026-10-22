using System;
using UnityEngine;

namespace OperationBlacktide.Client.Gameplay.Cameras
{
    [Serializable]
    public sealed class FirstPersonWeapon
    {
        public string id;
        public GameObject prefab;
        public AnimationClip idle, draw, fire, reload, alternate, prepare;
    }

    public sealed class FirstPersonCatalog : ScriptableObject
    {
        public FirstPersonWeapon[] weapons = Array.Empty<FirstPersonWeapon>();
        public FirstPersonWeapon Find(string id) => Array.Find(weapons, w => w.id == id);
    }
}
