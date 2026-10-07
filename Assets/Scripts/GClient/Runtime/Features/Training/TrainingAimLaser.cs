using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

namespace OperationBlacktide.Client.Features.Training
{
    /// <summary>Persistent 30-metre barrel-aligned laser for all equipped firearms, including snipers.</summary>
    // Read the same posed muzzle as firing, after weapon actions and upper-body aim.
    [DefaultExecutionOrder(210)]
    public sealed class TrainingAimLaser : MonoBehaviour
    {
        private TrainingCharacterController character;
        private TrainingWeaponController source;
        private const float BeamLength = 30f;
        private Transform weapon, barrel;
        private LineRenderer beam;
        private Material material;

        public void Initialize(TrainingCharacterController owner, TrainingWeaponController controller,
            Transform equippedWeapon, TrainingWeaponDefinition data, Material tracerMaterial)
        {
            Release();
            character = owner; source = controller; weapon = equippedWeapon;
            if (!data.HasAimLaser || tracerMaterial == null) return;
            var mesh = equippedWeapon.GetComponentInChildren<MeshFilter>();
            if (mesh == null) return;
            barrel = mesh.transform;

            // Keep the short-lived bullet tracer's shared material untouched.
            material = new Material(tracerMaterial) { name = "Aim Laser (Runtime)" };
            material.SetTexture("_MainTex", Texture2D.whiteTexture);
            material.SetFloat("_SwapUV", 0);
            material.SetFloat("_LaserBeam", 1);
            var visual = new GameObject("Aim Laser");
            visual.layer = gameObject.layer;
            visual.transform.SetParent(transform, false);
            beam = visual.AddComponent<LineRenderer>();
            beam.sharedMaterial = material;
            beam.positionCount = 2; beam.useWorldSpace = true;
            beam.alignment = LineAlignment.View; beam.textureMode = LineTextureMode.Stretch;
            beam.startWidth = beam.endWidth = .045f;
            beam.startColor = beam.endColor = new Color(1f, .22f, .035f, .9f);
            beam.shadowCastingMode = ShadowCastingMode.Off; beam.receiveShadows = false;
            beam.lightProbeUsage = LightProbeUsage.Off; beam.reflectionProbeUsage = ReflectionProbeUsage.Off;
            beam.enabled = false;
        }

        private void LateUpdate()
        {
            UpdateBeam(Application.isFocused && Time.timeScale > 0
                && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()));
        }

        public void UpdateBeam(bool visible)
        {
            if (beam == null) return;
            if (!visible || !isActiveAndEnabled || character == null || !character.InputEnabled
                || source == null || !source.isActiveAndEnabled || weapon == null || barrel == null || !weapon.gameObject.activeInHierarchy)
            { Hide(); return; }

            Vector3 origin = source.MuzzlePosition;
            // Imported gun geometry points along mesh-local -Y. Read the animated barrel,
            // including reload/recoil poses, rather than steering the laser back to the cursor.
            Vector3 direction = barrel.TransformDirection(Vector3.down).normalized;

            // Match firing's near-cover guard: a barrel through a wall must not illuminate the far side.
            Vector3 breech = character.transform.position;
            breech.y = origin.y;
            Vector3 barrelOffset = origin - breech;
            if (Physics.Raycast(breech, barrelOffset.normalized, barrelOffset.magnitude,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            { Hide(); return; }

            Vector3 end = Physics.Raycast(origin, direction, out RaycastHit hit, BeamLength,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                ? hit.point : origin + direction * BeamLength;
            var world = source.Throwables != null ? source.Throwables.World : null;
            if (world != null) end = Vector3.Lerp(origin, end, world.ClipVisibility(origin, end));
            beam.SetPosition(0, origin);
            beam.SetPosition(1, end);
            beam.enabled = true;
        }

        public void Hide() { if (beam != null) beam.enabled = false; }
        private void OnDisable() { Hide(); }
        private void Release()
        {
            Hide();
            if (beam != null) Destroy(beam.gameObject);
            if (material != null) Destroy(material);
            beam = null; material = null; barrel = null;
        }
        private void OnDestroy() { Release(); }
    }
}
