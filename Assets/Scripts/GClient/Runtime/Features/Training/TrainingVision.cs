using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OperationBlacktide.Client.Features.Training
{
    /// <summary>Character sight is independent of the overhead camera's building cutaway.</summary>
    [DefaultExecutionOrder(210), RequireComponent(typeof(Camera))]
    public sealed class TrainingVision : MonoBehaviour
    {
        [SerializeField, Range(30, 180)] private float fieldOfView = 110;
        [SerializeField, Min(1)] private float viewDistance = 28;
        [SerializeField, Min(1)] private float scopedDistance = 55;
        [SerializeField, Min(0)] private float awarenessRadius = 2.4f;
        [SerializeField, Range(0, .95f)] private float darkness = .64f;
        [SerializeField, Min(.01f)] private float edgeSoftness = .65f;
        [SerializeField, Min(.1f)] private float eyeHeight = 1.5f;
        private const int Samples = 720;
        private readonly float[] distances = new float[Samples];
        private readonly List<HiddenRenderer> hidden = new List<HiddenRenderer>();
        private RaycastHit[] hits = new RaycastHit[16];
        private Transform observer;
        private Camera viewCamera;
        private TrainingCameraController followCamera;
        private TrainingWeaponController weapon;
        private Texture2D distanceTexture;
        private float nextSample;
        private Vector3 sampledOrigin;
        private float sampledRange;
        public bool HasObserver => isActiveAndEnabled && observer != null;
        public Vector3 EyePosition => observer.position + Vector3.up * eyeHeight;
        public float ViewDistance => Mathf.Lerp(viewDistance, scopedDistance, followCamera != null ? followCamera.ScopeBlend : 0);
        public float FieldOfView => fieldOfView;
        public float AwarenessRadius => awarenessRadius;
        private TrainingThrowableWorld ThrowableWorld => weapon != null && weapon.Throwables != null ? weapon.Throwables.World : null;

        private struct HiddenRenderer
        {
            public Renderer Renderer;
            public bool Previous;
        }

        public void Follow(Transform character)
        {
            RestoreTargets();
            observer = character;
            followCamera = GetComponent<TrainingCameraController>();
            weapon = character != null ? character.GetComponent<TrainingWeaponController>() : null;
            nextSample = float.NegativeInfinity;
        }

        private void OnEnable()
        {
            viewCamera = GetComponent<Camera>();
            followCamera = GetComponent<TrainingCameraController>();
            nextSample = float.NegativeInfinity;
            RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
            RenderPipelineManager.endCameraRendering += EndCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= EndCameraRendering;
            RestoreTargets();
            if (distanceTexture != null) Destroy(distanceTexture);
            distanceTexture = null;
        }

        /// <summary>Exact 3D LOS for gameplay/UI; never trusts a camera-to-cursor ray.</summary>
        public bool CanSeePoint(Vector3 point)
        {
            if (!HasObserver) return true;
            Vector3 planar = Vector3.ProjectOnPlane(point - observer.position, Vector3.up);
            float distance = planar.magnitude;
            if (distance > ViewDistance) return false;
            if (distance > awarenessRadius && Vector3.Dot(observer.forward, planar / distance) <
                Mathf.Cos(fieldOfView * .5f * Mathf.Deg2Rad)) return false;
            var world = ThrowableWorld;
            if (world != null && (world.FlashOpacity > .15f || world.IsSmokeOccluded(EyePosition, point))) return false;
            Vector3 offset = point - EyePosition;
            float length = offset.magnitude;
            // All physical architecture counts, including buildings made transparent by cutaway.
            return length < .01f || ObstructionDistance(EyePosition, offset / length, length) >= length - .04f;
        }

        public bool CanSeeTarget(TrainingTarget target)
        {
            if (target == null || !target.IsAlive || !target.gameObject.activeInHierarchy) return false;
            Vector3 position = target.transform.position;
            // A head visible above low cover reveals the target; full-height walls hide all samples.
            return CanSeePoint(position + Vector3.up * 1.6f) ||
                CanSeePoint(position + Vector3.up) || CanSeePoint(position + Vector3.up * .45f);
        }

        private float ObstructionDistance(Vector3 origin, Vector3 direction, float length)
        {
            int count;
            do
            {
                count = Physics.RaycastNonAlloc(origin, direction, hits, length, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore);
                if (count < hits.Length) break;
                Array.Resize(ref hits, hits.Length * 2);
            } while (true);
            float nearest = length;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit.distance >= nearest || hit.collider.transform.IsChildOf(observer) ||
                    hit.collider.GetComponentInParent<TrainingTarget>() != null) continue;
                nearest = hit.distance;
            }
            return nearest;
        }

        public void RefreshMask(bool force = false)
        {
            if (!HasObserver) return;
            Vector3 origin = EyePosition;
            float range = ViewDistance;
            // Rotation is evaluated in the shader every frame. Geometry is sampled at up to 20 Hz;
            // teleports and changing scope range refresh immediately. Target LOS is always current.
            if (!force && Time.unscaledTime < nextSample && (origin - sampledOrigin).sqrMagnitude < 1 &&
                Mathf.Abs(range - sampledRange) < .5f) return;
            if (distanceTexture == null)
                distanceTexture = new Texture2D(Samples, 1, TextureFormat.RFloat, false, true) {
                    name = "Training Sight Distances", filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.DontSave
                };
            var world = ThrowableWorld;
            for (int i = 0; i < Samples; i++)
            {
                float angle = i * Mathf.PI * 2 / Samples;
                Vector3 direction = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
                float length = ObstructionDistance(origin, direction, range);
                if (world != null) length *= world.ClipVisibility(origin, origin + direction * length);
                distances[i] = length;
            }
            distanceTexture.SetPixelData(distances, 0);
            distanceTexture.Apply(false, false);
            sampledOrigin = origin; sampledRange = range; nextSample = Time.unscaledTime + .05f;
        }

        public void BindMaterial(Material material)
        {
            RefreshMask();
            material.SetTexture("_SightDistances", distanceTexture);
            material.SetVector("_SightOrigin", sampledOrigin);
            material.SetVector("_SightForward", observer.forward);
            material.SetVector("_SightSettings", new Vector4(ViewDistance, awarenessRadius,
                Mathf.Cos(fieldOfView * .5f * Mathf.Deg2Rad), darkness));
            material.SetVector("_SightFeather", new Vector4(edgeSoftness,
                Mathf.Cos((fieldOfView * .5f - 7) * Mathf.Deg2Rad), Samples, 0));
        }

        private void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            RestoreTargets();
            if (camera != viewCamera || !HasObserver) return;
            // Registration makes freshly spawned targets obey sight on their first rendered frame.
            foreach (var target in TrainingTarget.ActiveTargets)
            {
                if (target == null || target.gameObject.scene != observer.gameObject.scene || CanSeeTarget(target)) continue;
                foreach (var renderer in target.Surfaces)
                {
                    if (renderer == null) continue;
                    hidden.Add(new HiddenRenderer { Renderer = renderer, Previous = renderer.forceRenderingOff });
                    renderer.forceRenderingOff = true;
                }
            }
        }

        private void EndCameraRendering(ScriptableRenderContext context, Camera camera) { RestoreTargets(); }

        private void RestoreTargets()
        {
            foreach (var item in hidden)
                if (item.Renderer != null) item.Renderer.forceRenderingOff = item.Previous;
            hidden.Clear();
        }
    }
}
