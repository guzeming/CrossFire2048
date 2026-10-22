using OperationBlacktide.Client.Gameplay.Characters;
using OperationBlacktide.Client.Gameplay.Avatars;
using UnityEngine;
using System.Collections.Generic;

namespace OperationBlacktide.Client.Gameplay.Combat
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "OperationBlacktide.Client.Features.Training", "Assembly-CSharp", "TrainingThrowPhase")]
    public enum ThrowPhase { Drawing, Ready, Preparing, Holding, Throwing }
    /// <summary>Hold left mouse to aim; release to throw. Supply policy is configured by the mode.</summary>
    [DefaultExecutionOrder(195)]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "OperationBlacktide.Client.Features.Training", "Assembly-CSharp", "TrainingThrowableController")]
    public sealed class ThrowableController : MonoBehaviour
    {
        private CharacterMotor player;
        private ThrowableCatalog catalog;
        private ThrowableDefinition equipped;
        private Transform heldModel;
        private LineRenderer arc, landing;
        private Material guideMaterial;
        private AudioSource handling;
        private bool needsRelease = true, releaseQueued, pinPlayed, projectileReleased;
        private float phaseTime;
        private Vector3 modelCenter;
        private ThrowablePresentation presentation;
        private readonly Vector3[] points = new Vector3[190];
        private readonly Dictionary<string, int> supplies = new Dictionary<string, int>();
        public bool InfiniteSupply { get; set; }
        public int Remaining => equipped == null ? 0 : supplies.TryGetValue(equipped.id, out var count) ? count : 0;
        public ThrowableWorld World { get; private set; }
        public ThrowableDefinition Equipped => equipped;
        public bool CanThrow => equipped != null && (InfiniteSupply || Remaining > 0);
        public bool IsPreparing => Phase == ThrowPhase.Preparing || Phase == ThrowPhase.Holding;
        public bool IsThrowing => Phase == ThrowPhase.Throwing;
        public ThrowPhase Phase { get; private set; } = ThrowPhase.Ready;
        public float PhaseTime => phaseTime;
        public Vector3 LastReleasePosition { get; private set; }
        public Vector3 LastReleaseHandPosition { get; private set; }
        public int Throws { get; private set; }
        public string Status => IsThrowing ? "正在投掷…" : !CanThrow ? "投掷物已用完" : Phase == ThrowPhase.Drawing ? "取出投掷物…" :
            World != null && !World.HasCapacity ? "等待场上投掷物消散" : IsPreparing ? "松开投掷 · 右键取消" :
            "按住瞄准 · 松开投掷 · " + (InfiniteSupply ? "∞" : Remaining.ToString());

        public void SetSupply(string id, int count)
        {
            supplies[id] = Mathf.Max(0, count);
            UpdateHeldModel();
        }

        public void Bind(CharacterMotor owner, Transform model, string id)
        {
            ReleaseEquipment(); player = owner; heldModel = model;
            if (catalog == null) catalog = Resources.Load<ThrowableCatalog>("Gameplay/Throwables");
            equipped = catalog != null ? catalog.Find(id) : null;
            if (catalog == null) return;
            if (World == null)
            {
                var root = new GameObject("Combat Throwables");
                World = root.AddComponent<ThrowableWorld>(); World.Initialize(owner, catalog);
                handling = gameObject.AddComponent<AudioSource>(); handling.playOnAwake = false; handling.spatialBlend = 0;
                guideMaterial = new Material(catalog.trajectory); guideMaterial.SetFloat("_LaserBeam", 1);
                guideMaterial.SetFloat("_SwapUV", 0);
                arc = Guide("Throw Trajectory"); landing = Guide("Throw Landing"); landing.loop = true;
            }
            if (equipped == null) return;
            if (equipped.drawAnimation == null || equipped.prepareAnimation == null || equipped.idleAnimation == null || equipped.throwAnimation == null)
            {
                Debug.LogError("[Gameplay] Missing throwable actions. Run Training/Build Throwables."); equipped = null; return;
            }
            if (presentation == null) presentation = gameObject.AddComponent<ThrowablePresentation>();
            presentation.Bind(owner);
            modelCenter = heldModel.InverseTransformPoint(ModelComponent.StaticMeshBounds(heldModel.gameObject).center);
            BeginPhase(ThrowPhase.Drawing); PresentPose(.08f); UpdateHeldModel();
        }

        private LineRenderer Guide(string label)
        {
            var go = new GameObject(label); go.transform.SetParent(World.transform, false);
            var line = go.AddComponent<LineRenderer>(); line.sharedMaterial = guideMaterial;
            line.useWorldSpace = true; line.startWidth = line.endWidth = .035f;
            line.startColor = line.endColor = new Color(.4f, .9f, .8f, .75f); line.enabled = false;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return line;
        }

        public void ProcessInput(bool held, bool pressed, bool cancel, bool allowed, float dt)
        {
            allowed &= player != null && player.InputEnabled && isActiveAndEnabled;
            if (!allowed || equipped == null) { Cancel(); return; }
            if (cancel && (IsPreparing || (IsThrowing && !projectileReleased))) { Cancel(); return; }
            if (!held) needsRelease = false;
            if (pressed && !needsRelease && CanThrow && Phase == ThrowPhase.Ready && World.HasCapacity)
            {
                BeginPhase(ThrowPhase.Preparing); releaseQueued = pinPlayed = false; needsRelease = true;
            }
            if (IsPreparing && !held) releaseQueued = true;
            AdvanceAction(Mathf.Max(0, dt));
            PresentPose(Mathf.Max(0, dt)); UpdateHeldModel();
            if (IsPreparing) Preview(); else HideGuide();
        }

        private void AdvanceAction(float remaining)
        {
            // Carry frame overshoot across phases, and sample the exact release pose even in a long frame.
            for (int transitions = 0; transitions < 8; transitions++)
            {
                if (Phase == ThrowPhase.Ready) { phaseTime += remaining; break; }
                if (Phase == ThrowPhase.Holding)
                {
                    if (!releaseQueued) break;
                    BeginPhase(ThrowPhase.Throwing); projectileReleased = false;
                }
                float duration = Phase == ThrowPhase.Drawing ? equipped.drawAnimation.length :
                    Phase == ThrowPhase.Preparing ? equipped.prepareAnimation.length : equipped.throwAnimation.length;
                float before = phaseTime, next = Mathf.Min(duration, phaseTime + remaining);
                if (Phase == ThrowPhase.Preparing && !pinPlayed && next >= duration * equipped.pinSoundNormalizedTime)
                { handling.PlayOneShot(equipped.pin, .55f); pinPlayed = true; }
                if (IsThrowing && !projectileReleased && next >= equipped.ReleaseTime)
                {
                    phaseTime = equipped.ReleaseTime;
                    PresentPose(Mathf.Max(.08f, equipped.ReleaseTime - before));
                    LastReleaseHandPosition = heldModel.TransformPoint(modelCenter);
                    if (!GetLaunch(out var origin, out var velocity) || !World.Launch(equipped, origin, velocity))
                    { Cancel(); break; }
                    LastReleasePosition = origin; projectileReleased = true; Throws++;
                    if (!InfiniteSupply) supplies[equipped.id] = Mathf.Max(0, Remaining - 1);
                    handling.Stop(); handling.PlayOneShot(equipped.release, .6f);
                    heldModel.gameObject.SetActive(false);
                }
                remaining -= next - before; phaseTime = next;
                if (next < duration) break;
                switch (Phase)
                {
                    case ThrowPhase.Drawing: BeginPhase(ThrowPhase.Ready); break;
                    case ThrowPhase.Preparing: BeginPhase(ThrowPhase.Holding); break;
                    case ThrowPhase.Throwing: BeginPhase(CanThrow ? ThrowPhase.Drawing : ThrowPhase.Ready); releaseQueued = false; break;
                }
                if (remaining <= 0 && Phase != ThrowPhase.Holding) break;
            }
        }

        private void BeginPhase(ThrowPhase phase) { Phase = phase; phaseTime = 0; }

        private void PresentPose(float dt)
        {
            if (presentation == null || equipped == null) return;
            AnimationClip clip = Phase == ThrowPhase.Drawing ? equipped.drawAnimation :
                IsThrowing ? equipped.throwAnimation : IsPreparing ? equipped.prepareAnimation : equipped.idleAnimation;
            float time = Phase == ThrowPhase.Holding ? clip.length : Phase == ThrowPhase.Ready ? Mathf.Repeat(phaseTime, clip.length) : phaseTime;
            presentation.Present(clip, time, dt);
        }

        private void UpdateHeldModel()
        {
            if (heldModel != null && equipped != null)
                heldModel.gameObject.SetActive(CanThrow && (IsThrowing ? !projectileReleased : Phase != ThrowPhase.Drawing || phaseTime >= .12f));
        }

        public bool GetLaunch(out Vector3 origin, out Vector3 velocity)
        {
            Vector3 chest = player.transform.position + Vector3.up * 1.35f;
            Vector3 aim = player.AimPoint;
            Vector3 hand = heldModel != null ? (IsPreparing && presentation != null ?
                presentation.PointAt(equipped.throwAnimation, equipped.ReleaseTime, heldModel, modelCenter) : heldModel.TransformPoint(modelCenter)) : chest + player.transform.forward * .5f;
            Vector3 reach = hand - chest;
            origin = reach.sqrMagnitude > .00001f && Physics.SphereCast(chest, ThrowablePhysics.Radius, reach.normalized, out var hit, reach.magnitude,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                ? chest + reach.normalized * Mathf.Max(0, hit.distance - .02f) : chest + reach;
            velocity = ThrowablePhysics.LaunchVelocity(origin, aim);
            return !Physics.CheckSphere(origin, ThrowablePhysics.Radius * .9f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        private void Preview()
        {
            if (!GetLaunch(out Vector3 position, out Vector3 velocity)) { HideGuide(); return; }
            int count = 1; points[0] = position;
            for (float t = 0; t < equipped.fuse && count < points.Length; t += ThrowablePhysics.Step)
            {
                bool collision = ThrowablePhysics.Advance(ref position, ref velocity, ThrowablePhysics.Step, out var hit);
                points[count++] = position;
                if (collision && equipped.kind == ThrowableKind.Fire && hit.normal.y > .65f) break;
            }
            arc.positionCount = count; for (int i = 0; i < count; i++) arc.SetPosition(i, points[i]); arc.enabled = true;
            landing.positionCount = 40;
            for (int i = 0; i < 40; i++)
            {
                float angle = i * Mathf.PI * 2 / 40;
                landing.SetPosition(i, position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * .3f);
            }
            landing.enabled = true;
        }

        public void Cancel()
        {
            if (IsPreparing || IsThrowing) BeginPhase(IsThrowing && projectileReleased ? ThrowPhase.Drawing : ThrowPhase.Ready);
            releaseQueued = false; needsRelease = true;
            if (handling != null) handling.Stop();
            PresentPose(.08f); UpdateHeldModel(); HideGuide();
        }
        public void ReleaseEquipment()
        {
            Cancel(); presentation?.Release(); equipped = null; heldModel = null;
        }
        public void ResetEquipment() { Cancel(); BeginPhase(ThrowPhase.Drawing); World?.ResetSenses(); }
        private void HideGuide() { if (arc != null) arc.enabled = false; if (landing != null) landing.enabled = false; }
        private void OnDisable() { Cancel(); }
        private void OnDestroy()
        {
            if (World != null) Destroy(World.gameObject);
            if (guideMaterial != null) Destroy(guideMaterial);
        }
    }
}
