using System.Collections.Generic;
using UnityEngine;

namespace OperationBlacktide.Client.Features.Training
{
    /// <summary>Upper-body action layers and reload foley, driven by the same clock as ammunition.</summary>
    [DefaultExecutionOrder(180)]
    public sealed class TrainingWeaponPresentation : MonoBehaviour
    {
        public const string FireLayer = "Weapon Fire", ReloadLayer = "Weapon Reload";
        public const string HoldLayer = "Weapon Hold";
        private TrainingCharacterController character;
        private TrainingWeaponController weapon;
        private TrainingWeaponDefinition definition;
        private TrainingAmmoState ammo;
        private Animator animator;
        private RuntimeAnimatorController originalController;
        private AnimatorOverrideController overrides;
        private AudioSource reloadAudio;
        private int fireLayer = -1, reloadLayer = -1, holdLayer = -1, nextCue, previousMagazine;
        private float fireElapsed = float.MaxValue;
        private bool alternateFire;
        private bool wasReloading, audioPaused, subscribed;
        public int FireAnimationsPlayed { get; private set; }
        public int ReloadSoundsPlayed { get; private set; }
        public AudioClip LastReloadSound { get; private set; }
        public bool AudioPaused => audioPaused;
        public float PresentedReloadProgress { get; private set; }

        public void Initialize(TrainingCharacterController owner, TrainingWeaponController source, TrainingWeaponDefinition data)
        {
            character = owner; weapon = source; definition = data; ammo = source.Ammo;
            animator = owner.GetComponent<TrainingCharacterAnimator>()?.Animator;
            if (animator == null || data.fireAnimation == null || (!data.IsMelee && !data.IsTaser && data.reloadAnimation == null))
            {
                Debug.LogError("[Training] Missing weapon actions. Run Training/Build Weapon Actions.");
                enabled = false; return;
            }
            originalController = animator.runtimeAnimatorController;
            fireLayer = animator.GetLayerIndex(FireLayer); reloadLayer = animator.GetLayerIndex(ReloadLayer);
            holdLayer = animator.GetLayerIndex(HoldLayer);
            if (fireLayer < 0 || reloadLayer < 0)
            {
                Debug.LogError("[Training] Weapon animation layers are missing. Run Training/Build Weapon Actions.");
                enabled = false; return;
            }
            overrides = new AnimatorOverrideController(originalController);
            var clips = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            overrides.GetOverrides(clips);
            for (int i = 0; i < clips.Count; i++)
            {
                var clip = clips[i].Key;
                if (clip.name == "fire_m4a4") clips[i] = new KeyValuePair<AnimationClip, AnimationClip>(clip, data.fireAnimation);
                if (clip.name == "reload_m4a4") clips[i] = new KeyValuePair<AnimationClip, AnimationClip>(clip, data.reloadAnimation);
                if (clip.name == "fire_elite_left") clips[i] = new KeyValuePair<AnimationClip, AnimationClip>(clip, data.alternateFireAnimation ?? data.fireAnimation);
                if (clip.name == "idle_glock" && data.idleAnimation != null) clips[i] = new KeyValuePair<AnimationClip, AnimationClip>(clip, data.idleAnimation);
                if (clip.name.EndsWith("_rifle") && data.locomotionAnimations.Length > 0)
                {
                    string name = clip.name.Replace("_rifle", data.IsMelee ? "_knife" : "_pistol");
                    var movement = System.Array.Find(data.locomotionAnimations, c => c.name == name);
                    if (movement != null) clips[i] = new KeyValuePair<AnimationClip, AnimationClip>(clip, movement);
                }
                if (clip.name == "idle_rifle" && data.idleAnimation != null)
                    clips[i] = new KeyValuePair<AnimationClip, AnimationClip>(clip, data.idleAnimation);
            }
            overrides.ApplyOverrides(clips);
            animator.runtimeAnimatorController = overrides;
            var audioObject = new GameObject("Reload Audio"); audioObject.transform.SetParent(transform, false);
            reloadAudio = audioObject.AddComponent<AudioSource>();
            reloadAudio.playOnAwake = false; reloadAudio.loop = false; reloadAudio.spatialBlend = 0; reloadAudio.priority = 100;
            previousMagazine = ammo != null ? ammo.Magazine : 0;
            enabled = true;
            animator.Update(0);
            Subscribe();
        }

        private void OnEnable() { Subscribe(); }
        private void Subscribe()
        {
            if (subscribed || weapon == null || reloadAudio == null) return;
            if (ammo != null) ammo.Changed += OnAmmoChanged;
            weapon.AttackStarted += OnAttack; subscribed = true;
            previousMagazine = ammo != null ? ammo.Magazine : 0;
        }

        private void OnAttack(bool alternate)
        {
            fireElapsed = 0;
            alternateFire = alternate && definition.alternateFireAnimation != null;
            FireAnimationsPlayed++;
        }

