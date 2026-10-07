using UnityEngine;

namespace OperationBlacktide.Client.Features.Training
{
    /// <summary>Samples rotation-only CS upper-body clips after locomotion/held-pose sampling.</summary>
    public sealed class TrainingThrowablePresentation : MonoBehaviour
    {
        private Animator animator;
        private Transform[] bones;
        private Quaternion[] original, previous, transitionFrom, probeRotations;
        private float blendElapsed;
        public AnimationClip CurrentClip { get; private set; }
        public float SampleTime { get; private set; }

        public void Bind(TrainingCharacterController character)
        {
            Release();
            animator = character.GetComponent<TrainingCharacterAnimator>()?.Animator;
            if (animator == null) return;
            foreach (var node in animator.GetComponentsInChildren<Transform>())
                if (node.name == "spine_0") { bones = node.GetComponentsInChildren<Transform>(); break; }
            if (bones == null) return;
            original = new Quaternion[bones.Length]; previous = new Quaternion[bones.Length]; transitionFrom = new Quaternion[bones.Length];
            probeRotations = new Quaternion[bones.Length];
            for (int i = 0; i < bones.Length; i++) original[i] = previous[i] = bones[i].localRotation;
        }

        public void Present(AnimationClip clip, float time, float dt)
        {
            if (animator == null || bones == null || clip == null) return;
            if (CurrentClip != clip)
            {
                for (int i = 0; i < bones.Length; i++) transitionFrom[i] = previous[i];
                blendElapsed = 0; CurrentClip = clip;
            }
            blendElapsed += Mathf.Max(0, dt);
            SampleTime = Mathf.Clamp(time, 0, clip.length);
            clip.SampleAnimation(animator.gameObject, SampleTime);
            float blend = Mathf.Clamp01(blendElapsed / .08f);
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;
                bones[i].localRotation = Quaternion.Slerp(transitionFrom[i], bones[i].localRotation, blend);
                previous[i] = bones[i].localRotation;
            }
        }

        public void Release()
        {
            if (bones != null) for (int i = 0; i < bones.Length; i++) if (bones[i] != null) bones[i].localRotation = original[i];
            animator = null; bones = null; original = previous = transitionFrom = null; CurrentClip = null; SampleTime = 0;
        }

        public Vector3 PointAt(AnimationClip clip, float time, Transform attachment, Vector3 localPoint)
        {
            if (animator == null || bones == null) return attachment.TransformPoint(localPoint);
            for (int i = 0; i < bones.Length; i++) probeRotations[i] = bones[i].localRotation;
            clip.SampleAnimation(animator.gameObject, time);
            Vector3 result = attachment.TransformPoint(localPoint);
            for (int i = 0; i < bones.Length; i++) bones[i].localRotation = probeRotations[i];
            return result;
        }
        private void OnDestroy() { Release(); }
    }
}
