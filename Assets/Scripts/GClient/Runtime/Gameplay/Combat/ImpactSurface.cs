using System;
using System.Collections.Generic;
using UnityEngine;

namespace OperationBlacktide.Client.Gameplay.Combat
{
    public enum ImpactKind
    {
        Default, Concrete, Metal, MetalGrate, MetalBarrel, Wood, WoodPanel, Glass, Tile,
        Dirt, Sand, Grass, Plastic, Cardboard, Rubber, Cloth, Water, Body, Head, Armor, Helmet
    }

    /// <summary>Optional explicit surface for new props or collision-only geometry.</summary>
    [DisallowMultipleComponent]
    public sealed class ImpactSurface : MonoBehaviour
    {
        [SerializeField] private ImpactKind kind = ImpactKind.Concrete;
        public ImpactKind Kind { get => kind; set => kind = value; }
    }

    public static class ImpactSurfaceResolver
    {
        [Serializable] private sealed class SurfaceEntry { public string material, kind; }
        [Serializable] private sealed class SurfaceTable { public SurfaceEntry[] materials; }
        private static Dictionary<string, ImpactKind> materials;

        public static bool IsCharacter(ImpactKind kind) => kind == ImpactKind.Body || kind == ImpactKind.Head
            || kind == ImpactKind.Armor || kind == ImpactKind.Helmet;

        public static ImpactKind Resolve(RaycastHit hit)
        {
            if (hit.collider == null) return ImpactKind.Default;
            var target = hit.collider.GetComponentInParent<Damageable>();
            if (target != null)
            {
                var region = hit.collider.GetComponentInParent<HitRegion>();
                if (region != null && region.Part == HitBodyPart.Head)
                    return target.HasHeadArmor ? ImpactKind.Helmet : ImpactKind.Head;
                if (region != null && region.Part == HitBodyPart.Limb) return ImpactKind.Body;
                return target.HasBodyArmor ? ImpactKind.Armor : ImpactKind.Body;
            }
            var surface = hit.collider.GetComponentInParent<ImpactSurface>();
            if (surface != null) return surface.Kind;
            // One map mesh can contain wood, glass and metal. Pick the struck triangle's submesh.
            var renderer = hit.collider.GetComponent<Renderer>();
            if (renderer == null) return ImpactKind.Default;
            int slot = 0;
            if (hit.collider is MeshCollider meshCollider && meshCollider.sharedMesh != null && hit.triangleIndex >= 0)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh != meshCollider.sharedMesh) return ImpactKind.Default;
                var mesh = meshCollider.sharedMesh;
                long triangle = hit.triangleIndex;
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    long count = (long)mesh.GetIndexCount(i) / 3;
                    if (triangle < count) { slot = i; break; }
                    triangle -= count;
                }
            }
            var shared = renderer.sharedMaterials;
            return slot < shared.Length ? ResolveMaterial(shared[slot]) : ImpactKind.Default;
        }

        public static ImpactKind ResolveMaterial(Material material)
        {
            if (material == null) return ImpactKind.Default;
            if (materials == null)
            {
                materials = new Dictionary<string, ImpactKind>(StringComparer.Ordinal);
                var source = Resources.Load<TextAsset>("Combat/ImpactAudio/surfaces");
                var table = source != null ? JsonUtility.FromJson<SurfaceTable>(source.text) : null;
                if (table?.materials != null)
                    foreach (var entry in table.materials)
                        if (Enum.TryParse(entry.kind, out ImpactKind kind)) materials[entry.material] = kind;
            }
            string name = material.name;
            const string suffix = " (Instance)";
            if (name.EndsWith(suffix, StringComparison.Ordinal)) name = name.Substring(0, name.Length - suffix.Length);
            const string occluded = " (Occluded Building)";
            if (name.EndsWith(occluded, StringComparison.Ordinal)) name = name.Substring(0, name.Length - occluded.Length);
            return materials.TryGetValue(name, out var result) ? result : ImpactKind.Default;
        }
    }
}
