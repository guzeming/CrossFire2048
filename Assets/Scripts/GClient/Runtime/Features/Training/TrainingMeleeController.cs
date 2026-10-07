using UnityEngine;

namespace OperationBlacktide.Client.Features.Training
{
    /// <summary>One strike per knife action; camera-faded walls still block attacks.</summary>
    public sealed class TrainingMeleeController : MonoBehaviour
    {
        private TrainingCharacterController character;
        private TrainingWeaponController weapon;
        private TrainingWeaponDefinition definition;
        private AudioSource audioSource;
        private float elapsed, duration, strikeTime;
        private bool swinging, struck, heavy, needsRelease = true;
        public int AttacksStarted { get; private set; }
        public int Hits { get; private set; }
        public bool IsSwinging => swinging;

        public void Bind(TrainingCharacterController owner, TrainingWeaponController source, TrainingWeaponDefinition data)
        {
            Cancel(); character = owner; weapon = source; definition = data != null && data.IsMelee ? data : null;
            if (definition != null && audioSource == null)
            {
                var child = new GameObject("Melee Audio"); child.transform.SetParent(transform, false);
                audioSource = child.AddComponent<AudioSource>(); audioSource.playOnAwake = false; audioSource.spatialBlend = 0;
            }
        }

        public void ProcessInput(bool lightHeld, bool heavyHeld, bool allowed)
        {
            if (definition == null) return;
            allowed &= character.InputEnabled && weapon.isActiveAndEnabled && isActiveAndEnabled;
            if (!allowed) { Cancel(); return; }
            if (!lightHeld && !heavyHeld) { needsRelease = false; return; }
            if (needsRelease || swinging) return;
            heavy = heavyHeld; swinging = true; struck = false; elapsed = 0;
            var clip = heavy ? definition.alternateFireAnimation : definition.fireAnimation;
            duration = Mathf.Max(heavy ? 1.1f : definition.cycleTime, clip.length);
            strikeTime = Mathf.Min(duration * .45f, heavy ? .3f : .18f);
            AttacksStarted++;
            weapon.NotifyAttack(heavy);
            if (heavy) Play(definition.meleeHeavySound);
            else if (definition.shots.Length > 0) Play(definition.shots[(AttacksStarted - 1) % definition.shots.Length]);
        }

        public void Advance(float deltaTime)
        {
            if (!swinging || deltaTime <= 0) return;
            if (definition == null || !character.InputEnabled || !weapon.isActiveAndEnabled) { Cancel(); return; }
            elapsed += deltaTime;
            if (!struck && elapsed >= strikeTime) { struck = true; Strike(); }
            if (elapsed >= duration) swinging = false;
        }

        private void Strike()
        {
            Vector3 origin = character.transform.position + Vector3.up * 1.2f;
            Vector3 offset = character.AimPoint - origin;
            Vector3 direction = offset.sqrMagnitude > .0001f ? offset.normalized : character.transform.forward;
            if (!Physics.SphereCast(origin, .18f, direction, out var hit, definition.range,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return;
            var target = hit.collider.GetComponentInParent<TrainingTarget>();
            if (target != null && target.IsAlive)
            {
                float dealt = target.ApplyDamage(heavy ? 65 : definition.damage);
                if (dealt > 0) { Hits++; weapon.NotifyMeleeHit(target, dealt, hit.point); }
            }
            if (definition.meleeHitSounds.Length > 0) Play(definition.meleeHitSounds[Hits % definition.meleeHitSounds.Length]);
        }

        private void Play(AudioClip clip) { if (clip != null) audioSource.PlayOneShot(clip, .6f); }
        public void Cancel()
        {
            if (swinging) GetComponent<TrainingWeaponPresentation>()?.ResetPresentation();
            swinging = false; struck = false; needsRelease = true;
            if (audioSource != null) audioSource.Stop();
        }
        private void OnDisable() { Cancel(); }
    }
}
