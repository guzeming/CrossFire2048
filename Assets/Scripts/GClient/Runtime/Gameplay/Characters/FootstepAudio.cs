using System;
using OperationBlacktide.Client.Gameplay.Combat;
using UnityEngine;
using Random = UnityEngine.Random;

namespace OperationBlacktide.Client.Gameplay.Characters
{
    /// <summary>Grounded travel triggers CS2 contacts selected from the actual supporting material.</summary>
    [DefaultExecutionOrder(120)]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "OperationBlacktide.Client.Features.Training", "Assembly-CSharp", "TrainingFootstepAudio")]
    public sealed class FootstepAudio : MonoBehaviour
    {
        [SerializeField, Min(.1f)] private float walkStepDistance = 2.25f;
        [SerializeField, Min(.1f)] private float runStepDistance = 2.6f;
        [SerializeField, Range(0, 1)] private float walkVolume = .32f;
        [SerializeField, Range(0, 1)] private float runVolume = .55f;
        private CharacterMotor character;
        private AudioSource source;
        [Serializable] private sealed class BankDefinition { public string kind; public string[] steps, landings; }
        [Serializable] private sealed class BankFile { public BankDefinition[] banks; }
        private sealed class SoundBank
        {
            public AudioClip[] steps, landings;
            public int lastStep = -1, lastLanding = -1;
        }
        private SoundBank[] banks;
        private FootstepSurfaceProbe surfaceProbe;
        private Vector3 previousPosition;
        private float distance, airborneTime, fallSpeed, sinceSound;
        private bool wasGrounded, firstStep = true;
        public int StepsPlayed { get; private set; }
        public int LandingsPlayed { get; private set; }
        public AudioClip LastClip { get; private set; }
        public ImpactKind LastSurface { get; private set; }
        public Collider LastGroundCollider { get; private set; }

        public void Initialize(CharacterMotor owner)
        {
            character = owner;
            surfaceProbe = new FootstepSurfaceProbe(owner);
            if (!LoadBanks())
            {
                Debug.LogError("[Gameplay] Missing footstep/landing banks in Resources/Combat/FootstepAudio.");
                enabled = false;
                return;
            }
            if (source == null)
            {
                var audioObject = new GameObject("Footstep Audio");
                audioObject.transform.SetParent(transform, false);
                source = audioObject.AddComponent<AudioSource>();
            }
            source.playOnAwake = false;
            source.loop = false;
            // The local player's top-down camera is well above the ground, so do not attenuate by camera distance.
            source.spatialBlend = 0;
            source.volume = 1;
            source.priority = 160;
            ResetMotion();
        }

        private bool LoadBanks()
        {
            if (banks != null) return true;
            var json = Resources.Load<TextAsset>("Combat/FootstepAudio/banks");
            var file = json != null ? JsonUtility.FromJson<BankFile>(json.text) : null;
            if (file?.banks == null) return false;
            var loaded = new SoundBank[Enum.GetValues(typeof(ImpactKind)).Length];
            foreach (var definition in file.banks)
            {
                if (!Enum.TryParse(definition.kind, out ImpactKind kind)) return false;
                var bank = new SoundBank { steps = LoadClips(definition.steps), landings = LoadClips(definition.landings) };
                if (bank.steps == null || bank.landings == null) return false;
                loaded[(int)kind] = bank;
            }
            var concrete = loaded[(int)ImpactKind.Concrete];
            if (concrete == null || loaded[(int)ImpactKind.Wood] == null || loaded[(int)ImpactKind.Water] == null) return false;
            loaded[(int)ImpactKind.WoodPanel] = loaded[(int)ImpactKind.Wood];
            for (int i = 0; i < loaded.Length; i++) if (loaded[i] == null) loaded[i] = concrete;
            banks = loaded;
            return true;
        }

        private static AudioClip[] LoadClips(string[] paths)
        {
            if (paths == null || paths.Length == 0) return null;
            var clips = new AudioClip[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                clips[i] = Resources.Load<AudioClip>(paths[i]);
                if (clips[i] == null) return null;
            }
            return clips;
        }

        private void Update() { UpdateAudio(Time.deltaTime, Application.isFocused && Time.timeScale > 0); }

        // Separate sampling from Unity input/focus so movement/collision behaviour can be verified deterministically.
        public void UpdateAudio(float deltaTime, bool audible)
        {
            if (character == null || source == null) return;
            if (!isActiveAndEnabled || !audible || !character.InputEnabled || deltaTime <= 0 || deltaTime > .2f)
            {
                ResetMotion();
                return;
            }
            Vector3 travel = transform.position - previousPosition;
            previousPosition = transform.position;
            // A teleport/respawn must not be interpreted as a stride or a fall.
            if (travel.sqrMagnitude > 9f) { ResetMotion(); return; }
            sinceSound += deltaTime;
            bool grounded = character.IsGrounded;
            if (!grounded)
            {
                airborneTime += deltaTime;
                fallSpeed = Mathf.Min(fallSpeed, character.ActualVelocity.y);
                distance = 0;
                firstStep = true;
                wasGrounded = false;
                return;
            }
            if (!wasGrounded && airborneTime > .1f && fallSpeed < -1.5f)
            {
                PlayContact(true, Mathf.Lerp(.3f, .65f, Mathf.InverseLerp(2, 12, -fallSpeed)));
                LandingsPlayed++;
                distance = 0;
                firstStep = false; // The landing is already a foot contact; do not double it with a step.
                sinceSound = 0;
            }
            wasGrounded = true;
            airborneTime = fallSpeed = 0;
            float speed = Vector3.ProjectOnPlane(character.ActualVelocity, Vector3.up).magnitude;
            float travelled = Vector3.ProjectOnPlane(travel, Vector3.up).magnitude;
            if (speed < .15f || travelled < .001f)
            {
                distance = 0;
                firstStep = true;
                return;
            }
            float running = Mathf.InverseLerp(4.5f, 7f, speed);
            float stride = Mathf.Lerp(walkStepDistance, runStepDistance, running);
            distance += travelled;
            float threshold = firstStep ? .3f : stride;
            if (distance >= threshold && sinceSound >= .16f)
            {
                PlayContact(false, Mathf.Lerp(walkVolume, runVolume, running));
                StepsPlayed++;
                distance = Mathf.Min(distance - threshold, stride * .5f);
                firstStep = false;
                sinceSound = 0;
            }
        }

        private void PlayContact(bool landing, float volume)
        {
            LastSurface = surfaceProbe.Resolve(out var ground);
            LastGroundCollider = ground;
            var bank = banks[(int)LastSurface];
            if (landing) Play(bank.landings, ref bank.lastLanding, volume);
            else Play(bank.steps, ref bank.lastStep, volume);
        }

        private void Play(AudioClip[] clips, ref int previous, float volume)
        {
            // Exclude the previous sample without retry loops or per-step allocations.
            int index = Random.Range(0, clips.Length - (previous >= 0 && clips.Length > 1 ? 1 : 0));
            if (clips.Length > 1 && previous >= 0 && index >= previous) index++;
            previous = index;
            LastClip = clips[index];
            source.PlayOneShot(LastClip, volume);
        }

        public void ResetMotion()
        {
            previousPosition = transform.position;
            wasGrounded = character != null && character.IsGrounded;
            distance = airborneTime = fallSpeed = 0;
            sinceSound = .16f;
            firstStep = true;
            LastGroundCollider = null;
            if (source != null) source.Stop();
        }

        private void OnApplicationFocus(bool focused) { if (!focused) ResetMotion(); }
        private void OnDisable() { ResetMotion(); }
        private void OnDestroy() { if (source != null) Destroy(source.gameObject); }
    }
}
