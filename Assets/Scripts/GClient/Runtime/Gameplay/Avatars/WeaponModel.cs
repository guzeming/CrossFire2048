using OperationBlacktide.Shared.Components;
using UnityEngine;

namespace OperationBlacktide.Client.Gameplay.Avatars
{
    public sealed class WeaponModel : ComponentHost
    {
        public ModelComponent Model { get; }
        public Transform Transform => Model.Root != null ? Model.Root.transform : null;
        public WeaponModel(GameObject prefab, Transform parent, string name, params GameComponent[] extensions)
        {
            Model = Add(new ModelComponent()); Add(new SocketComponent());
            if (extensions != null) foreach (var extension in extensions) Add(extension);
            Start(); Model.Load(prefab, parent, name);
        }
        public void FramePreview(Transform origin)
        {
            Bounds bounds = ModelComponent.StaticMeshBounds(Model.Root);
            float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (size > .0001f) Transform.localScale *= 1.1f / size;
            bounds = ModelComponent.StaticMeshBounds(Model.Root);
            Transform.position += origin.TransformPoint(new Vector3(0, 1.2f, 0)) - bounds.center;
        }
    }
}
