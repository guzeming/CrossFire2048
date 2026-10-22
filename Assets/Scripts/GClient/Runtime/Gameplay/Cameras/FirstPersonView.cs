using System.Collections.Generic;
using OperationBlacktide.Client.Gameplay.Characters;
using OperationBlacktide.Client.Gameplay.Combat;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OperationBlacktide.Client.Gameplay.Cameras
{
    // This is presentation only: ammunition, hit tests and network commands remain owned
    // by the existing weapon controllers. The hands and weapon share one authored timeline.
    [DefaultExecutionOrder(220)]
    public sealed class FirstPersonView : MonoBehaviour
    {
        public const int ViewmodelLayer = 31;
        private CharacterMotor owner;
        private Camera worldCamera, armsCamera;
        private FirstPersonCatalog catalog;
        private GameObject model;
        private FirstPersonWeapon equipped;
        private WeaponPresentation actions;
        private readonly Dictionary<Renderer, ShadowCastingMode> hidden = new Dictionary<Renderer, ShadowCastingMode>();
        private float drawElapsed, walkPhase;
        private float weaponKick;
        private int originalMask;
        private float originalNear;
        private bool scoped;
        private ParticleSystem muzzleFlash;
        private Transform muzzle, leftMuzzle;
        private readonly List<Renderer> weaponSurfaces = new List<Renderer>();
        private int presentedShots;
        public GameObject Model => model;
        public string PresentedAction { get; private set; }

        public void Bind(CharacterMotor character)
        {
            Release();
            owner = character;
            if (owner == null) return;
            worldCamera = GetComponent<Camera>();
            originalMask = worldCamera.cullingMask; originalNear = worldCamera.nearClipPlane;
            worldCamera.cullingMask &= ~(1 << ViewmodelLayer);
            worldCamera.nearClipPlane = .04f;
            catalog = Resources.Load<FirstPersonCatalog>("Gameplay/FirstPerson");
            var child = new GameObject("First Person Hands Camera");
            child.transform.SetParent(transform, false);
            armsCamera = child.AddComponent<Camera>();
            armsCamera.cullingMask = 1 << ViewmodelLayer;
            armsCamera.nearClipPlane = .01f; armsCamera.farClipPlane = 5;
            armsCamera.fieldOfView = 60;
            armsCamera.allowHDR = worldCamera.allowHDR;
            var data = armsCamera.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Overlay; // Overlay cameras clear depth by default.
            data.renderPostProcessing = false; data.renderShadows = false;
            worldCamera.GetUniversalAdditionalCameraData().cameraStack.Add(armsCamera);
            enabled = true;
        }

        public void SetScoped(bool value) { scoped = value; }

        private void LateUpdate() { Present(Time.deltaTime); }
        public void Present(float dt)
        {
            if (owner == null || armsCamera == null) return;
            // Equipment can replace renderers while first person is active.
            foreach (var renderer in owner.GetComponentsInChildren<Renderer>(true))
            {
                if (!hidden.ContainsKey(renderer)) hidden.Add(renderer, renderer.shadowCastingMode);
                renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }
            actions = owner.GetComponent<WeaponPresentation>();
            var local = owner.GetComponent<WeaponController>();
            string id = local != null ? local.WeaponId : actions != null ? actions.Definition?.id : null;
            var next = catalog != null ? catalog.Find(id) : null;
            if (next != equipped)
            {
                if (model != null) { model.SetActive(false); Destroy(model); }
                model = null;
                equipped = next; drawElapsed = 0; weaponKick = 0;
                muzzle = leftMuzzle = null; muzzleFlash = null;
                weaponSurfaces.Clear();
                presentedShots = actions != null ? actions.FireAnimationsPlayed : 0;
                if (equipped != null)
                {
                    model = Instantiate(equipped.prefab, transform, false);
                    foreach (var node in model.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = ViewmodelLayer;
                    foreach (var renderer in model.GetComponentsInChildren<Renderer>())
                    {
                        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                        if (renderer.name != "Gloves" && renderer.name != "Sleeves") weaponSurfaces.Add(renderer);
                    }
                    foreach (var animator in model.GetComponentsInChildren<Animator>()) animator.enabled = false;
                    foreach (var node in model.GetComponentsInChildren<Transform>())
                    {
                        if (node.name == "ViewmodelMuzzle") muzzle = node;
                        if (node.name == "ViewmodelMuzzleLeft") leftMuzzle = node;
                    }
                    var flashObject = new GameObject("Viewmodel Muzzle Flash");
                    flashObject.layer = ViewmodelLayer; flashObject.transform.SetParent(model.transform, false);
                    muzzleFlash = flashObject.AddComponent<ParticleSystem>();
                    muzzleFlash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    var main = muzzleFlash.main; main.playOnAwake = false; main.loop = false;
                    main.startLifetime = .035f; main.startSpeed = 0; main.maxParticles = 4;
                    main.simulationSpace = ParticleSystemSimulationSpace.Local;
                    var emission = muzzleFlash.emission; emission.enabled = false;
                    var shape = muzzleFlash.shape; shape.enabled = false;
                    var flashRenderer = muzzleFlash.GetComponent<ParticleSystemRenderer>();
                    flashRenderer.sharedMaterial = Resources.Load<WeaponCatalog>("Gameplay/Weapons")?.flashMaterial;
                    flashRenderer.shadowCastingMode = ShadowCastingMode.Off;
                }
            }
            if (model == null) return;
            var throwable = local != null ? local.Throwables : null;
            bool throwing = throwable != null && throwable.isActiveAndEnabled && throwable.Equipped != null;
            bool released = throwing && throwable.IsThrowing && throwable.PhaseTime >= throwable.Equipped.ReleaseTime;
            foreach (var surface in weaponSurfaces) if (surface != null) surface.enabled = !released;
            model.SetActive(!scoped && (throwing ? throwable.CanThrow || throwable.IsThrowing : actions != null && actions.isActiveAndEnabled));
            float step = owner.InputEnabled ? Mathf.Max(0, dt) : 0;
            weaponKick *= Mathf.Exp(-18 * step);
            drawElapsed += step;
            AnimationClip clip = equipped.idle; float time = 0; PresentedAction = "idle";
            if (throwing)
            {
                var data = throwable.Equipped;
                clip = throwable.IsThrowing ? equipped.fire : throwable.IsPreparing ? equipped.prepare :
                    throwable.Phase == ThrowPhase.Drawing ? equipped.draw : equipped.idle;
                var source = throwable.IsThrowing ? data.throwAnimation : throwable.IsPreparing ? data.prepareAnimation :
                    throwable.Phase == ThrowPhase.Drawing ? data.drawAnimation : data.idleAnimation;
                if (clip != null) time = throwable.Phase == ThrowPhase.Holding ? clip.length :
                    throwable.PhaseTime / Mathf.Max(.001f, source.length) * clip.length;
                PresentedAction = throwable.Phase.ToString();
            }
            else if (actions != null && actions.IsReloading && equipped.reload != null)
            { clip = equipped.reload; time = actions.PresentedReloadProgress * clip.length; PresentedAction = "reload"; }
            else if (actions != null && actions.FireElapsed < (actions.AlternateFire && equipped.alternate != null ? equipped.alternate.length : equipped.fire.length))
            {
                clip = actions.AlternateFire && equipped.alternate != null ? equipped.alternate : equipped.fire;
                time = actions.FireElapsed; PresentedAction = "fire";
                drawElapsed = float.MaxValue;
            }
            else if (equipped.draw != null && drawElapsed < equipped.draw.length)
            { clip = equipped.draw; time = drawElapsed; PresentedAction = "draw"; }
            if (clip != null) clip.SampleAnimation(model, Mathf.Clamp(time, 0, clip.length));
            if (actions != null && presentedShots != actions.FireAnimationsPlayed)
            {
                presentedShots = actions.FireAnimationsPlayed;
                var definition = actions.Definition;
                if (definition != null && !definition.IsMelee && !definition.IsTaser)
                    weaponKick = Mathf.Min(.085f, weaponKick + (definition.IsSniper || definition.pelletCount > 1 ? .055f : .035f));
                if (!scoped && muzzle != null && definition != null && !definition.IsMelee && !definition.IsTaser)
                {
                    var attachment = actions.AlternateFire && leftMuzzle != null ? leftMuzzle : muzzle;
                    Vector3 point = model.transform.InverseTransformPoint(attachment.position);
                    muzzleFlash.Emit(new ParticleSystem.EmitParams { position = point, startSize = definition.silenced ? .08f : .16f }, 1);
                }
            }
            // Small movement only affects the model, never the aim ray or world camera.
            float speed = Vector3.ProjectOnPlane(owner.ActualVelocity, Vector3.up).magnitude;
            walkPhase += step * speed * 2.2f;
            float amount = owner.IsGrounded ? Mathf.Clamp01(speed / 4.5f) : 0;
            model.transform.localPosition = new Vector3(Mathf.Sin(walkPhase) * .006f, Mathf.Cos(walkPhase * 2) * .004f, 0) * amount
                + Vector3.back * weaponKick;
        }

        public void Release()
        {
            foreach (var entry in hidden) if (entry.Key != null) entry.Key.shadowCastingMode = entry.Value;
            hidden.Clear();
            if (worldCamera != null)
            {
                worldCamera.cullingMask = originalMask; worldCamera.nearClipPlane = originalNear;
                if (armsCamera != null) worldCamera.GetUniversalAdditionalCameraData().cameraStack.Remove(armsCamera);
            }
            if (armsCamera != null) { armsCamera.enabled = false; Destroy(armsCamera.gameObject); }
            if (model != null) { model.SetActive(false); Destroy(model); }
            armsCamera = null; worldCamera = null; model = null; owner = null; equipped = null;
            weaponSurfaces.Clear();
        }
        private void OnDisable() { Release(); }
    }
}
