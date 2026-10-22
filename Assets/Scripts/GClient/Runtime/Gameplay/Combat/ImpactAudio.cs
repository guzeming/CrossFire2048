using System;
using System.Collections.Generic;
using UnityEngine;

namespace OperationBlacktide.Client.Gameplay.Combat
{
    /// <summary>World impact voices plus clear local attacker feedback, with bounded overlap.</summary>
    public sealed class ImpactAudio : MonoBehaviour
    {
        public const int VoiceCapacity = 24;
        public const int MaxSoundsPerShot = 4;
        private AudioClip[][] clips;
        private int[] previous;
        private readonly AudioSource[] voices = new AudioSource[VoiceCapacity];
        private readonly HashSet<long> shotContacts = new HashSet<long>();
        private int nextVoice, soundsThisShot;
        public int SoundsPlayed { get; private set; }
        public ImpactKind LastKind { get; private set; }
        public AudioClip LastClip { get; private set; }

        public void Initialize()
        {
            if (clips != null) return;
            int count = Enum.GetValues(typeof(ImpactKind)).Length;
            clips = new AudioClip[count][];
            previous = new int[count];
            for (int i = 0; i < count; i++)
            {
                clips[i] = Resources.LoadAll<AudioClip>("Combat/ImpactAudio/" + (ImpactKind)i);
                previous[i] = -1;
                if (clips[i].Length == 0) Debug.LogError("[Gameplay] Missing impact audio: " + (ImpactKind)i);
            }
            for (int i = 0; i < voices.Length; i++)
            {
                var child = new GameObject("Impact Voice " + i);
                child.transform.SetParent(transform, false);
                var source = voices[i] = child.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.dopplerLevel = 0;
                source.minDistance = 10;
                source.maxDistance = 65;
                source.rolloffMode = AudioRolloffMode.Linear;
            }
        }

        public void BeginShot() { shotContacts.Clear(); soundsThisShot = 0; }

        public bool Play(Shot shot)
        {
            if (!isActiveAndEnabled || !shot.hasHit || shot.hit.collider == null) return false;
            Initialize();
            ImpactKind kind = shot.impactKind;
            var target = shot.hit.collider.GetComponentInParent<Damageable>();
            int contact = target != null ? target.GetInstanceID() : shot.hit.collider.GetInstanceID();
            long key = ((long)contact << 32) | (uint)kind;
            // A shotgun still resolves every pellet's damage, but repeating one surface is one sound.
            bool feedback = ImpactSurfaceResolver.IsCharacter(kind);
            int budget = kind == ImpactKind.Head || kind == ImpactKind.Helmet ? MaxSoundsPerShot : feedback ? 3 : 2;
            if (soundsThisShot >= budget || !shotContacts.Add(key)) return false;
            int group = (int)kind;
            var samples = clips[group];
            if (samples.Length == 0) samples = clips[group = (int)ImpactKind.Default];
            if (samples.Length == 0) return false;
            int last = previous[group];
            int index = UnityEngine.Random.Range(0, samples.Length - (last >= 0 && samples.Length > 1 ? 1 : 0));
            if (samples.Length > 1 && last >= 0 && index >= last) index++;
            previous[group] = index;
            // Reserve four voices for body/head feedback so rapid wall impacts cannot cut it off.
            int start = feedback ? 0 : 4, length = feedback ? 4 : voices.Length - 4;
            var voice = voices[start + nextVoice++ % length];
            voice.Stop();
            voice.transform.position = shot.end;
            voice.spatialBlend = feedback ? 0 : 1;
            voice.priority = feedback ? 80 : 150;
            voice.volume = kind == ImpactKind.Head || kind == ImpactKind.Helmet ? .7f : feedback ? .6f : .45f;
            voice.pitch = feedback ? 1 : UnityEngine.Random.Range(.96f, 1.04f);
            voice.clip = LastClip = samples[index];
            voice.Play();
            LastKind = kind;
            SoundsPlayed++;
            soundsThisShot++;
            return true;
        }

        public void Stop()
        {
            foreach (var voice in voices) if (voice != null) voice.Stop();
            BeginShot();
        }
        private void OnDisable() { Stop(); }
    }
}
