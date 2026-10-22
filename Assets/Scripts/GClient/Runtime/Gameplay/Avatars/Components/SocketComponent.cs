using System;
using System.Collections.Generic;
using System.Linq;
using OperationBlacktide.Shared.Components;
using UnityEngine;

namespace OperationBlacktide.Client.Gameplay.Avatars
{
    public sealed class SocketComponent : GameComponent
    {
        public override IEnumerable<Type> Dependencies => new[] { typeof(ModelComponent) };
        public Transform Find(string name)
        {
            var root = Host.Get<ModelComponent>().Root;
            var socket = root != null ? root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name) : null;
            return socket != null ? socket : throw new InvalidOperationException("Missing model socket: " + name);
        }
    }
}
