using System;
using UnityEngine;

namespace OperationBlacktide.Client.Features.Training
{
    public enum TrainingThrowableKind { HighExplosive, Fire, Smoke, Flash }

    [Serializable]
    public sealed class TrainingThrowableDefinition
    {
        public string id;
        public TrainingThrowableKind kind;
        public GameObject model;
        public float fuse = 1.7f, radius = 5, duration, damage;
        public AudioClip pin, release, bounce, loop, finish;
        public AudioClip[] detonate = Array.Empty<AudioClip>();
        public AnimationClip drawAnimation, idleAnimation, prepareAnimation, throwAnimation;
        [Range(0, 1)] public float releaseNormalizedTime = .25f;
        [Range(0, 1)] public float pinSoundNormalizedTime = .28f;
        public float ReleaseTime => throwAnimation.length * releaseNormalizedTime;
    }

    [CreateAssetMenu(menuName = "OperationBlacktide/Training Throwables")]
    public sealed class TrainingThrowableCatalog : ScriptableObject
    {
        public TrainingThrowableDefinition[] items = Array.Empty<TrainingThrowableDefinition>();
        public Material fire, explosion, smoke, trajectory;
        public AudioClip ringing;
        public TrainingThrowableDefinition Find(string id) => Array.Find(items, item => item.id == id);
    }
}
