using OperationBlacktide.Client.Gameplay.Cameras;
using OperationBlacktide.Client.Gameplay.Combat;
using UnityEngine;
using System;
using OperationBlacktide.Shared.Characters;

namespace OperationBlacktide.Client.Gameplay.Characters
{
    /// <summary>角色朝向相对移动、鼠标瞄准和重力；出生和重生规则由模式决定。</summary>
    [DefaultExecutionOrder(60), RequireComponent(typeof(CharacterController))]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "OperationBlacktide.Client.Features.Training", "Assembly-CSharp", "TrainingCharacterController")]
    public sealed class CharacterMotor : MonoBehaviour
    {
        [SerializeField] private float walkSpeed = 4.5f;
        [SerializeField] private float sprintSpeed = 7f;
        [SerializeField] private float jumpHeight = 1f;
        [SerializeField] private float gravity = -24f;
        [SerializeField] private float turnSpeed = 720f;
        // The legacy Mouse X/Y axes use a 0.1 sensitivity; restore pixel-sized cursor motion.
        [SerializeField, Min(0)] private float aimSensitivity = 10f;
        private CharacterController motor;
        public MovementComponent Movement { get; set; } = new MovementComponent();
        private Camera viewCamera;
        private TopDownCameraController cameraController;
        private CameraCutaway aimCutaway;
        private Vector3 aimOffset;
        private bool aimingView;
        private Vector2 aimingViewport;
        private float followAimDistance;
        private bool aimInputReady;
        private float verticalSpeed;
        private Vector3 spawnPosition;
        private Quaternion spawnRotation;

        public bool InputEnabled { get; set; } = true;
        public event Action Respawned;
        public event Action FellOutOfWorld;
        public bool IsGrounded => motor != null && motor.isGrounded;
        public Vector3 ActualVelocity { get; private set; }
        // Network presenters disable local polling and feed reconciled movement to animation.
        public void SetPresentationVelocity(Vector3 velocity) { ActualVelocity = velocity; }
        public Vector2 AimScreenPosition => viewCamera == null ? Vector2.zero :
            IsFirstPerson ? (Vector2)viewCamera.ViewportToScreenPoint(new Vector3(.5f, .5f)) :
            aimingView && !HasOrbitAim ? (Vector2)viewCamera.ViewportToScreenPoint(aimingViewport) :
            (Vector2)viewCamera.WorldToScreenPoint(AimAnchor);
        public Camera ViewCamera => viewCamera;
        public bool IsFirstPerson => cameraController != null && cameraController.IsFirstPerson;
        public void ResetViewAim()
        {
            aimOffset = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized * 6;
            aimingView = false; aimInputReady = false;
        }
        private bool HasOrbitAim => cameraController != null && cameraController.IsOrbiting;
        // The control anchor stays on the player's horizontal plane, independently of 3D hits.
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
            if (aimCutaway == null) aimCutaway = viewCamera.GetComponent<CameraCutaway>();
            return aimCutaway != null ? aimCutaway.RaycastAim(ray, out hit, viewCamera.farClipPlane) :
                Physics.Raycast(ray, out hit, viewCamera.farClipPlane, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        private void Awake() { motor = GetComponent<CharacterController>(); }

        public void Initialize(Camera camera, Vector3 position, Quaternion rotation)
        {
            viewCamera = camera;
            if (camera != null && GetComponent<LocalPlayerInput>() == null) gameObject.AddComponent<LocalPlayerInput>();
            cameraController = camera != null ? camera.GetComponent<TopDownCameraController>() : null;
            aimCutaway = null;
            spawnPosition = position;
            spawnRotation = rotation;
            Respawn();
        }

        // Network movement owns spawning and reconciliation. Bind the shared aim controller
        // without installing a second movement/input driver or moving the authoritative pawn.
        public void BindPresentationCamera(Camera camera)
        {
            viewCamera = camera;
            cameraController = camera != null ? camera.GetComponent<TopDownCameraController>() : null;
            aimCutaway = null;
            aimOffset = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized * 6f;
            aimingView = false;
            aimInputReady = false;
        }

        public void Respawn()
        {
            motor.enabled = false;
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            verticalSpeed = 0;
            ActualVelocity = Vector3.zero;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            aimOffset = (forward.sqrMagnitude > .01f ? forward : Vector3.forward) * 6f;
            aimingView = false;
            aimInputReady = false;
            motor.enabled = true;
            Respawned?.Invoke();
        }

        private void Update()
        {
            if (transform.position.y < spawnPosition.y - 20f) FellOutOfWorld?.Invoke();
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
                return;
            }
            if (IsFirstPerson)
            {
                if (aimInputReady) cameraController.RotateFirstPerson(mouseDelta);
                aimInputReady = true;
                return;
            }
            if (aimInputReady && mouseDelta.sqrMagnitude > 0)
            {
                Rect bounds = viewCamera.pixelRect;
                Vector2 screen = AimScreenPosition;
                if (!aimingView && viewCamera.WorldToScreenPoint(AimAnchor).z <= 0) screen = bounds.center;
                screen += mouseDelta * aimSensitivity;
                screen.x = Mathf.Clamp(screen.x, bounds.xMin + 1, bounds.xMax - 1);
                screen.y = Mathf.Clamp(screen.y, bounds.yMin + 1, bounds.yMax - 1);
                SelectAim(screen);
            }
            aimInputReady = true;
            // Aim view turns once, after this frame's camera movement has selected the final ray.
            if (!aimingView) TurnTowardAim(deltaTime);
        }

        // Movement is separate from input polling so collision behaviour can also be simulated deterministically.
        public void Move(Vector2 input, bool sprint, bool jump, float deltaTime)
        {
            if (deltaTime <= 0 || !motor.enabled) return;
            if (!InputEnabled) { input = Vector2.zero; jump = false; }
            bool scoped = GetComponent<WeaponController>()?.IsScoped == true;
            Movement.WalkSpeed = walkSpeed; Movement.SprintSpeed = sprintSpeed; Movement.Gravity = gravity; Movement.JumpHeight = jumpHeight;
            var value = Movement.Velocity(input.x, input.y, transform.eulerAngles.y, sprint && !scoped, jump,
                motor.isGrounded, ref verticalSpeed, deltaTime, scoped ? .45f : 1);
            Vector3 velocity = new Vector3(value.x, value.y, value.z);
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

        // While inspecting distant scenery, the mouse owns a screen cursor. Moving the camera
        // must keep that cursor at the edge so it can continue panning without more mouse input.
        public void SetAimingView(bool active)
        {
            if (aimingView == active || viewCamera == null) return;
            if (active)
            {
                aimingViewport = viewCamera.ScreenToViewportPoint(AimScreenPosition);
                followAimDistance = Mathf.Max(1f, aimOffset.magnitude);
            }
            else
            {
                Vector3 direction = aimOffset.sqrMagnitude > .01f ? aimOffset.normalized : transform.forward;
                aimOffset = Vector3.ProjectOnPlane(direction, Vector3.up).normalized * followAimDistance;
                aimInputReady = false;
            }
            aimingView = active;
        }

        // Called after camera pan/zoom and before upper-body aiming and firing.
        public void UpdateAimingView(Vector2 viewport, float deltaTime)
        {
            if (!aimingView || HasOrbitAim || !InputEnabled) return;
            SelectAim(viewCamera.ViewportToScreenPoint(viewport));
            TurnTowardAim(deltaTime);
        }

        private void SelectAim(Vector2 screenPosition)
        {
            if (aimingView) aimingViewport = viewCamera.ScreenToViewportPoint(screenPosition);
            Ray ray = viewCamera.ScreenPointToRay(screenPosition);
            // A wall/roof changes the weapon hit depth, never the horizontal control heading.
            // Preserve the last anchor if a ray cannot reach the reference plane.
            var plane = new Plane(Vector3.up, transform.position);
            if (plane.Raycast(ray, out float distance))
                aimOffset = Vector3.ProjectOnPlane(ray.GetPoint(distance) - transform.position, Vector3.up);
        }

        private void TurnTowardAim(float deltaTime)
        {
            // Orbit holds the ballistic aim point, but must not use its height/depth to steer the body.
            if (HasOrbitAim) return;
            Vector3 direction = aimOffset;
            if (direction.sqrMagnitude < .01f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(direction), turnSpeed * deltaTime);
        }
    }
}
