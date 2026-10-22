using System;
using System.Collections.Generic;
using System.Linq;
using OperationBlacktide.Shared.Components;
using UnityEngine;

namespace OperationBlacktide.Client.Gameplay.Avatars
{
    /// <summary>Unity model adapter; gameplay never stores these engine objects.</summary>
    public sealed class ModelComponent : GameComponent
    {
        public GameObject Root { get; private set; }
        public void Load(GameObject prefab, Transform parent, string name)
        { Dispose(); Root = UnityEngine.Object.Instantiate(prefab, parent, false); Root.name = name; }
        public override void Dispose() { Release(Root); Root = null; }
        public static void Release(UnityEngine.Object value)
        {
            if (value == null) return;
            if (value is GameObject go) go.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value);
        }
        public static Bounds StaticMeshBounds(GameObject root)
        {
            var result = new Bounds(); bool first = true;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                var bounds = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var point = filter.transform.TransformPoint(bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                    if (first) { result = new Bounds(point, Vector3.zero); first = false; } else result.Encapsulate(point);
                }
            }
            return result;
        }
    }
}
