using OperationBlacktide.Client.Content;
using OperationBlacktide.Client.Gameplay.Avatars;
using UnityEngine;

namespace OperationBlacktide.Client.Features.Lobby
{
    /// <summary>Scene/UI serialization adapter. Appearance ownership belongs to LobbyAvatar.</summary>
    public sealed class LobbyLoadoutPreview : MonoBehaviour
    {
        [SerializeField] private GameObject originalModel;
        private LobbyAvatar avatar;
        public string AgentId => avatar?.AgentId;
        public string WeaponId => avatar?.WeaponId;
        public Animator Animator => avatar?.Animator;
        public Transform EquippedWeapon => avatar?.EquippedWeapon;
        private void Awake()
        { if (originalModel == null && transform.childCount > 0) originalModel = transform.GetChild(0).gameObject; }
        public void Show(EquipmentCatalog assets, string agent, string item)
        {
            if (originalModel != null) originalModel.SetActive(false);
            if (avatar == null) avatar = gameObject.AddComponent<LobbyAvatar>();
            avatar.Show(assets, agent, item);
        }
        public void Inspect() { avatar?.Inspect(); }
        public static Bounds StaticMeshBounds(GameObject root) => ModelComponent.StaticMeshBounds(root);
    }
}
