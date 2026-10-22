using OperationBlacktide.Client.Gameplay.Characters;
using OperationBlacktide.Client.Gameplay.Combat;
using OperationBlacktide.Client.Gameplay.Vision;
using UnityEngine;
using UnityEngine.EventSystems;

namespace OperationBlacktide.Client.Gameplay.Cameras
{
    /// <summary>随角色朝向转动的俯视相机；中键调整相对角度，滚轮缩放，建筑遮挡时整体半透明。</summary>
    [DefaultExecutionOrder(50), RequireComponent(typeof(Camera))]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "OperationBlacktide.Client.Features.Training", "Assembly-CSharp", "TrainingCameraController")]
    public sealed class TopDownCameraController : MonoBehaviour
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
        [SerializeField, Range(.5f, .99f), Tooltip("归一化屏幕坐标；0.8 表示外侧 10% 为平移区。")]
        private float edgePanThreshold = .8f;
        [SerializeField, Min(0)] private float edgePanSpeed = 14f;
        private Transform target;
        private Vector3 focus;
        private Vector3 previousTarget;
        private float followedYaw;
        private WeaponController weapon;
        // Network modes supply presentation state, without a local damage/ammunition controller.
        public bool ExternalWeaponState { get; set; }
        public bool ExternalAiming { get; set; }
        public bool ExternalScoped { get; set; }
        private CharacterMotor character;
        private Camera viewCamera;
        private float normalFov;
        public float ScopeBlend { get; private set; }
        public Vector3 Focus => focus;
        public bool IsOrbiting { get; private set; }
        public bool IsAimingView { get; private set; }
        public bool IsDetached { get; private set; }
        public Vector3 OrbitAimPoint { get; private set; }
        private CameraCutaway cutaway;
        private CharacterVision vision;
        public bool InputEnabled { get; set; } = true;
        [SerializeField, Min(0)] private float firstPersonSensitivity = 2f;
        private float firstPersonPitch;
        private Vector2 firstPersonRecoil;
        private float recoilRecoveryDelay;
        private int recoilShots;
        public Vector2 FirstPersonRecoil => firstPersonRecoil;
        private FirstPersonView firstPersonView;
        public bool IsFirstPerson { get; private set; }
        public CursorLockMode GameplayCursorLock => IsFirstPerson ? CursorLockMode.Locked : CursorLockMode.Confined;

        public void SetFirstPerson(bool active)
        {
            if (active == IsFirstPerson || target == null || character == null) return;
            EndAimingView(); IsOrbiting = false; ScopeBlend = 0;
            IsFirstPerson = active;
            ResetFirstPersonRecoil(character);
            character.ResetViewAim();
            if (cutaway != null) cutaway.enabled = !active && isActiveAndEnabled;
            if (vision != null) vision.enabled = !active && isActiveAndEnabled;
            if (active)
            {
                firstPersonPitch = 0;
                if (firstPersonView == null) firstPersonView = gameObject.AddComponent<FirstPersonView>();
                firstPersonView.Bind(character);
                PositionFirstPerson(false);
            }
            else
            {
                if (firstPersonView != null) firstPersonView.enabled = false;
                Snap();
            }
        }

        public void RotateFirstPerson(Vector2 delta)
        {
            if (!IsFirstPerson || !InputEnabled || character == null) return;
            float sensitivity = firstPersonSensitivity * (ScopeBlend > .5f ? .35f : 1);
            firstPersonPitch = Mathf.Clamp(firstPersonPitch - delta.y * sensitivity, -85, 85);
            target.rotation = Quaternion.Euler(0, TargetHorizontalYaw() + delta.x * sensitivity, 0);
            PositionFirstPerson(ScopeBlend > .5f);
        }

        private void PositionFirstPerson(bool scoped)
        {
            var capsule = character.GetComponent<CharacterController>();
            float eye = capsule != null ? capsule.center.y + capsule.height * .5f - .12f : 1.6f;
            transform.SetPositionAndRotation(target.position + Vector3.up * eye,
                Quaternion.Euler(Mathf.Clamp(firstPersonPitch - firstPersonRecoil.x, -85, 85),
                    TargetHorizontalYaw() + firstPersonRecoil.y, 0));
            ScopeBlend = scoped ? 1 : 0;
            viewCamera.fieldOfView = scoped ? 26 : 74;
            firstPersonView?.SetScoped(scoped);
            focus = transform.position + transform.forward * 6;
        }

        // Called after a real shot (or a server-confirmed shot), never on a trigger attempt.
        // Keep the punch separate from mouse input so it recovers without moving the body pitch.
        public void ApplyFirstPersonRecoil(CharacterMotor source, WeaponDefinition definition)
        {
            if (!IsFirstPerson || source == null || source != character || !InputEnabled || !source.InputEnabled ||
                definition == null || definition.IsMelee || definition.IsTaser) return;
            float kick = definition.IsSniper ? 2.4f : definition.pelletCount > 1 ? 2.2f :
                definition.IsPistol ? 1.3f : definition.id.StartsWith("weapon_smg_") ? .65f : 1.05f;
            if (definition.silenced) kick *= .85f;
            firstPersonRecoil.x = Mathf.Min(12, firstPersonRecoil.x + kick);
            firstPersonRecoil.y = Mathf.Clamp(firstPersonRecoil.y + Mathf.Sin(++recoilShots * 1.7f) * kick * .22f, -3, 3);
            recoilRecoveryDelay = Mathf.Max(.12f, definition.cycleTime * 1.2f);
            PositionFirstPerson(ScopeBlend > .5f);
        }

        public void ResetFirstPersonRecoil(CharacterMotor source)
        {
            if (source != character) return;
            firstPersonRecoil = Vector2.zero; recoilRecoveryDelay = 0; recoilShots = 0;
            if (IsFirstPerson && target != null) PositionFirstPerson(ScopeBlend > .5f);
        }

        private void RecoverFirstPersonRecoil(float deltaTime)
        {
            float step = Mathf.Max(0, deltaTime);
            float recovery = Mathf.Max(0, step - recoilRecoveryDelay);
            recoilRecoveryDelay = Mathf.Max(0, recoilRecoveryDelay - step);
            firstPersonRecoil *= Mathf.Exp(-8 * recovery);
            if (firstPersonRecoil.sqrMagnitude < .000001f)
            { firstPersonRecoil = Vector2.zero; recoilShots = 0; }
        }

        public void Follow(Transform character)
        {
            bool restoreFirstPerson = IsFirstPerson;
            SetFirstPerson(false);
            EndAimingView();
            target = character;
            this.character = character != null ? character.GetComponent<CharacterMotor>() : null;
            weapon = character != null ? character.GetComponent<WeaponController>() : null;
            viewCamera = GetComponent<Camera>();
            if (normalFov <= 0) normalFov = viewCamera.fieldOfView;
            if (Application.isPlaying)
            {
                if (cutaway == null) cutaway = GetComponent<CameraCutaway>();
                if (cutaway == null) cutaway = gameObject.AddComponent<CameraCutaway>();
                cutaway.enabled = isActiveAndEnabled;
                cutaway.Follow(character);
                if (vision == null) vision = GetComponent<CharacterVision>();
                if (vision == null) vision = gameObject.AddComponent<CharacterVision>();
                vision.enabled = isActiveAndEnabled;
                vision.Follow(character);
            }
            Snap();
            if (restoreFirstPerson) SetFirstPerson(true);
        }

        private void OnEnable()
        {
            if (cutaway != null) cutaway.enabled = !IsFirstPerson;
            if (vision != null) vision.enabled = !IsFirstPerson;
        }

        private void OnDisable()
        {
            SetFirstPerson(false);
            IsOrbiting = false;
            EndAimingView();
            if (cutaway != null) cutaway.enabled = false;
            if (vision != null) vision.enabled = false;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) { IsOrbiting = false; EndAimingView(); }
        }

        private void OnDestroy()
        {
            if (cutaway != null) Destroy(cutaway);
            if (vision != null) Destroy(vision);
            if (firstPersonView != null) Destroy(firstPersonView);
        }

        public void Snap()
        {
            IsOrbiting = false;
            EndAimingView();
            if (target == null) return;
            focus = target.position + Vector3.up;
            previousTarget = target.position;
            followedYaw = TargetHorizontalYaw();
            ScopeBlend = 0;
            if (IsFirstPerson) { firstPersonPitch = 0; ResetFirstPersonRecoil(character); PositionFirstPerson(false); return; }
            PositionCamera();
        }

        private bool CanReadInput => InputEnabled && Application.isFocused && Time.timeScale > 0 &&
            (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject());

        private void Update()
        {
            if (CanReadInput && character != null && character.InputEnabled && Input.GetKeyDown(KeyCode.P))
                SetFirstPerson(!IsFirstPerson);
            if (IsFirstPerson) return;
            ProcessOrbit(Input.GetMouseButton(2), Input.GetMouseButtonDown(2),
                new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")), CanReadInput);
        }

        // Mouse axes already contain displacement for this frame; multiplying by deltaTime
        // would make the same drag rotate different amounts at different frame rates.
        public void ProcessOrbit(bool held, bool pressed, Vector2 delta, bool allowed)
        {
            if (IsFirstPerson || !allowed || !InputEnabled || !held || target == null)
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
            UpdateView(new Vector2(cursor.x * 2 - 1, cursor.y * 2 - 1), ExternalWeaponState ? ExternalScoped : weapon != null && weapon.IsScoped,
                CanReadInput, Input.mouseScrollDelta.y, Time.deltaTime, ExternalWeaponState ? ExternalAiming : weapon != null && weapon.IsAiming);
        }

        // Heading comes from the character; yaw is the user-controlled orbit offset.
        // Aim input is anchored independently of camera motion to avoid a rotation feedback loop.
        public void UpdateView(Vector2 cursor, bool scoped, bool allowed, float scroll, float deltaTime, bool aiming = false)
        {
            if (target == null) return;
            allowed &= InputEnabled;
            if (IsFirstPerson)
            {
                RecoverFirstPersonRecoil(allowed && character.InputEnabled ? deltaTime : 0);
                PositionFirstPerson(scoped && allowed); return;
            }
            if (allowed) distance = Mathf.Clamp(distance - scroll * 1.2f, minDistance, maxDistance);
            float smoothing = 1f - Mathf.Exp(-followSharpness * Mathf.Max(0, deltaTime));
            bool teleported = Vector3.Distance(previousTarget, target.position) > 10f;
            bool aimingView = allowed && (aiming || scoped) && !teleported;
            if (aimingView && !IsAimingView)
            {
                IsAimingView = true;
                character?.SetAimingView(true);
            }
            else if (!aimingView && IsAimingView)
            {
                EndAimingView();
                if (character != null)
                {
                    Vector3 restored = viewCamera.ScreenToViewportPoint(character.AimScreenPosition);
                    cursor = new Vector2(restored.x * 2 - 1, restored.y * 2 - 1);
                }
            }
            float targetYaw = TargetHorizontalYaw();
            // A fixed heading lets the cursor reach any edge without body/camera rotation
            // pulling it back to the centre. Ordinary heading follow resumes on release.
            if (!IsAimingView)
                followedYaw = teleported ? targetYaw : Mathf.LerpAngle(followedYaw, targetYaw, smoothing);
            ScopeBlend = allowed ? Mathf.Lerp(ScopeBlend, scoped ? 1 : 0, smoothing) : 0;
            Vector2 aimCursor = new Vector2(Mathf.Clamp(cursor.x, -1, 1), Mathf.Clamp(cursor.y, -1, 1));
            Vector2 edge = IsAimingView && !IsOrbiting ? new Vector2(EdgeSpeed(aimCursor.x), EdgeSpeed(aimCursor.y)) : Vector2.zero;
            if (edge.sqrMagnitude > 0) IsDetached = true;
            // Orbit around the character, without the drag also steering look-ahead.
            cursor = allowed && !IsOrbiting ? Vector2.ClampMagnitude(cursor, 1) : Vector2.zero;
            float magnitude = cursor.magnitude;
            cursor = magnitude > .08f ? cursor / magnitude * ((magnitude - .08f) / .92f) : Vector2.zero;
            Vector3 offset = Quaternion.Euler(0, followedYaw + yaw, 0) * new Vector3(cursor.x, 0, cursor.y);
            offset *= Mathf.Lerp(lookAhead, scopedLookAhead, ScopeBlend);
            Vector3 desiredFocus = target.position + Vector3.up + offset;
            if (IsDetached)
            {
                Vector3 pan = Quaternion.Euler(0, followedYaw + yaw, 0) *
                    new Vector3(edge.x, 0, edge.y);
                focus += Vector3.ClampMagnitude(pan, 1) * (edgePanSpeed * Mathf.Max(0, deltaTime));
                focus.y = target.position.y + 1;
            }
            else focus = teleported ? desiredFocus : Vector3.Lerp(focus, desiredFocus, smoothing);
            previousTarget = target.position;
            PositionCamera();
            if (IsAimingView && !IsOrbiting)
                character?.UpdateAimingView((aimCursor + Vector2.one) * .5f, deltaTime);
        }

        private float EdgeSpeed(float coordinate)
        {
            float amount = Mathf.InverseLerp(edgePanThreshold, 1, Mathf.Abs(coordinate));
            return Mathf.Sign(coordinate) * Mathf.SmoothStep(0, 1, amount);
        }

        private void EndAimingView()
        {
            character?.SetAimingView(false);
            IsAimingView = IsDetached = false;
        }

        private float TargetHorizontalYaw()
        {
            // Ignore character pitch/roll, including the ambiguous Euler yaw at vertical pitch.
            Vector3 forward = Vector3.ProjectOnPlane(target.forward, Vector3.up);
            return forward.sqrMagnitude > .0001f
                ? Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg : followedYaw;
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
