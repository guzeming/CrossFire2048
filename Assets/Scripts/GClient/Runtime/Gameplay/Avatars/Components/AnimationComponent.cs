using System;
using System.Collections.Generic;
using System.Linq;
using OperationBlacktide.Shared.Components;
using UnityEngine;

namespace OperationBlacktide.Client.Gameplay.Avatars
{
    public sealed class AnimationComponent : GameComponent
    {
        private AnimatorOverrideController ownedOverride;
        public override IEnumerable<Type> Dependencies => new[] { typeof(ModelComponent) };
        public Animator Animator { get { var root = Host.Get<ModelComponent>().Root; return root != null ? root.GetComponentInChildren<Animator>() : null; } }
        public void ApplyPose(RuntimeAnimatorController controller, AnimationClip basisIdle, AnimationClip basisInspect,
            AnimationClip idle, AnimationClip inspect, bool unscaled)
        {
            Dispose();
            if (Animator == null) throw new InvalidOperationException("Avatar model has no Animator.");
            ownedOverride = new AnimatorOverrideController(controller);
            ownedOverride.ApplyOverrides(new List<KeyValuePair<AnimationClip, AnimationClip>> {
                new KeyValuePair<AnimationClip, AnimationClip>(basisIdle, idle),
                new KeyValuePair<AnimationClip, AnimationClip>(basisInspect, inspect) });
            Animator.runtimeAnimatorController = ownedOverride;
            Animator.updateMode = unscaled ? AnimatorUpdateMode.UnscaledTime : AnimatorUpdateMode.Normal;
            Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; Animator.applyRootMotion = false;
            Animator.Rebind(); Animator.Play("Idle", 0, 0); Animator.Update(0);
        }
        public override void Dispose()
        {
            if (Animator != null && Animator.runtimeAnimatorController == ownedOverride) Animator.runtimeAnimatorController = null;
            ModelComponent.Release(ownedOverride); ownedOverride = null;
        }
    }
}
