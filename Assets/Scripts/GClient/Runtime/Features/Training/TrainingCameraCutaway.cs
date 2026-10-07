using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OperationBlacktide.Client.Features.Training
{
    /// <summary>Fades the whole obstructing building, including its roof and attached details.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class TrainingCameraCutaway : MonoBehaviour
    {
        [SerializeField, Range(.05f, .8f)] private float buildingOpacity = .2f;
        [SerializeField, Min(.05f)] private float probeRadius = .3f;
        private readonly Dictionary<Material, Material> fadedMaterials = new Dictionary<Material, Material>();
        private readonly Dictionary<MeshRenderer, Surface> surfaces = new Dictionary<MeshRenderer, Surface>();
        private readonly HashSet<Surface> obstructing = new HashSet<Surface>();
        private readonly HashSet<Surface> seeds = new HashSet<Surface>();
        private readonly List<Surface> applied = new List<Surface>();
        private readonly List<Surface> environment = new List<Surface>();
        private RaycastHit[] hits = new RaycastHit[32];
        private RaycastHit[] aimHits = new RaycastHit[32];
        private int obstructionFrame = -1;
        private Vector3 obstructionCameraPosition, obstructionTargetPosition;
        private Transform target;
        private Camera viewCamera;
        private bool previousOcclusionCulling;
        private bool ownsOcclusionCulling;

        private sealed class Surface
        {
            public MeshRenderer Renderer;
            public Material[] Original, Faded;
            public TrainingOcclusionGroup Group;
            public Bounds Bounds;
            public bool Structure, Ground;
        }

        public void Follow(Transform character)
        {
            Release();
            target = character;
            if (isActiveAndEnabled && target != null) Prepare();
        }

        private void OnEnable()
        {
            viewCamera = GetComponent<Camera>();
            RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
            RenderPipelineManager.endCameraRendering += EndCameraRendering;
            if (target != null) Prepare();
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= EndCameraRendering;
            Release();
        }

        private void Prepare()
        {
            // FBX chunks contain pieces from several buildings. Never fade their common
            // import parent or a shared material asset, which would affect unrelated buildings.
            foreach (var root in target.gameObject.scene.GetRootGameObjects())
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.transform.IsChildOf(target)) continue;
                var surface = new Surface {
                    Renderer = renderer, Original = renderer.sharedMaterials,
                    Group = renderer.GetComponentInParent<TrainingOcclusionGroup>(), Bounds = renderer.bounds
                };
                bool supported = false;
                string names = renderer.name.ToLowerInvariant();
                foreach (var material in surface.Original)
                {
                    if (material == null) continue;
                    supported |= material.shader.name == "Universal Render Pipeline/Lit";
                    names += " " + material.name.ToLowerInvariant();
                }
                if (!supported) continue;
                surface.Ground = ContainsAny(names, "floor", "ground", "terrain", "sidewalk", "curb", "road", "pavement");
                surface.Structure = ContainsAny(names, "plaster", "brick", "wall", "roof", "ceiling", "kasbah", "concrete",
                    "window", "door", "trim", "arch", "pillar", "column", "overlay", "decal", "sign", "asphalt");
                surfaces.Add(renderer, surface);
                environment.Add(surface);
            }
            previousOcclusionCulling = viewCamera.useOcclusionCulling;
            ownsOcclusionCulling = true;
            viewCamera.useOcclusionCulling = false;
        }

        private static bool ContainsAny(string value, params string[] words)
        {
            foreach (string word in words) if (value.Contains(word)) return true;
            return false;
        }

        // Aim picking alone ignores the surfaces faded by this camera. Their colliders remain
        // enabled on their original layers, so movement, muzzle rays and other cameras stay physical.
        public bool RaycastAim(Ray ray, out RaycastHit hit, float maxDistance)
        {
            if (!isActiveAndEnabled || target == null)
                return Physics.Raycast(ray, out hit, maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            RefreshObstructions();
            if (obstructing.Count == 0)
                return Physics.Raycast(ray, out hit, maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            int count;
            do
            {
                count = Physics.RaycastNonAlloc(ray, aimHits, maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                if (count < aimHits.Length) break;
                Array.Resize(ref aimHits, aimHits.Length * 2);
            } while (true);
            hit = default;
            float nearest = float.PositiveInfinity;
            bool found = false;
            // NonAlloc hits are unordered. Scan all of them, including geometry behind multiple faded roofs.
            for (int i = 0; i < count; i++)
            {
                var candidate = aimHits[i];
                if (candidate.distance >= nearest) continue;
                var surface = SurfaceFor(candidate.collider);
                if (surface != null && obstructing.Contains(surface)) continue;
                hit = candidate; nearest = candidate.distance; found = true;
            }
            return found;
        }

        private Surface SurfaceFor(Collider collider)
        {
            // Some prefabs place their collision shapes below the rendered mesh.
            var renderer = collider.GetComponentInParent<MeshRenderer>();
            return renderer != null && surfaces.TryGetValue(renderer, out var surface) ? surface : null;
        }

        private void RefreshObstructions(bool force = false)
        {
            // Picking happens before rendering. Refresh from the latest follow-camera/player pose,
            // and reuse that result for body aim, weapon aim and HUD queries in the same frame.
            Vector3 cameraPosition = viewCamera.transform.position, targetPosition = target.position;
            if (!force && obstructionFrame == Time.frameCount && obstructionCameraPosition == cameraPosition
                && obstructionTargetPosition == targetPosition) return;
            FindObstructions();
            obstructionFrame = Time.frameCount;
            obstructionCameraPosition = cameraPosition; obstructionTargetPosition = targetPosition;
        }

        private void FindObstructions()
        {
            seeds.Clear();
            obstructing.Clear();
            // Probe from the character towards the camera, including the full upper body.
            // This also finds the entry face when the camera itself is inside a building.
            for (int sample = 0; sample < 3; sample++)
            {
                Vector3 origin = target.position + Vector3.up * (.45f + sample * .6f);
                Vector3 offset = viewCamera.transform.position - origin;
                float length = offset.magnitude;
                if (length < .01f) continue;
                int count;
                do
                {
                    count = Physics.SphereCastNonAlloc(origin, probeRadius, offset / length, hits, length,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    if (count < hits.Length) break;
                    Array.Resize(ref hits, hits.Length * 2);
                } while (true);
                for (int i = 0; i < count; i++)
                {
                    if (hits[i].collider.transform.IsChildOf(target) || hits[i].point.y <= target.position.y + .15f) continue;
                    var surface = SurfaceFor(hits[i].collider);
                    if (surface != null && CanFade(surface))
                        seeds.Add(surface);
                }
            }

            foreach (var seed in seeds)
            {
                obstructing.Add(seed);
                Bounds building = seed.Renderer.bounds;
                building.Expand(.6f);
                foreach (var candidate in environment)
                {
                    if (seed.Group != null)
                    {
                        if (candidate.Group == seed.Group && CanFade(candidate)) obstructing.Add(candidate);
                    }
                    else if (candidate.Group == null && seed.Structure && candidate.Structure &&
                             building.Intersects(candidate.Bounds) && CanFade(candidate))
                    {
                        // Imported architecture has separate roof, wall, trim and decal meshes.
                        // Collect complete attached surfaces, without recursively spreading
                        // through adjoining blocks or the ground.
                        obstructing.Add(candidate);
                    }
                }
            }
        }

        private bool CanFade(Surface surface)
        {
            if (surface.Renderer == null || !surface.Renderer.enabled || !surface.Renderer.gameObject.activeInHierarchy) return false;
            if (surface.Group != null) return true;
            Bounds bounds = surface.Renderer.bounds;
            if (bounds.max.y <= target.position.y + .3f) return false;
            if (surface.Ground) return false;
            // Flat walkable surfaces at the player's feet stay opaque. Elevated roofs can fade.
            return bounds.size.y > .35f || bounds.min.y > target.position.y + 1.8f;
        }

        private Material[] FadedMaterials(Surface surface)
        {
            if (surface.Faded != null) return surface.Faded;
            surface.Faded = (Material[])surface.Original.Clone();
            for (int i = 0; i < surface.Original.Length; i++)
            {
                Material original = surface.Original[i];
                if (original == null || original.shader.name != "Universal Render Pipeline/Lit") continue;
                if (!fadedMaterials.TryGetValue(original, out Material faded))
                {
                    faded = new Material(original) { name = original.name + " (Occluded Building)", hideFlags = HideFlags.DontSave };
                    Color color = original.GetColor("_BaseColor");
                    color.a *= buildingOpacity;
                    faded.SetColor("_BaseColor", color);
                    // Preserve alpha-cutout shapes and their shadows when scaling opacity.
                    faded.SetFloat("_Cutoff", original.GetFloat("_Cutoff") * buildingOpacity);
                    faded.SetFloat("_Surface", 1);
                    faded.SetFloat("_Blend", 0);
                    faded.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    faded.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    faded.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                    faded.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                    faded.SetFloat("_ZWrite", 0);
                    faded.SetFloat("_AlphaToMask", 0);
                    faded.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    faded.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    faded.DisableKeyword("_ALPHAMODULATE_ON");
                    faded.SetOverrideTag("RenderType", "Transparent");
                    faded.renderQueue = (int)RenderQueue.Transparent;
                    faded.SetShaderPassEnabled("DepthOnly", false);
                    faded.SetShaderPassEnabled("DepthNormals", false);
                    fadedMaterials.Add(original, faded);
                }
                surface.Faded[i] = faded;
            }
            return surface.Faded;
        }

        private void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            RestoreApplied();
            if (target == null) { if (ownsOcclusionCulling) Release(); return; }
            if (camera != viewCamera) return;
            RefreshObstructions(true);
            foreach (var surface in obstructing)
            {
                surface.Renderer.sharedMaterials = FadedMaterials(surface);
                applied.Add(surface);
            }
        }

        private void EndCameraRendering(ScriptableRenderContext context, Camera camera) { RestoreApplied(); }

        private void RestoreApplied()
        {
            foreach (var surface in applied)
                if (surface.Renderer != null) surface.Renderer.sharedMaterials = surface.Original;
            applied.Clear();
        }

        private void Release()
        {
            RestoreApplied();
            foreach (var material in fadedMaterials.Values) Destroy(material);
            fadedMaterials.Clear();
            surfaces.Clear(); environment.Clear(); obstructing.Clear(); seeds.Clear();
            obstructionFrame = -1;
            if (ownsOcclusionCulling && viewCamera != null) viewCamera.useOcclusionCulling = previousOcclusionCulling;
            ownsOcclusionCulling = false;
        }
    }
}
