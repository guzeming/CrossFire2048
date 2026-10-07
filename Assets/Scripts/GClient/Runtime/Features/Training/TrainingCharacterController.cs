using UnityEngine;
using UnityEngine.EventSystems;

namespace OperationBlacktide.Client.Features.Training
{
    /// <summary>角色朝向相对移动、鼠标瞄准和带重力的本地训练角色。</summary>
    [DefaultExecutionOrder(60), RequireComponent(typeof(CharacterController))]
    public sealed class TrainingCharacterController : MonoBehaviour
    {
        [SerializeField] private float walkSpeed = 4.5f;
        [SerializeField] private float sprintSpeed = 7f;
        [SerializeField] private float jumpHeight = 1f;
        [SerializeField] private float gravity = -24f;
        [SerializeField] private float turnSpeed = 720f;
        // The legacy Mouse X/Y axes use a 0.1 sensitivity; restore pixel-sized cursor motion.
        [SerializeField, Min(0)] private float aimSensitivity = 10f;
        private CharacterController motor;
        private Camera viewCamera;
        private TrainingCameraController cameraController;
        private TrainingCameraCutaway aimCutaway;
        private Vector3 aimOffset;
        private bool aimInputReady;
        private float verticalSpeed;
        private Vector3 spawnPosition;
        private Quaternion spawnRotation;

        public bool InputEnabled { get; set; } = true;
        public bool IsGrounded => motor != null && motor.isGrounded;
        public Vector3 ActualVelocity { get; private set; }
        public Vector2 AimScreenPosition => viewCamera != null
            ? (Vector2)viewCamera.WorldToScreenPoint(AimAnchor) : Vector2.zero;
        public Camera ViewCamera => viewCamera;
        private bool HasOrbitAim => cameraController != null && cameraController.IsOrbiting;
        // Keep the aiming direction in world axes while translating its anchor with the player.
        // Camera rotation must not turn the character, and walking must not pass a fixed aim point.
        private Vector3 AimAnchor => HasOrbitAim ? cameraController.OrbitAimPoint : transform.position + aimOffset;

        // Resolve against the current camera pose, including follow/zoom updates before a shot.
        public Vector3 AimPoint
        {
            get
            {
                if (HasOrbitAim) return cameraController.OrbitAimPoint;
                if (viewCamera == null) return transform.position + transform.forward * 1000f;
                Ray ray = viewCamera.ScreenPointToRay(AimScreenPosition);
                return TryGetAimHit(out var hit) ? hit.point : ray.GetPoint(viewCamera.farClipPlane);
            }
        }

        public bool TryGetAimHit(out RaycastHit hit)
        {
            hit = default;
            if (viewCamera == null) return false;
            Ray ray = viewCamera.ScreenPointToRay(AimScreenPosition);
            return RaycastAim(ray, out hit);
        }

        private bool RaycastAim(Ray ray, out RaycastHit hit)
        {
            // Follow installs cutaway after character initialization, so resolve it lazily.
            if (aimCutaway == null) aimCutaway = viewCamera.GetComponent<TrainingCameraCutaway>();
            return aimCutaway != null ? aimCutaway.RaycastAim(ray, out hit, viewCamera.farClipPlane) :
                Physics.Raycast(ray, out hit, viewCamera.farClipPlane, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        private void Awake() { motor = GetComponent<CharacterController>(); }

        public void Initialize(Camera camera, Vector3 position, Quaternion rotation)
        {
            viewCamera = camera;
            cameraController = camera != null ? camera.GetComponent<TrainingCameraController>() : null;
            aimCutaway = null;
            spawnPosition = position;
            spawnRotation = rotation;
            Respawn();
        }

        public void Respawn()
        {
            motor.enabled = false;
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            verticalSpeed = 0;
            ActualVelocity = Vector3.zero;
            aimOffset = transform.forward * 6f;
            aimInputReady = false;
            motor.enabled = true;
            GetComponent<TrainingVitals>()?.Restore();
            GetComponent<TrainingWeaponController>()?.ResetEquipment();
            GetComponent<TrainingFootstepAudio>()?.ResetMotion();
        }

        private void Update()
        {
            bool canReadInput = InputEnabled && Application.isFocused && Time.timeScale > 0;
            UpdateAimInput(new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")),
                canReadInput && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()), Time.deltaTime);
            Vector2 input = canReadInput
                ? new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")) : Vector2.zero;
            Move(input, canReadInput && Input.GetKey(KeyCode.LeftShift),
                canReadInput && Input.GetKeyDown(KeyCode.Space), Time.deltaTime);
            if (transform.position.y < spawnPosition.y - 20f) Respawn();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) aimInputReady = false;
        }

