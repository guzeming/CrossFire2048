using UnityEngine;
using System.Collections.Generic;

namespace OperationBlacktide.Client.Features.Training
{
    /// <summary>用实际位移驱动完整的步枪战斗动画，保留移动时躯干与手臂的瞄准补偿。</summary>
    [DefaultExecutionOrder(100)]
    public sealed class TrainingCharacterAnimator : MonoBehaviour
    {
        private static readonly int Speed = Animator.StringToHash("Speed");
        private static readonly int MoveX = Animator.StringToHash("MoveX");
        private static readonly int MoveZ = Animator.StringToHash("MoveZ");
        private static readonly int Grounded = Animator.StringToHash("Grounded");
        private TrainingCharacterController character;
        private Animator animator;
        private readonly Dictionary<Transform, Quaternion> heldPose = new Dictionary<Transform, Quaternion>();
        public Animator Animator => animator;

        public void Initialize(TrainingCharacterController controller, Animator target, bool preserveHeldPose = false)
        {
            character = controller;
            animator = target;
            heldPose.Clear();
            if (preserveHeldPose)
                foreach (var bone in target.GetComponentsInChildren<Transform>())
                    if (bone.name == "spine_0")
                        foreach (var upper in bone.GetComponentsInChildren<Transform>()) heldPose[upper] = upper.localRotation;
            var locomotion = Resources.Load<RuntimeAnimatorController>("Training/TrainingLocomotion");
            if (locomotion == null)
            {
                Debug.LogError("[Training] 缺少 TrainingLocomotion 动画控制器，请重建训练场动画。");
                enabled = false;
                return;
            }
            animator.runtimeAnimatorController = locomotion;
            animator.applyRootMotion = false;
            animator.updateMode = AnimatorUpdateMode.Normal;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            animator.SetBool(Grounded, true);
            animator.SetFloat(MoveZ, 1);
            animator.Update(0);
        }

        private void Update() { UpdateAnimation(Time.deltaTime); }

        private void LateUpdate()
        {
            foreach (var pose in heldPose) if (pose.Key != null) pose.Key.localRotation = pose.Value;
        }

        public void UpdateAnimation(float deltaTime)
        {
            if (animator == null || character == null || animator.runtimeAnimatorController == null) return;
            Vector3 horizontal = Vector3.ProjectOnPlane(character.ActualVelocity, Vector3.up);
            float speed = character.InputEnabled ? horizontal.magnitude : 0;
            // Keep the last direction when stopping so the blend returns cleanly to idle.
            if (speed > .05f)
            {
                Vector3 direction = character.transform.InverseTransformDirection(horizontal / speed);
                animator.SetFloat(MoveX, direction.x, .06f, deltaTime);
                animator.SetFloat(MoveZ, direction.z, .06f, deltaTime);
            }
            else if (!character.IsGrounded)
            {
                // An in-place jump must use the centre clip, not the last walking direction.
                animator.SetFloat(MoveX, 0, .06f, deltaTime);
                animator.SetFloat(MoveZ, 0, .06f, deltaTime);
            }
            animator.SetFloat(Speed, speed < .05f ? 0 : speed, .08f, deltaTime);
            animator.SetBool(Grounded, character.IsGrounded);
        }

    }
}
