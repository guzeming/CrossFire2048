using System;
using UnityEngine;

namespace OperationBlacktide.Client.Gameplay.Combat
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "OperationBlacktide.Client.Features.Training", "Assembly-CSharp", "TrainingThrowableKind")]
    public enum ThrowableKind { HighExplosive, Fire, Smoke, Flash }

    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "OperationBlacktide.Client.Features.Training", "Assembly-CSharp", "TrainingThrowableDefinition")]
    public sealed class ThrowableDefinition
    {
        public string id;
        public ThrowableKind kind;
        public GameObject model;
        public float fuse = 1.7f, radius = 5, duration, damage;
        public AudioClip pin, release, bounce, loop, finish;
        public AudioClip[] detonate = Array.Empty<AudioClip>();
        public AnimationClip drawAnimation, idleAnimation, prepareAnimation, throwAnimation;
        [Range(0, 1)] public float releaseNormalizedTime = .25f;
        [Range(0, 1)] public float pinSoundNormalizedTime = .28f;
        public float ReleaseTime => throwAnimation.length * releaseNormalizedTime;
    }

    [CreateAssetMenu(menuName = "OperationBlacktide/Gameplay/Throwables")]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "OperationBlacktide.Client.Features.Training", "Assembly-CSharp", "TrainingThrowableCatalog")]
    public sealed class ThrowableCatalog : ScriptableObject
    {
        public ThrowableDefinition[] items = Array.Empty<ThrowableDefinition>();
        public Material fire, explosion, smoke, trajectory;
        public AudioClip ringing;
        public ThrowableDefinition Find(string id) => Array.Find(items, item => item.id == id);
    }
}
