using UnityEngine;
using UnityEngine.Rendering;

namespace OperationBlacktide.Client.Features.Training
{
    /// <summary>Bounded, surface-aligned impact marks. No colliders or per-hit material instances.</summary>
    public sealed class TrainingBulletHoles : MonoBehaviour
    {
        public const int Capacity = 128;
        private const float Lifetime = 45f;
        private static readonly int Opacity = Shader.PropertyToID("_Opacity");
        private sealed class Mark
        {
            public Transform transform;
            public MeshRenderer renderer;
            public Collider surface;
            public Vector3 point, normal, tangent;
            public float born;
        }
        private readonly Mark[] marks = new Mark[Capacity];
        private MaterialPropertyBlock properties;
        private Material[] materials;
        private Mesh quad;
        private int next;

        public void Initialize(Material[] textures)
        {
            properties = new MaterialPropertyBlock();
            materials = textures;
            if (materials == null || materials.Length == 0) { enabled = false; return; }
            quad = new Mesh { name = "Bullet Hole Quad" };
            quad.vertices = new[] { new Vector3(-.5f,-.5f,0), new Vector3(.5f,-.5f,0), new Vector3(.5f,.5f,0), new Vector3(-.5f,.5f,0) };
            quad.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            quad.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            quad.RecalculateBounds();
            for (int i = 0; i < Capacity; i++)
            {
                var go = new GameObject("Bullet Hole");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.enabled = false;
                marks[i] = new Mark { transform = go.transform, renderer = renderer };
            }
        }

        public void Place(RaycastHit hit)
        {
            if (!enabled || hit.collider == null || hit.collider.isTrigger || quad == null) return;
            Quaternion rotation = Quaternion.LookRotation(hit.normal) * Quaternion.Euler(0, 0, Random.Range(0f, 360f));
            float size = Random.Range(.24f, .30f);
            // Shrink near an edge instead of leaving part of a flat mark floating beyond the wall.
            for (int attempt = 0; !FitsSurface(hit, rotation, size); attempt++)
            {
                if (attempt == 4) return;
                size *= .6f;
            }
            Mark mark = null;
            foreach (var candidate in marks)
            {
                if (candidate.surface == hit.collider && candidate.renderer.enabled &&
                    Vector3.Distance(hit.collider.transform.TransformPoint(candidate.point), hit.point) < size * .3f)
                { mark = candidate; break; }
            }
            if (mark == null) { mark = marks[next]; next = (next + 1) % Capacity; }
            mark.surface = hit.collider;
            var surface = hit.collider.transform;
            mark.point = surface.InverseTransformPoint(hit.point);
            mark.normal = surface.localToWorldMatrix.transpose.MultiplyVector(hit.normal).normalized;
            mark.tangent = surface.InverseTransformVector(rotation * Vector3.right);
            mark.born = Time.time;
            mark.transform.localScale = Vector3.one * size;
            mark.renderer.sharedMaterial = materials[Random.Range(0, materials.Length)];
            properties.SetFloat(Opacity, 1);
            mark.renderer.SetPropertyBlock(properties);
            mark.renderer.enabled = true;
            UpdatePose(mark);
        }

        private static bool FitsSurface(RaycastHit hit, Quaternion rotation, float size)
        {
            for (int corner = 0; corner < 4; corner++)
            {
                var offset = rotation * new Vector3((corner & 1) == 0 ? -.5f : .5f, (corner & 2) == 0 ? -.5f : .5f, 0) * size;
                Vector3 sample = hit.point + offset;
                if (!hit.collider.Raycast(new Ray(sample + hit.normal * .025f, -hit.normal), out var contact, .05f)
                    || Vector3.Dot(hit.normal, contact.normal) < .96f
                    || Mathf.Abs(Vector3.Dot(contact.point - sample, hit.normal)) > .008f) return false;
            }
            return true;
        }

        private void LateUpdate() { UpdateMarks(Time.time); }

        public void UpdateMarks(float now)
        {
            if (quad == null) return;
            foreach (var mark in marks)
            {
                if (!mark.renderer.enabled) continue;
                float remaining = Lifetime - (now - mark.born);
                if (remaining <= 0 || mark.surface == null || !mark.surface.enabled || !mark.surface.gameObject.activeInHierarchy)
                { mark.renderer.enabled = false; mark.surface = null; continue; }
                UpdatePose(mark);
                if (remaining < 3)
                {
                    properties.SetFloat(Opacity, remaining / 3);
                    mark.renderer.SetPropertyBlock(properties);
                }
            }
        }

        private static void UpdatePose(Mark mark)
        {
            Transform surface = mark.surface.transform;
            Vector3 normal = surface.worldToLocalMatrix.transpose.MultiplyVector(mark.normal).normalized;
            Vector3 right = surface.TransformVector(mark.tangent).normalized;
            mark.transform.SetPositionAndRotation(surface.TransformPoint(mark.point) + normal * .0025f,
                Quaternion.LookRotation(normal, Vector3.Cross(normal, right)));
        }

        private void OnDestroy() { if (quad != null) Destroy(quad); }
    }
}
