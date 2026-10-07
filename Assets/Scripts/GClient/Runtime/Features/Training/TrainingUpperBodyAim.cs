using UnityEngine;

namespace OperationBlacktide.Client.Features.Training
{
    /// <summary>Aim the generic character rig after animation, keeping both hands attached to the rifle.</summary>
    [DefaultExecutionOrder(185)]
    public sealed class TrainingUpperBodyAim : MonoBehaviour
    {
        private static readonly float[] Weights = { .15f, .25f, .3f, .3f };
        private readonly Transform[] spine = new Transform[4];
        private readonly Quaternion[] animatedRotations = new Quaternion[4];
        private readonly Quaternion[] aimedRotations = new Quaternion[4];
        private TrainingCharacterController character;
        private TrainingWeaponController source;
        private Transform barrel;
        private Vector3 readyMuzzle, readyDirection;
        private bool initialized, poseApplied;

        public void Initialize(TrainingCharacterController owner, TrainingWeaponController weapon, Transform equippedWeapon)
        {
            character = owner;
            source = weapon;
            var animator = owner.GetComponent<TrainingCharacterAnimator>()?.Animator;
            var mesh = equippedWeapon.GetComponentInChildren<MeshFilter>();
            if (animator == null || mesh == null) return;
            barrel = mesh.transform;
            foreach (var bone in animator.GetComponentsInChildren<Transform>())
                for (int i = 0; i < spine.Length; i++)
                    if (bone.name == "spine_" + i) spine[i] = bone;
            foreach (var bone in spine)
                if (bone == null) return;

            // All equipped training rifles point along mesh-local -Y. Save a ready-pose reference
            // so reload gestures do not make the torso chase the deliberately tilted weapon.
            readyMuzzle = spine[3].InverseTransformPoint(source.MuzzlePosition);
            readyDirection = spine[3].InverseTransformDirection(barrel.TransformDirection(Vector3.down));
            initialized = true;
        }

        // Remove last frame's additive correction before the Animator samples the next pose.
        private void Update() { RestorePose(); }
        private void LateUpdate() { ApplyAim(); }

        public void ApplyAim()
        {
            RestorePose();
            if (!initialized || !isActiveAndEnabled || source == null || !source.isActiveAndEnabled) return;
            Vector3 target = character.AimPoint;
            for (int i = 0; i < spine.Length; i++) animatedRotations[i] = spine[i].localRotation;

            bool reloading = source.Ammo.IsReloading;
            // Rotating the torso also moves the muzzle. Re-solve the direction from that new
            // position, especially for nearby ground targets, instead of applying a camera pitch.
            for (int iteration = 0; iteration < 16; iteration++)
            {
                Vector3 muzzle = reloading ? spine[3].TransformPoint(readyMuzzle) : source.MuzzlePosition;
                Vector3 forward = reloading ? spine[3].TransformDirection(readyDirection) : barrel.TransformDirection(Vector3.down);
                Vector3 pivot = Vector3.zero;
                for (int i = 0; i < spine.Length; i++) pivot += spine[i].position * Weights[i];
                Vector3 offset = target - pivot;
                if (offset.sqrMagnitude < .000001f) break;
                Vector3 desired = ClampDirection(offset.normalized) * offset.magnitude;
                if (Vector3.Angle(forward, pivot + desired - muzzle) < .05f) break;

                // Rotate a point on the barrel ray at the target's distance from the torso.
                // A direction-only correction overshoots when the target is close to the muzzle.
                Vector3 muzzleOffset = muzzle - pivot;
                float along = Vector3.Dot(muzzleOffset, forward);
                float sidewaysSquared = Mathf.Max(0, muzzleOffset.sqrMagnitude - along * along);
                float reach = Mathf.Max(.001f, Mathf.Sqrt(Mathf.Max(0, offset.sqrMagnitude - sidewaysSquared)) - along);
                Quaternion correction = Quaternion.FromToRotation(muzzleOffset + forward * reach, desired);
                for (int i = 0; i < spine.Length; i++)
                    spine[i].rotation = Quaternion.Slerp(Quaternion.identity, correction, Weights[i] * .8f) * spine[i].rotation;
            }

            for (int i = 0; i < spine.Length; i++) aimedRotations[i] = spine[i].localRotation;
            poseApplied = true;
        }

        private Vector3 ClampDirection(Vector3 direction)
        {
            Vector3 local = character.transform.InverseTransformDirection(direction);
            // The body catches up with large horizontal turns; avoid twisting the spine backwards.
            float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -80, 80);
            float pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(local.y, -1, 1)) * Mathf.Rad2Deg, -85, 80);
            return character.transform.TransformDirection(Quaternion.Euler(-pitch, yaw, 0) * Vector3.forward);
        }

        public void RestorePose()
        {
            if (!poseApplied) return;
            for (int i = 0; i < spine.Length; i++)
            {
                // Explicit Animator.Update/SampleAnimation calls may already have replaced the pose.
                if (spine[i] != null && Mathf.Abs(Quaternion.Dot(spine[i].localRotation, aimedRotations[i])) > .999999f)
                    spine[i].localRotation = animatedRotations[i];
            }
            poseApplied = false;
        }

        private void OnDisable() { RestorePose(); }
    }
}