        private void OnAmmoChanged()
        {
            if (ammo != null && ammo.IsReloading && !wasReloading)
            {
                nextCue = 0; wasReloading = true;
                if (!definition.IsTaser) fireElapsed = float.MaxValue;
            }
            else if (!ammo.IsReloading && wasReloading)
            {
                // Completion can cross the last sound cue in a single long frame. Reset/respawn cancels instead.
                if (ammo.ReloadElapsed >= ammo.ReloadDuration) PlayReloadCues(1);
                else ResetPresentation();
                wasReloading = false;
            }
            else if (!ammo.IsReloading && ammo.Magazine > previousMagazine) ResetPresentation();
            previousMagazine = ammo.Magazine;
        }

        private void LateUpdate()
        {
            UpdatePresentation(Time.deltaTime, Application.isFocused && Time.timeScale > 0);
        }

        public void UpdatePresentation(float deltaTime, bool active)
        {
            if (animator == null || overrides == null || !isActiveAndEnabled) return;
            if (!weapon.isActiveAndEnabled) { ResetPresentation(); return; }
            if (ammo != null && ammo.IsReloading && !wasReloading)
            {
                // Re-enabling a pooled/disabled actor resumes the pose without replaying old foley.
                wasReloading = true;
                while (nextCue < definition.reloadSounds.Length && definition.reloadSounds[nextCue].normalizedTime <= ammo.ReloadProgress) nextCue++;
            }
            active &= character.InputEnabled;
            if (holdLayer >= 0)
            {
                // Idle already uses the weapon's grip. Moving/jumping clips use generic family
                // locomotion, so add only the matching upper-body posture difference there.
                float moving = animator.GetBool("Grounded") ? Mathf.Clamp01(animator.GetFloat("Speed") / 4.5f) : 1;
                bool hasGrip = !definition.IsMelee && definition.idleAnimation != null;
                animator.SetLayerWeight(holdLayer, hasGrip ? moving : 0);
                if (hasGrip) animator.Play("Hold", holdLayer, 0);
            }
            if (active && audioPaused) { reloadAudio.UnPause(); audioPaused = false; }
            else if (!active && !audioPaused) { reloadAudio.Pause(); audioPaused = true; }

            bool reloading = ammo != null && ammo.IsReloading;
            PresentedReloadProgress = reloading ? ammo.ReloadProgress : 0;
            animator.SetLayerWeight(reloadLayer, reloading && definition.reloadAnimation != null ? 1 : 0);
            if (reloading)
            {
                if (definition.reloadAnimation != null) animator.Play("Reload", reloadLayer, PresentedReloadProgress);
                if (active) PlayReloadCues(PresentedReloadProgress);
            }
            var fireClip = alternateFire ? definition.alternateFireAnimation : definition.fireAnimation;
            bool firing = (!reloading || definition.IsTaser) && fireElapsed < fireClip.length;
            animator.SetLayerWeight(fireLayer, firing ? 1 : 0);
            if (firing)
            {
                animator.Play(alternateFire ? "AlternateFire" : "Fire", fireLayer, fireElapsed / fireClip.length);
                if (active) fireElapsed += Mathf.Max(0, deltaTime);
            }
            // The layer states have speed 0: sample before upper-body aim/firing, without advancing locomotion again.
            character.GetComponent<TrainingUpperBodyAim>()?.RestorePose();
            animator.Update(0);
        }

        private void PlayReloadCues(float progress)
        {
            var cues = definition.reloadSounds;
            while (nextCue < cues.Length && cues[nextCue].normalizedTime <= progress)
            {
                var cue = cues[nextCue++];
                if (cue.clip == null) continue;
                reloadAudio.PlayOneShot(cue.clip, cue.volume);
                LastReloadSound = cue.clip; ReloadSoundsPlayed++;
            }
        }

        public void ResetPresentation()
        {
            fireElapsed = float.MaxValue; alternateFire = false; wasReloading = false; nextCue = 0; PresentedReloadProgress = 0;
            if (reloadAudio != null) reloadAudio.Stop();
            audioPaused = false;
            if (animator != null && fireLayer >= 0 && reloadLayer >= 0)
            { animator.SetLayerWeight(fireLayer, 0); animator.SetLayerWeight(reloadLayer, 0); }
        }

        private void OnDisable()
        {
            if (subscribed) { if (ammo != null) ammo.Changed -= OnAmmoChanged; weapon.AttackStarted -= OnAttack; subscribed = false; }
            ResetPresentation();
        }

        public void Release()
        {
            OnDisable();
            if (animator != null && holdLayer >= 0) animator.SetLayerWeight(holdLayer, 0);
            if (animator != null && overrides != null && animator.runtimeAnimatorController == overrides) animator.runtimeAnimatorController = originalController;
            if (overrides != null) Destroy(overrides);
            if (reloadAudio != null) Destroy(reloadAudio.gameObject);
            overrides = null; reloadAudio = null; ammo = null;
        }
        private void OnDestroy() { Release(); }
    }
}
