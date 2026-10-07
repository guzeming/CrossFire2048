using UnityEngine;
using UnityEngine.Rendering;

namespace OperationBlacktide.Client.Features.Training
{
    /// <summary>Small Unity particle compositions built from CS sprite atlases and audio samples.</summary>
    public sealed class TrainingThrowableEffects : MonoBehaviour
    {
        private float age, lifetime;
        private Light glow;
        private float glowStrength;
        private AudioSource loop;
        private ParticleSystem[] systems;
        private AudioSource[] sounds;
        private bool paused;
        public bool Finished => age >= lifetime;

        public static TrainingThrowableEffects Create(Transform parent, string label, Vector3 position, float duration)
        {
            var go = new GameObject(label); go.transform.SetParent(parent, false); go.transform.position = position;
            var effect = go.AddComponent<TrainingThrowableEffects>(); effect.lifetime = duration; return effect;
        }

        public ParticleSystem Particles(Material material, float size, float particleLife, Color color, bool continuous,
            float rate, float spread = 0, bool animated = true)
        {
            var go = new GameObject("CS " + material.name); go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = continuous; main.duration = Mathf.Max(.1f, lifetime);
            main.playOnAwake = false; main.startLifetime = particleLife; main.startSpeed = 0;
            main.startSize = new ParticleSystem.MinMaxCurve(size * .8f, size * 1.15f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startColor = color; main.maxParticles = 160; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission; emission.enabled = continuous; emission.rateOverTime = rate;
            var shape = ps.shape; shape.enabled = spread > 0; shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = spread; shape.scale = new Vector3(1, .45f, 1);
            var animation = ps.textureSheetAnimation; animation.enabled = animated;
            if (animated) { animation.numTilesX = 4; animation.numTilesY = 4; animation.cycleCount = 1; }
            var colors = ps.colorOverLifetime; colors.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .12f), new GradientAlphaKey(1, .65f), new GradientAlphaKey(0, 1) });
            colors.color = gradient;
            var sizes = ps.sizeOverLifetime; sizes.enabled = true;
            sizes.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .65f, 1, 1.25f));
            var renderer = go.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            ps.Play(); if (!continuous) ps.Emit(Mathf.RoundToInt(rate));
            systems = null; return ps;
        }

        public void Sound(AudioClip clip, float volume = .7f, bool repeating = false, bool listenerRelative = false)
        {
            if (clip == null) return;
            var audio = gameObject.AddComponent<AudioSource>(); audio.playOnAwake = false; audio.clip = clip;
            audio.spatialBlend = listenerRelative ? 0 : 1;
            // In this top-down game the listener is above the player; retain audible nearby effects.
            audio.minDistance = 10; audio.maxDistance = 65; audio.rolloffMode = AudioRolloffMode.Linear;
            audio.volume = volume; audio.loop = repeating; audio.dopplerLevel = 0; audio.Play();
            if (repeating) loop = audio;
            sounds = null;
        }

        public void Glow(Color color, float intensity, float radius)
        {
            glow = gameObject.AddComponent<Light>(); glow.type = LightType.Point; glow.color = color;
            glow.intensity = glowStrength = intensity; glow.range = radius; glow.shadows = LightShadows.None;
        }

        public void Advance(float dt)
        {
            age += dt;
            if (glow != null) glow.intensity = glowStrength * Mathf.Clamp01((lifetime - age) / .4f)
                * (lifetime < 3 ? Mathf.Exp(-age * 9) : .9f + .1f * Mathf.Sin(age * 27));
            if (loop != null) loop.volume = .48f * Mathf.Clamp01((lifetime - age) / 1.2f);
            if (systems == null) systems = GetComponentsInChildren<ParticleSystem>();
            foreach (var ps in systems)
                if (ps.main.loop && lifetime - age < ps.main.startLifetime.constantMax)
                    ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }

        public void SetPaused(bool value)
        {
            if (paused == value) return;
            paused = value;
            if (systems == null) systems = GetComponentsInChildren<ParticleSystem>();
            if (sounds == null) sounds = GetComponentsInChildren<AudioSource>();
            foreach (var ps in systems) { if (value) ps.Pause(); else if (ps.isPaused) ps.Play(); }
            foreach (var audio in sounds) { if (value) audio.Pause(); else audio.UnPause(); }
        }
    }
}
