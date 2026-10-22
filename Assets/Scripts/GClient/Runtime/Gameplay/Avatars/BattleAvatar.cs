using OperationBlacktide.Client.Content;
using UnityEngine;

namespace OperationBlacktide.Client.Gameplay.Avatars
{
    public sealed class BattleAvatar : MonoBehaviour
    {
        private Avatar avatar;
        public Animator Animator => avatar?.Animation.Animator;
        public Transform EquippedWeapon => avatar?.Attachments.Weapon?.Transform;
        public void Show(EquipmentCatalog catalog, string agent, string weapon)
        {
            var character = catalog.Agent(agent); var item = catalog.Weapon(weapon);
            if (character?.prefab == null || item?.prefab == null || !item.held) return;
            if (avatar == null) avatar = new Avatar();
            avatar.Load(agent, character.prefab, transform);
            if (!avatar.Equip(weapon, item.prefab, item.offhandPrefab)) return;
            // Sample imported grip pose before locomotion/action components bind.
            var pose = catalog.Pose(item.profile); var basis = catalog.Pose("m4");
            avatar.Animation.ApplyPose(catalog.controller, basis.idle, basis.inspect, pose.idle, pose.inspect, false);
        }
        private void OnDestroy() { avatar?.Dispose(); }
    }
}
