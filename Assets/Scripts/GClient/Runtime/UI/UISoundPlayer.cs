using System;
using System.Collections.Generic;
using UnityEngine;

namespace OperationBlacktide.Client.UI
{
    public enum UISoundCue
    {
        Auto = -1, Click, Tab, Home, Agents, Loadout, Career, Settings, Start,
        Confirm, Back, Open, Option, ToggleOn, ToggleOff, Select, Equip, AgentSelect,
        Input, Slider, Purchase
    }

    /// <summary>Persistent, non-spatial UI voices; independent of menus, scene cameras and game pause.</summary>
    public sealed class UISoundPlayer : MonoBehaviour
    {
        [Serializable] private sealed class Bank { public Entry[] cues; }
        [Serializable] private sealed class Entry { public string id; public float volume; }
        private struct Cue { public AudioClip clip; public float volume; }
        private static UISoundPlayer instance;
        private readonly Dictionary<UISoundCue, Cue> cues = new Dictionary<UISoundCue, Cue>();
        private readonly Dictionary<UISoundCue, float> lastPlayed = new Dictionary<UISoundCue, float>();
        private readonly AudioSource[] voices = new AudioSource[4];
        private int nextVoice;
        public int PlayCount { get; private set; }
        public UISoundCue LastCue { get; private set; }

        public static UISoundPlayer Ensure()
        {
            if (!Application.isPlaying) return null;
            if (instance == null) instance = FindObjectOfType<UISoundPlayer>();
            if (instance == null) instance = new GameObject("UI audio").AddComponent<UISoundPlayer>();
            return instance;
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
            var asset = Resources.Load<TextAsset>("UI/Audio/catalog");
            if (asset == null) { Debug.LogWarning("UI audio catalog is missing."); return; }
            foreach (var entry in JsonUtility.FromJson<Bank>(asset.text).cues)
            {
                if (!Enum.TryParse(entry.id, out UISoundCue id) || id == UISoundCue.Auto) continue;
                var clip = Resources.Load<AudioClip>("UI/Audio/" + entry.id);
                if (clip == null) { Debug.LogWarning("Missing UI cue: " + entry.id); continue; }
                clip.LoadAudioData();
                cues[id] = new Cue { clip = clip, volume = Mathf.Clamp01(entry.volume) };
            }
            for (int i = 0; i < voices.Length; i++)
            {
                var child = new GameObject("UI voice " + i); child.transform.SetParent(transform, false);
                var source = voices[i] = child.AddComponent<AudioSource>();
                source.playOnAwake = false; source.loop = false; source.spatialBlend = 0;
                source.ignoreListenerPause = true; source.bypassReverbZones = true;
                source.dopplerLevel = 0; source.priority = 16;
            }
        }

        public static void Play(UISoundCue cue)
        {
            var player = Ensure();
            if (player != null) player.PlayCue(cue);
        }

        private void PlayCue(UISoundCue id)
        {
            if (!cues.TryGetValue(id, out var cue)) return;
            float now = Time.unscaledTime;
            float interval = id == UISoundCue.Slider ? .075f : .035f;
            if (lastPlayed.TryGetValue(id, out float previous) && now - previous < interval) return;
            lastPlayed[id] = now;
            // Prefer an idle voice; rapid input can replace the oldest of four bounded voices.
            int voice = nextVoice;
            for (int i = 0; i < voices.Length; i++)
            {
                int candidate = (nextVoice + i) % voices.Length;
                if (!voices[candidate].isPlaying) { voice = candidate; break; }
            }
            var source = voices[voice]; nextVoice = (voice + 1) % voices.Length;
            source.Stop(); source.clip = cue.clip; source.volume = cue.volume; source.pitch = 1;
            source.Play();
            LastCue = id; PlayCount++;
        }

        private void OnDestroy() { if (instance == this) instance = null; }
    }
}