        private void OnDisable() { aimInputReady = false; }

        // Only physical mouse displacement selects a new aim direction. Reprojecting the
        // existing anchor keeps the reticle/weapon aligned while the camera follows our yaw.
        public void UpdateAimInput(Vector2 mouseDelta, bool allowed, float deltaTime)
        {
            if (!allowed || !InputEnabled || viewCamera == null || deltaTime <= 0)
            {
                aimInputReady = false;
                return;
            }
            if (HasOrbitAim)
            {
                aimInputReady = false;
                TurnTowardAim(deltaTime);
                return;
            }
            if (aimInputReady && mouseDelta.sqrMagnitude > 0)
            {
                Rect bounds = viewCamera.pixelRect;
                Vector3 projected = viewCamera.WorldToScreenPoint(AimAnchor);
                Vector2 screen = projected.z > 0 ? (Vector2)projected : bounds.center;
                screen += mouseDelta * aimSensitivity;
                screen.x = Mathf.Clamp(screen.x, bounds.xMin + 1, bounds.xMax - 1);
                screen.y = Mathf.Clamp(screen.y, bounds.yMin + 1, bounds.yMax - 1);
                SelectAim(screen);
            }
            aimInputReady = true;
            TurnTowardAim(deltaTime);
        }

        // Movement is separate from input polling so collision behaviour can also be simulated deterministically.
        public void Move(Vector2 input, bool sprint, bool jump, float deltaTime)
        {
            if (deltaTime <= 0 || !motor.enabled) return;
            if (!InputEnabled) { input = Vector2.zero; jump = false; }
            input = Vector2.ClampMagnitude(input, 1f);
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            bool scoped = GetComponent<TrainingWeaponController>()?.IsScoped == true;
            Vector3 velocity = (right * input.x + forward * input.y) * (scoped ? walkSpeed * .45f : sprint ? sprintSpeed : walkSpeed);
            if (motor.isGrounded && verticalSpeed < 0) verticalSpeed = -2f;
            if (jump && motor.isGrounded) verticalSpeed = Mathf.Sqrt(jumpHeight * -2f * gravity);
            verticalSpeed = Mathf.Max(verticalSpeed + gravity * deltaTime, -40f);
            velocity.y = verticalSpeed;
            Vector3 before = transform.position;
            CollisionFlags flags = motor.Move(velocity * deltaTime);
            ActualVelocity = (transform.position - before) / deltaTime;
            if ((flags & CollisionFlags.Above) != 0 && verticalSpeed > 0) verticalSpeed = 0;
        }

        public void Aim(Vector2 screenPosition, float deltaTime)
        {
            if (!InputEnabled || viewCamera == null) return;
            if (!HasOrbitAim) SelectAim(screenPosition);
            TurnTowardAim(deltaTime);
        }

        private void SelectAim(Vector2 screenPosition)
        {
            Ray ray = viewCamera.ScreenPointToRay(screenPosition);
            Vector3 point = RaycastAim(ray, out var hit) ? hit.point : ray.GetPoint(viewCamera.farClipPlane);
            aimOffset = point - transform.position;
        }

        private void TurnTowardAim(float deltaTime)
        {
            // The body stays upright; weapon picking still uses the final camera pose.
            Vector3 direction = Vector3.ProjectOnPlane(AimAnchor - transform.position, Vector3.up);
            if (direction.sqrMagnitude < .01f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(direction), turnSpeed * deltaTime);
        }
    }
}
