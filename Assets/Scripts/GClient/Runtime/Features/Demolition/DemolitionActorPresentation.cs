using System;
using OperationBlacktide.Client.Gameplay.Combat;
using UnityEngine;

namespace OperationBlacktide.Client.Features.Demolition
{
    /// <summary>Server-confirmed C4 interaction, sampled after weapon and aim presentation.</summary>
    [DefaultExecutionOrder(195)]
    public sealed class DemolitionActorPresentation : MonoBehaviour
    {
        DemolitionPresentationAssets assets;
        Animator animator;
        NetworkWeaponPresentation weapon;
        Transform hand, back;
        GameObject bomb;
        AudioSource foley;
        Transform[] bones;
        Vector3[] positions;
        Quaternion[] rotations;
        bool planting, defusing;
        float progress, duration, receivedAt, previousProgress;
        Vector3 objectivePosition;
        bool hasObjectivePosition;
        Quaternion modelRotation;
        public bool Interacting => planting || defusing;
        public GameObject Bomb => bomb;
        public AnimationClip CurrentClip { get; private set; }
        public int SoundsPlayed { get; private set; }

        public void Bind(DemolitionPresentationAssets data, Animator target, NetworkWeaponPresentation source, bool local)
        {
            Release(); assets = data; animator = target; weapon = source;
            if (data == null || target == null) return;
            var all = target.GetComponentsInChildren<Transform>(true);
            hand = Array.Find(all, t => t.name == "hand_R"); back = Array.Find(all, t => t.name == "spine_2");
            var pelvis = Array.Find(all, t => t.name == "pelvis");
            bones = pelvis != null ? pelvis.GetComponentsInChildren<Transform>() : Array.Empty<Transform>();
            positions = new Vector3[bones.Length]; rotations = new Quaternion[bones.Length];
            if (hand != null && data.bombPrefab != null)
            { bomb = Instantiate(data.bombPrefab, hand, false); bomb.name = "Carried C4"; bomb.SetActive(false); }
            if (foley == null)
            {
                var sound = new GameObject("C4 interaction audio"); sound.transform.SetParent(transform, false);
                foley = sound.AddComponent<AudioSource>(); foley.playOnAwake = false;
                foley.minDistance = 3; foley.maxDistance = 28; foley.rolloffMode = AudioRolloffMode.Linear; foley.dopplerLevel = 0;
            }
            foley.spatialBlend = local ? 0 : 1;
        }

        public void Present(bool plant, bool defuse, bool carried, float normalizedProgress, float seconds)
        {
            if (assets == null || animator == null) return;
            bool restart = plant != planting || defuse != defusing || normalizedProgress + .02f < progress;
            if (restart)
            {
                StopInteraction(); planting = plant; defusing = defuse;
                if (Interacting)
                {
                    modelRotation = animator.transform.localRotation;
                    for (int i = 0; i < bones.Length; i++)
                    { positions[i] = bones[i].localPosition; rotations[i] = bones[i].localRotation; }
                    weapon?.SetObjectiveBusy(true);
                    // A reconnect in the middle of an interaction must not replay earlier cues.
                    previousProgress = Mathf.Clamp01(normalizedProgress);
                    if (normalizedProgress < .12f) Play(plant ? assets.draw : assets.defuseStart);
                }
            }
            progress = Mathf.Clamp01(normalizedProgress); duration = Mathf.Max(.1f, seconds); receivedAt = Time.unscaledTime;
            if (bomb != null)
            {
                bomb.SetActive(carried && !defuse);
                if (carried)
                {
                    bomb.transform.SetParent(plant ? hand : back != null ? back : hand, false);
                    if (plant)
                    {
                        bomb.transform.localPosition = assets.bombPrefab.transform.localPosition;
                        bomb.transform.localRotation = assets.bombPrefab.transform.localRotation;
                    }
                    else
                    {
                        // Source rigs use centimetres; authored carrying offsets are in world metres.
                        bomb.transform.position = bomb.transform.parent.position + transform.TransformDirection(assets.carryPosition);
                        bomb.transform.rotation = transform.rotation * Quaternion.Euler(assets.carryEuler);
                    }
                }
            }
        }

        void LateUpdate() => Sample(Time.unscaledTime);
        public void SetObjectivePosition(Vector3 position) { objectivePosition = position; hasObjectivePosition = true; }
        public void Sample(float now)
        {
            if (!Interacting || animator == null) return;
            // Extrapolate at most one short snapshot gap; never predict completion.
            float p = Mathf.Clamp(progress + Mathf.Clamp(now - receivedAt, 0, .15f) / duration, 0, .999f);
            if (planting)
            {
                CurrentClip = assets.plant;
                CurrentClip.SampleAnimation(animator.gameObject, p * CurrentClip.length);
                Cue(.15f, assets.initiate, p);
                for (int i = 0; i < 6; i++) Cue(.25f + i * .1f, assets.click, p);
            }
            else
            {
                if (hasObjectivePosition)
                {
                    var direction = Vector3.ProjectOnPlane(objectivePosition - transform.position, Vector3.up);
                    if (direction.sqrMagnitude > .01f)
                        animator.transform.rotation = Quaternion.LookRotation(direction) * modelRotation;
                }
                float elapsed = p * duration;
                CurrentClip = elapsed < assets.defuseEnter.length ? assets.defuseEnter : assets.defuseLoop;
                float time = CurrentClip == assets.defuseEnter ? elapsed : Mathf.Repeat(elapsed - assets.defuseEnter.length, assets.defuseLoop.length);
                CurrentClip.SampleAnimation(animator.gameObject, time);
            }
            previousProgress = Mathf.Max(previousProgress, p);
        }
        void Cue(float at, AudioClip clip, float current) { if (previousProgress < at && current >= at) Play(clip); }
        void Play(AudioClip clip) { if (clip == null || foley == null) return; foley.PlayOneShot(clip, .8f); SoundsPlayed++; }
        public void StopInteraction()
        {
            if (foley != null) foley.Stop();
            if (Interacting)
            {
                if (animator != null) animator.transform.localRotation = modelRotation;
                for (int i = 0; i < bones.Length; i++) if (bones[i] != null)
                { bones[i].localPosition = positions[i]; bones[i].localRotation = rotations[i]; }
                weapon?.SetObjectiveBusy(false);
                if (animator != null && animator.isActiveAndEnabled) animator.Update(0);
            }
            planting = defusing = false; CurrentClip = null; progress = previousProgress = 0;
        }
        public void Release()
        {
            StopInteraction();
            if (bomb != null) { bomb.SetActive(false); Destroy(bomb); }
            bomb = null; animator = null; weapon = null; bones = null;
        }
        void OnDisable() { StopInteraction(); if (bomb != null) bomb.SetActive(false); }
        void OnDestroy() { Release(); }
    }
}
