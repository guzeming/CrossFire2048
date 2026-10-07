using UnityEngine;
using UnityEngine.EventSystems;

namespace OperationBlacktide.Client.Features.Training
{
    /// <summary>随角色朝向转动的俯视相机；中键调整相对角度，滚轮缩放，建筑遮挡时整体半透明。</summary>
    [DefaultExecutionOrder(50), RequireComponent(typeof(Camera))]
    public sealed class TrainingCameraController : MonoBehaviour
    {
        [SerializeField] private float pitch = 60f;
        [SerializeField, Tooltip("相对角色朝向的水平角度偏移。")] private float yaw;
        [SerializeField, Min(0)] private float orbitSensitivity = 3f;
        [SerializeField, Range(1, 89)] private float minPitch = 45f;
        [SerializeField, Range(1, 89)] private float maxPitch = 70f;
        [SerializeField] private float distance = 13f;
        [SerializeField] private float minDistance = 8f;
        [SerializeField] private float maxDistance = 20f;
        [SerializeField] private float followSharpness = 14f;
        [SerializeField] private float lookAhead = 2.4f;
        [SerializeField] private float scopedLookAhead = 9f;
        private Transform target;
        private Vector3 focus;
        private Vector3 previousTarget;
        private float followedYaw;
        private TrainingWeaponController weapon;
        private TrainingCharacterController character;
        private Camera viewCamera;
        private float normalFov;
        public float ScopeBlend { get; private set; }
        public Vector3 Focus => focus;
        public bool IsOrbiting { get; private set; }
        public Vector3 OrbitAimPoint { get; private set; }
        private TrainingCameraCutaway cutaway;
        private TrainingVision vision;
        public bool InputEnabled { get; set; } = true;

        public void Follow(Transform character)
        {
            target = character;
            this.character = character != null ? character.GetComponent<TrainingCharacterController>() : null;
            weapon = character != null ? character.GetComponent<TrainingWeaponController>() : null;
            viewCamera = GetComponent<Camera>();
            if (normalFov <= 0) normalFov = viewCamera.fieldOfView;
            if (Application.isPlaying)
            {
                if (cutaway == null) cutaway = GetComponent<TrainingCameraCutaway>();
                if (cutaway == null) cutaway = gameObject.AddComponent<TrainingCameraCutaway>();
                cutaway.enabled = isActiveAndEnabled;
                cutaway.Follow(character);
                if (vision == null) vision = GetComponent<TrainingVision>();
                if (vision == null) vision = gameObject.AddComponent<TrainingVision>();
                vision.enabled = isActiveAndEnabled;
                vision.Follow(character);
            }
            Snap();
        }

        private void OnEnable()
        {
            if (cutaway != null) cutaway.enabled = true;
            if (vision != null) vision.enabled = true;
        }

        private void OnDisable()
        {
            IsOrbiting = false;
            if (cutaway != null) cutaway.enabled = false;
            if (vision != null) vision.enabled = false;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) IsOrbiting = false;
        }

        private void OnDestroy()
        {
            if (cutaway != null) Destroy(cutaway);
            if (vision != null) Destroy(vision);
        }

        public void Snap()
        {
            IsOrbiting = false;
            if (target == null) return;
            focus = target.position + Vector3.up;
            previousTarget = target.position;
            followedYaw = target.eulerAngles.y;
            ScopeBlend = 0;
            PositionCamera();
        }

        private bool CanReadInput => InputEnabled && Application.isFocused && Time.timeScale > 0 &&
            (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject());

        private void Update()
        {
            ProcessOrbit(Input.GetMouseButton(2), Input.GetMouseButtonDown(2),
                new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")), CanReadInput);
        }

        // Mouse axes already contain displacement for this frame; multiplying by deltaTime
        // would make the same drag rotate different amounts at different frame rates.
        public void ProcessOrbit(bool held, bool pressed, Vector2 delta, bool allowed)
        {
            if (!allowed || !InputEnabled || !held || target == null)
            {
                IsOrbiting = false;
                return;
            }
            if (!IsOrbiting)
            {
                // A drag interrupted by a menu/focus change requires a fresh middle click.
                if (!pressed) return;
                OrbitAimPoint = character != null ? character.AimPoint : target.position + Vector3.up;
                IsOrbiting = true;
                return; // Ignore movement that happened before the press in this frame.
            }
            yaw = Mathf.Repeat(yaw + delta.x * orbitSensitivity, 360f);
            pitch = Mathf.Clamp(pitch - delta.y * orbitSensitivity, minPitch, maxPitch);
        }

        private void LateUpdate()
        {
            if (target == null) return;
            Vector3 cursor = viewCamera.ScreenToViewportPoint(character != null ? character.AimScreenPosition : (Vector2)Input.mousePosition);
            UpdateView(new Vector2(cursor.x * 2 - 1, cursor.y * 2 - 1), weapon != null && weapon.IsScoped,
                CanReadInput, Input.mouseScrollDelta.y, Time.deltaTime);
        }

        // Heading comes from the character; yaw is the user-controlled orbit offset.
        // Aim input is anchored independently of camera motion to avoid a rotation feedback loop.
        public void UpdateView(Vector2 cursor, bool scoped, bool allowed, float scroll, float deltaTime)
        {
            if (target == null) return;
            if (allowed) distance = Mathf.Clamp(distance - scroll * 1.2f, minDistance, maxDistance);
            float smoothing = 1f - Mathf.Exp(-followSharpness * Mathf.Max(0, deltaTime));
            bool teleported = Vector3.Distance(previousTarget, target.position) > 10f;
            followedYaw = teleported ? target.eulerAngles.y : Mathf.LerpAngle(followedYaw, target.eulerAngles.y, smoothing);
            ScopeBlend = allowed ? Mathf.Lerp(ScopeBlend, scoped ? 1 : 0, smoothing) : 0;
            // Orbit around the character, without the drag also steering look-ahead.
            cursor = allowed && !IsOrbiting ? Vector2.ClampMagnitude(cursor, 1) : Vector2.zero;
            float magnitude = cursor.magnitude;
            cursor = magnitude > .08f ? cursor / magnitude * ((magnitude - .08f) / .92f) : Vector2.zero;
            Vector3 offset = Quaternion.Euler(0, followedYaw + yaw, 0) * new Vector3(cursor.x, 0, cursor.y);
            offset *= Mathf.Lerp(lookAhead, scopedLookAhead, ScopeBlend);
            Vector3 desiredFocus = target.position + Vector3.up + offset;
            focus = teleported ? desiredFocus : Vector3.Lerp(focus, desiredFocus, smoothing);
            previousTarget = target.position;
            PositionCamera();
        }

        private void PositionCamera()
        {
            Quaternion rotation = Quaternion.Euler(pitch, followedYaw + yaw, 0);
            Vector3 direction = rotation * Vector3.back;
            transform.SetPositionAndRotation(focus + direction * distance, rotation);
            if (viewCamera != null) viewCamera.fieldOfView = Mathf.Lerp(normalFov, normalFov * .72f, ScopeBlend);
        }
    }
}
