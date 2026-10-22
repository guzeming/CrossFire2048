using OperationBlacktide.Client.Content;
using UnityEngine;

namespace OperationBlacktide.Client.Gameplay.Avatars
{
    public sealed class LobbyAvatar : MonoBehaviour
    {
        private Avatar avatar;
        private WeaponModel preview;
        private string displayId;
        public string AgentId => avatar?.AgentId;
        public string WeaponId => displayId;
        public Animator Animator => avatar?.Animation.Animator;
        public Transform EquippedWeapon => avatar?.Attachments.Weapon?.Transform;
        public void Show(EquipmentCatalog catalog, string agent, string item)
        {
            var character = catalog.Agent(agent); var equipment = catalog.Weapon(item);
            if (character?.prefab == null || equipment?.prefab == null) return;
            if (avatar == null) avatar = new Avatar();
            bool changedActor = avatar.AgentId != agent;
            avatar.Load(agent, character.prefab, transform);
            avatar.Model.Root.SetActive(equipment.held);
            if (displayId == item && !changedActor) return;
            preview?.Dispose(); preview = null; displayId = item;
            if (!equipment.held)
            {
                avatar.Attachments.Dispose();
                preview = new WeaponModel(equipment.prefab, transform, "Preview_" + item);
                preview.FramePreview(transform); return;
            }
            avatar.Equip(item, equipment.prefab, equipment.offhandPrefab);
            var pose = catalog.Pose(equipment.profile); var basis = catalog.Pose("m4");
            avatar.Animation.ApplyPose(catalog.controller, basis.idle, basis.inspect, pose.idle, pose.inspect, true);
        }
        public void Inspect()
        { if (avatar?.Model.Root != null && avatar.Model.Root.activeSelf && Animator != null) Animator.CrossFadeInFixedTime("Inspect", .25f, 0, 0); }
        private void OnDestroy() { preview?.Dispose(); avatar?.Dispose(); }
    }
}
