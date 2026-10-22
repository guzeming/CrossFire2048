using System;
using System.Collections.Generic;
using OperationBlacktide.Shared.Components;
using UnityEngine;

namespace OperationBlacktide.Client.Gameplay.Avatars
{
    public sealed class AttachmentComponent : GameComponent
    {
        public override IEnumerable<Type> Dependencies => new[] { typeof(SocketComponent) };
        public WeaponModel Weapon { get; private set; }
        public WeaponModel Offhand { get; private set; }
        public void Equip(string id, GameObject prefab, GameObject offhand)
        {
            Dispose(); var sockets = Host.Get<SocketComponent>();
            Weapon = new WeaponModel(prefab, sockets.Find("hand_R"), "Equipped_" + id);
            if (offhand != null) Offhand = new WeaponModel(offhand, sockets.Find("hand_L"), "Equipped_Offhand_" + id);
        }
        public override void Dispose() { Weapon?.Dispose(); Offhand?.Dispose(); Weapon = Offhand = null; }
    }
    /// <summary>Common model composition; behavior belongs to LobbyAvatar or BattleAvatar.</summary>
    public sealed class Avatar : ComponentHost
    {
        public string AgentId { get; private set; }
        public string WeaponId { get; private set; }
        public ModelComponent Model { get; }
        public AttachmentComponent Attachments { get; }
        public AnimationComponent Animation { get; }
        public Avatar(params GameComponent[] extensions)
        {
            Model = Add(new ModelComponent()); Add(new SocketComponent());
            Attachments = Add(new AttachmentComponent()); Animation = Add(new AnimationComponent());
            if (extensions != null) foreach (var extension in extensions) Add(extension);
            Start();
        }
        public void Load(string id, GameObject prefab, Transform parent)
        {
            if (AgentId == id && Model.Root != null) return;
            Attachments.Dispose(); Animation.Dispose(); WeaponId = null;
            Model.Load(prefab, parent, "SelectedAgent_" + id); AgentId = id;
        }
        public bool Equip(string id, GameObject prefab, GameObject offhand)
        {
            if (WeaponId == id && Attachments.Weapon != null) return false;
            Attachments.Equip(id, prefab, offhand); WeaponId = id; return true;
        }
    }
}
