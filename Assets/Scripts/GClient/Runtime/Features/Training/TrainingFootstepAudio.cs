using UnityEngine;

namespace OperationBlacktide.Client.Features.Training
{
    /// <summary>Local-player footsteps driven by grounded travel, using imported CS2 concrete/landing samples.</summary>
    [DefaultExecutionOrder(120)]
    public sealed class TrainingFootstepAudio : MonoBehaviour
    {
        [SerializeField, Min(.1f)] private float walkStepDistance = 2.25f;
        [SerializeField, Min(.1f)] private float runStepDistance = 2.6f;
        [SerializeField, Range(0, 1)] private float walkVolume = .32f;
        [SerializeField, Range(0, 1)] private float runVolume = .55f;
        private TrainingCharacterController character;
        private AudioSource source;
        private AudioClip[] steps, landings;
        private Vector3 previousPosition;
        private float distance, airborneTime, fallSpeed, sinceSound;
        private bool wasGrounded, firstStep = true;
        private int lastStep = -1, lastLanding = -1;
        public int StepsPlayed { get; private set; }
        public int LandingsPlayed { get; private set; }
        public AudioClip LastClip { get; private set; }

        public void Initialize(TrainingCharacterController owner)
        {
            character = owner;
            steps = Resources.LoadAll<AudioClip>("Training/Audio/Footsteps");
            landings = Resources.LoadAll<AudioClip>("Training/Audio/Landings");
            if (steps.Length == 0 || landings.Length == 0)
            {
                Debug.LogError("[Training] Missing footstep/landing audio in Resources/Training/Audio.");
                enabled = false;
                return;
            }
            var audioObject = new GameObject("Footstep Audio");
            audioObject.transform.SetParent(transform, false);
            source = audioObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            // The local player's top-down camera is well above the ground, so do not attenuate by camera distance.
            source.spatialBlend = 0;
            source.volume = 1;
            source.priority = 160;
            ResetMotion();
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
                Play(landings, ref lastLanding, Mathf.Lerp(.3f, .65f, Mathf.InverseLerp(2, 12, -fallSpeed)));
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
                Play(steps, ref lastStep, Mathf.Lerp(walkVolume, runVolume, running));
                StepsPlayed++;
                distance = Mathf.Min(distance - threshold, stride * .5f);
                firstStep = false;
                sinceSound = 0;
            }
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
            if (source != null) source.Stop();
        }

        private void OnApplicationFocus(bool focused) { if (!focused) ResetMotion(); }
        private void OnDisable() { ResetMotion(); }
        private void OnDestroy() { if (source != null) Destroy(source.gameObject); }
    }
}
